using System.Runtime.CompilerServices;
using System.Text.Json;

[assembly: InternalsVisibleTo("OcrLineTool.Tests")]

namespace OcrLineTool;

public sealed record OcrSecret(string Id, string Secret);

public sealed class OcrSecrets
{
    private readonly IReadOnlyDictionary<(OcrProvider Provider, string Slot), OcrSecret> values;

    internal OcrSecrets(IReadOnlyDictionary<(OcrProvider Provider, string Slot), OcrSecret> values) =>
        this.values = values;

    public OcrSecret Get(OcrProvider provider, string slot) =>
        values.TryGetValue((provider, slot), out OcrSecret? secret)
            ? secret
            : throw new OcrException(
                $"{ProviderName(provider)} OCR 账号 {slot} 缺少配置。",
                "OCR_SECRETS_MISSING_FIELD");

    private static string ProviderName(OcrProvider provider) =>
        provider == OcrProvider.Tencent ? "腾讯" : "百度";
}

public static class OcrSecretsLoader
{
    public const string DefaultPath =
        @"C:\Users\Administrator\Desktop\每天工具\ocrKey\secrets.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static OcrSecrets Load(string? path = null)
    {
        string resolvedPath = string.IsNullOrWhiteSpace(path) ? DefaultPath : path;
        if (!File.Exists(resolvedPath))
            throw new OcrException(
                $"未找到 OCR 密钥配置文件：{resolvedPath}",
                "OCR_SECRETS_MISSING");

        try
        {
            SecretsDocument? document = JsonSerializer.Deserialize<SecretsDocument>(
                File.ReadAllText(resolvedPath), JsonOptions);
            if (document is null)
                throw InvalidConfiguration(resolvedPath);

            var values = new Dictionary<(OcrProvider Provider, string Slot), OcrSecret>();
            foreach (string slot in Slots)
            {
                if (document.Tencent?.ContainsKey(slot) == true)
                    values[(OcrProvider.Tencent, slot)] = RequireTencent(document, slot);
                if (document.Baidu?.ContainsKey(slot) == true)
                    values[(OcrProvider.Baidu, slot)] = RequireBaidu(document, slot);
            }

            return new OcrSecrets(values);
        }
        catch (OcrException)
        {
            throw;
        }
        catch (JsonException)
        {
            throw InvalidConfiguration(resolvedPath);
        }
        catch (UnauthorizedAccessException)
        {
            throw new OcrException(
                $"无法读取 OCR 密钥配置文件：{resolvedPath}",
                "OCR_SECRETS_READ_FAILED");
        }
        catch (IOException)
        {
            throw new OcrException(
                $"无法读取 OCR 密钥配置文件：{resolvedPath}",
                "OCR_SECRETS_READ_FAILED");
        }
    }

    private static OcrSecret RequireTencent(SecretsDocument document, string slot)
    {
        if (document.Tencent is null || !document.Tencent.TryGetValue(slot, out TencentAccount? account) || account is null)
            throw MissingAccount("腾讯", slot);

        return new(
            RequireValue(account.SecretId, "腾讯", slot, "secretId"),
            RequireValue(account.SecretKey, "腾讯", slot, "secretKey"));
    }

    private static OcrSecret RequireBaidu(SecretsDocument document, string slot)
    {
        if (document.Baidu is null || !document.Baidu.TryGetValue(slot, out BaiduAccount? account) || account is null)
            throw MissingAccount("百度", slot);

        return new(
            RequireValue(account.ApiKey, "百度", slot, "apiKey"),
            RequireValue(account.SecretKey, "百度", slot, "secretKey"));
    }

    private static string RequireValue(string? value, string provider, string slot, string field) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new OcrException(
                $"{provider} OCR 账号 {slot} 缺少 {field} 配置。",
                "OCR_SECRETS_MISSING_FIELD")
            : value;

    private static OcrException MissingAccount(string provider, string slot) =>
        new($"{provider} OCR 账号 {slot} 缺少配置。", "OCR_SECRETS_MISSING_FIELD");

    private static OcrException InvalidConfiguration(string path) =>
        new($"OCR 密钥配置文件格式无效：{path}", "OCR_SECRETS_INVALID");

    private static readonly string[] Slots = ["A", "B", "C", "D"];

    private sealed class SecretsDocument
    {
        public Dictionary<string, TencentAccount>? Tencent { get; set; }
        public Dictionary<string, BaiduAccount>? Baidu { get; set; }
    }

    private sealed class TencentAccount
    {
        public string? SecretId { get; set; }
        public string? SecretKey { get; set; }
    }

    private sealed class BaiduAccount
    {
        public string? ApiKey { get; set; }
        public string? SecretKey { get; set; }
    }
}
