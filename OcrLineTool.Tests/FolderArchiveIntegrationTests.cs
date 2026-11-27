using System.Text;
using OcrLineTool;

namespace OcrLineTool.Tests;

// 目录封存和已有功能的接缝：自动 OCR 名单、群结果/缺失汇总、手点被封存的子目录。
public sealed class FolderArchiveIntegrationTests
{
    private static string NewRoot() =>
        Path.Combine(Path.GetTempPath(), "folder-archive-integration-" + Guid.NewGuid().ToString("N"));

    private static string ImageRoot(string root) => Path.Combine(root, "结果");

    private static void Catalog(string root, string group, string rulesJson)
    {
        string config = ResultFilePaths.ConfigurationDirectory(root);
        Directory.CreateDirectory(config);
        File.WriteAllText(Path.Combine(config, group + ".json"),
            $"{{\"group\":\"{group}\",\"rules\":[{rulesJson}]}}", new UTF8Encoding(false));
    }

    private static void Sealed(string root, string group, string folder)
    {
        var archive = new FolderArchive();
        archive.Entries[FolderArchive.KeyFor(group, folder)] = new FolderArchive.Entry
        {
            LastImageDate = "2026-11-24",
            IdleDays = FolderArchive.IdleDaysBeforeSealing,
            Sealed = true,
            SealedDate = "2026-11-27"
        };
        archive.Save(root);
    }

    private static void Image(string folder, string name = "80641")
    {
        Directory.CreateDirectory(folder);
        File.WriteAllBytes(Path.Combine(folder, $"20261127_172056_a926b173_{name}.jpg"),
            [0xFF, 0xD8, 0xFF, 0xE0]);
    }

    [Fact]
    public void GroupWhoseEnabledRulesAllLiveInSealedFoldersIsNotOfferedToTheAutomaticRecognizer()
    {
        string root = NewRoot();
        try
        {
            Catalog(root, "蜻蜓一套",
                "{\"keyword\":\"神算双波\",\"label\":\"神算子双波\",\"type\":\"波\",\"folder\":\"神算子\"}");
            string folder = Path.Combine(ImageRoot(root), "11.27-蜻蜓一套");
            Image(Path.Combine(folder, "神算子"));
            Sealed(root, "蜻蜓一套", "神算子");
            var autoSettings = new AutoLocalOcrSettings();
            autoSettings.SetSelectedGroup("蜻蜓一套", true);

            // 唯一要识别的条目在被封存的目录里：这一轮没有活可干，跳过而不是空跑一轮。
            Assert.Empty(MainForm.FindVisibleAutomaticGroups(root, ImageRoot(root), autoSettings));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public void GroupWithRulesOutsideTheSealedFolderIsStillRecognized()
    {
        string root = NewRoot();
        try
        {
            Catalog(root, "蜻蜓一套",
                "{\"keyword\":\"神算双波\",\"label\":\"神算子双波\",\"type\":\"波\",\"folder\":\"神算子\"},"
                + "{\"keyword\":\"黑字杀头\",\"type\":\"头\",\"folder\":\"各种杀\"}");
            string folder = Path.Combine(ImageRoot(root), "11.27-蜻蜓一套");
            Image(Path.Combine(folder, "神算子"));
            Image(Path.Combine(folder, "各种杀"), "80642");
            Sealed(root, "蜻蜓一套", "神算子");
            var autoSettings = new AutoLocalOcrSettings();
            autoSettings.SetSelectedGroup("蜻蜓一套", true);

            Assert.Single(MainForm.FindVisibleAutomaticGroups(root, ImageRoot(root), autoSettings));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task MissingLinesFromASealedFolderAreNotReportedInTheSummary()
    {
        string root = NewRoot();
        try
        {
            Catalog(root, "蜻蜓一套",
                "{\"keyword\":\"神算双波\",\"label\":\"神算子双波\",\"type\":\"波\",\"folder\":\"神算子\"},"
                + "{\"keyword\":\"黑字杀头\",\"type\":\"头\",\"folder\":\"各种杀\"}");
            string folder = Path.Combine(ImageRoot(root), "11.27-蜻蜓一套");
            Image(Path.Combine(folder, "神算子"));
            Sealed(root, "蜻蜓一套", "神算子");
            Directory.CreateDirectory(ResultFilePaths.GroupResultsDirectory(root));
            File.WriteAllLines(
                Path.Combine(ResultFilePaths.GroupResultsDirectory(root), "蜻蜓一套_329期.txt"),
                ["【波】", "缺失（未找到对应图片） 神算子双波", "【头】", "3头 黑字杀头"],
                new UTF8Encoding(true));

            var result = await MissingResultSummary.WriteAsync(root, ImageRoot(root));

            // 封存目录的条目等同于关掉：既不出现在的缺失清单里，也不列出来。
            Assert.Equal(0, result.Count);
            Assert.Equal(["没有缺失的数据。"], await File.ReadAllLinesAsync(result.Path));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task MissingLinesFromFoldersThatAreNotSealedStayInTheSummary()
    {
        string root = NewRoot();
        try
        {
            Catalog(root, "蜻蜓一套",
                "{\"keyword\":\"神算双波\",\"label\":\"神算子双波\",\"type\":\"波\",\"folder\":\"神算子\"},"
                + "{\"keyword\":\"黑字杀头\",\"type\":\"头\",\"folder\":\"各种杀\"}");
            string folder = Path.Combine(ImageRoot(root), "11.27-蜻蜓一套");
            Image(Path.Combine(folder, "神算子"));
            Sealed(root, "蜻蜓一套", "神算子");
            Directory.CreateDirectory(ResultFilePaths.GroupResultsDirectory(root));
            File.WriteAllLines(
                Path.Combine(ResultFilePaths.GroupResultsDirectory(root), "蜻蜓一套_329期.txt"),
                ["【头】", "缺失（未找到对应图片） 黑字杀头"], new UTF8Encoding(true));

            var result = await MissingResultSummary.WriteAsync(root, ImageRoot(root));

            Assert.Equal(1, result.Count);
            Assert.Contains("缺失（未找到对应图片） 黑字杀头",
                await File.ReadAllTextAsync(result.Path));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public void SelectingASealedSubfolderIsRecognisedAsSealedButTheGroupFolderIsNot()
    {
        string root = NewRoot();
        try
        {
            Sealed(root, "蜻蜓一套", "神算子");
            FolderArchive archive = FolderArchive.Load(root);
            string group = Path.Combine(ImageRoot(root), "11.27-蜻蜓一套");

            Assert.True(archive.IsSelectedFolderSealed("蜻蜓一套", Path.Combine(group, "神算子")));
            Assert.False(archive.IsSelectedFolderSealed("蜻蜓一套", Path.Combine(group, "各种杀")));
            // 点群目录本身（不是任何被封存的子目录）照旧可以识别。
            Assert.False(archive.IsSelectedFolderSealed("蜻蜓一套", group));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public void ManuallyArchivingIsNotOfferedSoOnlyANewImageReleasesTheFolder()
    {
        string root = NewRoot();
        try
        {
            Sealed(root, "蜻蜓一套", "神算子");
            FolderArchive archive = FolderArchive.Load(root);

            // 状态只从文件读、手动识别和自动识别都不写它：没有任何“解封”入口。
            Assert.True(archive.IsSealed("蜻蜓一套", "神算子"));
            Assert.Equal(1, archive.SealedCount("蜻蜓一套"));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
