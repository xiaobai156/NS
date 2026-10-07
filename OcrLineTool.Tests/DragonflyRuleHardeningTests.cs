using OcrLineTool;

namespace OcrLineTool.Tests;

public sealed class DragonflyRuleHardeningTests
{
    private static IReadOnlyList<OcrRule> Rules => RuleCatalog.Load(Path.Combine(
        ResultFilePaths.ConfigurationDirectory(AppContext.BaseDirectory), "蜻蜓一套.json"));

    public static TheoryData<string, string, string> Samples => new()
    {
        { "黑字杀头", "4头 木 10合 开?00准", "4头" },
        { "黑字杀行", "4头 木 10合 开?00准", "木" },
        { "黑字杀合", "4头 木 10合 开?00准", "10合" },
        { "公式杀两肖肖", "（平1*4+特-D4+平3+正1-2）=杀狗猴√", "狗猴" },
        { "公式杀两尾尾", "（平4-2-D2+正3）=杀31尾√", "3尾+1尾" },
        { "红蜻蜓", "红蜻蜓必中⑨肖【马龙虎鼠牛猪兔蛇羊】开00准", "马龙虎鼠牛猪兔蛇羊" },
        { "红蜻蜓杀一肖", "红蜻蜓绝杀一肖【蛇】开00准，铁杀", "蛇" },
        { "神奇宇宙", "神秘宇宙绝杀半波【红单】开00准", "红单" },
        { "墨羽", "墨羽尘曦精杀一肖【鸡】开00准", "鸡" }
    };

    [Fact]
    public void CatalogHardensEveryConfirmedRuleAndUsesTheCurrentTitles()
    {
        Assert.Equal(9, Rules.Count);
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
        Assert.Equal("猴", RuleEngine.ExtractFinalValue(lines, 245,
            Assert.Single(Rules, rule => rule.Id == "红蜻蜓杀一肖")));
    }

    [Theory]
    [InlineData(7)]
    [InlineData(280)]
    [InlineData(1001)]
    public void OneZodiacUsesItsOwnIssueAndSplitField(int issue)
    {
        OcrRule rule = Assert.Single(Rules, rule => rule.Id == "红蜻蜓杀一肖");
        string[] lines =
        [
            $"{issue}期红蜻蜓必中⑨肖【马龙虎鼠牛猪兔蛇羊】开00准",
            $"{issue}期：", "红蜻蜓绝杀一肖", "【蛇】", "开00准，铁杀",
            $"{issue - 1}期红蜻蜓绝杀一肖【牛】开10准，铁杀"
        ];
        Assert.Equal("蛇", RuleEngine.ExtractFinalValue(lines, issue, rule));
        Assert.Equal("牛", RuleEngine.ExtractFinalValue(lines, issue - 1, rule));
        Assert.Null(RuleEngine.ExtractFinalValue(lines, issue + 1, rule));
    }

    [Theory]
    [InlineData("【蛇牛】")]
    [InlineData("【蛇蛇】")]
    [InlineData("【待更新】")]
    [InlineData("【?】")]
    [InlineData("")]
    public void OneZodiacNeverBorrowsTheNineZodiacsOrOpeningResult(string field)
    {
        OcrRule rule = Assert.Single(Rules, rule => rule.Id == "红蜻蜓杀一肖");
        Assert.Null(RuleEngine.ExtractFinalValue(
            ["280期红蜻蜓必中⑨肖【猪兔鸡猴马牛羊龙鼠】开00准",
             $"280期红蜻蜓绝杀一肖{field}开牛10准，铁杀",
             "279期红蜻蜓绝杀一肖【牛】开10准，铁杀"], 280, rule));
    }

    [Fact]
    public void OneZodiacConflictsStayMissing()
    {
        OcrRule rule = Assert.Single(Rules, rule => rule.Id == "红蜻蜓杀一肖");
        Assert.Null(RuleEngine.ExtractFinalValue(
            ["280期红蜻蜓绝杀一肖【蛇】开00准", "280期红蜻蜓绝杀一肖【牛】开00准"], 280, rule));
    }

    [Fact]
    public void BothDragonflyFieldsShareOnlyTheCorrectSubfolderImage()
    {
        string path = @"C:\图片\10.7-蜻蜓一套\一套组合拳\sample.jpg";
        string wrongFolder = @"C:\图片\10.7-蜻蜓一套\各种杀\sample.jpg";
        string unrelated = @"C:\图片\10.7-蜻蜓一套\一套组合拳\other.jpg";
        string[] lines = ["280期红蜻蜓必中⑨肖【猪兔鸡猴马牛羊龙鼠】开00准",
            "280期红蜻蜓绝杀一肖【蛇】开00准，铁杀"];
        var texts = new Dictionary<string, IReadOnlyList<string>>
        {
            [path] = lines, [wrongFolder] = lines,
            [unrelated] = ["280期墨羽尘曦精杀一肖【鸡】开00准"]
        };
        var rules = Rules.Where(rule => rule.Id is "红蜻蜓" or "红蜻蜓杀一肖").ToArray();
        Assert.Equal(2, rules.Length);
        var plan = Assert.Single(LocalCandidatePlanner.Build([path, wrongFolder, unrelated], texts, rules, 280, Rules));
        Assert.Equal(path, plan.Path);
        Assert.Equal(2, plan.Rules.Count);
    }

    [Theory]
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
