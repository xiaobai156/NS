using OcrLineTool;

namespace OcrLineTool.Tests;

public sealed class RuleEngineTests
{
    [Theory]
    [InlineData("梁微微")]
    [InlineData("梁薇薇")]
    public void LiangWeiweiSpellingsUseOneConfiguredRuleAndOutputLabel(string name)
    {
        IReadOnlyList<OcrRule> rules = RuleCatalog.Load(
            Path.Combine(ResultFilePaths.ConfigurationDirectory(AppContext.BaseDirectory), "嫣然心水.json"));
        OcrRule rule = Assert.Single(rules, item => item.Id is "梁微微" or "梁薇薇");
        Assert.Equal("梁微微", rule.Id);
        Assert.Equal("生肖", rule.Type);
        Assert.Equal("梁薇薇", rule.Folder);
        string[] lines = [$"245期{name}杀一肖【兔】开？00准"];

        Assert.Equal(rule, Assert.Single(RuleEngine.FindMatches(lines, [rule])));
        Assert.Equal(rule, Assert.Single(RuleEngine.FindMatches(
            @"C:\结果\9.2-嫣然心水\梁薇薇\图.jpg", lines, [rule])));
        Assert.Empty(RuleEngine.FindMatches(@"C:\结果\9.2-嫣然心水\其他资料\图.jpg", lines, [rule]));
        Assert.Equal("兔", RuleEngine.ExtractValue(lines, 245, rule));
        Assert.Equal("兔", RuleEngine.ExtractValue([name, "245期杀一肖【兔】开？00准"], 245, rule));
        Assert.Equal("兔", RuleEngine.ExtractFinalValue(lines, 245, rule));
        Assert.Equal(["兔 梁微微"], RuleEngine.FormatOutput([rule], new Dictionary<string, string> { [rule.Id] = "兔" }));
    }

    [Theory]
    [InlineData("梁微微")]
    [InlineData("梁薇薇")]
    public void LiangWeiweiSummaryReadsOnlyHerOwnRowForEitherSpelling(string name)
    {
        var rule = new OcrRule("梁微微", "生肖", Folder: "梁薇薇");
        string[] lines = ["GG团队245期新澳禁肖统计表", "东南仔禁虎", $"{name}正正禁兔", "烟草味禁羊"];

        Assert.Equal("兔", RuleEngine.ExtractValue(lines, 245, rule));
        Assert.Equal("兔", RuleEngine.ExtractFinalValue(lines, 245, rule));
        Assert.Equal("兔", RuleEngine.ExtractFinalValue(
            [lines[0], name, "禁", "兔", "烟草味禁羊"], 245, rule));
        Assert.Null(RuleEngine.ExtractFinalValue(lines, 246, rule));
        Assert.Null(RuleEngine.ExtractFinalValue([lines[0], name, "烟草味禁羊"], 245, rule));
        Assert.Null(RuleEngine.ExtractFinalValue([lines[0], "东南仔禁虎", "烟草味禁羊"], 245, rule));
    }

    [Fact]
    public void LiangWeiweiEntertainmentFooterKeepsTheCurrentIssueValue()
    {
        var rule = new OcrRule("梁微微", "生肖", Folder: "梁薇薇");
        string[] lines =
        [
            "2026年1-8错（19）",
            "244期杀兔开46中",
            "245期杀猴开18中",
            "246期杀鼠开?",
            "(梁微微娱乐)"
        ];

        Assert.Equal(rule, Assert.Single(RuleEngine.FindMatches(lines, [rule])));
        Assert.Equal("鼠", RuleEngine.ExtractFinalValue(lines, 246, rule));
        Assert.Null(RuleEngine.ExtractFinalValue(lines, 247, rule));
    }

    [Theory]
    [InlineData("梁薇微")]
    [InlineData("梁微薇")]
    [InlineData("梁薇")]
    public void LiangWeiweiAliasDoesNotEnableGeneralShortNameFuzzyMatching(string name)
    {
        var rule = new OcrRule("梁微微", "生肖", Folder: "梁薇薇");
        Assert.Empty(RuleEngine.FindMatches([$"245期{name}杀一肖兔"], [rule]));
        Assert.Null(RuleEngine.ExtractValue([$"245期{name}杀一肖兔"], 245, rule));
        Assert.Null(RuleEngine.ExtractFinalValue(["245期禁肖统计表", $"{name}禁兔"], 245, rule));
    }

    [Fact]
    public void MatchesKeywordsAgainstLocalOcrText()
    {
        var rules = new[]
        {
            new OcrRule("南国挽心", "生肖"),
            new OcrRule("青玉", "合")
        };

        var actual = RuleEngine.FindMatches(["241期：新澳门【天机阁论坛●南国挽心正正正杀一肖】猴"], rules);

        Assert.Equal(["南国挽心"], actual.Select(rule => rule.Keyword));
    }

    [Fact]
    public void MatchesLongKeywordWhenLocalOcrGetsOneCharacterWrong()
    {
        var rules = new[] { new OcrRule("南国挽心", "生肖") };

        var actual = RuleEngine.FindMatches(["241期：南国晚心杀一肖猴"], rules);

        Assert.Equal(["南国挽心"], actual.Select(rule => rule.Keyword));
    }

    [Fact]
    public void MatchesCompleteTitleWhileIgnoringDecorativeSymbols()
    {
        var rule = new OcrRule("小马哥庄家杀12码", "号码:12", "小马哥");

        Assert.Single(RuleEngine.FindMatches(["小马哥庄家--【杀12码】"], [rule]));
    }

    [Fact]
    public void DoesNotMatchAnotherMaterialFromTheSameAuthor()
    {
        var rule = new OcrRule("小马哥庄家杀12码", "号码:12", "小马哥");

        Assert.Empty(RuleEngine.FindMatches(["小马哥【九肖到1肖】"], [rule]));
    }

    [Fact]
    public void RequiresSecondaryKeywordAnywhereInTheImage()
    {
        var rule = new OcrRule("恩平公式", "生肖组合", null, "杀二肖", "杀二肖");

        Assert.Empty(RuleEngine.FindMatches(["作者：恩平公式", "241期杀一尾3尾"], [rule]));
        Assert.Empty(RuleEngine.FindMatches(["作者：恩平公式", "241期杀一肖马"], [rule]));
        Assert.Single(RuleEngine.FindMatches(["作者：恩平公式", "恩平公式杀二肖", "241期杀马猪"], [rule]));
    }

    [Fact]
    public void UsesFolderNameThenLocalTypeToSelectTheRightImage()
    {
        var rules = new[]
        {
            new OcrRule("小灰灰", "单生肖", "小灰灰一肖"),
            new OcrRule("小灰灰", "生肖组合", "小灰灰两肖"),
            new OcrRule("小灰灰", "尾数组合", "小灰灰两尾")
        };
        string image = Path.Combine("C:\\结果", "小灰灰", "sample.jpg");

        IReadOnlyList<OcrRule> actual = RuleEngine.FindMatches(image, ["241期绝杀二肖", "【兔+鸡】开：？00准"], rules);

        Assert.Equal(["小灰灰两肖"], actual.Select(rule => rule.Id));
    }

    [Fact]
    public void UsesAnExclusiveFolderAsIdentityWhenTheImageOmitsTheAuthorName()
    {
        OcrRule[] rules =
        [
            new("华林", "生肖", "华林肖", RequiredKeyword: "华林", Folder: "华林"),
            new("华林", "尾", "华林尾", RequiredKeyword: "华林", Folder: "华林"),
            new("华林", "色单双", "华林半波", RequiredKeyword: "华林", Folder: "华林")
        ];
        string image = Path.Combine("C:\\结果", "华林", "sample.jpg");

        IReadOnlyList<OcrRule> actual = RuleEngine.FindMatches(
            image,
            ["243期杀虎💝杀绿双💝杀9尾💝"],
            rules);

        Assert.Equal(["华林肖", "华林尾", "华林半波"], actual.Select(rule => rule.Id));
    }

    [Fact]
    public void LoadsDuaoSatuoSingleAndDoubleZodiacRulesFromItsFolder()
    {
        IReadOnlyList<OcrRule> rules = RuleCatalog.Load(
            Path.Combine(ResultFilePaths.ConfigurationDirectory(AppContext.BaseDirectory), "嫣然心水.json"));

        OcrRule single = Assert.Single(rules, rule => rule.Id == "独傲洒脱杀肖肖");
        OcrRule pair = Assert.Single(rules, rule => rule.Id == "独傲洒脱杀二肖");

        Assert.Equal(("生肖", "独傲洒脱", "一特肖", "一特肖"),
            (single.Type, single.Folder, single.Section, single.RequiredKeyword));
        Assert.Equal(("生肖组合", "独傲洒脱", "双规", "双规"),
            (pair.Type, pair.Folder, pair.Section, pair.RequiredKeyword));
    }

    [Fact]
    public void ExtractsDuaoSatuoSingleAndDoubleZodiacsForTheTargetIssue()
    {
        IReadOnlyList<OcrRule> rules = RuleCatalog.Load(
            Path.Combine(ResultFilePaths.ConfigurationDirectory(AppContext.BaseDirectory), "嫣然心水.json"));
        OcrRule single = Assert.Single(rules, rule => rule.Id == "独傲洒脱杀肖肖");
        OcrRule pair = Assert.Single(rules, rule => rule.Id == "独傲洒脱杀二肖");

        Assert.Equal("马", RuleEngine.ExtractValue(
            ["独傲洒脱", "澳彩杀一特肖", "247期：杀马!?"], 247, single));
        Assert.Equal("马牛", RuleEngine.ExtractValue(
            ["独傲洒脱", "澳彩杀2特肖（双规）", "247期：杀马!?+牛!?"], 247, pair));
    }

    [Fact]
    public void UsesTheDuaoSatuoFolderAndTitleToAvoidTheNeighboringCard()
    {
        IReadOnlyList<OcrRule> rules = RuleCatalog.Load(
            Path.Combine(ResultFilePaths.ConfigurationDirectory(AppContext.BaseDirectory), "嫣然心水.json"));
        string image = @"C:\结果\9.4-嫣然心水\独傲洒脱\sample.jpg";
        string[] lines = ["独傲洒脱", "澳彩杀一特肖", "247期：杀马!?", "澳彩杀2特肖（双规）", "247期：杀马!?+牛!?"];

        IReadOnlyList<OcrRule> actual = RuleEngine.FindMatches(image, lines,
            rules.Where(rule => rule.Id is "独傲洒脱杀肖肖" or "独傲洒脱杀二肖").ToArray());

        Assert.Equal(["独傲洒脱杀肖肖", "独傲洒脱杀二肖"], actual.Select(rule => rule.Id));
    }

    [Fact]
    public void ExtractsAlianZodiacAndTailForTheTargetIssue()
    {
        IReadOnlyList<OcrRule> rules = RuleCatalog.Load(
            Path.Combine(ResultFilePaths.ConfigurationDirectory(AppContext.BaseDirectory), "嫣然心水.json"));
        OcrRule zodiac = Assert.Single(rules, rule => rule.Id == "阿莲杀肖肖");
        OcrRule tail = Assert.Single(rules, rule => rule.Id == "阿莲杀尾尾");
        string[] lines = ["阿莲", "9月份杀号", "247期禁猪开??", "247期禁1尾开??"];

        Assert.Equal("猪", RuleEngine.ExtractValue(lines, 247, zodiac));
        Assert.Equal("1尾", RuleEngine.ExtractValue(lines, 247, tail));
    }

    [Fact]
    public void UsesTheAlianFolderAndTitleToAvoidAnotherCardInTheFolder()
    {
        IReadOnlyList<OcrRule> rules = RuleCatalog.Load(
            Path.Combine(ResultFilePaths.ConfigurationDirectory(AppContext.BaseDirectory), "嫣然心水.json"));
        OcrRule[] alianRules = rules.Where(rule => rule.Id is "阿莲杀肖肖" or "阿莲杀尾尾").ToArray();
        string image = @"C:\结果\9.4-嫣然心水\阿莲\sample.jpg";

        Assert.Equal(
            ["阿莲杀肖肖", "阿莲杀尾尾"],
            RuleEngine.FindMatches(image,
                ["阿莲", "9月份杀号", "247期禁猪开??", "247期禁1尾开??"], alianRules)
                .Select(rule => rule.Id));
        Assert.Empty(RuleEngine.FindMatches(image,
            ["绿来如此【新澳门】综合料", "247期杀一肖【羊】开00"], alianRules));
    }

    [Fact]
    public void ExtractsTheHighestFrequencyZodiacFromEachStatisticMaterial()
    {
        IReadOnlyList<OcrRule> rules = RuleCatalog.Load(
            Path.Combine(ResultFilePaths.ConfigurationDirectory(AppContext.BaseDirectory), "嫣然心水.json"));
        OcrRule sixDoors = Assert.Single(rules, item => item.Id == "借花献佛");
        OcrRule pinjian = Assert.Single(rules, item => item.Id == "品鉴");
        OcrRule xiaohui = Assert.Single(rules, item => item.Id == "小慧慧");
        OcrRule grayGroup = Assert.Single(rules, item => item.Id == "灰灰团");

        Assert.Equal("牛", RuleEngine.ExtractValue(
            [
                "新澳彩247期", "247期", "借花献佛杀号统计以少数为主",
                "0次：【羊蛇猴马】", "1次：【虎猪兔】", "2次：【龙鸡狗鼠】", "3次：【牛】杀！！"
            ], 247, sixDoors));
        Assert.Equal("狗", RuleEngine.ExtractValue(
            [
                "品鉴新澳一肖", "==2026247期您的统计结果", "生肖统计（总次数:46次）",
                "[00次] 猪", "[01次] 蛇", "[09次] 狗"
            ], 247, pinjian));
        Assert.Equal("虎", RuleEngine.ExtractValue(
            ["小慧慧新澳杀肖团队", "第247期", "[0次]马", "[1次]兔龙猴", "[5次]虎"], 247, xiaohui));
        Assert.Equal("鸡", RuleEngine.ExtractValue(
            ["灰灰团", "新澳09月份（杀错排名垫后）", "第247期统计(30人):", "[3次]牛虎猴狗", "[5次]鸡"], 247, grayGroup));
    }

    [Fact]
    public void KeepsAllZodiacsWhenTheHighestFrequencyIsShared()
    {
        var rule = new OcrRule("品鉴", "统计生肖", RequiredKeyword: "生肖统计");

        Assert.Equal("狗猴", RuleEngine.ExtractValue(
            ["品鉴新澳一肖", "2026247期", "生肖统计", "[09次] 狗猴", "[08次] 牛"], 247, rule));
    }

    [Fact]
    public void StatisticRulesUseTheirFolderAndMaterialTitleAsIdentity()
    {
        IReadOnlyList<OcrRule> rules = RuleCatalog.Load(
            Path.Combine(ResultFilePaths.ConfigurationDirectory(AppContext.BaseDirectory), "嫣然心水.json"));
        (string Id, string Folder, string[] Lines)[] samples =
        [
            ("借花献佛", "六扇门", ["新澳彩247期", "247期", "借花献佛杀号统计", "3次：【牛】"]),
            ("品鉴", "品鉴", ["品鉴新澳一肖", "2026247期", "生肖统计", "[09次] 狗"]),
            ("小慧慧", "小慧慧", ["小慧慧新澳杀肖团队", "第247期", "[5次] 虎"]),
            ("灰灰团", "灰灰团", ["灰灰团", "新澳09月份（杀错排名垫后）", "第247期统计(30人):", "[5次] 鸡"])
        ];

        foreach ((string id, string folder, string[] lines) in samples)
        {
            OcrRule rule = Assert.Single(rules, item => item.Id == id);
            string image = $@"C:\结果\9.4-嫣然心水\{folder}\sample.jpg";
            Assert.Single(RuleEngine.FindMatches(image, lines, [rule]));
        }

        OcrRule xiaohui = Assert.Single(rules, item => item.Id == "小慧慧");
        Assert.Empty(RuleEngine.FindMatches(
            @"C:\结果\9.4-嫣然心水\小慧慧\sample.jpg",
            ["灰灰团", "新澳09月份（杀错排名垫后）", "第247期统计(30人):", "[5次] 鸡"],
            [xiaohui]));
    }

    [Fact]
    public void StatisticRowSupportsFrequencyAndZodiacOnSeparateLines()
    {
        var rule = new OcrRule("借花献佛", "统计生肖", RequiredKeyword: "借花献佛", Folder: "六扇门");

        Assert.Equal("牛", RuleEngine.ExtractValue(
            ["新澳彩247期", "借花献佛杀号统计", "3次：", "牛"], 247, rule));
    }

    [Fact]
    public void SeparatesFirewolfTwoZodiacAndTwoTailSections()
    {
        IReadOnlyList<OcrRule> rules = RuleCatalog.Load(
            Path.Combine(ResultFilePaths.ConfigurationDirectory(AppContext.BaseDirectory), "嫣然心水.json"));
        OcrRule zodiac = Assert.Single(rules, item => item.Id == "火狼女两肖");
        OcrRule tails = Assert.Single(rules, item => item.Id == "火狼女两尾");
        string[] lines =
        [
            "火狼女", "247期禁<牛狗>开猫50", "狼女新澳二尾", "247期杀<0-1尾>开50"
        ];

        Assert.Equal("牛狗", RuleEngine.ExtractValue(lines, 247, zodiac));
        Assert.Equal("0尾+1尾", RuleEngine.ExtractValue(lines, 247, tails));
    }

    [Fact]
    public void FirewolfRulesDoNotMatchItsSixPlusOneCards()
    {
        IReadOnlyList<OcrRule> rules = RuleCatalog.Load(
            Path.Combine(ResultFilePaths.ConfigurationDirectory(AppContext.BaseDirectory), "嫣然心水.json"));
        OcrRule[] firewolfRules = rules.Where(rule => rule.Id is "火狼女两肖" or "火狼女两尾").ToArray();
        string image = @"C:\结果\9.4-嫣然心水\火狼女\sample.jpg";

        Assert.Empty(RuleEngine.FindMatches(image,
            ["新澳规则A版", "火狼女6+1肖", "247期六肖<猪蛇猴羊龙兔+鼠>开猫50"], firewolfRules));
        Assert.Empty(RuleEngine.FindMatches(image,
            ["新澳6+1尾数规律B版", "火狼女出料", "247期<482356+0尾>开50"], firewolfRules));
    }

    [Fact]
    public void UsesFolderAndTypeToSeparateBaiShaohuaImages()
    {
        var rules = new[]
        {
            new OcrRule("秒杀半头", "半头", "白少华半头", null, "秒杀半头", "白少华"),
            new OcrRule("绝杀五码", "号码:5", "白少华五码", null, "绝杀五码", "白少华")
        };
        string image = Path.Combine("C:\\结果", "白少华", "sample.jpg");

        IReadOnlyList<OcrRule> actual = RuleEngine.FindMatches(
            image,
            ["241期秒杀半头[4头双]开49", "242期秒杀半头[3头双]开888"],
            rules);

        Assert.Equal(["白少华半头"], actual.Select(rule => rule.Id));
    }

    [Fact]
    public void UsesFormulaFolderAsIdentityWhenFormulaTitleIsOmitted()
    {
        var rules = new[]
        {
            new OcrRule("公式杀两肖", "生肖组合", "公式杀两肖肖", Folder: "公式杀料"),
            new OcrRule("公式杀两尾", "尾数组合", "公式杀两尾尾", Folder: "公式杀料")
        };
        string image = Path.Combine("C:\\结果", "蜻蜓一套骁腾", "公式杀料", "sample.jpg");

        IReadOnlyList<OcrRule> actual = RuleEngine.FindMatches(
            image,
            ["244期（平2-2-D2+正3）=杀46尾", "244期（平2*4+特-D4+平3+正E1-2）=杀狗猴√"],
            rules);

        Assert.Equal(["公式杀两肖肖", "公式杀两尾尾"], actual.Select(rule => rule.Id));
    }

    [Fact]
    public void FolderCandidateSelectionUsesAnyIssueInTheImage()
    {
        var rules = new[]
        {
            new OcrRule("秒杀半头", "半头", "白少华半头", null, "秒杀半头", "白少华"),
            new OcrRule("绝杀五码", "号码:5", "白少华五码", null, "绝杀五码", "白少华")
        };
        string image = Path.Combine("C:\\结果", "白少华", "sample.jpg");

        IReadOnlyList<OcrRule> actual = RuleEngine.FindMatches(
            image,
            ["242期绝杀五码[0513464748开888"],
            rules);

        Assert.Equal(["白少华五码"], actual.Select(rule => rule.Id));
    }

    [Fact]
    public void DetectsWhenTheFirstTenCloudResultsAllHaveAnotherLatestIssue()
    {
        IReadOnlyList<string>[] samples = Enumerable.Range(0, 10)
            .Select(_ => (IReadOnlyList<string>)["241期往期", "242期当期"])
            .ToArray();

        Assert.Equal(242, RuleEngine.DetectIssueMismatch(samples, 243));
    }

    [Fact]
    public void DoesNotReportIssueMismatchWhenAnySampleContainsTheSelectedIssue()
    {
        IReadOnlyList<string>[] samples = Enumerable.Range(0, 10)
            .Select(index => (IReadOnlyList<string>)[index == 9 ? "243期当期" : "242期当期"])
            .ToArray();

        Assert.Null(RuleEngine.DetectIssueMismatch(samples, 243));
    }

    [Fact]
    public void DetectsAConsistentOtherIssueWhenSomeTextResultsOmitTheIssueNumber()
    {
        IReadOnlyList<string>[] samples = Enumerable.Range(0, 10)
            .Select(index => (IReadOnlyList<string>)(index < 5
                ? ["242期当期"]
                : ["云端返回了其他有效文字但漏掉期号数字"]))
            .ToArray();

        Assert.Equal(242, RuleEngine.DetectIssueMismatch(samples, 243));
    }

    [Fact]
    public void DoesNotTreatEmptyOrInsufficientCloudResultsAsIssueMismatch()
    {
        IReadOnlyList<string>[] withEmpty = Enumerable.Range(0, 10)
            .Select(index => (IReadOnlyList<string>)(index == 5 ? [] : ["242期当期"]))
            .ToArray();

        Assert.Null(RuleEngine.DetectIssueMismatch(withEmpty, 243));
        Assert.Null(RuleEngine.DetectIssueMismatch(withEmpty.Take(9), 243));
    }

    [Fact]
    public void SupportsFolderAliasWithoutUsingItAsFinalOcrValue()
    {
        var rule = new OcrRule("恩平公式", "生肖组合", null, "杀二肖", "杀二肖", "恩平");
        string image = Path.Combine("C:\\结果", "恩平", "sample.jpg");

        Assert.Single(RuleEngine.FindMatches(image, ["恩平公式杀二肖", "241期杀马猪"], [rule]));
        Assert.Empty(RuleEngine.FindMatches(image, ["恩平公式杀一尾", "241期杀3尾"], [rule]));
    }

    [Fact]
    public void DoesNotFuzzyMatchTwoCharacterKeywords()
    {
        var rules = new[] { new OcrRule("青玉", "合") };

        var actual = RuleEngine.FindMatches(["241期：青云杀一合07合"], rules);

        Assert.Empty(actual);
    }

    [Theory]
    [InlineData("生肖", "241期：关键词杀一肖猴", "猴")]
    [InlineData("合", "241期：关键词杀一合】07合开00", "07合")]
    [InlineData("段", "241期：关键词杀段[3段]开00", "3段")]
    [InlineData("头", "241期：关键词杀一头】4头", "4头")]
    [InlineData("尾", "241期：关键词杀一尾】0尾", "0尾")]
    [InlineData("色单双", "241期：关键词杀半波【绿单】特00", "绿单")]
    [InlineData("单生肖", "241关键词杀兔开鸭88", "兔")]
    [InlineData("五行", "241关键词资料网澳门【土水木火】开00", "土水木火")]
    public void ExtractsTypedValueFromRequestedIssue(string type, string line, string expected)
    {
        var rule = new OcrRule("关键词", type);

        Assert.Equal(expected, RuleEngine.ExtractValue([line], 241, rule));
    }

    [Fact]
    public void DoesNotExtractValueFromAnotherIssue()
    {
        var rule = new OcrRule("关键词", "生肖");

        Assert.Null(RuleEngine.ExtractValue(["240期：关键词猴", "241期：其他鼠"], 241, rule));
    }

    [Fact]
    public void FinalExtractionDoesNotUseLocalOcrAsFallback()
    {
        var rule = new OcrRule("关键词", "生肖");

        Assert.Null(RuleEngine.ExtractFinalValue(["241期：关键词"], 241, rule));
    }

    [Fact]
    public void FinalExtractionUsesOnlyTheCloudValueAfterLocalCandidateMatch()
    {
        var rule = new OcrRule("傻丫头", "生肖");

        Assert.Equal("龙", RuleEngine.ExtractFinalValue(["234期杀兔龙傻丫头", "241期杀龙"], 241, rule));
    }

    [Fact]
    public void FormatsEveryRuleAndMarksMissingValues()
    {
        var rules = new[]
        {
            new OcrRule("南国挽心", "生肖"),
            new OcrRule("天之涯", "合")
        };
        var values = new Dictionary<string, string> { ["南国挽心"] = "猴" };

        Assert.Equal(["猴 南国挽心", "缺失 天之涯"], RuleEngine.FormatOutput(rules, values));
    }

    [Theory]
    [InlineData(false, false, "未找到对应图片")]
    [InlineData(true, false, "图片文字识别失败")]
    [InlineData(true, true, "未识别到当期目标数据")]
    public void DescribesTheActualReasonForAMissingResult(
        bool foundImage,
        bool recognizedText,
        string expected)
    {
        Assert.Equal(expected, RuleEngine.DescribeMissing(foundImage, recognizedText));
    }

    [Fact]
    public void IncludesTheKnownReasonWhenFormattingAMissingResult()
    {
        var rule = new OcrRule("小马哥", "号码:4");
        var reasons = new Dictionary<string, string>
        {
            [rule.Id] = "未找到对应图片"
        };

        Assert.Equal(
            ["缺失（未找到对应图片） 小马哥"],
            RuleEngine.FormatOutput([rule], new Dictionary<string, string>(), reasons));
    }

    [Fact]
    public void FormatsFinalValuesForNumbersTailsAndFiveElementReverseInference()
    {
        OcrRule[] rules =
        [
            new("小马哥", "号码:4"),
            new("妈祖两尾", "尾数组合"),
            new("天机阁五行", "五行"),
            new("普通五行", "五行")
        ];
        var values = new Dictionary<string, string>
        {
            ["小马哥"] = "03 09 21 25",
            ["妈祖两尾"] = "1尾+4尾",
            ["天机阁五行"] = "金木水火",
            ["普通五行"] = "金木水火"
        };

        Assert.Equal(
        [
            "03,09,21,25 小马哥",
            "1尾 4尾 妈祖两尾",
            "土行 天机阁五行",
            "土行 普通五行"
        ], RuleEngine.FormatOutput(rules, values));
    }

    [Fact]
    public void ExtractsExactlyFourElementsForReverseInference()
    {
        var rule = new OcrRule("五行中特码", "五行");

        Assert.Equal("金木水火", RuleEngine.ExtractFinalValue(
            ["241期 必中【 金 木 水 火 】码 开??"], 241, rule));
        Assert.Null(RuleEngine.ExtractFinalValue(
            ["241期 必中【 金 木 水 】码 开??"], 241, rule));
    }

    [Fact]
    public void KeepsMultipleItemsForTheSameKeywordSeparate()
    {
        var rules = new[]
        {
            new OcrRule("紫燕儿", "生肖", "紫燕儿杀一肖"),
            new OcrRule("紫燕儿", "尾", "紫燕儿尾")
        };
        var values = new Dictionary<string, string> { ["紫燕儿杀一肖"] = "鸡" };

        Assert.Equal(2, RuleEngine.FindMatches(["紫燕儿241期新澳杀鸡"], rules).Count);
        Assert.Equal(["鸡 紫燕儿杀一肖", "缺失 紫燕儿尾"], RuleEngine.FormatOutput(rules, values));
    }

    [Theory]
    [InlineData("生肖组合", "241期杀蛇+鸡开", "蛇鸡")]
    [InlineData("生肖组合", "241期杀，马猪", "马猪")]
    [InlineData("尾数组合", "241期杀4+7尾开", "4尾+7尾")]
    public void ExtractsCombinationValues(string type, string line, string expected)
    {
        var rule = new OcrRule("君军", type);

        Assert.Equal(expected, RuleEngine.ExtractValue(["君军新澳料", line], 241, rule));
    }

    [Theory]
    [InlineData("半头", "242期秒杀半头[3头双]开888", "3头双")]
    [InlineData("头数组合", "242期中1234头", "1头+2头+3头+4头")]
    [InlineData("头数组合", "242期，0123头", "0头+1头+2头+3头")]
    [InlineData("缺头", "242期中1234头", "0头")]
    [InlineData("缺头", "242期，0123头", "4头")]
    [InlineData("色单双", "242期禁兰双", "蓝双")]
    [InlineData("色单双", "242期禁兰单", "蓝单")]
    public void ExtractsNewYanranTypes(string type, string line, string expected)
    {
        Assert.Equal(expected, RuleEngine.ExtractFinalValue([line], 242, new OcrRule("目录名", type)));
    }

    [Theory]
    [InlineData("月来月好", "月来月好", "羊")]
    [InlineData("欧阳肖", "欧阳", "猴")]
    [InlineData("苏柒若", "苏柒若", "兔")]
    public void ExtractsZodiacFromTheNamedRowInACurrentIssueSummary(
        string label,
        string keyword,
        string expected)
    {
        string[] lines =
        [
            "GG团队242期新澳禁肖统计表",
            "Alice正正正正禁鼠",
            "欧阳正正正正禁猴",
            "苏柒若正正正禁兔",
            "月来月好正正禁羊"
        ];

        Assert.Equal(expected, RuleEngine.ExtractFinalValue(
            lines,
            242,
            new OcrRule(keyword, "生肖", label)));
    }

    [Fact]
    public void AcceptsOutputLabelWhenCloudOcrUsesTheDisplayedTitle()
    {
        var rule = new OcrRule("九宫寻肖", "生肖", "九宫格肖肖");

        Assert.Equal("虎", RuleEngine.ExtractFinalValue(
            ["第243期 九宫格肖肖 虎"], 243, rule));
    }

    [Fact]
    public void TimePointNumbersNeedNoIssueMarker()
    {
        var rule = new OcrRule("十点半集团大围", "号码:36", "时点半", IgnoreIssue: true);
        string[] lines = ["十点半集团大围 36码", "28 01 06 47 42 49", "27 21 19 09 08 45", "43 17 35 12 14 29", "07 39 41 46 37 23", "48 10 13 16 26 15", "11 31 22 38 33 32"];
        string expected = "28 01 06 47 42 49 27 21 19 09 08 45 43 17 35 12 14 29 07 39 41 46 37 23 48 10 13 16 26 15 11 31 22 38 33 32";

        // 时点半 identity comes from the matched candidate/template; no issue
        // marker is required and only the body numbers are validated.
        Assert.Equal(expected, RuleEngine.ExtractFinalValue(lines, 243, rule));
        Assert.Equal(expected, RuleEngine.ExtractFinalValue(["243期", ..lines], 243, rule));
    }

    [Fact]
    public void ExtractsNearbyZodiacWhenCardPlacesValueBeforeIssue()
    {
        var rule = new OcrRule("九宫寻肖", "生肖", "九宫格肖肖", AllowNearbyValue: true);
        Assert.Equal("虎", RuleEngine.ExtractFinalValue(["九宫格肖肖", "虎", "第243期"], 243, rule));
    }

    [Fact]
    public void ExtractsNearbyZodiacWhenTemplateCropOmitsTheHeading()
    {
        var rule = new OcrRule("九宫寻肖", "生肖", "九宫格肖肖", AllowNearbyValue: true, StrictIssueBlock: true);
        Assert.Equal("羊", RuleEngine.ExtractFinalValue(["羊", "第244期"], 244, rule));
    }

    [Fact]
    public void ExtractsJieshaoRealCardRowForSelectedIssue()
    {
        var rule = new OcrRule("杰少", "生肖", "杰少杀一肖", "杀①肖", "原创杰少", "杰少");

        string[] lines = ["原创杰少", "{248期}新澳杀①肖：女狗开猫50准"];
        Assert.Equal("狗", RuleEngine.ExtractFinalValue(lines, 248, rule));
        Assert.Null(RuleEngine.ExtractFinalValue(lines, 247, rule));
    }

    [Fact]
    public void DoesNotUseNearbyZodiacFromDifferentIssue()
    {
        var rule = new OcrRule("九宫寻肖", "生肖", "九宫格肖肖", AllowNearbyValue: true, StrictIssueBlock: true);

        Assert.Null(RuleEngine.ExtractFinalValue(["羊", "第243期", "第244期"], 244, rule));
        Assert.Null(RuleEngine.ExtractFinalValue(["第243期", "第244期", "虎"], 244, rule));
        Assert.Null(RuleEngine.ExtractFinalValue(["第244期", "第243期", "虎"], 244, rule));
    }

    [Fact]
    public void DoesNotUseIncompleteNearbyZodiacAsSuccess()
    {
        var rule = new OcrRule("九宫寻肖", "生肖", "九宫格肖肖", AllowNearbyValue: true);

        Assert.Null(RuleEngine.ExtractFinalValue(["九宫格肖肖", "第244期", "宣传文字"], 244, rule));
    }

    [Theory]
    [InlineData("水哥杀一肖", "水哥肖", "龍", "龙")]
    [InlineData("包公图", "包公肖肖", "羊", "羊")]
    [InlineData("禁肖图", "禁止肖肖", "猴", "猴")]
    [InlineData("关公杀一肖又来了", "关公又来了肖", "虎", "虎")]
    [InlineData("王者九点禁一肖", "王者肖肖", "猴", "猴")]
    [InlineData("小精一一禁肖", "小精肖肖", "马", "马")]
    [InlineData("九宫寻肖", "九宫格肖肖", "兔", "兔")]
    public void ExtractsStandalonePosterZodiacSeveralLinesFromTheIssue(
        string keyword, string label, string posterValue, string expected)
    {
        var rule = new OcrRule(
            keyword, "生肖", label, AllowNearbyValue: true, StrictIssueBlock: true);
        string[] lines =
        [
            "第247期",
            keyword,
            "宣传文字",
            "更多宣传文字",
            posterValue,
            "上期开奖结果：22 24 19 10 20 01 T30"
        ];

        Assert.Equal(expected, RuleEngine.ExtractFinalValue(lines, 247, rule));
        Assert.Null(RuleEngine.ExtractFinalValue(lines, 248, rule));
    }

    [Fact]
    public void CurrencyGlyphIsNotHardCodedIntoAZodiacGuess()
    {
        var baogong = new OcrRule(
            "包公图", "生肖", "包公肖肖", AllowNearbyValue: true, StrictIssueBlock: true);
        var other = new OcrRule(
            "禁肖图", "生肖", "禁止肖肖", AllowNearbyValue: true, StrictIssueBlock: true);
        string[] lines = ["包公图", "第247期", "杀一肖一码", "￥", "24"];

        Assert.Null(RuleEngine.ExtractFinalValue(lines, 247, baogong));
        Assert.Null(RuleEngine.ExtractFinalValue(lines, 247, other));
    }

    [Fact]
    public void MatchesTraditionalDianVariantOfWangzheTitle()
    {
        OcrRule rule = new("王者九点禁一肖", "生肖", "王者肖肖", AllowNearbyValue: true);

        Assert.Contains(rule, RuleEngine.FindMatches(["王者九點禁一宵", "第244期", "鸡"], [rule]));
        Assert.Equal("鸡", RuleEngine.ExtractFinalValue(["王者九點禁一宵", "第244期", "鸡"], 244, rule));
    }

    [Fact]
    public void ExtractsMissingTailFromConcatenatedPrinceTailDigits()
    {
        OcrRule rule = new("公子送尾数", "缺尾", "翩翩公子尾", AllowValueWithoutKeyword: true);

        Assert.Equal("8尾", RuleEngine.ExtractFinalValue(
            ["翩翩公子九尾必中", "244期", "公子送尾数：0123456", "79准！", "开??"],
            244,
            rule));
    }

    [Fact]
    public void DoesNotUseUnrelatedTailWhenKeywordIsMissing()
    {
        OcrRule rule = new("公子送尾数", "缺尾", "翩翩公子尾", AllowValueWithoutKeyword: true);

        Assert.Null(RuleEngine.ExtractFinalValue(["244期", "其他栏目：0123456", "79准！"], 244, rule));
    }

    [Fact]
    public void ExtractsMissingTailFromPrinceTailRow()
    {
        var rule = new OcrRule("公子送尾数", "缺尾", "翩翩公子尾");
        Assert.Equal("8尾", RuleEngine.ExtractFinalValue(
            ["243期", "公子送尾数：0 1 2 3 4 5 6 7 9 准！", "开??"], 243, rule));
    }

    [Fact]
    public void ExtractsGreenAppleSixNumbersFromCurrentIssueRow()
    {
        var rule = new OcrRule("杀六码", "号码:6", "青苹果");
        Assert.Equal("09 11 12 29 47 49", RuleEngine.ExtractFinalValue(
            ["243期杀六码:09.11.12.29.47.49开00猫准"], 243, rule));
    }

    [Fact]
    public void ExtractsSplitSummaryRowButDoesNotBorrowTheNextPersonsZodiac()
    {
        string[] splitRow = ["GG团队242期新澳", "禁", "肖统计表", "苏柒若正正", "禁", "兔"];
        string[] missingRow = ["GG团队242期新澳禁肖统计表", "苏柒若正正", "月来月好正正禁羊"];
        var rule = new OcrRule("苏柒若", "生肖");

        Assert.Equal("兔", RuleEngine.ExtractFinalValue(splitRow, 242, rule));
        Assert.Null(RuleEngine.ExtractFinalValue(missingRow, 242, rule));
    }

    [Theory]
    [MemberData(nameof(NewYanranNumberSamples))]
    public void ExtractsNewYanranNumberSamples(string type, string[] lines, string expected)
    {
        Assert.Equal(expected, RuleEngine.ExtractFinalValue(lines, 242, new OcrRule("目录名", type)));
    }

    public static TheoryData<string, string[], string> NewYanranNumberSamples => new()
    {
        { "号码:2", ["正242期正【长安之星***100新澳门六合彩", "100***杀码】【20,45】开00..对鸿运论坛"], "20 45" },
        { "号码:3", ["潮汕陈龙", "242期", "禁05.17.29"], "05 17 29" },
        { "号码:2", ["Alice", "241期", "22.36开04", "242期", "01.45开"], "01 45" },
        { "号码:5", ["242期绝杀五码[05134647", "48开888"], "05 13 46 47 48" }
    };

    [Fact]
    public void ExtractsDragonKingThirtySixNumbersFromTheCurrentFourLineLayout()
    {
        string[] lines =
        [
            "龙王庙【36个码中特码】付费版",
            "242期",
            "01 02 03 04 05 07 10 11 12",
            "13 16 18 20 21 22 23 24 26",
            "27 29 30 31 32 33 34 35 36",
            "37 40 41 42 43 46 47 48 49",
            "开??"
        ];

        Assert.Equal(
            "01 02 03 04 05 07 10 11 12 13 16 18 20 21 22 23 24 26 27 29 30 31 32 33 34 35 36 37 40 41 42 43 46 47 48 49",
            RuleEngine.ExtractFinalValue(lines, 242, new OcrRule(
                "龙王庙36个中特码付费版",
                "号码:36",
                "龙王")));
    }

    [Fact]
    public void RequiresTheUniqueEnpingFourHeadHistoryInsideTheSharedFolder()
    {
        var rule = new OcrRule("恩平澳彩四头", "缺头", "恩平杀头", null, "220期止38期错5", "恩平");
        string image = Path.Combine("C:\\结果", "恩平", "sample.jpg");

        Assert.Empty(RuleEngine.FindMatches(image, ["恩平澳彩四头", "242期2031头"], [rule]));
        Assert.Single(RuleEngine.FindMatches(
            image,
            ["恩平澳彩四头，220期止38期错5", "242期0123头"],
            [rule]));
    }

    [Theory]
    [InlineData("号码:10", "241期杀：02 15 20 26 29 31 37 43 44 45 开??", "02 15 20 26 29 31 37 43 44 45")]
    [InlineData("号码:4", "241期 马(49) 鸡(34) 龙(03) 猴(35) 开??", "49 34 03 35")]
    [InlineData("号码:36", "241期 01 02 03 04 05 06 07 08 09 10 11 12 13 14 15 16 17 18 19 20 21 22 23 24 25 26 27 28 29 30 31 32 33 34 35 36 开??", "01 02 03 04 05 06 07 08 09 10 11 12 13 14 15 16 17 18 19 20 21 22 23 24 25 26 27 28 29 30 31 32 33 34 35 36")]
    public void ExtractsConfiguredNumberCount(string type, string line, string expected)
    {
        Assert.Equal(expected, RuleEngine.ExtractFinalValue([line], 241, new OcrRule("目录名", type)));
    }

    [Fact]
    public void ExtractsNumbersSplitBeforeAndAfterTheIssueCell()
    {
        string[] lines =
        [
            "庄家必杀:03 09 21 25 26 33 35 39",
            "241期开??",
            "40 45 46 47",
            "庄家必杀:07 08 11 19 22 24 27 28",
            "240期龍27错"
        ];

        Assert.Equal(
            "03 09 21 25 26 33 35 39 40 45 46 47",
            RuleEngine.ExtractFinalValue(lines, 241, new OcrRule("小马哥", "号码:12")));
    }

    [Theory]
    [MemberData(nameof(StrictSplitNumberSamples))]
    public void ExtractsStrictNumberRowsSplitByTheIssueAndOpeningColumns(
        string id,
        string type,
        string[] lines,
        string expected)
    {
        foreach (int issue in new[] { 7, 246, 1001 })
        {
            string[] dynamicLines = lines
                .Select(line => line.Replace("{issue}", issue.ToString(), StringComparison.Ordinal)
                    .Replace("{previous}", (issue - 1).ToString(), StringComparison.Ordinal))
                .ToArray();
            Assert.Equal(expected, RuleEngine.ExtractFinalValue(
                dynamicLines, issue, new OcrRule(id, type, id, StrictIssueBlock: true)));
        }
    }

    public static TheoryData<string, string, string[], string> StrictSplitNumberSamples => new()
    {
        {
            "小马哥", "号码:12",
            ["庄家必杀:03 05 09 16 21 30 33 34", "{issue}期开??", "40 41 46 48", "{previous}期"],
            "03 05 09 16 21 30 33 34 40 41 46 48"
        },
        {
            "狗庄", "号码:12",
            ["01 02 04 20 21 26 27 28 31 35 43", "{issue}期开??", "49", "{previous}期"],
            "01 02 04 20 21 26 27 28 31 35 43 49"
        },
        {
            "杀料", "号码:10",
            ["杀→03 05 06 10 19 22 29 32", "{issue}期开??", "40 49", "{previous}期"],
            "03 05 06 10 19 22 29 32 40 49"
        },
        {
            "天线宝杀", "号码:12",
            ["12码→11 15 26 28 31 34 41 43 44 45 46", "{issue}期开??", "48←精选杀", "{previous}期"],
            "11 15 26 28 31 34 41 43 44 45 46 48"
        },
        {
            "金钱网", "号码:12",
            ["02 04 07 08 16 21 23 26 28", "{issue}期开??", "30 43 44", "{previous}期"],
            "02 04 07 08 16 21 23 26 28 30 43 44"
        }
    };

    [Fact]
    public void ExtractsStrictSplitNumberRowWhenTheHeadingContainsAPercentage()
    {
        string[] lines =
        [
            "金钱网【必杀12个特码】100%准",
            "杀特码：02", "04", "07", "08", "16", "21", "23", "26", "28",
            "246期", "开？？", "30", "43", "44",
            "245期", "杀特码：10", "16", "18", "21", "22", "24", "25", "32", "38", "牛18错", "45", "46", "49"
        ];

        Assert.Equal(
            "02 04 07 08 16 21 23 26 28 30 43 44",
            RuleEngine.ExtractFinalValue(
                lines, 246, new OcrRule("金钱网必杀12个特码", "号码:12", "金钱网", StrictIssueBlock: true)));
    }

    [Fact]
    public void ExtractsStandaloneZodiacWithoutBorrowingZodiacsFromThePosterBorder()
    {
        var rule = new OcrRule(
            "九宫寻肖", "生肖", "九宫格肖肖", AllowNearbyValue: true, StrictIssueBlock: true);

        Assert.Equal("羊", RuleEngine.ExtractFinalValue(
            ["羊", "第246期", "六合彩大赛马会*六合彩*赛马会大六合彩"], 246, rule));
    }

    [Fact]
    public void ExtractsXiaotengKillZodiacFromVerticalCloudOcrColumns()
    {
        string[] lines =
        [
            "准准准准准准准准准准准准", "921275799180", "331110240210",
            "龙猪猴羊虎虎龙马狗狗牛發", "肖降", "横财杀一肖", "財源液滚", "回富",
            "开开开开开开开开开开开开", "》》》》》》》》》》》》", "天降横財",
            "鸡马牛虎龙鸡猪鼠猪兔马龙", "才貝", "肖肖肖肖肖肖肖肖肖肖肖肖",
            "杀杀杀杀杀杀杀杀杀杀杀杀", "黄金万两", "□□□□□□□□□□□□□□□",
            "期期期期期期期期期期期期", "456789012356", "333333444444", "222222222222"
        ];
        var rule = new OcrRule(
            "骁腾杀一肖", "生肖", "骁腾杀肖", RequiredKeyword: "杀一肖",
            Folder: "骁腾系列", AllowNearbyValue: true, StrictIssueBlock: true);

        Assert.Equal("龙", RuleEngine.ExtractFinalValue(lines, 246, rule));
        Assert.Equal("马", RuleEngine.ExtractFinalValue(lines, 245, rule));
        Assert.Null(RuleEngine.ExtractFinalValue(lines, 244, rule));
    }

    [Fact]
    public void ExtractsStandaloneZodiacFromDuplicatedSingleIssuePosterOcr()
    {
        var rule = new OcrRule(
            "九宫寻肖", "生肖", "九宫格肖肖", AllowNearbyValue: true, StrictIssueBlock: true);

        foreach (int issue in new[] { 7, 246, 1001 })
            Assert.Equal("羊", RuleEngine.ExtractFinalValue(
                ["羊", $"第{issue}期", "边框文字", $"第{issue}期", "合"], issue, rule));
    }

    [Fact]
    public void ExplainsWhyRecognizedNumberTextFailedValidation()
    {
        var rule = new OcrRule("金钱网必杀12个特码", "号码:12", "金钱网", StrictIssueBlock: true);

        Assert.Equal(
            "已找到候选图片和246期文字，但未通过12个两位数号码校验（要求01-49且不重复）",
            RuleEngine.DescribeExtractionFailure(["246期 01 02 03"], 246, rule));
    }

    [Fact]
    public void IncludesALabeledNumberRowImmediatelyAfterTheIssueCell()
    {
        string[] lines =
        [
            "老人味【绝杀6码】必中",
            "241期",
            "不开:03 08 20 34 35 48",
            "开??"
        ];

        Assert.Equal(
            "03 08 20 34 35 48",
            RuleEngine.ExtractFinalValue(lines, 241, new OcrRule("老人杀", "号码:6")));
    }

    [Fact]
    public void DoesNotTreatAConcatenatedPairContinuationAsAnotherIssue()
    {
        string[] lines =
        [
            "241期",
            "杀→0215202629313743",
            "4445",
            "开？？",
            "240期"
        ];

        Assert.Equal(
            "02 15 20 26 29 31 37 43 44 45",
            RuleEngine.ExtractFinalValue(lines, 241, new OcrRule("杀料", "号码:10")));
    }

    [Fact]
    public void RejectsDuplicateOcrNumbersEvenWhenTheUniqueCountMatches()
    {
        Assert.Null(RuleEngine.ExtractFinalValue(
            ["241期 01 02 02 03 04 开??"],
            241,
            new OcrRule("重复号码", "号码:4")));
    }

    [Theory]
    [InlineData("241期 1 02 03 04 开??", false)]
    [InlineData("241期 01 02 03 4 开??", true)]
    [InlineData("241期 00 02 03 04 开??", false)]
    [InlineData("241期 01 02 03 50 开??", true)]
    [InlineData("241期 01 02 03 04 05 开??", false)]
    public void RejectsNumberRowsThatViolateTheSharedHardLimits(string line, bool strict)
    {
        Assert.Null(RuleEngine.ExtractFinalValue(
            [line],
            241,
            new OcrRule("号码资料", "号码:4", StrictIssueBlock: strict)));
    }

    [Theory]
    [InlineData("241期 01020304 开??", false)]
    [InlineData("241期 01020304 开??", true)]
    public void AcceptsConcatenatedNumbersOnlyWhenTheySplitIntoCompleteTwoDigitValues(string line, bool strict)
    {
        Assert.Equal("01 02 03 04", RuleEngine.ExtractFinalValue(
            [line],
            241,
            new OcrRule("号码资料", "号码:4", StrictIssueBlock: strict)));
    }

    [Fact]
    public void IgnoresTheConfiguredCountPrintedBeforeTheActualNumbers()
    {
        Assert.Equal(
            "02 04 05 06 07 08 09 10 11 13",
            RuleEngine.ExtractFinalValue(
                ["35码赛马会02 04 05 06", "241期07 08 09 10 11 13开??"],
                241,
                new OcrRule("赛马会", "号码:10")));
    }

    [Fact]
    public void IgnoresAConfiguredCountWrittenAsNumberOfSpecialCodes()
    {
        Assert.Equal(
            "02 03 06 13 20 22 25 30 31 34 37 41",
            RuleEngine.ExtractFinalValue(
                ["姨妈封杀→[12个特码]", "241期杀02 03 06 13 20 22 25 30 31 34开??", "37 41", "240期"],
                241,
                new OcrRule("姨妈", "号码:12")));
    }

    [Fact]
    public void ExtractsARealTenNumberRowWhoseLastPairIsASeparateDigitRun()
    {
        string[] lines =
        [
            "聚彩堂[庄家必吃10码]收费版",
            "241期杀:杀:01 06 11 13 16 24 35 43开??",
            "4546",
            "240期杀:02 03 05 09 10 11 15 23龍27中"
        ];

        Assert.Equal(
            "01 06 11 13 16 24 35 43 45 46",
            RuleEngine.ExtractFinalValue(lines, 241, new OcrRule("聚彩", "号码:10")));
    }

    [Fact]
    public void ExtractsASingleNumberContinuationMarkedByAnArrow()
    {
        string[] lines =
        [
            "天线宝宝--杀12码",
            "12码→0608091014223132353637",
            "241期开??",
            "47←精选杀",
            "12码→0306071012141923373840",
            "240期龍27中"
        ];

        Assert.Equal(
            "06 08 09 10 14 22 31 32 35 36 37 47",
            RuleEngine.ExtractFinalValue(lines, 241, new OcrRule("天线宝杀", "号码:12")));
    }

    [Fact]
    public void RejectsAThirtySixNumberRowContainingADuplicateOcrNumber()
    {
        string[] lines =
        [
            "宝典宝典[特围36码]",
            "包围36码01 02 03 04 05 06 07",
            "08 0911 13 14 15 16 19 20",
            "241期22 23222324262728303132开??",
            "333439404142434447",
            "48 49",
            "包围36码02 03 04 05 07 08 09",
            "240期2930龍27"
        ];

        Assert.Null(RuleEngine.ExtractFinalValue(lines, 241, new OcrRule("宝典", "号码:36")));
    }

    [Fact]
    public void ReadsManyShortCloudLinesAroundTheIssueCell()
    {
        string[] lines =
        [
            "心水站三十六码，期期中一码",
            "36计：01", "02", "03", "04", "07", "08", "09", "101214151617181921",
            "241期", "222324252627303133", "开？中", "343738404344454647", "4849",
            "36计：02040607080910", "240期"
        ];

        Assert.Equal(
            "01 02 03 04 07 08 09 10 12 14 15 16 17 18 19 21 22 23 24 25 26 27 30 31 33 34 37 38 40 43 44 45 46 47 48 49",
            RuleEngine.ExtractFinalValue(lines, 241, new OcrRule("心水", "号码:36")));
    }

    [Theory]
    [MemberData(nameof(YanranVerticalThirtySixNumberSamples))]
    public void ReadsYanranThirtySixNumbersSplitIntoSingleLocalOcrLines(
        string[] lines,
        string expected)
    {
        Assert.Equal(
            expected,
            RuleEngine.ExtractFinalValue(lines, 244, new OcrRule("36码", "号码:36")));
    }

    public static TheoryData<string[], string> YanranVerticalThirtySixNumberSamples => new()
    {
        {
            [
                "244期:特码开在", "【發00准】", "身上!",
                "鼠", "牛", "虎", "兔", "龙", "蛇", "马", "羊", "猴", "鸡", "狗", "猪",
                "19", "06", "05", "04", "03", "02", "13", "12", "23", "10", "09", "08",
                "31", "18", "17", "16", "15", "14", "25", "24", "35", "22", "21", "20",
                "43", "42", "29", "40", "39", "38", "49", "36", "47", "46", "33", "32",
                "243期:特码开在"
            ],
            "19 06 05 04 03 02 13 12 23 10 09 08 31 18 17 16 15 14 25 24 35 22 21 20 43 42 29 40 39 38 49 36 47 46 33 32"
        },
        {
            [
                "243期:开奖结果:42-17-12-46-09-24特:狗21准", "Q",
                "01", "02", "03", "04", "05", "06", "07", "20", "09", "10", "11", "24",
                "37", "26", "27", "28", "29", "30", "19", "32", "21", "34", "35", "36",
                "49", "38", "39", "40", "41", "42", "31", "44", "33", "46", "47", "48",
                "244期:开奖结果:88-88-88-88-88-88特:發88准"
            ],
            "01 02 03 04 05 06 07 20 09 10 11 24 37 26 27 28 29 30 19 32 21 34 35 36 49 38 39 40 41 42 31 44 33 46 47 48"
        }
    };

    [Fact]
    public void ReadsColorNumbersBeforeTheTargetIssueWithoutBorrowingAppendedFallbackLines()
    {
        string[] numbers =
        [
            "01", "02", "03", "04", "05", "06", "07", "20", "09", "10", "11", "24",
            "37", "26", "27", "28", "29", "30", "19", "32", "21", "34", "35", "36",
            "49", "38", "39", "40", "41", "42", "31", "44", "33", "46", "47", "48"
        ];
        string[] lines =
        [
            "243期：开奖结果：42-17-12-46-09-24特：狗21准",
            .. numbers,
            "244期：开奖结果：88-88-88-88-88-88特：發88准",
            "02030507080912",
            "13141528291819322122353636",
            "25339404142314445344748",
            "242期:开奖结果:29-35-46-32-13-31特:狗09准"
        ];

        Assert.Equal(
            string.Join(' ', numbers),
            RuleEngine.ExtractFinalValue(
                lines,
                244,
                new OcrRule("彩图", "号码:36", "彩图", RequiredKeyword: "开奖结果", Folder: "36码")));
    }

    [Fact]
    public void ExtractsConcatenatedNumberPairsAroundIssueAndAfterOpeningMarker()
    {
        string[] lines =
        [
            "内幕网【必中36码】",
            "01020305060708111213141516",
            "241期",
            "17192023242627282930333436",
            "开？？",
            "38414243444546474849",
            "01030405070809101213141516",
            "240期"
        ];

        Assert.Equal(
            "01 02 03 05 06 07 08 11 12 13 14 15 16 17 19 20 23 24 26 27 28 29 30 33 34 36 38 41 42 43 44 45 46 47 48 49",
            RuleEngine.ExtractFinalValue(lines, 241, new OcrRule("内幕", "号码:36")));
    }

    [Fact]
    public void DoesNotBorrowNumberRowsFromTheNextIssue()
    {
        string[] lines =
        [
            "241期开??",
            "38 41 42 43 44 45 46 47 48 49",
            "01030405070809101213141516",
            "240期17181920212223242526272829龍27中"
        ];

        Assert.Null(RuleEngine.ExtractFinalValue(lines, 241, new OcrRule("内幕", "号码:36")));
    }

    [Fact]
    public void ExtractsTraditionalZodiacCharacters()
    {
        Assert.Equal(
            "兔鸡",
            RuleEngine.ExtractFinalValue(
                ["241期杀生肖:[兔雞]开??"],
                241,
                new OcrRule("白小姐", "生肖组合")));
    }

    [Fact]
    public void RejectsNumberRowsWithTheWrongCount()
    {
        Assert.Null(RuleEngine.ExtractFinalValue(
            ["241期 02 06 09 13 14 19 44 45 开??"],
            241,
            new OcrRule("通天资料", "号码:9")));
    }

    [Fact]
    public void InfersTheOnlyMissingTailFromNineTails()
    {
        Assert.Equal("5尾", RuleEngine.ExtractFinalValue(
            ["241期 狂赢九尾：0 1 2 3 4 6 7 8 9 开??"],
            241,
            new OcrRule("九尾", "缺尾")));
    }

    [Fact]
    public void InfersTheOnlyMissingTailFromContiguousDigits()
    {
        Assert.Equal("5尾", RuleEngine.ExtractFinalValue(
            ["241期 狂赢九尾：012346789 开??"],
            241,
            new OcrRule("九尾", "缺尾")));
    }

    [Fact]
    public void InfersPrinceMissingTailForCurrentIssue243()
    {
        Assert.Equal("8尾", RuleEngine.ExtractFinalValue(
            ["翩翩公子九尾必中", "243期 公子送尾数：0 1 2 3 4 5 6 7 9 准！ 开??"],
            243,
            new OcrRule("公子送尾数", "缺尾", "翩翩公子尾")));
    }

    [Fact]
    public void CollapsesRepeatedOcrTailDigitsToOneTail()
    {
        Assert.Equal("7尾", RuleEngine.ExtractFinalValue(
            ["241期一尾绝杀→77777开??"],
            241,
            new OcrRule("宝典尾", "尾")));
    }

    [Theory]
    [InlineData("241期 特码封杀→【0 1尾】 开??", "0尾+1尾")]
    [InlineData("241期 精杀【1 4】尾 开??", "1尾+4尾")]
    public void ExtractsTwoTailsWhenTheImageUsesOnlyWhitespace(string line, string expected)
    {
        Assert.Equal(expected, RuleEngine.ExtractFinalValue([line], 241, new OcrRule("两尾", "尾数组合")));
    }

    [Theory]
    [InlineData("铁甲小宝四头出特", "铁甲小宝", "244期【四头出特→→→0 2 3 4】开??", "1头")]
    [InlineData("特头必中四头准全年", "特头必中", "244期 0头 1头 3头 4头 开??", "2头")]
    public void InfersTheMissingHeadForNewMacauFourHeadCards(
        string keyword,
        string label,
        string line,
        string expected)
    {
        var rule = new OcrRule(keyword, "缺头", label);
        string? value = RuleEngine.ExtractFinalValue([line], 244, rule);

        Assert.Equal(expected, value);
        Assert.Equal(
            [$"{expected} {label}"],
            RuleEngine.FormatOutput([rule], new Dictionary<string, string> { [rule.Id] = value! }));
    }

    [Theory]
    [InlineData("241期 禁【2】头 开??", "2头")]
    [InlineData("241期 买 二头 会输很惨 开??", "2头")]
    public void ExtractsBracketedOrChineseHead(string line, string expected)
    {
        Assert.Equal(expected, RuleEngine.ExtractFinalValue([line], 241, new OcrRule("一头", "头")));
    }

    [Fact]
    public void ExtractsHeadValueAfterColonInsteadOfInstructionHead()
    {
        Assert.Equal(
            "0头",
            RuleEngine.ExtractFinalValue(
                ["245期 公子禁止1头：零头 准！"],
                245,
                new OcrRule("公子", "头")));
    }

    [Fact]
    public void ExtractsNewMacauHighQualityFormatsWithoutCrossContamination()
    {
        string[] thirtySixNumbers =
        [
            "245期【无错36码特围】→全网最早原创36码特围十——→开00准",
            "44 05 17 04 48 16 30 18 15 29 08 28 35 14 13 36 22 03",
            "25 46 43 01 02 42 40 06 23 20 39 09 34 24 07 33 12 26"
        ];

        Assert.Equal(
            "44 05 17 04 48 16 30 18 15 29 08 28 35 14 13 36 22 03 25 46 43 01 02 42 40 06 23 20 39 09 34 24 07 33 12 26",
            RuleEngine.ExtractFinalValue(
                [
                    "245期 澳门无错36码",
                    "44 05 17 04 48 16 30 18 15 29 08 28 35 14 13 36 22 03",
                    "25 46 43 01 02 42 40 06 23 20 39 09 34 24 07 33 12 26",
                    "开00准"
                ],
                245,
                new OcrRule("澳门无错36码", "号码:36")));
        Assert.Equal(
            "44 05 17 04 48 16 30 18 15 29 08 28 35 14 13 36 22 03 25 46 43 01 02 42 40 06 23 20 39 09 34 24 07 33 12 26",
            RuleEngine.ExtractFinalValue(thirtySixNumbers, 245, new OcrRule("全网最早原创36码特围", "号码:36")));
        Assert.Equal(
            "鸡牛",
            RuleEngine.ExtractFinalValue(["245期稳杀(2)肖【鸡牛】开00"], 245, new OcrRule("稳杀(2)肖", "生肖组合")));
        Assert.Equal(
            "3头",
            RuleEngine.ExtractFinalValue(["245期：绝杀一头■杀3头■开？00赢"], 245, new OcrRule("绝杀一头", "头")));
        Assert.Equal(
            "龙蛇",
            RuleEngine.ExtractFinalValue(
                ["245期【男人味稳杀二肖】→龙蛇 开？准", "245期【男人味六肖】→兔马猴鸡牛鼠"],
                245,
                new OcrRule("男人味稳杀二肖", "生肖组合", Section: "男人味稳杀二肖")));
        Assert.Equal(
            "猪牛",
            RuleEngine.ExtractFinalValue(["245期：绝杀2肖（猪牛）开？00准"], 245, new OcrRule("完美杀号心水料", "生肖组合")));
        Assert.Equal(
            "猪马鼠蛇虎羊牛猴鸡",
            RuleEngine.ExtractFinalValue(["2026-245 解九肖：猪马鼠蛇虎羊牛猴鸡"], 245, new OcrRule("解九肖", "九肖", AllowNearbyValue: true)));
    }

    [Fact]
    public void ExtractsZeroSegment()
    {
        Assert.Equal("0段", RuleEngine.ExtractFinalValue(
            ["242期 杀：0段!! 不会开"],
            242,
            new OcrRule("心水站期期绝杀一段", "段", "心水杀段")));
    }

    [Fact]
    public void ExtractsSpacedFiveElements()
    {
        Assert.Equal("金木水火", RuleEngine.ExtractFinalValue(
            ["241期 必中【 金 木 水 火 】码 开??"],
            241,
            new OcrRule("五行中特码", "五行")));
    }

    [Theory]
    [InlineData("241期绝杀二尾【6.7】开00", "6尾+7尾")]
    [InlineData("241《5尾+7尾》开?", "5尾+7尾")]
    public void ExtractsTwoTailFormats(string line, string expected)
    {
        Assert.Equal(expected, RuleEngine.ExtractFinalValue([line], 241, new OcrRule("目录名", "尾数组合")));
    }

    [Fact]
    public void ExtractsWhenKeywordIsOnlyInTheHeader()
    {
        var rule = new OcrRule("大小姐", "尾");

        Assert.Equal("2尾", RuleEngine.ExtractValue(["大小姐新澳杀尾", "【241期】【大小姐新澳杀尾】2尾开00"], 241, rule));
        Assert.Equal("狗", RuleEngine.ExtractValue(["木桃新澳门禁肖", "241期木桃禁（狗）开"], 241, new OcrRule("木桃", "生肖")));
    }

    [Fact]
    public void SupportsIssueNumberWithoutIssueCharacter()
    {
        var rule = new OcrRule("沁园春", "生肖组合");

        Assert.Equal("鼠虎", RuleEngine.ExtractValue(["241💗沁园春💗杀二肖【鼠虎】开特"], 241, rule));
    }

    [Fact]
    public void ExtractsTailNumberWhenTailCharacterIsInTheHeading()
    {
        var rule = new OcrRule("缘来如此", "尾", "缘来如此尾", "杀一尾");

        Assert.Equal("3尾", RuleEngine.ExtractValue(["缘来如此综合料", "《新澳门杀一尾》", "241期杀一尾【3】开00"], 241, rule));
    }

    [Fact]
    public void UsesSectionToDistinguishTwoItemsInOneImage()
    {
        string[] lines =
        [
            "钦差大臣", "澳门杀一肖(公式一)", "241期杀《羊》开鸡",
            "钦差大臣", "澳门杀一肖(公式二)", "241期杀《虎》开鸡"
        ];

        Assert.Equal("羊", RuleEngine.ExtractValue(lines, 241, new OcrRule("钦差大臣", "生肖", "钦差大臣公式一", "公式一")));
        Assert.Equal("虎", RuleEngine.ExtractValue(lines, 241, new OcrRule("钦差大臣", "生肖", "钦差大臣公式二", "公式二")));
    }

    [Fact]
    public void ReassemblesCloudOcrLinesSplitIntoSeveralPieces()
    {
        var rule = new OcrRule("杨丽珠", "生肖");

        Assert.Equal("虎", RuleEngine.ExtractValue(["241", "杨丽珠", "禁一肖", "【虎】开?"], 241, rule));
        Assert.Equal("猪", RuleEngine.ExtractValue(["===241期紫蝴", "蝶今期铁杀=(杀猪)【对】"], 241, new OcrRule("紫蝴蝶", "生肖")));
    }

    [Fact]
    public void PrefersRequestedIssueCandidateThatContainsTheKeyword()
    {
        var rule = new OcrRule("紫蝴蝶", "生肖");
        string[] lines = ["241期新澳门九肖", "虎牛猴猪马鸡兔龙狗开00", "===241期紫蝴", "蝶今期铁杀=(杀猪)对"];

        Assert.Equal("猪", RuleEngine.ExtractValue(lines, 241, rule));
    }

    [Fact]
    public void ExtractsTheNewMacauPremiumSamples()
    {
        Assert.Equal(
            "10 12 14 17 20 26 36 38 44 47",
            RuleEngine.ExtractFinalValue(
                ["会员特供【绝杀10码】VIP", "242期 绝杀:10 12 14 17 20 26 36 38 44 47 开??"],
                242,
                new OcrRule("会员特供绝杀10码", "号码:10", "会员特供杀十码")));
        Assert.Equal(
            "01 02 03 04 05 07 08 10 11 12 13 14 17 18 19 20 21 22 23 24 25 26 28 29 31 34 35 36 38 39 40 42 46 47 48 49",
            RuleEngine.ExtractFinalValue(
                [
                    "【表弟主36码】", "242期", "36码【01 02 03 04 05 07 08 10 11",
                    "12 13 14 17 18 19 20 21 22 23 24", "25 26 28 29 31 34 35 36 38 39 40", "42 46 47 48 49】开??"
                ],
                242,
                new OcrRule("表弟主36码", "号码:36", "表弟")));
        Assert.Equal(
            "1尾",
            RuleEngine.ExtractFinalValue(
                ["翩翩公子九尾必中", "242期 公子送尾数：0 2 3 4 5 6 7 8 9 开??"],
                242,
                new OcrRule("公子送尾数", "缺尾", "翩翩公子尾")));
        Assert.Equal(
            "猴",
            RuleEngine.ExtractFinalValue(
                ["翩翩公子杀一特肖", "242期 公子杀一肖：猴 猴 猴 开??"],
                242,
                new OcrRule("公子杀一肖", "生肖", "翩翩公子肖")));
        Assert.Equal(
            "红单",
            RuleEngine.ExtractFinalValue(
                ["翩翩公子妙算杀半波", "242期 公子秒杀半波：红波单 开??"],
                242,
                new OcrRule("公子秒杀半波", "色单双", "翩翩公子半波")));
        Assert.Equal(
            "木水火土",
            RuleEngine.ExtractFinalValue(
                ["翩翩公子四行码中特码", "242期 公子4行：木 水 火 土 开??"],
                242,
                new OcrRule("公子4行", "五行", "翩翩公子五行")));
        Assert.Equal(
            "05 19 30 31 32 34 39 40 42 48",
            RuleEngine.ExtractFinalValue(
                ["翩翩公子妙算杀10码", "242期 公子神算杀10码：05 19 30 31 32 34 39 40 42 48 开??"],
                242,
                new OcrRule("公子神算杀10码", "号码:10", "翩翩公子杀十码")));
        Assert.Equal(
            "1头",
            RuleEngine.ExtractFinalValue(
                ["杀一头", "242期 公子禁止1头：一头 开??"],
                242,
                new OcrRule("公子禁止1头", "头", "翩翩公子头")));
        Assert.Equal(
            "牛虎",
            RuleEngine.ExtractFinalValue(
                ["祥瑞阁 杀2肖", "242期 祥瑞阁杀：牛 虎 开??"],
                242,
                new OcrRule("祥瑞阁", "生肖组合", "祥瑞阁二肖", null, "杀2肖")));
        Assert.Equal(
            "01 02 03 04 05 06 07 08 11 12 13 14 17 18 19 22 23 25 26 27 28 30 33 34 35 36 37 38 40 41 42 44 46 47 48 49",
            RuleEngine.ExtractFinalValue(
                [
                    "祥瑞阁王中王包围36码", "242期", "01 02 03 04 05 06 07 08 11 12 13 14 17 18 19 22 23",
                    "25 26 27 28 30 33 34 35 36 37 38 40 41 42 44 46 47", "48 49 开??"
                ],
                242,
                new OcrRule("祥瑞阁王中王包围36码", "号码:36", "祥瑞阁")));
    }

    [Fact]
    public void ExtractsNewGroupBracketedAndDerivedValues()
    {
        Assert.Equal(
            "2段",
            RuleEngine.ExtractFinalValue(
                ["243期 小陈 杀一段【2段】开??"], 243,
                new OcrRule("小陈", "段", "68小陈")));
        Assert.Equal(
            "1头",
            RuleEngine.ExtractFinalValue(
                ["243期 赵高 杀一头【0234】开??"], 243,
                new OcrRule("赵高", "缺头", "68赵高")));
        Assert.Equal(
            "6尾",
            RuleEngine.ExtractFinalValue(
                ["243期 旺仔 杀一尾【6尾】开??"], 243,
                new OcrRule("旺仔", "尾", "68旺仔")));
        Assert.Equal(
            "土",
            RuleEngine.ExtractFinalValue(
                ["243期 兴旺 杀一行【土】开??"], 243,
                new OcrRule("兴旺", "单五行", "68兴旺")));
        Assert.Equal(
            "3头",
            RuleEngine.ExtractFinalValue(
                ["243期 蔷薇 四头中特 0 1 2 4 特??"], 243,
                new OcrRule("蔷薇", "缺头", "战狼蔷薇")));
    }

    [Fact]
    public void SameFolderRulesRequireTheirOwnImageKeyword()
    {
        string configuration = ResultFilePaths.ConfigurationDirectory(AppContext.BaseDirectory);
        IReadOnlyList<OcrRule> rules = RuleCatalog.Load(Path.Combine(configuration, "黄大仙新澳.json"));

        IReadOnlyList<OcrRule> matches = RuleEngine.FindMatches(
            @"C:\图片\8.31-黄大仙新澳\68\sample.jpg",
            ["243期 小陈 杀一段【2段】开??"],
            rules);

        Assert.Collection(matches, rule => Assert.Equal("68小陈", rule.Id));
    }

    [Fact]
    public void ExplicitFolderRulesNeverMatchImagesFromAnotherFolder()
    {
        var rule = new OcrRule(
            "绝杀二肖",
            "生肖组合",
            "高手两肖",
            RequiredKeyword: "绝杀二肖",
            Folder: "高手榜");

        Assert.Empty(RuleEngine.FindMatches(
            @"C:\图片\9.1-新澳高手\老男女味\sample.jpg",
            ["244期：绝杀二肖→虎羊←开？？"],
            [rule]));
        Assert.Single(RuleEngine.FindMatches(
            @"C:\图片\9.1-新澳高手\高手榜\sample.jpg",
            ["244期：绝杀二肖→虎羊←开？？"],
            [rule]));
    }

    [Fact]
    public void MatchesTheGalleryTraditionalChineseTitleInsideItsConfiguredFolder()
    {
        var rule = new OcrRule(
            "澳门无错36码",
            "号码:36",
            "图库",
            RequiredKeyword: "澳门无错36码",
            Folder: "36码围特");
        string[] lines =
        [
            "澳門無錯36碼",
            "244期",
            "开00准",
            "01.02.03.04.05.07.10.11.13.16.18.19",
            "20.21.22.23.24.25.26.29.30.31.32.34",
            "35.36.38.39.41.42.43.45.46.47.48.49"
        ];

        Assert.Single(RuleEngine.FindMatches(
            @"C:\图片\9.1-新澳高手\36码围特\sample.jpg",
            lines,
            [rule]));
    }

    [Fact]
    public void ExtractsTheCurrentManTwoZodiacs()
    {
        var rule = new OcrRule(
            "男人味稳杀二肖",
            "生肖组合",
            "男人牛",
            "男人味稳杀二肖",
            "男人味稳杀二肖",
            "老男女味");

        Assert.Equal("兔龙", RuleEngine.ExtractFinalValue(
            [
                "243期【男人味稳杀二肖】→虎兔 开狗21准",
                "244期【男人味稳杀二肖】→兔龙 开？准",
                "244期【男人味六肖】→兔马蛇羊猴鸡 开？准"
            ],
            244,
            rule));
    }

    [Fact]
    public void ExtractsHighMountainNineZodiacsFromTheNineZodiacTier()
    {
        var rule = new OcrRule(
            "高山流水",
            "九肖",
            RequiredKeyword: "精选⑨肖",
            Folder: "高山流水",
            AllowNearbyValue: true);
        string[] lines =
        [
            "高山流水新澳门",
            "第247期",
            "247期精选⑨肖：虎猪马蛇羊猴鼠鸡狗",
            "247期精选⑦肖：虎猪马蛇羊猴鼠",
            "247期精选⑤肖：虎猪马蛇羊",
            "247期精选③肖：虎猪马",
            "247期精选①肖：虎"
        ];

        Assert.Equal(rule, Assert.Single(RuleEngine.FindMatches(
            @"C:\结果\9.4-新澳高手\高山流水\图.jpg", lines, [rule])));
        Assert.Equal("虎猪马蛇羊猴鼠鸡狗", RuleEngine.ExtractFinalValue(lines, 247, rule));
    }

    [Fact]
    public void ExtractsDogNineZodiacsFromTheHeadingWhenTheIssueIsElsewhere()
    {
        var rule = new OcrRule(
            "解九肖",
            "九肖",
            "跑狗",
            RequiredKeyword: "解九肖",
            Folder: "跑狗图",
            AllowNearbyValue: true);

        Assert.Equal("蛇龙羊虎猴鸡牛鼠狗", RuleEngine.ExtractFinalValue(
            [
                "2026-244",
                "澳门跑狗",
                "解九肖：蛇龙羊虎猴鸡牛鼠狗",
                "244期09月01日[虎冲猴]243期：42 17 12 46 09 24特21狗"
            ],
            244,
            rule));
    }

    [Fact]
    public void IgnoresZeroIssueNoiseWhenFindingCandidateValues()
    {
        var rule = new OcrRule("杀肖", "生肖");

        Assert.False(RuleEngine.HasValueForAnyIssue(["000期杀猴"], rule));
    }
}
