using OcrLineTool;

namespace OcrLineTool.Tests;

public sealed class HuangdaxianRuleHardeningTests
{
    private static IReadOnlyList<OcrRule> Rules => RuleCatalog.Load(Path.Combine(
        ResultFilePaths.ConfigurationDirectory(AppContext.BaseDirectory), "黄大仙新澳.json"));

    public static TheoryData<string, string, string> Samples => new()
    {
        { "68小陈", "🤖小陈🤖杀一段【4段】开", "4段" },
        { "68赵高", "赵高杀一头【0134】开", "2头" },
        { "68宝爷", "宝爷杀一肖【兔】开", "兔" },
        { "68兴旺", "兴旺杀一行【土】开", "土" },
        { "68老大", "九肖中特（蛇虎牛猪狗马鸡龙羊）开", "蛇虎牛猪狗马鸡龙羊" },
        { "68旺仔", "旺仔杀一尾【1尾】开", "1尾" },
        { "68凯哥", "凯哥杀五码【03,19,22,39,40】开", "03 19 22 39 40" },
        { "68波波", "波波杀半波【绿双】开", "绿双" },
        { "香奈风清扬", "四头出特『0-2-3-4』开", "1头" },
        { "香奈微风细雨", "⑨肖中特【羊鸡狗马虎兔猴蛇猪】开", "羊鸡狗马虎兔猴蛇猪" },
        { "香奈老大", "四行中特【水土火金】开", "水土火金" },
        { "香奈肖肖", "禁肖【猪】禁尾【3尾】特", "猪" },
        { "香奈尾", "禁肖【猪】禁尾【3尾】特", "3尾" },
        { "战狼八戒", "绝杀二肖【龙鼠】开", "龙鼠" },
        { "战狼小馒头", "【杀:-羊-】开", "羊" },
        { "战狼九点", "『05 10 14 17 27 28 31』开", "05 10 14 17 27 28 31" },
        { "战狼天空", "九肖【鸡猪虎兔蛇马猴狗牛】开", "鸡猪虎兔蛇马猴狗牛" },
        { "战狼蔷薇", "🍀0 1 2 3🍀特", "4头" },
        { "战狼老王", "老王杀半头{4头单}开", "4头单" },
        { "红人关公肖", "关公杀一肖【蛇】开猫", "蛇" },
        { "红人关公尾", "关公杀一尾【3尾】开00", "3尾" },
        { "红人极点肖", "🍂极点🍂杀一肖【鸡】开猫", "鸡" },
        { "绿格子双杀", "绝杀2肖【龙、狗】开?00准", "龙狗" },
        { "完美两肖", ":绝杀2肖(龙马】开？00准", "龙马" }
    };

    [Fact]
    public void CatalogEnablesStrictDynamicIssueValidationForEveryRule()
    {
        Assert.Equal(24, Rules.Count);
        Assert.All(
            Rules.Where(rule => rule.Id != "完美两肖"),
            rule => Assert.True(rule.StrictIssueBlock));
        Assert.Equal("缺头", Assert.Single(Rules, rule => rule.Id == "香奈风清扬").Type);
        OcrRule green = Assert.Single(Rules, rule => rule.Id == "绿格子双杀");
        Assert.Equal("生肖组合", green.Type);
        Assert.Equal("综合", green.Folder);
        OcrRule perfect = Assert.Single(Rules, rule => rule.Id == "完美两肖");
        Assert.Equal("生肖组合", perfect.Type);
        Assert.Equal("两版绿杀+完美杀", perfect.Folder);
        Assert.False(perfect.StrictIssueBlock);
    }

    [Theory]
    [MemberData(nameof(Samples))]
    public void ExtractsEveryConfirmedMaterialFromAnySelectedIssue(string id, string row, string expected)
    {
        OcrRule rule = Assert.Single(Rules, rule => rule.Id == id);
        foreach (int issue in new[] { 7, 245, 1001 })
        {
            string[] lines = [rule.Keyword, $"{issue}期{row}"];
            Assert.Equal(expected, RuleEngine.ExtractValue(lines, issue, rule));
            Assert.Equal(expected, RuleEngine.ExtractFinalValue(lines, issue, rule));
            Assert.Null(RuleEngine.ExtractFinalValue(lines, issue + 1, rule));
        }
    }

    [Fact]
    public void ZhanLangJiuDianDeduplicatesPrintedDuplicates()
    {
        OcrRule rule = Assert.Single(Rules, item => item.Id == "战狼九点");
        string[] lines =
        [
            "战狼团队原创九點半", "新澳", "⑨點半杀码",
            "249期:『09111423 28 3843』开23",
            "250期:『11131617203348』开14√",
            "251期：『05062325364549』开30√",
            "252期：010504 07273349』开22√",
            "253期：『04102026 273048』开16√",
            "254期：05121718363741』开02√",
            "255期：『0506101526 3349』开44√",
            "256期:『03 16 16 28 3143 44』开✓"
        ];

        Assert.Equal("03 16 28 31 43 44", RuleEngine.ExtractFinalValue(lines, 256, rule));
        Assert.True(RuleEngine.IsFormattedOutputValueValid(rule, "03,16,28,31,43,44"));
        Assert.False(RuleEngine.IsFormattedOutputValueValid(rule, "03,16,16,28,31,43,44"));
        Assert.False(RuleEngine.IsFormattedOutputValueValid(rule, "03,16,28,31,43,44,45,46"));
    }

    [Fact]
    public void DerivedValuesAreFormattedAsTheUniqueMissingItem()
    {
        var values = new Dictionary<string, string>
        {
            ["68赵高"] = "2头",
            ["香奈风清扬"] = "1头",
            ["香奈老大"] = "水土火金",
            ["战狼蔷薇"] = "4头"
        };
        OcrRule[] rules = Rules.Where(rule => values.ContainsKey(rule.Id)).ToArray();
        Assert.Equal(
            ["2头 68赵高", "1头 香奈风清扬", "木行 香奈老大", "4头 战狼蔷薇"],
            RuleEngine.FormatOutput(rules, values));
    }

    [Fact]
    public void LaodadaDuplicateStaysMissingAndReportsTheRealReason()
    {
        OcrRule rule = Assert.Single(Rules, item => item.Id == "68老大");
        string[] lines = ["68", "254期九肖中特", "(蛇狗马兔牛蛇鼠虎羊)开√", "公平"];

        Assert.Null(RuleEngine.ExtractFinalValue(lines, 254, rule));
        Assert.Equal(
            "已找到254期文字，但未通过9个不同生肖校验（识别到9个，去重后8个）",
            RuleEngine.DescribeExtractionFailure(lines, 254, rule));
    }

    [Theory]
    [InlineData("68凯哥", "245期凯哥杀五码【03 19 22 39】开")]
    [InlineData("68凯哥", "245期凯哥杀五码【03 19 22 39 39】开")]
    [InlineData("68凯哥", "245期凯哥杀五码【03 19 22 39 50】开")]
    [InlineData("68老大", "245期九肖中特【蛇虎牛猪狗马鸡龙龙】开")]
    [InlineData("战狼八戒", "245期绝杀二肖【龙龙】开")]
    [InlineData("绿格子双杀", "245期绝杀2肖【龙、龙】开?00准")]
    [InlineData("完美两肖", "245期:绝杀2肖(龙龙】开？00准")]
    [InlineData("战狼老王", "245期老王杀半头【4头大】开")]
    [InlineData("68波波", "245期波波杀半波【紫双】开")]
    public void RejectsIncompleteDuplicateOutOfRangeOrUnknownValues(string id, string row)
    {
        OcrRule rule = Assert.Single(Rules, rule => rule.Id == id);
        Assert.Null(RuleEngine.ExtractFinalValue([rule.Keyword, row], 245, rule));
    }

    [Fact]
    public void CompositeCardReadsOnlyTheTwoZodiacQuadrant()
    {
        OcrRule rule = Assert.Single(Rules, rule => rule.Id == "绿格子双杀");
        string[] lines =
        [
            "245期：绝杀2肖【龙、狗】开?00准",
            "245期：必中波色【蓝波、绿波】开?00准",
            "245期：3行必中【土、火、水】开?00准",
            "245期：单双中特【双数+龙虎】开?00准",
            "258期：绝杀2肖【鼠、马】开？00准",
            "258期：必中波色【绿波、红波】开？00准",
            "258期：3行必中【水、土、火】开？00准",
            "258期：单双中特【单数+蛇羊】开？00准"
        ];
        string composite = Path.Combine(@"C:\图片\9.15-黄大仙新澳\综合", "20260915_171316_207031.jpg");

        Assert.Equal("龙狗", RuleEngine.ExtractFinalValue(lines, 245, rule));
        Assert.Equal("鼠马", RuleEngine.ExtractFinalValue(lines, 258, rule));
        Assert.Contains(rule.Id, RuleEngine.FindMatches(composite, lines, Rules, Rules).Select(item => item.Id));
    }

    [Fact]
    public void SharedImagesReadOnlyTheirOwnFieldAndRejectConflictingTargetCopies()
    {
        OcrRule zodiac = Assert.Single(Rules, rule => rule.Id == "红人关公肖");
        OcrRule tail = Assert.Single(Rules, rule => rule.Id == "红人关公尾");
        string[] shared =
        [
            "红人馆团队之关公",
            "244期关公杀一肖【羊】开鸡",
            "245期关公杀一肖【蛇】开猫",
            "244期关公杀一尾【8尾】开46",
            "245期关公杀一尾【3尾】开00"
        ];
        Assert.Equal("蛇", RuleEngine.ExtractFinalValue(shared, 245, zodiac));
        Assert.Equal("3尾", RuleEngine.ExtractFinalValue(shared, 245, tail));
        Assert.Null(RuleEngine.ExtractFinalValue([.. shared, "245期关公杀一肖【猪】开猫"], 245, zodiac));
    }
}
