using System.Security.Cryptography;

namespace OcrLineTool.Tests;

/// <summary>
/// 构建配置始终与源码比对；实际部署只在获得发布授权后显式验收。
/// </summary>
public sealed class PublishedConfigTests
{
    [Fact]
    public void BuiltConfigurationMatchesTheSourceFiles() =>
        AssertConfigurationMatchesSource(ResultFilePaths.ConfigurationDirectory(AppContext.BaseDirectory));

    [PublishedConfigFact]
    [Trait("Category", "Deployment")]
    public void PublishedConfigurationMatchesTheSourceFiles() =>
        AssertConfigurationMatchesSource(Path.Combine(Environment.GetEnvironmentVariable("OCR_PUBLISHED_CONFIG_ROOT")!, "配置文件"));

    private static void AssertConfigurationMatchesSource(string configuration)
    {
        string root = Assert.IsType<string>(RepositoryRoot());
        string source = Path.Combine(root, "OcrLineTool.App");
        Assert.True(Directory.Exists(configuration), "配置目录不存在：" + configuration);
        var stale = new List<string>();
        string[] files = Directory.GetFiles(source, "*.json");
        Assert.NotEmpty(files);
        foreach (string path in files)
        {
            string name = Path.GetFileName(path);
            string copy = Path.Combine(configuration, name);
            if (!File.Exists(copy) || !SHA256.HashData(File.ReadAllBytes(path)).SequenceEqual(SHA256.HashData(File.ReadAllBytes(copy))))
                stale.Add(name);
        }
        Assert.True(stale.Count == 0,
            "配置缺失或与源码不一致：" + string.Join("、", stale.OrderBy(name => name, StringComparer.Ordinal)));
    }

    private static string? RepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "OcrLineTool.App")))
                return directory.FullName;
            directory = directory.Parent;
        }
        return null;
    }
}

public sealed class PublishedConfigFactAttribute : FactAttribute
{
    public PublishedConfigFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OCR_PUBLISHED_CONFIG_ROOT")))
            Skip = "发布验收：将 OCR_PUBLISHED_CONFIG_ROOT 设为明确授权的发布根目录后执行。";
    }
}
