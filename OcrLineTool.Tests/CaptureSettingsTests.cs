using System.Text;
using OcrLineTool;

namespace OcrLineTool.Tests;

public sealed class CaptureSettingsTests
{
    private static string NewRoot() =>
        Path.Combine(Path.GetTempPath(), "capture-settings-" + Guid.NewGuid().ToString("N"));

    private static OcrRule Rule(string keyword, string type = "头", string? label = null) =>
        new(keyword, type, label);

    [Fact]
    public void MissingFileMeansEveryGroupAndRuleStaysEnabled()
    {
        string root = NewRoot();
        try
        {
            CaptureSettings settings = CaptureSettings.Load(root);
            OcrRule[] rules = [Rule("甲"), Rule("乙", label: "乙组")];

            Assert.True(settings.IsGroupEnabled("嫣然心水"));
            Assert.True(settings.IsRuleEnabled("嫣然心水", rules[0]));
            Assert.Equal(rules, settings.EnabledRules("嫣然心水", rules));
            Assert.False(File.Exists(CaptureSettings.PathFor(root)));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public void CorruptFileFallsBackToEverythingEnabledInsteadOfBlockingCapture()
    {
        string root = NewRoot();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(CaptureSettings.PathFor(root))!);
            File.WriteAllText(CaptureSettings.PathFor(root), "{ 这不是合法 JSON");
            CaptureSettings settings = CaptureSettings.Load(root);
            OcrRule[] rules = [Rule("甲")];

            Assert.True(settings.IsGroupEnabled("嫣然心水"));
            Assert.Single(settings.EnabledRules("嫣然心水", rules));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public void DisablingAGroupDropsAllItsRulesButLeavesOtherGroupsAlone()
    {
        string root = NewRoot();
        try
        {
            var settings = new CaptureSettings();
            OcrRule[] rules = [Rule("甲"), Rule("乙")];
            settings.SetGroupEnabled("嫣然心水", false);
            settings.Save(root);

            CaptureSettings reloaded = CaptureSettings.Load(root);
            Assert.False(reloaded.IsGroupEnabled("嫣然心水"));
            Assert.Empty(reloaded.EnabledRules("嫣然心水", rules));
            // 群关了，条目开关也一律视为关闭。
            Assert.False(reloaded.IsRuleEnabled("嫣然心水", rules[0]));
            // 别的群不受影响。
            Assert.True(reloaded.IsGroupEnabled("黄大仙新澳"));
            Assert.Equal(2, reloaded.EnabledRules("黄大仙新澳", rules).Count);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public void RuleSwitchOnlyAffectsItsOwnGroup()
    {
        string root = NewRoot();
        try
        {
            var settings = new CaptureSettings();
            settings.SetRuleEnabled("嫣然心水", Rule("祖師公"), false);
            settings.Save(root);

            CaptureSettings reloaded = CaptureSettings.Load(root);
            Assert.False(reloaded.IsRuleEnabled("嫣然心水", Rule("祖師公")));
            Assert.True(reloaded.IsRuleEnabled("黄大仙新澳", Rule("祖師公")));
            Assert.True(reloaded.IsRuleEnabled("嫣然心水", Rule("别的条目")));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public void DuplicateKeywordsAreSeparatedByLabel()
    {
        string root = NewRoot();
        try
        {
            var settings = new CaptureSettings();
            OcrRule first = Rule("祖师公杀1肖1尾珍藏版", "一肖", label: "祖师公");
            OcrRule second = Rule("祖师公杀1肖1尾珍藏版", "一肖", label: "废料收藏");
            settings.SetRuleEnabled("新澳六合彩资料", first, false);

            // 关掉带 label 的那一条，另一条同 keyword 的规则必须留着。
            Assert.False(settings.IsRuleEnabled("新澳六合彩资料", first));
            Assert.True(settings.IsRuleEnabled("新澳六合彩资料", second));
            var remaining = settings.EnabledRules("新澳六合彩资料", [first, second]);
            Assert.Equal(second, Assert.Single(remaining));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public void DisabledRuleIsRemovedButOriginalOrderIsKept()
    {
        string root = NewRoot();
        try
        {
            var settings = new CaptureSettings();
            OcrRule[] rules = [Rule("一"), Rule("二"), Rule("三"), Rule("四")];
            settings.SetRuleEnabled("新澳高手", rules[1], false);

            var enabled = settings.EnabledRules("新澳高手", rules);
            Assert.Equal(["一", "三", "四"], enabled.Select(rule => rule.Keyword));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public void GroupNamesAreNormalizedSoSeparatorsAndSpacesShareOneSwitch()
    {
        string root = NewRoot();
        try
        {
            var settings = new CaptureSettings();
            // 群名会以“嫣然 心水”“嫣然-心水”等写法出现，归一化后必须是同一个开关。
            settings.SetGroupEnabled("嫣然 心水", false);
            settings.Save(root);

            CaptureSettings reloaded = CaptureSettings.Load(root);
            Assert.False(reloaded.IsGroupEnabled("嫣然心水"));
            Assert.False(reloaded.IsGroupEnabled("嫣然-心水"));
            Assert.Equal(["嫣然心水"], reloaded.DisabledGroups);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public void ReEnablingClearsTheRecordAndKeepsTheFileTidy()
    {
        string root = NewRoot();
        try
        {
            var settings = new CaptureSettings();
            OcrRule rule = Rule("甲");
            settings.SetGroupEnabled("嫣然心水", false);
            settings.SetGroupEnabled("嫣然心水", true);
            settings.SetRuleEnabled("黄大仙新澳", rule, false);
            settings.SetRuleEnabled("黄大仙新澳", rule, true);
            settings.Save(root);

            CaptureSettings reloaded = CaptureSettings.Load(root);
            Assert.Empty(reloaded.DisabledGroups);
            Assert.Empty(reloaded.DisabledRules);
            Assert.True(reloaded.IsRuleEnabled("黄大仙新澳", rule));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public void CategorizeGroupsByDistributionTypeInTxtOrder()
    {
        OcrRule[] rules =
        [
            Rule("尾条目", "尾"),
            Rule("号码30", "号码:30"),
            Rule("生肖条目", "生肖"),
            Rule("头条目", "头"),
            Rule("号码6", "号码:6"),
            Rule("号码5", "号码:5")
        ];

        IReadOnlyList<(string Category, List<OcrRule> Rules)> grouped = CaptureSettingsForm.Categorize(rules);

        Assert.Equal(["头", "尾", "一肖", "5个数字", "5个以上数字", "30个以上数字"],
            grouped.Select(group => group.Category));
        Assert.Equal(["头条目"], grouped[0].Rules.Select(rule => rule.Keyword));
        Assert.Equal(["号码5"], grouped[3].Rules.Select(rule => rule.Keyword));
        Assert.Equal(["号码6"], grouped[4].Rules.Select(rule => rule.Keyword));
        Assert.Equal(["号码30"], grouped[5].Rules.Select(rule => rule.Keyword));
    }

    [Fact]
    public void SettingsFileIsNotMistakenForARuleCatalog()
    {
        string root = NewRoot();
        string config = ResultFilePaths.ConfigurationDirectory(root);
        Directory.CreateDirectory(config);
        try
        {
            File.WriteAllText(Path.Combine(config, "抓取设置.json"), "{\"group\":\"目标群\",\"entries\":[]}");
            Assert.False(RuleCatalog.IsRuleCatalogFile("抓取设置.json"));
            Assert.Equal(Path.Combine(config, "267期-目标群.json"),
                RuleCatalog.PathForFolder(root, @"C:\images\267期-目标群"));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public void RulePathForGroupFindsTheJsonByItsGroupName()
    {
        string root = NewRoot();
        string config = ResultFilePaths.ConfigurationDirectory(root);
        Directory.CreateDirectory(config);
        try
        {
            string path = Path.Combine(config, "嫣然心水.json");
            File.WriteAllText(path, "{\"group\":\"嫣然心水\",\"rules\":[]}");
            File.WriteAllText(Path.Combine(config, "界面设置.json"), "{\"group\":\"界面设置\",\"rules\":[]}");

            Assert.Equal(path, RuleCatalog.RulePathForGroup(root, "嫣然心水"));
            Assert.Null(RuleCatalog.RulePathForGroup(root, "不存在的群"));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public void DisabledGroupIsNotOfferedToTheAutomaticRecognizer()
    {
        string root = NewRoot();
        string config = ResultFilePaths.ConfigurationDirectory(root);
        Directory.CreateDirectory(config);
        string folder = Path.Combine(root, "10.6-嫣然心水");
        Directory.CreateDirectory(folder);
        try
        {
            File.WriteAllText(Path.Combine(config, "嫣然心水.json"), "{\"group\":\"嫣然心水\",\"rules\":[]}");
            File.WriteAllBytes(Path.Combine(folder, "1.png"), [0x89, 0x50, 0x4E, 0x47]);
            var autoSettings = new AutoLocalOcrSettings();
            autoSettings.SetSelectedGroup("嫣然心水", true);

            Assert.Single(MainForm.FindVisibleAutomaticGroups(root, root, autoSettings));

            var capture = new CaptureSettings();
            capture.SetGroupEnabled("嫣然心水", false);
            Assert.Empty(MainForm.FindVisibleAutomaticGroups(root, root, autoSettings, null, capture));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public void SettingsFileLivesInTheConfigurationFolderNextToTheRuleFiles()
    {
        string root = NewRoot();
        try
        {
            Assert.Equal(Path.Combine(ResultFilePaths.ConfigurationDirectory(root), "抓取设置.json"),
                CaptureSettings.PathFor(root));

            var settings = new CaptureSettings();
            settings.SetGroupEnabled("嫣然心水", false);
            // Save 必须自己把“配置文件”建出来，不能指望调用方先建目录。
            settings.Save(root);

            Assert.True(File.Exists(CaptureSettings.PathFor(root)));
            Assert.False(File.Exists(Path.Combine(root, "抓取设置.json")));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public void GroupSwitchKeepsPerRuleChoicesSoReEnablingRestoresThem()
    {
        string root = NewRoot();
        try
        {
            var settings = new CaptureSettings();
            OcrRule kept = Rule("甲");
            OcrRule closed = Rule("乙");
            settings.SetRuleEnabled("嫣然心水", closed, false);
            settings.SetGroupEnabled("嫣然心水", false);
            settings.Save(root);

            CaptureSettings reloaded = CaptureSettings.Load(root);
            Assert.False(reloaded.IsGroupEnabled("嫣然心水"));
            // 群关着：条目一律不识别，但各自的开关记录必须还在，重新开群才能恢复。
            Assert.False(reloaded.IsRuleEnabled("嫣然心水", kept));
            Assert.True(reloaded.IsRuleEnabledIgnoringGroup("嫣然心水", kept));
            Assert.False(reloaded.IsRuleEnabledIgnoringGroup("嫣然心水", closed));

            reloaded.SetGroupEnabled("嫣然心水", true);
            Assert.True(reloaded.IsRuleEnabled("嫣然心水", kept));
            Assert.False(reloaded.IsRuleEnabled("嫣然心水", closed));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public void GroupWithEveryRuleDisabledIsNotOfferedToTheAutomaticRecognizer()
    {
        string root = NewRoot();
        string config = ResultFilePaths.ConfigurationDirectory(root);
        Directory.CreateDirectory(config);
        string folder = Path.Combine(root, "10.6-嫣然心水");
        Directory.CreateDirectory(folder);
        try
        {
            File.WriteAllText(Path.Combine(config, "嫣然心水.json"),
                "{\"group\":\"嫣然心水\",\"rules\":[{\"keyword\":\"甲\",\"type\":\"头\"}]}");
            File.WriteAllBytes(Path.Combine(folder, "1.png"), [0x89, 0x50, 0x4E, 0x47]);
            var autoSettings = new AutoLocalOcrSettings();
            autoSettings.SetSelectedGroup("嫣然心水", true);

            var capture = new CaptureSettings();
            capture.SetRuleEnabled("嫣然心水", Rule("甲"), false);

            // 群开着但条目全关 = 没东西要识别：当作跳过，不能记成失败把当天锁死。
            Assert.Empty(MainForm.FindVisibleAutomaticGroups(root, root, autoSettings, null, capture));
            // 没传抓取设置时行为不变，仍然是看得见的群。
            Assert.Single(MainForm.FindVisibleAutomaticGroups(root, root, autoSettings));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task ClosedRuleDoesNotShowUpAsMissingInTheSummary()
    {
        string root = NewRoot();
        string config = ResultFilePaths.ConfigurationDirectory(root);
        string results = ResultFilePaths.GroupResultsDirectory(root);
        Directory.CreateDirectory(config);
        Directory.CreateDirectory(results);
        try
        {
            File.WriteAllText(Path.Combine(config, "嫣然心水.json"),
                "{\"group\":\"嫣然心水\",\"rules\":[{\"keyword\":\"甲\",\"type\":\"头\"}]}");
            File.WriteAllLines(Path.Combine(results, "嫣然心水_267期.txt"),
                ["缺失（未找到对应图片） 甲"], new UTF8Encoding(true));

            var capture = new CaptureSettings();
            capture.SetRuleEnabled("嫣然心水", Rule("甲"), false);
            capture.Save(root);

            var (_, count) = await MissingResultSummary.WriteAsync(root);
            Assert.Equal(0, count);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
