using OcrLineTool;
using Xunit;

namespace OcrLineTool.Tests;

// 嫣然心水 严格身份与主图专用规则（253 期实测口径）。
public sealed class YanranStrictMatchingTests
{
    private static OcrRule[] Rules() => RuleCatalog.Load(
        Path.Combine(ResultFilePaths.ConfigurationDirectory(AppContext.BaseDirectory), "嫣然心水.json"))
        .ToArray();

    private static OcrRule Rule(string id) => Assert.Single(Rules(), rule => rule.Id == id);

    [Fact]
    public void YanranRequiresTheKeywordOnTheImage()
    {
        OcrRule[] catalog = Rules();
        OcrRule laba = Assert.Single(catalog, rule => rule.Id == "木桃");
        string image = @"C:\结果\9.10-嫣然心水\木桃\sample.jpg";

        Assert.Single(RuleEngine.FindMatches(
            image, ["木桃新澳门禁肖", "253期杀猪开??"], [laba], catalog));
        Assert.Empty(RuleEngine.FindMatches(
            image, ["253期杀猪开??"], [laba], catalog));
        Assert.Empty(RuleEngine.FindMatches(
            image, ["紫燕儿", "253期:杀一尾【2尾】"], [laba], catalog));
    }

    [Fact]
    public void ChiliPorkFolderIdentityAppliesOnlyToItsOwnRules()
    {
        OcrRule[] catalog = Rules();
        OcrRule chili = Assert.Single(catalog, rule => rule.Id == "辣椒炒肉肖肖");
        string image = @"C:\结果\9.10-嫣然心水\辣椒炒肉\sample.jpg";

        Assert.Single(RuleEngine.FindMatches(
            image, ["228期，杀羊√开23", "253期，杀羊开??"], [chili], catalog));
        Assert.Empty(RuleEngine.FindMatches(
            image, ["紫燕儿", "253期:杀一尾【2尾】"], [chili], catalog));

        // A rule without the folder-identity flag stays strict.
        OcrRule laba = Assert.Single(catalog, rule => rule.Id == "木桃");
        Assert.Empty(RuleEngine.FindMatches(
            @"C:\结果\9.10-嫣然心水\木桃\sample.jpg",
            ["253期杀猪开??"], [laba], catalog));
    }

    [Fact]
    public void OtherGroupsKeepTheDedicatedFolderFallback()
    {
        var rule = new OcrRule("华林", "生肖", "华林肖", RequiredKeyword: "华林", Folder: "华林");
        string image = @"C:\结果\华林\sample.jpg";

        Assert.Single(RuleEngine.FindMatches(
            image, ["243期杀虎💝杀绿双💝杀9尾💝"], [rule], [rule]));
    }

    [Fact]
    public void XiaohuihuiRequiresHonorProudAndOneOfKillCharacters()
    {
        OcrRule single = Rule("小灰灰一肖");
        string image = @"C:\结果\9.10-嫣然心水\小灰灰\sample.jpg";
        string[] killCard =
        [
            "新澳", "小灰灰荣誉出品",
            "251杀蛇开牛30√", "252杀虎开鸡22√", "253杀鸡开鸭88?",
            "对是必然，错是偶然。"
        ];
        string[] flatCard =
        [
            "新澳", "小灰灰荣誉出品", "平特肖", "253《虎》开41?", "平特尾", "253《2尾》开12?"
        ];

        Assert.Single(RuleEngine.FindMatches(image, killCard, [single], [single]));
        Assert.Empty(RuleEngine.FindMatches(image, flatCard, [single], [single]));
        Assert.Equal("鸡", RuleEngine.ExtractFinalValue(killCard, 253, single));
    }

    [Fact]
    public void ChiliPorkPlannerPrefersTheTypeValidNamelessCard()
    {
        OcrRule[] catalog = Rules();
        OcrRule chili = Assert.Single(catalog, rule => rule.Id == "辣椒炒肉肖肖");
        string junk = @"C:\结果\9.10-嫣然心水\辣椒炒肉\36码.jpg";
        string data = @"C:\结果\9.10-嫣然心水\辣椒炒肉\肖.jpg";
        var results = new Dictionary<string, IReadOnlyList<string>>
        {
            [junk] = ["辣椒炒肉", "253期，01.02.03.04.05.06", "02.03.04.05.06.07"],
            [data] = ["228期，杀羊√开23", "253期，杀羊开??"]
        };

        IReadOnlyList<LocalCandidatePlan> plans = LocalCandidatePlanner.Build(
            [junk, data], results, [chili], 253, [chili]);

        LocalCandidatePlan plan = Assert.Single(plans);
        Assert.True(plan.IsPrimary);
        Assert.Equal(data, plan.Path);
        Assert.Equal("羊", RuleEngine.ExtractFinalValue(results[data], 253, chili));
    }

    [Fact]
    public void YanyumomoSkipsTheThreeZodiacRowAndKeepsTheSingleOne()
    {
        OcrRule rule = Rule("烟雨沫沫");
        string[] lines =
        [
            "烟雨沫沫(新澳)", "禁一肖",
            "251期兔开?0", "252期兔开?2", "253期蛇",
            "禁三肖",
            "251期兔猴马开?", "252期禁兔龙鸡开?", "253期蛇龙猪?"
        ];

        Assert.Equal("蛇", RuleEngine.ExtractFinalValue(lines, 253, rule));
    }

    [Fact]
    public void DongnanziUsesTheFirstZodiacBeforeTheDoublePlusColumn()
    {
        OcrRule rule = Rule("东南仔");
        string[] lines =
        [
            "【独行客】", "NEW新澳禁1霄",
            "252区杀-猪++虎✓", "253区杀-猪++牛",
            "慎参", "主禁第一个"
        ];

        Assert.Equal("猪", RuleEngine.ExtractFinalValue(lines, 253, rule));
    }

    [Fact]
    public void GgTeamSummaryRowsAreReadEvenWhenTheSheetPrintsAnEarlierIssue()
    {
        OcrRule[] catalog = Rules();
        string[] lines =
        [
            "GG团队252期新澳禁肖统计表",
            "Alice正正正正禁龙",
            "玉亚正正正正禁蛇",
            "欧阳正正正正禁狗",
            "陈思思正正正正禁兔",
            "苏柒若正正正正禁狗",
            "简单爱正正正正禁鼠",
            "月来月好正正禁兔",
            "潮汕陈龙正正禁虎"
        ];

        Assert.Equal("鼠", RuleEngine.ExtractFinalValue(lines, 253, Assert.Single(catalog, r => r.Id == "简单爱")));
        Assert.Equal("狗", RuleEngine.ExtractFinalValue(lines, 253, Assert.Single(catalog, r => r.Id == "苏柒若")));
        Assert.Equal("狗", RuleEngine.ExtractFinalValue(lines, 253, Assert.Single(catalog, r => r.Id == "欧阳肖")));
        Assert.Equal("兔", RuleEngine.ExtractFinalValue(lines, 253, Assert.Single(catalog, r => r.Id == "月来月好")));
        Assert.Equal("兔", RuleEngine.ExtractFinalValue(lines, 253, Rule("陈思思")));
    }

    [Fact]
    public void ChenSisiMatchesOnlyTheGgSummarySheetNotTheDedicatedCard()
    {
        OcrRule[] catalog = Rules();
        OcrRule rule = Rule("陈思思");
        Assert.Equal("统计表", rule.RequiredKeyword);
        Assert.True(rule.AllowIssueLessSummary);

        string summary = @"C:\结果\9.10-嫣然心水\乖乖团队\summary.jpg";
        Assert.Single(RuleEngine.FindMatches(
            summary,
            ["GG团队255期新澳禁肖统计表", "陈思思正正禁马", "简单爱正禁猴"],
            [rule], catalog));

        string dedicated = @"C:\结果\9.10-嫣然心水\乖乖团队\card.jpg";
        Assert.Empty(RuleEngine.FindMatches(
            dedicated,
            ["团队", "不忘初心", "新澳门六合彩", "陈思思", "254期禁羊", "255期禁马"],
            [rule], catalog));
    }

    [Fact]
    public void YongBuQiAcceptsTheLeafVariantOfItsName()
    {
        OcrRule rule = Rule("永卟弃杀头");
        Assert.Single(RuleEngine.FindMatches(
            @"C:\结果\9.10-嫣然心水\乖乖团队\yongbuqi.jpg",
            ["团队", "新澳门六合彩", "永叶弃", "254期中0134头", "255期中0234头"],
            [rule], Rules()));
        Assert.Equal("1头", RuleEngine.ExtractFinalValue(
            ["团队", "不忘初心", "新澳门六合彩", "永叶弃", "253期中0134头", "254期中0134头", "255期中0234头"],
            255, rule));
    }

    [Fact]
    public void GgTeamSummaryAcceptsBoxyTallyMarks()
    {
        OcrRule[] catalog = Rules();
        string[] lines =
        [
            "GG团队254期新澳禁肖统计表",
            "简单爱囸正禁狗",
            "欧阳囸囸正禁虎G",
            "月来月好正正禁鼠"
        ];

        Assert.Equal("狗", RuleEngine.ExtractFinalValue(lines, 254, Assert.Single(catalog, r => r.Id == "简单爱")));
        Assert.Equal("虎", RuleEngine.ExtractFinalValue(lines, 254, Assert.Single(catalog, r => r.Id == "欧阳肖")));
        Assert.Equal("鼠", RuleEngine.ExtractFinalValue(lines, 254, Assert.Single(catalog, r => r.Id == "月来月好")));
    }

    [Fact]
    public void YuyahalfWaveKeepsTheValueBeforeThePlusColumn()
    {
        OcrRule rule = Rule("玉亚半波");

        Assert.Equal("绿单", RuleEngine.ExtractFinalValue(["玉亚", "252期禁 绿单 + 蓝单"], 252, rule));
        Assert.Equal("蓝单", RuleEngine.ExtractFinalValue(["玉亚", "251期禁 蓝单✓ + 红单✓"], 251, rule));
    }

    [Fact]
    public void YanranPlannerEmitsNoBackupPlans()
    {
        OcrRule rule = Rule("木桃");
        string dataImage = @"C:\结果\9.10-嫣然心水\木桃\a.jpg";
        string secondImage = @"C:\结果\9.10-嫣然心水\木桃\b.jpg";
        var results = new Dictionary<string, IReadOnlyList<string>>
        {
            [dataImage] = ["木桃新澳门禁肖", "253期杀猪开??"],
            [secondImage] = ["木桃心水", "253期杀蛇开??"]
        };

        IReadOnlyList<LocalCandidatePlan> plans = LocalCandidatePlanner.Build(
            [dataImage, secondImage], results, [rule], 253, [rule]);

        LocalCandidatePlan plan = Assert.Single(plans);
        Assert.True(plan.IsPrimary);
        Assert.Equal(rule, Assert.Single(plan.Rules));
    }
}
