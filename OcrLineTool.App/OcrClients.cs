using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace OcrLineTool;

public sealed class OcrException(string message, string? code = null) : Exception(message)
{
    public string? Code { get; } = code;
}

public interface IOcrClient
{
    Task<IReadOnlyList<string>> RecognizeAsync(string imagePath, CancellationToken cancellationToken = default);
}

public static class OcrClientFactory
{
    public static IOcrClient CreateDeferred(OcrCredential descriptor) => new DeferredOcrClient(descriptor);

    private sealed class DeferredOcrClient(OcrCredential descriptor) : IOcrClient
    {
        private IOcrClient? client;
        public Task<IReadOnlyList<string>> RecognizeAsync(string imagePath, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            client ??= Create(string.IsNullOrWhiteSpace(descriptor.Id) ? CredentialSchedule.Resolve(descriptor) : descriptor);
            return client.RecognizeAsync(imagePath, cancellationToken);
        }
    }

    public static IOcrClient Create(OcrCredential credential)
    {
        if (string.IsNullOrWhiteSpace(credential.Id) || string.IsNullOrWhiteSpace(credential.Secret))
            throw new OcrException($"{credential.DisplayName} 凭据未配置。");
        return credential.Provider switch
        {
            OcrProvider.Tencent => new TencentOcrClient(credential),
            OcrProvider.Baidu => new BaiduOcrClient(credential),
            _ => throw new ArgumentOutOfRangeException(nameof(credential))
        };
    }
}

public static class CloudOcrPolicy
{
    public const int MaxAutomaticRetries = 3;

    public static TimeSpan MinimumInterval(OcrProvider provider) => provider switch
    {
        OcrProvider.Baidu => TimeSpan.FromSeconds(1),
        OcrProvider.Tencent => TimeSpan.FromMilliseconds(500),
        _ => throw new ArgumentOutOfRangeException(nameof(provider))
    };

    public static TimeSpan RetryDelay(int retryNumber) => retryNumber switch
    {
        1 => TimeSpan.FromSeconds(2),
        2 => TimeSpan.FromSeconds(4),
        _ => TimeSpan.FromSeconds(8)
    };

    public static bool IsRateLimit(OcrProvider provider, OcrException exception) =>
        provider == OcrProvider.Baidu
            ? exception.Code == "18"
            : exception.Code?.StartsWith("RequestLimitExceeded", StringComparison.Ordinal) == true;
}

internal static class OcrHttp
{
    internal static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(90) };

    internal static async Task<byte[]> ReadImageAsync(string path, long maxBytes, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
            throw new OcrException("图片不存在。");

        string extension = Path.GetExtension(path).ToLowerInvariant();
        if (extension is not (".jpg" or ".jpeg" or ".png" or ".bmp"))
            throw new OcrException("仅支持 JPG、JPEG、PNG、BMP 图片。");

        var info = new FileInfo(path);
        if (info.Length == 0 || info.Length > maxBytes)
            throw new OcrException($"图片大小必须在 1 字节到 {maxBytes / 1024 / 1024} MB 之间。");

        return await File.ReadAllBytesAsync(path, cancellationToken);
    }
}

public sealed class TencentOcrClient : IOcrClient
{
    private const string Host = "ocr.tencentcloudapi.com";
    private const string Action = "GeneralAccurateOCR";
    private const string Version = "2018-11-19";
    private const string Service = "ocr";
    private readonly OcrCredential credential;
    private readonly HttpClient httpClient;

    public TencentOcrClient(OcrCredential credential)
        : this(credential, OcrHttp.Client)
    {
    }

    internal TencentOcrClient(OcrCredential credential, HttpClient httpClient)
    {
        this.credential = credential;
        this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }

    public async Task<IReadOnlyList<string>> RecognizeAsync(
        string imagePath,
        CancellationToken cancellationToken = default)
    {
        byte[] bytes = await OcrHttp.ReadImageAsync(imagePath, 7_500_000, cancellationToken);
        string payload = JsonSerializer.Serialize(new
        {
            ImageBase64 = Convert.ToBase64String(bytes),
            EnableDetectSplit = true,
            ConfigID = "OCR",
            WordsType = "2"
        });

        long timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        using var request = new HttpRequestMessage(HttpMethod.Post, $"https://{Host}/");
        request.Content = new StringContent(payload, Encoding.UTF8, "application/json");
        request.Headers.TryAddWithoutValidation("Authorization", BuildAuthorization(payload, timestamp));
        request.Headers.TryAddWithoutValidation("X-TC-Action", Action);
        request.Headers.TryAddWithoutValidation("X-TC-Timestamp", timestamp.ToString(CultureInfo.InvariantCulture));
        request.Headers.TryAddWithoutValidation("X-TC-Version", Version);
        request.Headers.TryAddWithoutValidation("X-TC-Region", "ap-guangzhou");

        try
        {
            using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);
            string json = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
                throw new OcrException($"腾讯云请求失败（HTTP {(int)response.StatusCode}）。");
            return ParseLines(json);
        }
        catch (OcrException)
        {
            throw;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            throw new OcrException("腾讯云连接失败，请检查网络后重试。");
        }
    }

    internal string BuildAuthorization(string payload, long timestamp)
    {
        const string algorithm = "TC3-HMAC-SHA256";
        const string signedHeaders = "content-type;host;x-tc-action";
        string canonicalHeaders =
            $"content-type:application/json; charset=utf-8\n" +
            $"host:{Host}\n" +
            $"x-tc-action:{Action.ToLowerInvariant()}\n";
        string canonicalRequest =
            $"POST\n/\n\n{canonicalHeaders}\n{signedHeaders}\n{Sha256Hex(payload)}";

        DateTimeOffset utc = DateTimeOffset.FromUnixTimeSeconds(timestamp);
        string date = utc.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        string scope = $"{date}/{Service}/tc3_request";
        string stringToSign = $"{algorithm}\n{timestamp}\n{scope}\n{Sha256Hex(canonicalRequest)}";

        byte[] secretDate = HmacSha256(Encoding.UTF8.GetBytes("TC3" + credential.Secret), date);
        byte[] secretService = HmacSha256(secretDate, Service);
        byte[] secretSigning = HmacSha256(secretService, "tc3_request");
        string signature = Convert.ToHexString(HmacSha256(secretSigning, stringToSign)).ToLowerInvariant();

        return $"{algorithm} Credential={credential.Id}/{scope}, SignedHeaders={signedHeaders}, Signature={signature}";
    }

    private static string Sha256Hex(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static byte[] HmacSha256(byte[] key, string value) =>
        HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(value));

    public static IReadOnlyList<string> ParseLines(string json)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement response = document.RootElement.GetProperty("Response");
            if (response.TryGetProperty("Error", out JsonElement error))
            {
                string code = error.TryGetProperty("Code", out JsonElement c) ? c.GetString() ?? "未知错误" : "未知错误";
                string message = error.TryGetProperty("Message", out JsonElement m) ? m.GetString() ?? "" : "";
                throw new OcrException($"腾讯云 OCR 错误：{code} {message}".Trim(), code);
            }

            if (!response.TryGetProperty("TextDetections", out JsonElement detections))
                throw new OcrException("腾讯云 OCR 未返回文字结果。");

            var pieces = detections.EnumerateArray()
                .Select((item, index) => ParsePiece(item, index))
                .Where(piece => piece.Text.Length > 0)
                .ToArray();
            if (pieces.All(piece => piece.HasPosition))
                return AssembleRows(pieces);
            return pieces.OrderBy(piece => piece.Index).Select(piece => piece.Text).ToArray();
        }
        catch (OcrException)
        {
            throw;
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or KeyNotFoundException)
        {
            throw new OcrException("腾讯云 OCR 返回格式异常。");
        }
    }

    private sealed record TextPiece(string Text, int X, int Y, int Height, int Index, bool HasPosition)
    {
        public double CenterY => Y + Height / 2d;
    }

    private static TextPiece ParsePiece(JsonElement item, int index)
    {
        string text = item.GetProperty("DetectedText").GetString() ?? "";
        if (!item.TryGetProperty("ItemPolygon", out JsonElement polygon))
            return new(text, 0, 0, 0, index, false);
        return new(
            text,
            polygon.GetProperty("X").GetInt32(),
            polygon.GetProperty("Y").GetInt32(),
            polygon.GetProperty("Height").GetInt32(),
            index,
            true);
    }

    private static IReadOnlyList<string> AssembleRows(IEnumerable<TextPiece> pieces)
    {
        var rows = new List<List<TextPiece>>();
        foreach (TextPiece piece in pieces.OrderBy(piece => piece.CenterY))
        {
            List<TextPiece>? row = rows.FirstOrDefault(candidate =>
            {
                double center = candidate.Average(item => item.CenterY);
                double height = candidate.Average(item => item.Height);
                return Math.Abs(center - piece.CenterY) <= Math.Max(3, Math.Min(height, piece.Height) * 0.55);
            });
            if (row is null)
                rows.Add([piece]);
            else
                row.Add(piece);
        }

        return rows
            .OrderBy(row => row.Average(piece => piece.CenterY))
            .Select(row => string.Concat(row.OrderBy(piece => piece.X).Select(piece => piece.Text)))
            .ToArray();
    }
}

public sealed class BaiduOcrClient : IOcrClient
{
    private readonly OcrCredential credential;
    private readonly HttpClient httpClient;
    private string? accessToken;
    private DateTimeOffset accessTokenExpiresAt;

    public BaiduOcrClient(OcrCredential credential)
        : this(credential, OcrHttp.Client)
    {
    }

    internal BaiduOcrClient(OcrCredential credential, HttpClient httpClient)
    {
        this.credential = credential;
        this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }

    public async Task<IReadOnlyList<string>> RecognizeAsync(
        string imagePath,
        CancellationToken cancellationToken = default)
    {
        byte[] bytes = await OcrHttp.ReadImageAsync(imagePath, 2_500_000, cancellationToken);
        string token = await GetAccessTokenAsync(cancellationToken);
        using var content = new FormUrlEncodedContent(
        [
            new("image", Convert.ToBase64String(bytes)),
            new("language_type", "CHN_ENG"),
            new("detect_direction", "true")
        ]);

        try
        {
            string url = "https://aip.baidubce.com/rest/2.0/ocr/v1/accurate_basic?access_token=" + Uri.EscapeDataString(token);
            using HttpResponseMessage response = await httpClient.PostAsync(url, content, cancellationToken);
            string json = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
                throw new OcrException($"百度 OCR 请求失败（HTTP {(int)response.StatusCode}）。");
            return ParseLines(json);
        }
        catch (OcrException)
        {
            throw;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            throw new OcrException("百度 OCR 连接失败，请检查网络后重试。");
        }
    }

    private async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        if (accessToken is not null && DateTimeOffset.UtcNow < accessTokenExpiresAt)
            return accessToken;

        using var content = new FormUrlEncodedContent(
        [
            new("grant_type", "client_credentials"),
            new("client_id", credential.Id),
            new("client_secret", credential.Secret)
        ]);

        try
        {
            using HttpResponseMessage response = await httpClient.PostAsync(
                "https://aip.baidubce.com/oauth/2.0/token", content, cancellationToken);
            string json = await response.Content.ReadAsStringAsync(cancellationToken);
            using JsonDocument document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("access_token", out JsonElement tokenElement))
            {
                string description = document.RootElement.TryGetProperty("error_description", out JsonElement error)
                    ? error.GetString() ?? "未知错误"
                    : "未知错误";
                throw new OcrException($"百度鉴权失败：{description}");
            }

            accessToken = tokenElement.GetString() ?? throw new OcrException("百度鉴权未返回令牌。");
            int expiresIn = document.RootElement.TryGetProperty("expires_in", out JsonElement expires)
                ? expires.GetInt32()
                : 2_592_000;
            accessTokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(60, expiresIn - 300));
            return accessToken;
        }
        catch (OcrException)
        {
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            throw new OcrException("百度鉴权连接失败，请检查网络后重试。");
        }
    }

    public static IReadOnlyList<string> ParseLines(string json)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            if (document.RootElement.TryGetProperty("error_code", out JsonElement errorCode))
            {
                string message = document.RootElement.TryGetProperty("error_msg", out JsonElement error)
                    ? error.GetString() ?? ""
                    : "";
                string code = errorCode.ToString();
                throw new OcrException($"百度 OCR 错误：{code} {message}".Trim(), code);
            }

            if (!document.RootElement.TryGetProperty("words_result", out JsonElement results))
                throw new OcrException("百度 OCR 未返回文字结果。");

            return results.EnumerateArray()
                .Select(item => item.GetProperty("words").GetString() ?? "")
                .Where(line => line.Length > 0)
                .ToArray();
        }
        catch (OcrException)
        {
            throw;
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or KeyNotFoundException)
        {
            throw new OcrException("百度 OCR 返回格式异常。");
        }
    }
}
