using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace OcrLineTool;

internal static class OcrLayoutMarkers
{
    internal const string RegionBoundary = "\u001e";

    // 页脚/论坛铬（编辑时间戳、最后修改、[日志] 标记）不是资料行：字段不能跨过
    // 它继续向后合并，否则时间戳里的数字会被当成同一期的第二个值。
    // 恩平杀一尾 曾因为把 "2026-09-17 09:00:00编辑本帖" 并进 260 期候选，判成同期冲突。
    private static readonly System.Text.RegularExpressions.Regex PageChrome = new(
        @"(编辑本帖|最后修改|\[日志\]|\d{4}\s*[-/—]\s*\d{1,2}\s*[-/—]\s*\d{1,2})",
        System.Text.RegularExpressions.RegexOptions.Compiled |
        System.Text.RegularExpressions.RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(5));

    internal static bool IsPageChrome(string line)
    {
        try
        {
            return PageChrome.IsMatch(line);
        }
        catch (System.Text.RegularExpressions.RegexMatchTimeoutException)
        {
            // 超时按“不是页脚”处理：判错页脚只影响行合并，不能中断整轮识别。
            return false;
        }
    }

    internal static bool IsBoundary(string line) =>
        line == RegionBoundary || IsPageChrome(line);
}

public sealed class OcrException(string message, string? code = null) : Exception(message)
{
    public string? Code { get; } = code;
}

public interface IOcrClient
{
    Task<IReadOnlyList<string>> RecognizeAsync(string imagePath, CancellationToken cancellationToken = default);

    async Task<OcrEvidence> RecognizeEvidenceAsync(
        string imagePath,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<string> lines = await RecognizeAsync(imagePath, cancellationToken);
        return OcrEvidence.FromLines(imagePath, lines, "legacy");
    }
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

        public Task<OcrEvidence> RecognizeEvidenceAsync(string imagePath, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            client ??= Create(string.IsNullOrWhiteSpace(descriptor.Id) ? CredentialSchedule.Resolve(descriptor) : descriptor);
            return client.RecognizeEvidenceAsync(imagePath, cancellationToken);
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

    // 权限/欠费/无效凭据/无额度：可以换下一个账号重试（限流除外）。
    public static bool IsCredentialProblem(OcrProvider provider, OcrException exception)
    {
        string? code = exception.Code;
        if (string.IsNullOrWhiteSpace(code) || IsRateLimit(provider, exception))
            return false;
        if (provider == OcrProvider.Baidu)
            return code is "6" or "14" or "17" or "19" or "110" or "111";
        return code.Contains("AuthFailure", StringComparison.Ordinal)
            || code.Contains("Unauthorized", StringComparison.Ordinal)
            || code.Contains("NoPermission", StringComparison.Ordinal)
            || code.Contains("Arrears", StringComparison.Ordinal)
            || code.Contains("LimitExceeded", StringComparison.Ordinal)
            || code.Contains("ResourcesSoldOut", StringComparison.Ordinal)
            || code.Contains("ResourceUnavailable", StringComparison.Ordinal);
    }
}

internal static class OcrHttp
{
    internal static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(90) };

    /// <summary>
    /// 读取待送云图片。<paramref name="maxBytes"/> 默认按原图字节数校验；腾讯接口限制的是
    /// base64 编码后的长度，此时传 <paramref name="maxBytesApplyToBase64"/> = true。
    /// </summary>
    internal static async Task<byte[]> ReadImageAsync(
        string path, long maxBytes, CancellationToken cancellationToken, bool maxBytesApplyToBase64 = false)
    {
        if (!File.Exists(path))
            throw new OcrException("图片不存在。");

        string extension = Path.GetExtension(path).ToLowerInvariant();
        if (extension is not (".jpg" or ".jpeg" or ".png" or ".bmp"))
            throw new OcrException("仅支持 JPG、JPEG、PNG、BMP 图片。");

        // 上限口径随模式变化：base64 模式按接口的编码后长度算，普通模式按原图字节数算。
        string limitText = maxBytesApplyToBase64
            ? $"{maxBytes / 1_000_000} MB（base64 编码后）"
            : $"{maxBytes / 1024 / 1024} MB";

        var info = new FileInfo(path);
        if (info.Length == 0)
            throw new OcrException($"图片大小必须在 1 字节到 {limitText} 之间。");

        if (!maxBytesApplyToBase64)
        {
            if (info.Length > maxBytes)
                throw new OcrException($"图片大小必须在 1 字节到 {limitText} 之间。");
            return await File.ReadAllBytesAsync(path, cancellationToken);
        }

        // base64 后长度 = 4 × ⌈n/3⌉：先按上限反推原图字节数快速拒绝，读入后再用同一公式
        // 复核。只校验原图字节会让 5.3～7.5MB 的图在本地通过、到云端必然报错。
        if (info.Length > maxBytes / 4 * 3)
            throw new OcrException($"图片超过 {limitText} 上限，请压缩后再识别。");
        byte[] bytes = await File.ReadAllBytesAsync(path, cancellationToken);
        if ((long)4 * ((bytes.LongLength + 2) / 3) > maxBytes)
            throw new OcrException($"图片超过 {limitText} 上限，请压缩后再识别。");
        return bytes;
    }

    internal static void EnsureImageUnchanged(string path, byte[] sentBytes)
    {
        try
        {
            byte[] current = File.ReadAllBytes(path);
            if (!current.AsSpan().SequenceEqual(sentBytes))
                throw new OcrException("图片在云 OCR 请求期间发生变化，请重新识别。", "OCR_IMAGE_CHANGED");
        }
        catch (OcrException) { throw; }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new OcrException("图片在云 OCR 请求期间不可用，请重新识别。", "OCR_IMAGE_CHANGED");
        }
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
        CancellationToken cancellationToken = default) =>
        (await RecognizeEvidenceAsync(imagePath, cancellationToken)).Lines;

    public async Task<OcrEvidence> RecognizeEvidenceAsync(
        string imagePath,
        CancellationToken cancellationToken = default)
    {
        byte[] bytes = await OcrHttp.ReadImageAsync(
            imagePath, 7_000_000, cancellationToken, maxBytesApplyToBase64: true);
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
            IReadOnlyList<OcrLineEvidence> items = ParseEvidenceItems(json, out var tokens);
            OcrHttp.EnsureImageUnchanged(imagePath, bytes);
            return OcrEvidence.FromCapturedBytes(imagePath, bytes, "tencent", items, tokens);
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
        IReadOnlyList<OcrLineEvidence> items = ParseEvidenceItems(json);
        return items.All(item => item.Box is null)
            ? items.Select(item => item.Text).ToArray()
            : OcrEvidence.SafeLines(items);
    }

    internal static IReadOnlyList<OcrLineEvidence> ParseEvidenceItems(string json) => ParseEvidenceItems(json, out _);

    internal static IReadOnlyList<OcrLineEvidence> ParseEvidenceItems(string json, out IReadOnlyList<OcrLineEvidence> tokens)
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

            OcrLineEvidence[] pieces = detections.EnumerateArray()
                .Select((item, index) => ParseEvidencePiece(item, index))
                .Where(piece => piece is not null)
                .Select(piece => piece!)
                .ToArray();
            tokens = pieces;
            if (pieces.Length == 0)
                return [];
            return OcrEvidenceLayout.Partition(pieces);
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

    private static OcrLineEvidence? ParseEvidencePiece(JsonElement item, int index)
    {
        string text = item.GetProperty("DetectedText").GetString() ?? "";
        if (text.Length == 0)
            return null;
        double? confidence = item.TryGetProperty("Confidence", out JsonElement confidenceElement)
            && confidenceElement.TryGetDouble(out double parsedConfidence)
            ? parsedConfidence
            : null;
        // 坐标字段按“能取到才算”处理：缺字段不让整行抛异常，只降级为无坐标行。
        // X/Y/Height 缺失时无法定位，整行丢框；Width 保持原有“可缺省默认 0”语义，
        // 不让一个非必需的宽度字段把整行坐标一起丢掉。
        if (!item.TryGetProperty("ItemPolygon", out JsonElement polygon)
            || polygon.ValueKind != JsonValueKind.Object
            || !TryGetPolygonInt(polygon, "X", out int x)
            || !TryGetPolygonInt(polygon, "Y", out int y)
            || !TryGetPolygonInt(polygon, "Height", out int height))
            return new(text, null, confidence, "tencent", "main");
        TryGetPolygonInt(polygon, "Width", out int width);
        return new(text, new OcrBox(x, y, width, height), confidence, "tencent", "main");
    }

    private static bool TryGetPolygonInt(JsonElement polygon, string name, out int value)
    {
        value = 0;
        return polygon.TryGetProperty(name, out JsonElement element)
            && element.ValueKind == JsonValueKind.Number
            && element.TryGetInt32(out value);
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
        CancellationToken cancellationToken = default) =>
        (await RecognizeEvidenceAsync(imagePath, cancellationToken)).Lines;

    public async Task<OcrEvidence> RecognizeEvidenceAsync(
        string imagePath,
        CancellationToken cancellationToken = default)
    {
        byte[] bytes = await OcrHttp.ReadImageAsync(imagePath, 2_500_000, cancellationToken);
        string token = await GetAccessTokenAsync(cancellationToken);
        try
        {
            return await RecognizeWithTokenAsync(imagePath, bytes, token, cancellationToken);
        }
        catch (OcrException exception) when (IsExpiredToken(exception) && !cancellationToken.IsCancellationRequested)
        {
            // 令牌被服务端提前作废（110/111）时缓存里那份仍然“没过期”，不主动清空就会
            // 让整轮所有请求持续失败。清掉后只重取一次，避免无限重试。
            accessToken = null;
            accessTokenExpiresAt = DateTimeOffset.MinValue;
            string refreshed = await GetAccessTokenAsync(cancellationToken);
            return await RecognizeWithTokenAsync(imagePath, bytes, refreshed, cancellationToken);
        }
    }

    // 百度 access_token 无效/过期的错误码。
    private static bool IsExpiredToken(OcrException exception) =>
        exception.Code is "110" or "111";

    private async Task<OcrEvidence> RecognizeWithTokenAsync(
        string imagePath, byte[] bytes, string token, CancellationToken cancellationToken)
    {
        using var content = new FormUrlEncodedContent(
        [
            new("image", Convert.ToBase64String(bytes)),
            new("language_type", "CHN_ENG"),
            new("detect_direction", "true"),
            new("probability", "true")
        ]);

        try
        {
            string url = "https://aip.baidubce.com/rest/2.0/ocr/v1/accurate?access_token=" + Uri.EscapeDataString(token);
            using HttpResponseMessage response = await httpClient.PostAsync(url, content, cancellationToken);
            string json = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
                throw new OcrException($"百度 OCR 请求失败（HTTP {(int)response.StatusCode}）。");
            IReadOnlyList<OcrLineEvidence> items = ParseEvidenceItems(json, out var tokens);
            OcrHttp.EnsureImageUnchanged(imagePath, bytes);
            return OcrEvidence.FromCapturedBytes(imagePath, bytes, "baidu", items, tokens);
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
            // expires_in 理论上一定是数字，但服务端异常响应可能是字符串或缺失；
            // 非数字时退回默认有效期，不能让 InvalidOperationException 逃出 OcrException 包装。
            int expiresIn = document.RootElement.TryGetProperty("expires_in", out JsonElement expires)
                && expires.ValueKind == JsonValueKind.Number
                && expires.TryGetInt32(out int parsedExpires)
                ? parsedExpires
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
        IReadOnlyList<OcrLineEvidence> items = ParseEvidenceItems(json);
        return items.All(item => item.Box is null)
            ? items.Select(item => item.Text).ToArray()
            : OcrEvidence.SafeLines(items);
    }

    internal static IReadOnlyList<OcrLineEvidence> ParseEvidenceItems(string json) => ParseEvidenceItems(json, out _);

    internal static IReadOnlyList<OcrLineEvidence> ParseEvidenceItems(string json, out IReadOnlyList<OcrLineEvidence> tokens)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            if (document.RootElement.TryGetProperty("error_code", out JsonElement errorCodeElement)
                && errorCodeElement.TryGetInt32(out int errorCode))
            {
                string message = document.RootElement.TryGetProperty("error_msg", out JsonElement error)
                    ? error.GetString() ?? ""
                    : "";
                string code = errorCode.ToString();
                throw new OcrException($"百度 OCR 错误：{code} {message}".Trim(), code);
            }

            if (!document.RootElement.TryGetProperty("words_result", out JsonElement results))
                throw new OcrException("百度 OCR 未返回文字结果。");

            var items = new List<OcrLineEvidence>();
            int index = 0;
            foreach (JsonElement item in results.EnumerateArray())
            {
                string text = item.GetProperty("words").GetString() ?? "";
                if (text.Length == 0) { index++; continue; }
                OcrBox? box = null;
                // 与腾讯侧同口径：坐标四个字段逐个“能取到才算”，单条结果字段缺失或类型
                // 异常只让这一行丢掉坐标，不能让整张图判成“返回格式异常”。
                if (item.TryGetProperty("location", out JsonElement location)
                    && location.ValueKind == JsonValueKind.Object
                    && TryGetBoxField(location, "left", out int left)
                    && TryGetBoxField(location, "top", out int top)
                    && TryGetBoxField(location, "width", out int width)
                    && TryGetBoxField(location, "height", out int height))
                    box = new(left, top, width, height);
                double? confidence = null;
                if (item.TryGetProperty("probability", out JsonElement probability)
                    && probability.ValueKind == JsonValueKind.Object
                    && probability.TryGetProperty("average", out JsonElement average)
                    && average.TryGetDouble(out double parsed))
                    confidence = parsed;
                items.Add(new(text, box, confidence, "baidu", "main"));
                index++;
            }
            tokens = items.ToArray();
            return OcrEvidenceLayout.Partition(items);
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

    private static bool TryGetBoxField(JsonElement location, string name, out int value)
    {
        value = 0;
        return location.TryGetProperty(name, out JsonElement element)
            && element.ValueKind == JsonValueKind.Number
            && element.TryGetInt32(out value);
    }

}
