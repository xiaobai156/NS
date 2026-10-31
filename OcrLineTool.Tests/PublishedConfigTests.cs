using System.Security.Cryptography;

namespace OcrLineTool.Tests;

/// <summary>
/// 程序运行时读的是 发布\配置文件 那一份，但它不受任何测试约束。
/// 只改了 OcrLineTool.App\*.json 却忘了同步，改动静默失效（比如 extractor 漏写会变成缺失），
/// 所以这里按哈希逐个比对，不同就红。
/// </summary>
public sealed class PublishedConfigTests
{
    [Fact]
    public void PublishedConfigurationMatchesTheSourceFiles()
    {
        string? root = RepositoryRoot();
        if (root is null)
            return; // 不是源码树（例如只有测试输出的机器），没有可比对的发布副本。
        string source = Path.Combine(root, "OcrLineTool.App");
        string published = Path.Combine(root, "发布", "配置文件");
        if (!Directory.Exists(published))
            return; // 还没发布过。

        var compared = new List<string>();
        var stale = new List<string>();
        foreach (string path in Directory.GetFiles(source, "*.json"))
        {
            string name = Path.GetFileName(path);
            string copy = Path.Combine(published, name);
            if (!File.Exists(copy))
                continue;
            compared.Add(name);
            if (!SHA256.HashData(File.ReadAllBytes(path)).SequenceEqual(SHA256.HashData(File.ReadAllBytes(copy))))
                stale.Add(name);
        }

        Assert.NotEmpty(compared);
        Assert.True(stale.Count == 0,
            "发布\\配置文件 与源码不一致，请先同步：" + string.Join("、", stale.OrderBy(name => name, StringComparer.Ordinal)));
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
