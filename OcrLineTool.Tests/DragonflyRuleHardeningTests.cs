using OcrLineTool;

namespace OcrLineTool.Tests;

public sealed class DragonflyRuleHardeningTests
{
    private static IReadOnlyList<OcrRule> Rules => RuleCatalog.Load(Path.Combine(
        ResultFilePaths.ConfigurationDirectory(AppContext.BaseDirectory), "蜻蜓一套骁腾.json"));

    public static TheoryData<string, string, string> Samples => new()
    {
        { "绿格子双杀", "绝杀2肖【龙、狗】开?00准", "龙狗" },
        { "黑字杀头", "4头 木 10合 开?00准", "4头" },
        { "黑字杀行", "4头 木 10合 开?00准", "木" },
        { "黑字杀合", "4头 木 10合 开?00准", "10合" },
        { "公式杀两肖肖", "（平1*4+特-D4+平3+正1-2）=杀狗猴√", "狗猴" },
        { "公式杀两尾尾", "（平4-2-D2+正3）=杀31尾√", "3尾+1尾" },
        { "红蜻蜓", "红蜻蜓必中⑨肖【马龙虎鼠牛猪兔蛇羊】开00准", "马龙虎鼠牛猪兔蛇羊" },
        { "神奇宇宙", "神秘宇宙绝杀半波【红单】开00准", "红单" },
        { "墨羽", "墨羽尘曦精杀一肖【鸡】开00准", "鸡" },
        { "骁腾杀肖", "杀一肖《鸡》开:发00准", "鸡" },
        { "骁腾", "九肖[鼠牛虎龙蛇马羊猴猪]发00准", "鼠牛虎龙蛇马羊猴猪" }
    };

    [Fact]
    public void CatalogHardensEveryConfirmedRuleAndUsesTheCurrentTitles()
    {
        Assert.Equal(11, Rules.Count);
        Assert.All(Rules, rule => Assert.True(rule.StrictIssueBlock));
        Assert.Equal("红蜻蜓必中⑨肖", Assert.Single(Rules, rule => rule.Id == "红蜻蜓").Keyword);
        Assert.Equal("神秘宇宙绝杀半波", Assert.Single(Rules, rule => rule.Id == "神奇宇宙").Keyword);
    }

    [Theory]
    [MemberData(nameof(Samples))]
    public void ExtractsEveryStyleAtAnySoftwareSelectedIssue(string id, string row, string expected)
    {
        OcrRule rule = Assert.Single(Rules, rule => rule.Id == id);
        foreach (int issue in new[] { 7, 245, 1001 })
        {
            string[] lines = [rule.Keyword, $"{issue}期：{row}"];
            Assert.Equal(expected, RuleEngine.ExtractValue(lines, issue, rule));
            Assert.Equal(expected, RuleEngine.ExtractFinalValue(lines, issue, rule));
            Assert.Null(RuleEngine.ExtractFinalValue(lines, issue + 1, rule));
        }
    }

    [Fact]
    public void GreenCardReadsOnlyTheTwoZodiacQuadrant()
    {
        OcrRule rule = Assert.Single(Rules, rule => rule.Id == "绿格子双杀");
        string[] lines =
        [
            "245期：绝杀2肖【龙、狗】开?00准",
            "245期：必中波色【蓝波、绿波】开?00准",
            "245期：3行必中【土、火、水】开?00准",
            "245期：单双中特【双数+龙虎】开?00准"
        ];
        Assert.Equal("龙狗", RuleEngine.ExtractFinalValue(lines, 245, rule));
    }

    [Fact]
    public void BlackCardSplitsAllThreeColumnsFromOneTargetRow()
    {
        string[] lines = ["期数 杀一头 杀一行 杀一合 开奖结果", "245期 4头 木 10合 开?00准"];
        Assert.Equal("4头", RuleEngine.ExtractFinalValue(lines, 245, Assert.Single(Rules, rule => rule.Id == "黑字杀头")));
        Assert.Equal("木", RuleEngine.ExtractFinalValue(lines, 245, Assert.Single(Rules, rule => rule.Id == "黑字杀行")));
        Assert.Equal("10合", RuleEngine.ExtractFinalValue(lines, 245, Assert.Single(Rules, rule => rule.Id == "黑字杀合")));
    }

    [Fact]
    public void FormulaCardsIgnoreEveryNumberBeforeTheFinalKillMarker()
    {
        string[] lines =
        [
            "245期（平4-2-D2+正3）=杀31尾√",
            "245期（平1*4+特-D4+平3+正1-2）=杀狗猴√"
        ];
        Assert.Equal("狗猴", RuleEngine.ExtractFinalValue(lines, 245, Assert.Single(Rules, rule => rule.Id == "公式杀两肖肖")));
        Assert.Equal("3尾+1尾", RuleEngine.ExtractFinalValue(lines, 245, Assert.Single(Rules, rule => rule.Id == "公式杀两尾尾")));
    }

    [Fact]
    public void RedDragonflyIgnoresTheOneZodiacSectionOnTheSameImage()
    {
        OcrRule rule = Assert.Single(Rules, rule => rule.Id == "红蜻蜓");
        string[] lines =
        [
            "245期红蜻蜓必中⑨肖【马龙虎鼠牛猪兔蛇羊】开00准",
            "245期红蜻蜓绝杀一肖【猴】开00准"
        ];
        Assert.Equal("马龙虎鼠牛猪兔蛇羊", RuleEngine.ExtractFinalValue(lines, 245, rule));
    }

    [Theory]
    [InlineData("绿格子双杀", "245期绝杀2肖【龙龙】开00准")]
    [InlineData("红蜻蜓", "245期红蜻蜓必中⑨肖【马龙虎鼠牛猪兔蛇蛇】开00准")]
    [InlineData("公式杀两肖肖", "245期（平1*4+特-D4）=杀狗√")]
    [InlineData("公式杀两尾尾", "245期（平4-2-D2）=杀33尾√")]
    [InlineData("黑字杀头", "245期5头 木 10合 开00准")]
    [InlineData("黑字杀合", "245期4头 木 14合 开00准")]
    [InlineData("神奇宇宙", "245期神秘宇宙绝杀半波【紫单】开00准")]
    public void RejectsDuplicateIncompleteOutOfRangeOrUnknownValues(string id, string row)
    {
        OcrRule rule = Assert.Single(Rules, rule => rule.Id == id);
        Assert.Null(RuleEngine.ExtractFinalValue([rule.Keyword, row], 245, rule));
    }

    [Fact]
    public void ConflictingCopiesOfTheSelectedIssueAreMissing()
    {
        OcrRule rule = Assert.Single(Rules, rule => rule.Id == "墨羽");
        Assert.Null(RuleEngine.ExtractFinalValue(
            ["245期墨羽尘曦精杀一肖【鸡】开00准", "245期墨羽尘曦精杀一肖【兔】开00准"],
            245, rule));
    }
}
