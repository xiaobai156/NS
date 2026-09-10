using OcrLineTool;
using Xunit;

namespace OcrLineTool.Tests;

// Regression coverage for the 新澳高手 issue 253 local-medium lines that
// previously produced missing/incorrect extraction results.
public sealed class NewAogaoShou253RulesTests
{
    private static OcrRule[] Rules() => RuleCatalog.Load(
        Path.Combine(ResultFilePaths.ConfigurationDirectory(AppContext.BaseDirectory), "新澳高手.json"))
        .ToArray();

    private static OcrRule Rule(string id) => Assert.Single(Rules(), rule => rule.Id == id);

    [Fact]
    public void ExtractsYataiHeadsFromTheBracketedFourHeadCard()
    {
        OcrRule rule = Rule("亚太一头");
        string[] lines =
        [
            "【亚太地区四头】",
            "253期：【0213】√",
            "252期：【3124】√",
            "251期：【4213】√",
            "250期：【0134】√"
        ];

        Assert.Equal("4头", RuleEngine.ExtractFinalValue(lines, 253, rule));
    }

    [Fact]
    public void ExtractsYataiTailsElementsAndTenZodiacs()
    {
        string[] tails = ["【亚太地区八尾】", "253期：【03456789尾】√", "252期：【04567238尾】√"];
        Assert.Equal("1尾 2尾", RuleEngine.ExtractFinalValue(tails, 253, Rule("亚太尾")));

        string[] elements = ["【亚太地区四行】", "253期：【火木金土】√", "252期：【火木金土】×"];
        Assert.Equal("水", RuleEngine.ExtractFinalValue(elements, 253, Rule("亚太五行")));

        string[] zodiacs = ["【亚太地区十肖】", "253期：【鼠虎鸡猴羊蛇龙马牛兔】√", "252期：【兔羊狗龙牛鼠猪鸡虎猴】√"];
        Assert.Equal("猪狗", RuleEngine.ExtractFinalValue(zodiacs, 253, Rule("亚太两肖")));
    }

    [Fact]
    public void ExtractsZhanaoTeamValuesDespiteStatusMarks()
    {
        string[] zodiacs = ["253期团队十肖:兔鼠猴牛羊龙马鸡虎猪√", "252期团队十肖：鼠牛马龙猴兔虎猪鸡狗√"];
        Assert.Equal("蛇狗", RuleEngine.ExtractFinalValue(zodiacs, 253, Rule("战澳两肖")));

        string[] tails = ["253期团队八尾：18463950√", "252期团队八尾:92135768√"];
        Assert.Equal("2尾 7尾", RuleEngine.ExtractFinalValue(tails, 253, Rule("战澳尾")));

        string[] heads = ["253期团队四头:4210√", "252期团队四头：1204√"];
        Assert.Equal("3头", RuleEngine.ExtractFinalValue(heads, 253, Rule("战澳头")));

        string[] elements = ["253期团队四行:金木火土√", "252期团队四行：水火金土√"];
        Assert.Equal("水", RuleEngine.ExtractFinalValue(elements, 253, Rule("战澳五行")));
    }

    [Fact]
    public void ExtractsGaoShouBangFiveNumbersBeforeTheOpeningPlaceholder()
    {
        OcrRule rule = Rule("高手榜五码");
        string[] lines =
        [
            "252期精杀五码-《29.38.27.33.39》-开:鸡22准",
            "253期精杀五码-《14.34.46.11.39》-开:？00准"
        ];

        Assert.Equal("14 34 46 11 39", RuleEngine.ExtractFinalValue(lines, 253, rule));
    }

    [Fact]
    public void ExtractsTukuThirtySixNumbersBelowTheOpeningPlaceholder()
    {
        OcrRule rule = Rule("图库");
        string[] lines =
        [
            "澳門無錯36碼",
            "253期",
            "开00准",
            "02.03.05.06.07.08.09.11.12.13.14.16",
            "19.20.21.23.24.25.26.27.28.29.30.31",
            "33.35.36.37.38.40.41.43.44.46.47.49"
        ];

        Assert.Equal(
            "02 03 05 06 07 08 09 11 12 13 14 16 19 20 21 23 24 25 26 27 28 29 30 31 "
            + "33 35 36 37 38 40 41 43 44 46 47 49",
            RuleEngine.ExtractFinalValue(lines, 253, rule));
    }

    [Fact]
    public void TukuDoesNotCrossTheNextPeriodOrApplyToSmallNumberRules()
    {
        string[] crossed =
        [
            "澳門無錯36碼",
            "253期",
            "开00准",
            "02.03.05.06.07.08.09.11.12.13.14.16",
            "252期",
            "19.20.21.23.24.25.26.27.28.29.30.31",
            "33.35.36.37.38.40.41.43.44.46.47.49"
        ];
        Assert.Null(RuleEngine.ExtractFinalValue(crossed, 253, Rule("图库")));

        var small = new OcrRule("精杀五码", "号码:5");
        Assert.Null(RuleEngine.ExtractFinalValue(
            ["253期", "开00准", "01.02.03.04.05"], 253, small));
    }

    [Fact]
    public void WanmeiTwoZodiacsOnlyMatchesThePerfectCard()
    {
        OcrRule rule = Rule("完美两肖");
        string folder = @"C:\结果\9.10-新澳高手\绿杀+完美杀";
        string perfectCard = Path.Combine(folder, "155699.jpg");
        string greenCard = Path.Combine(folder, "155698.jpg");
        string[] perfectLines =
        [
            "完美杀肖心水料",
            "251期:绝杀2肖(猪鸡】开牛30准",
            "252期：绝杀2肖(牛蛇】开鸡22准",
            "253期：绝杀2肖(猪龙】开？00准"
        ];
        string[] greenLines =
        [
            "绝杀2肖",
            "252期：鼠-猴√",
            "253期：鸡-兔？"
        ];

        Assert.Single(RuleEngine.FindMatches(perfectCard, perfectLines, [rule], [rule]));
        Assert.Empty(RuleEngine.FindMatches(greenCard, greenLines, [rule], [rule]));

        IReadOnlyList<LocalCandidatePlan> plans = LocalCandidatePlanner.Build(
            [greenCard, perfectCard],
            new Dictionary<string, IReadOnlyList<string>>
            {
                [greenCard] = greenLines,
                [perfectCard] = perfectLines
            },
            [rule], 253, [rule]);

        LocalCandidatePlan plan = Assert.Single(plans);
        Assert.True(plan.IsPrimary);
        Assert.Equal(perfectCard, plan.Path);
        Assert.Equal("猪龙", RuleEngine.ExtractFinalValue(perfectLines, 253, rule));
    }
}
