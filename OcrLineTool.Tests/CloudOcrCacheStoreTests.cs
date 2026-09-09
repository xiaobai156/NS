using OcrLineTool;

namespace OcrLineTool.Tests;

public sealed class CloudOcrCacheStoreTests
{
    [Fact]
    public void KeepsEachGroupInItsOwnCacheAndFiltersByIssue()
    {
        string root = Path.Combine(Path.GetTempPath(), "OcrLineTool-cache-" + Guid.NewGuid().ToString("N"));
        try
        {
            CloudOcrCacheStore.SaveEntry(root, "嫣然心水", 243, "C:\\图片\\a.jpg", ["243期杀虎"]);
            CloudOcrCacheStore.SaveEntry(root, "新澳高级会员", 243, "C:\\图片\\a.jpg", ["243期杀龙"]);
            CloudOcrCacheStore.SaveEntry(root, "嫣然心水", 242, "C:\\图片\\b.jpg", ["242期杀牛"]);

            var yanran = CloudOcrCacheStore.Load(root, "嫣然心水", 243);
            var member = CloudOcrCacheStore.Load(root, "新澳高级会员", 243);

            Assert.Equal(["243期杀虎"], yanran["C:\\图片\\a.jpg"]);
            Assert.DoesNotContain("C:\\图片\\b.jpg", yanran.Keys);
            Assert.Equal(["243期杀龙"], member["C:\\图片\\a.jpg"]);
            Assert.NotEqual(
                ResultFilePaths.ForCloudOcrCache(root, "嫣然心水"),
                ResultFilePaths.ForCloudOcrCache(root, "新澳高级会员"));
            Assert.StartsWith(
                ResultFilePaths.ConfigurationDirectory(root),
                ResultFilePaths.ForCloudOcrCache(root, "嫣然心水"),
                StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }
}
