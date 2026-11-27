using System.Text;
using OcrLineTool;

namespace OcrLineTool.Tests;

public sealed class FolderArchiveTests
{
    private static string NewRoot() =>
        Path.Combine(Path.GetTempPath(), "folder-archive-" + Guid.NewGuid().ToString("N"));

    // 抓图根 = <root>\结果；抓图程序把状态写在 <root>\outputs\抓取状态\。
    private static string ImageRoot(string root) => Path.Combine(root, "结果");

    private static OcrRule Rule(string keyword, string type = "头", string? label = null, string? folderName = null) =>
        new(keyword, type, label, Folder: folderName);

    private static string GroupFolder(string root, string group = "嫣然心水", string date = "11.27") =>
        Path.Combine(ImageRoot(root), $"{date}-{group}");

    // 抓图出来的图片名就是 20261127_172056_源hash_消息id.ext，日期从文件名前缀取。
    private static void Image(string folder, string date, string name = "80641")
    {
        Directory.CreateDirectory(folder);
        string stamp = date.Replace("-", string.Empty, StringComparison.Ordinal);
        File.WriteAllBytes(Path.Combine(folder, $"{stamp}_172056_a926b173_{name}.jpg"),
            [0xFF, 0xD8, 0xFF, 0xE0]);
    }

    private static void Catalog(string root, string group, params OcrRule[] rules)
    {
        string config = ResultFilePaths.ConfigurationDirectory(root);
        Directory.CreateDirectory(config);
        var text = new StringBuilder();
        text.Append("{\n  \"group\": \"").Append(group).Append("\",\n  \"rules\": [\n");
        for (int index = 0; index < rules.Length; index++)
        {
            OcrRule rule = rules[index];
            text.Append("    { \"keyword\": \"").Append(rule.Keyword).Append("\", \"type\": \"")
                .Append(rule.Type).Append('"');
            if (rule.Label is not null)
                text.Append(", \"label\": \"").Append(rule.Label).Append('"');
            if (rule.Folder is not null)
                text.Append(", \"folder\": \"").Append(rule.Folder).Append('"');
            text.Append(" }").Append(index == rules.Length - 1 ? "\n" : ",\n");
        }
        text.Append("  ]\n}\n");
        File.WriteAllText(Path.Combine(config, group + ".json"), text.ToString(), new UTF8Encoding(false));
    }

    // 抓取状态里的 date 就是抓图程序当天跑过的北京日期。
    private static void CaptureStatus(string root, string group, string date)
    {
        string directory = Path.Combine(root, "outputs", "抓取状态");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, group + "抓取状态.json"),
            $"{{\n  \"group_name\": \"{group}\",\n  \"date\": \"{date}\",\n  \"remarks\": {{}}\n}}\n",
            new UTF8Encoding(false));
    }

    private static bool Run(FolderArchive archive, string root, string today, List<string>? notices = null)
    {
        Catalog(root, "嫣然心水", Rule("甲", folderName: "神算子"), Rule("乙"));
        return FolderArchive.Refresh(archive, root, ImageRoot(root),
            DateOnly.ParseExact(today, "yyyy-MM-dd", null), notices is null ? null : notices.Add);
    }

    [Fact]
    public void MissingStateFileArchivesNothing()
    {
        string root = NewRoot();
        try
        {
            FolderArchive archive = FolderArchive.Load(root);

            Assert.Equal(0, archive.SealedCount("嫣然心水"));
            Assert.Empty(archive.SealedFolders("嫣然心水"));
            Assert.False(File.Exists(FolderArchive.PathFor(root)));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public void CorruptStateFileFallsBackToNothingArchivedInsteadOfBlockingRecognition()
    {
        string root = NewRoot();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FolderArchive.PathFor(root))!);
            File.WriteAllText(FolderArchive.PathFor(root), "{ 这不是合法 JSON");
            FolderArchive archive = FolderArchive.Load(root);
            OcrRule[] rules = [Rule("甲", folderName: "神算子")];

            Assert.Equal(0, archive.SealedCount("嫣然心水"));
            Assert.Equal(rules, archive.FilterRules("嫣然心水", rules, ["神算子"]));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public void ThreeConsecutiveCaptureDaysWithoutNewImagesSealTheFolder()
    {
        string root = NewRoot();
        try
        {
            string folder = Path.Combine(GroupFolder(root), "神算子");
            Image(folder, "2026-11-24");
            var archive = new FolderArchive();
            var notices = new List<string>();

            foreach (string day in new[] { "2026-11-25", "2026-11-26", "2026-11-27" })
            {
                CaptureStatus(root, "嫣然心水", day);
                Assert.True(Run(archive, root, day, notices));
                bool sealedYet = day == "2026-11-27";
                Assert.Equal(sealedYet, archive.IsSealed("嫣然心水", "神算子"));
            }

            Assert.Equal(3, FolderArchive.IdleDaysBeforeSealing);
            Assert.Contains(notices, note => note.Contains("连续 3 天没有新图", StringComparison.Ordinal));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public void NewImageResetsTheCounterAndUnsealsTheFolder()
    {
        string root = NewRoot();
        try
        {
            string folder = Path.Combine(GroupFolder(root), "神算子");
            Image(folder, "2026-11-24");
            var archive = new FolderArchive();
            for (int day = 25; day <= 27; day++)
            {
                string text = $"2026-11-{day}";
                CaptureStatus(root, "嫣然心水", text);
                Run(archive, root, text);
            }
            Assert.True(archive.IsSealed("嫣然心水", "神算子"));

            // 同一轮抓到新图：立刻解封，并且当天不再记账。
            Image(folder, "2026-11-28", name: "90001");
            CaptureStatus(root, "嫣然心水", "2026-11-28");
            var notices = new List<string>();
            Assert.True(Run(archive, root, "2026-11-28", notices));

            Assert.False(archive.IsSealed("嫣然心水", "神算子"));
            Assert.Contains(notices, note => note.Contains("已自动解封", StringComparison.Ordinal));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public void DaysTheCaptureProgramDidNotRunDoNotCountTowardsTheThreeDays()
    {
        string root = NewRoot();
        try
        {
            string folder = Path.Combine(GroupFolder(root), "神算子");
            Image(folder, "2026-11-24");
            var archive = new FolderArchive();

            // 只有 11-25 那天的抓取状态是“今天”，其余两天抓图根本没跑过。
            CaptureStatus(root, "嫣然心水", "2026-11-25");
            foreach (string day in new[] { "2026-11-25", "2026-11-26", "2026-11-27", "2026-11-28" })
                Run(archive, root, day);

            Assert.False(archive.IsSealed("嫣然心水", "神算子"));
            Assert.Equal(1, archive.Entries[FolderArchive.KeyFor("嫣然心水", "神算子")].IdleDays);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public void AFolderThatDownloadedNothingTodayStillGetsCounted()
    {
        string root = NewRoot();
        try
        {
            // 抓图程序不建空目录：某天一张图都没下到时“神算子”这个子目录根本不存在。
            string folder = Path.Combine(GroupFolder(root), "神算子");
            Image(folder, "2026-11-24");
            var archive = new FolderArchive();
            CaptureStatus(root, "嫣然心水", "2026-11-25");
            Run(archive, root, "2026-11-25");
            Directory.Delete(folder, true);

            CaptureStatus(root, "嫣然心水", "2026-11-26");
            Assert.True(Run(archive, root, "2026-11-26"));
            CaptureStatus(root, "嫣然心水", "2026-11-27");
            Assert.True(Run(archive, root, "2026-11-27"));

            Assert.True(archive.IsSealed("嫣然心水", "神算子"));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public void OneDayIsOnlyCountedOnceNoMatterHowOftenTheStateIsRefreshed()
    {
        string root = NewRoot();
        try
        {
            string folder = Path.Combine(GroupFolder(root), "神算子");
            Image(folder, "2026-11-24");
            var archive = new FolderArchive();
            CaptureStatus(root, "嫣然心水", "2026-11-25");

            Run(archive, root, "2026-11-25");
            Run(archive, root, "2026-11-25");
            Run(archive, root, "2026-11-25");

            Assert.Equal(1, archive.Entries[FolderArchive.KeyFor("嫣然心水", "神算子")].IdleDays);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public void RulesInTheSealedFolderAreDroppedAndOthersStay()
    {
        string root = NewRoot();
        try
        {
            var archive = new FolderArchive();
            archive.Entries[FolderArchive.KeyFor("嫣然心水", "神算子")] = new FolderArchive.Entry { Sealed = true };
            OcrRule sealedRule = Rule("甲", folderName: "神算子");
            OcrRule otherRule = Rule("乙", folderName: "跑狗图");

            IReadOnlyList<OcrRule> kept = archive.FilterRules("嫣然心水",
                [sealedRule, otherRule], ["神算子", "跑狗图"]);

            Assert.Equal([otherRule], kept);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public void SealingEveryImagesFolderAlsoHidesTheRulesWithoutAFolder()
    {
        var archive = new FolderArchive();
        archive.Entries[FolderArchive.KeyFor("黑色六合彩", FolderArchive.AllImagesFolderName)] =
            new FolderArchive.Entry { Sealed = true };
        OcrRule noFolder = Rule("锦囊九尾", "缺尾", "锦囊杀尾");
        OcrRule otherFolder = Rule("红蜻蜓", "波", "红蜻蜓双波", folderName: "红蜻蜓");

        IReadOnlyList<OcrRule> kept = archive.FilterRules("黑色六合彩",
            [noFolder, otherFolder], [FolderArchive.AllImagesFolderName, "红蜻蜓"]);

        Assert.Equal([otherFolder], kept);
    }

    [Fact]
    public void GroupWithoutAnAllImagesFolderKeepsFolderlessRulesUntilEveryFolderIsSealed()
    {
        var archive = new FolderArchive();
        archive.Entries[FolderArchive.KeyFor("蜻蜓一套", "公式杀料")] =
            new FolderArchive.Entry { Sealed = true };
        OcrRule noFolder = Rule("旧资料", "头");
        string[] knownFolders = ["公式杀料", "各种杀"];

        Assert.Equal([noFolder], archive.FilterRules("蜻蜓一套", [noFolder], knownFolders));

        archive.Entries[FolderArchive.KeyFor("蜻蜓一套", "各种杀")] =
            new FolderArchive.Entry { Sealed = true };
        Assert.Empty(archive.FilterRules("蜻蜓一套", [noFolder], knownFolders));
    }

    [Fact]
    public void ImagesInsideASealedFolderAreSkippedButGroupRootImagesAreNot()
    {
        var archive = new FolderArchive();
        archive.Entries[FolderArchive.KeyFor("嫣然心水", "神算子")] = new FolderArchive.Entry { Sealed = true };
        string groupFolder = Path.Combine(Path.GetTempPath(), "11.27-嫣然心水");
        string sealedImage = Path.Combine(groupFolder, "神算子", "20261127_172056_a926b173_80641.jpg");
        string otherImage = Path.Combine(groupFolder, "跑狗图", "20261127_172056_a926b173_80642.jpg");
        string rootImage = Path.Combine(groupFolder, "20261127_172056_a926b173_80643.jpg");

        Assert.True(FolderArchive.IsImageArchived(groupFolder, sealedImage, archive.SealedFolders("嫣然心水")));
        Assert.False(FolderArchive.IsImageArchived(groupFolder, otherImage, archive.SealedFolders("嫣然心水")));
        Assert.False(FolderArchive.IsImageArchived(groupFolder, rootImage, archive.SealedFolders("嫣然心水")));
        Assert.Equal([otherImage, rootImage],
            archive.FilterImages("嫣然心水", groupFolder, [sealedImage, otherImage, rootImage]));
    }

    [Fact]
    public void ArchiveStateSurvivesSaveAndLoad()
    {
        string root = NewRoot();
        try
        {
            var archive = new FolderArchive();
            archive.Entries[FolderArchive.KeyFor("嫣然心水", "神算子")] = new FolderArchive.Entry
            {
                LastImageDate = "2026-11-24",
                IdleDays = 3,
                LastCountedDate = "2026-11-27",
                Sealed = true,
                SealedDate = "2026-11-27"
            };
            archive.Save(root);

            FolderArchive reloaded = FolderArchive.Load(root);
            Assert.True(reloaded.IsSealed("嫣然心水", "神算子"));
            Assert.Equal(3, reloaded.Entries[FolderArchive.KeyFor("嫣然心水", "神算子")].IdleDays);
            // 状态文件跟规则文件放在一起，带 BOM，和抓取设置一个写法。
            Assert.Equal([0xEF, 0xBB, 0xBF], File.ReadAllBytes(FolderArchive.PathFor(root))[..3]);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public void ArchiveStateFileIsNotMistakenForARuleCatalog()
    {
        Assert.False(RuleCatalog.IsRuleCatalogFile("目录封存.json"));
        Assert.True(RuleCatalog.IsRuleCatalogFile("嫣然心水.json"));
    }

    [Fact]
    public void FolderNamesIncludeArchivedFoldersThatAreMissingFromDisk()
    {
        var archive = new FolderArchive();
        archive.Entries[FolderArchive.KeyFor("嫣然心水", "神算子")] = new FolderArchive.Entry
        {
            LastImageDate = "2026-11-24"
        };

        Assert.Equal(["神算子"], archive.FolderNames("嫣然心水"));
        Assert.Empty(archive.FolderNames("别的群"));
    }
}
