from pathlib import Path

files = {
    name: Path(name).read_text(encoding='utf-8')
    for name in [
        'OcrLineTool.App/RuleEngine.cs',
        'OcrLineTool.App/OcrClients.cs',
        'OcrLineTool.App/CloudImageDeduplicator.cs',
    ]
}

def repl(name: str, old: str, new: str) -> None:
    text = files[name]
    count = text.count(old)
    if count != 1:
        raise RuntimeError(f'{name}: expected one anchor, found {count}: {old[:160]!r}')
    files[name] = text.replace(old, new, 1)

# RuleEngine: business extraction can consume provenance-rich OCR evidence.
name = 'OcrLineTool.App/RuleEngine.cs'
repl(name,
'''    public static string? ExtractFinalValue(IEnumerable<string> cloudLines, int issue, OcrRule rule) =>
        ExtractValueCore(RejectCrossIssueNearbyValue(cloudLines, issue, rule), issue, rule, requireCloudKeyword: false);
''',
'''    public static string? ExtractFinalValue(IEnumerable<string> cloudLines, int issue, OcrRule rule) =>
        ExtractValueCore(RejectCrossIssueNearbyValue(cloudLines, issue, rule), issue, rule, requireCloudKeyword: false);

    public static string? ExtractFinalValue(OcrEvidence evidence, int issue, OcrRule rule)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        string? value = ExtractFinalValue(evidence.Lines, issue, rule);
        if (value is null)
            return null;

        OcrLineEvidence[] nonEmpty = evidence.Items
            .Where(item => !string.IsNullOrWhiteSpace(item.Text))
            .ToArray();
        if (nonEmpty.Length == 0)
            return null;

        // Positioned evidence has already been partitioned into physical
        // regions/columns. For an engine that cannot provide geometry, never
        // accept a value that only becomes valid after joining multiple opaque
        // lines: one returned physical OCR item must independently prove it.
        if (evidence.HasCompleteGeometry)
            return value;
        return nonEmpty.Any(item =>
            string.Equals(ExtractFinalValue(new[] { item.Text }, issue, rule), value, StringComparison.Ordinal))
            ? value
            : null;
    }
''')

# IOcrClient remains source-compatible with test fakes, but real clients can
# preserve structured evidence instead of being flattened to strings.
name = 'OcrLineTool.App/OcrClients.cs'
repl(name,
'''public interface IOcrClient
{
    Task<IReadOnlyList<string>> RecognizeAsync(string imagePath, CancellationToken cancellationToken = default);
}
''',
'''public interface IOcrClient
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
''')
repl(name,
'''        public Task<IReadOnlyList<string>> RecognizeAsync(string imagePath, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            client ??= Create(string.IsNullOrWhiteSpace(descriptor.Id) ? CredentialSchedule.Resolve(descriptor) : descriptor);
            return client.RecognizeAsync(imagePath, cancellationToken);
        }
''',
'''        public Task<IReadOnlyList<string>> RecognizeAsync(string imagePath, CancellationToken cancellationToken = default)
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
''')

# Tencent request returns the exact bytes it sent plus positioned/confidence evidence.
old_tencent = '''    public async Task<IReadOnlyList<string>> RecognizeAsync(
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
            IReadOnlyList<string> lines = ParseLines(json);
            OcrHttp.EnsureImageUnchanged(imagePath, bytes);
            return lines;
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
'''
new_tencent = '''    public async Task<IReadOnlyList<string>> RecognizeAsync(
        string imagePath,
        CancellationToken cancellationToken = default) =>
        (await RecognizeEvidenceAsync(imagePath, cancellationToken)).Lines;

    public async Task<OcrEvidence> RecognizeEvidenceAsync(
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
            IReadOnlyList<OcrLineEvidence> items = ParseEvidenceItems(json);
            OcrHttp.EnsureImageUnchanged(imagePath, bytes);
            return OcrEvidence.FromCapturedBytes(imagePath, bytes, "tencent", items);
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
'''
repl(name, old_tencent, new_tencent)

# Replace Tencent's parser/layout block through the class boundary.
start = files[name].index('    public static IReadOnlyList<string> ParseLines(string json)\n    {')
end = files[name].index('\n}\n\npublic sealed class BaiduOcrClient', start)
tencent_parser = r'''    public static IReadOnlyList<string> ParseLines(string json) =>
        OcrEvidence.SafeLines(ParseEvidenceItems(json));

    internal static IReadOnlyList<OcrLineEvidence> ParseEvidenceItems(string json)
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
        if (!item.TryGetProperty("ItemPolygon", out JsonElement polygon))
            return new(text, null, confidence, "tencent", $"unpositioned-{index}");
        int x = polygon.GetProperty("X").GetInt32();
        int y = polygon.GetProperty("Y").GetInt32();
        int width = polygon.TryGetProperty("Width", out JsonElement widthElement) ? widthElement.GetInt32() : 0;
        int height = polygon.GetProperty("Height").GetInt32();
        return new(text, new OcrBox(x, y, width, height), confidence, "tencent", "main");
    }
'''
files[name] = files[name][:start] + tencent_parser + files[name][end:]

# Baidu request returns evidence too; location/probability are consumed when
# the service includes them, otherwise every opaque line is isolated.
old_baidu = '''    public async Task<IReadOnlyList<string>> RecognizeAsync(
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
            IReadOnlyList<string> lines = ParseLines(json);
            OcrHttp.EnsureImageUnchanged(imagePath, bytes);
            return lines;
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
'''
new_baidu = '''    public async Task<IReadOnlyList<string>> RecognizeAsync(
        string imagePath,
        CancellationToken cancellationToken = default) =>
        (await RecognizeEvidenceAsync(imagePath, cancellationToken)).Lines;

    public async Task<OcrEvidence> RecognizeEvidenceAsync(
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
            IReadOnlyList<OcrLineEvidence> items = ParseEvidenceItems(json);
            if (!response.IsSuccessStatusCode)
                throw new OcrException($"百度 OCR 请求失败（HTTP {(int)response.StatusCode}）。");
            OcrHttp.EnsureImageUnchanged(imagePath, bytes);
            return OcrEvidence.FromCapturedBytes(imagePath, bytes, "baidu", items);
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
'''
repl(name, old_baidu, new_baidu)

# Replace Baidu ParseLines through class closing.
start = files[name].index('    public static IReadOnlyList<string> ParseLines(string json)\n    {', files[name].index('public sealed class BaiduOcrClient'))
end = len(files[name].rstrip()) - 1  # final class brace
baidu_parser = r'''    public static IReadOnlyList<string> ParseLines(string json) =>
        OcrEvidence.SafeLines(ParseEvidenceItems(json));

    internal static IReadOnlyList<OcrLineEvidence> ParseEvidenceItems(string json)
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
                if (item.TryGetProperty("location", out JsonElement location)
                    && location.ValueKind == JsonValueKind.Object
                    && location.TryGetProperty("left", out JsonElement left)
                    && location.TryGetProperty("top", out JsonElement top)
                    && location.TryGetProperty("width", out JsonElement width)
                    && location.TryGetProperty("height", out JsonElement height))
                    box = new(left.GetInt32(), top.GetInt32(), width.GetInt32(), height.GetInt32());
                double? confidence = null;
                if (item.TryGetProperty("probability", out JsonElement probability)
                    && probability.ValueKind == JsonValueKind.Object
                    && probability.TryGetProperty("average", out JsonElement average)
                    && average.TryGetDouble(out double parsed))
                    confidence = parsed;
                items.Add(new(text, box, confidence, "baidu", box is null ? $"unpositioned-{index}" : "main"));
                index++;
            }
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
'''
files[name] = files[name][:start] + baidu_parser + '\n}' + '\n'

# Cloud deduplicator keeps structured provenance when using evidence-aware calls.
name = 'OcrLineTool.App/CloudImageDeduplicator.cs'
repl(name,
'''    private readonly Dictionary<string, IReadOnlyList<string>> responses =
        new(StringComparer.Ordinal);
''',
'''    private readonly Dictionary<string, IReadOnlyList<string>> responses =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, OcrEvidence> evidenceResponses =
        new(StringComparer.Ordinal);
''')
repl(name,
'''    private static bool ShouldDeduplicate(string groupDirectory, IReadOnlyList<OcrRule> rules) =>
''',
'''    public async Task<OcrEvidence> RecognizeEvidenceAsync(
        string groupDirectory,
        string originalImagePath,
        IReadOnlyList<OcrRule> rules,
        Func<Task<OcrEvidence>> request)
    {
        if (!ShouldDeduplicate(groupDirectory, rules))
            return await request();

        string? fingerprint = TryFingerprint(originalImagePath);
        if (fingerprint is null)
            return await request();
        if (evidenceResponses.TryGetValue(fingerprint, out OcrEvidence? cached))
            return cached;

        OcrEvidence evidence = await request();
        evidenceResponses[fingerprint] = evidence;
        return evidence;
    }

    private static bool ShouldDeduplicate(string groupDirectory, IReadOnlyList<OcrRule> rules) =>
''')

for name, text in files.items():
    Path(name).write_text(text, encoding='utf-8')
print('Applied F18 structured cloud evidence patch')
