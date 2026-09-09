using OcrLineTool;

namespace OcrLineTool.Tests;

public sealed class OcrSecretsLoaderTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), $"ocr-secrets-{Guid.NewGuid():N}");
    private readonly string path;

    public OcrSecretsLoaderTests()
    {
        Directory.CreateDirectory(root);
        path = Path.Combine(root, "secrets.json");
    }

    [Fact]
    public void LoadsProviderSpecificFields()
    {
        File.WriteAllText(path, ValidJson);

        OcrSecrets secrets = OcrSecretsLoader.Load(path);

        Assert.Equal("tencent-a-id", secrets.Get(OcrProvider.Tencent, "A").Id);
        Assert.Equal("tencent-a-key", secrets.Get(OcrProvider.Tencent, "A").Secret);
        Assert.Equal("baidu-d-key", secrets.Get(OcrProvider.Baidu, "D").Id);
        Assert.Equal("baidu-d-secret", secrets.Get(OcrProvider.Baidu, "D").Secret);
    }

    [Fact]
    public void UsesTheFixedProductionPathByDefault()
    {
        Assert.Equal(
            @"C:\Users\Administrator\Desktop\每天工具\ocrKey\secrets.json",
            OcrSecretsLoader.DefaultPath);
    }

    [Fact]
    public void MissingFileFailsWithoutSecretContent()
    {
        OcrException exception = Assert.Throws<OcrException>(() => OcrSecretsLoader.Load(path));

        Assert.Equal("OCR_SECRETS_MISSING", exception.Code);
        Assert.Contains("未找到 OCR 密钥配置文件", exception.Message);
        Assert.DoesNotContain("tencent-a-key", exception.Message);
    }

    [Fact]
    public void InvalidJsonFailsWithoutEchoingTheDocument()
    {
        File.WriteAllText(path, "{ invalid");

        OcrException exception = Assert.Throws<OcrException>(() => OcrSecretsLoader.Load(path));

        Assert.Equal("OCR_SECRETS_INVALID", exception.Code);
        Assert.Contains("OCR 密钥配置文件格式无效", exception.Message);
        Assert.DoesNotContain("tencent-a-key", exception.Message);
    }

    [Fact]
    public void MissingFieldNamesTheProviderAndFieldOnly()
    {
        File.WriteAllText(path, ValidJson.Replace("\"secretKey\": \"tencent-a-key\"", "\"secretKey\": \"\""));

        OcrException exception = Assert.Throws<OcrException>(() => OcrSecretsLoader.Load(path));

        Assert.Equal("OCR_SECRETS_MISSING_FIELD", exception.Code);
        Assert.Contains("腾讯 OCR 账号 A 缺少 secretKey 配置", exception.Message);
        Assert.DoesNotContain("tencent-a-key", exception.Message);
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
            Directory.Delete(root, recursive: true);
    }

    private const string ValidJson = """
    {
      "tencent": {
        "A": { "secretId": "tencent-a-id", "secretKey": "tencent-a-key" },
        "B": { "secretId": "tencent-b-id", "secretKey": "tencent-b-key" },
        "C": { "secretId": "tencent-c-id", "secretKey": "tencent-c-key" },
        "D": { "secretId": "tencent-d-id", "secretKey": "tencent-d-key" }
      },
      "baidu": {
        "A": { "apiKey": "baidu-a-key", "secretKey": "baidu-a-secret" },
        "B": { "apiKey": "baidu-b-key", "secretKey": "baidu-b-secret" },
        "C": { "apiKey": "baidu-c-key", "secretKey": "baidu-c-secret" },
        "D": { "apiKey": "baidu-d-key", "secretKey": "baidu-d-secret" }
      }
    }
    """;
}
