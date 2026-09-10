using OcrLineTool;

namespace OcrLineTool.Tests;

public sealed class YanranThreeImageRegressionTests
{
    private static IReadOnlyList<OcrRule> Rules => RuleCatalog.Load(Path.Combine(
        ResultFilePaths.ConfigurationDirectory(AppContext.BaseDirectory), "嫣然心水.json"));

    [Fact]
    public void EnpingKillHeadUsesTheCurrentCardTitleAndTheSelectedIssueRow()
    {
        OcrRule rule = Assert.Single(Rules, item => item.Id == "恩平杀头");
        string image = Path.Combine("C:\\结果", "嫣然心水", "恩平", "当前图.jpg");
        string[] lines = ["恩平杀一头", "301期杀，0头开20", "302期杀，2头开18"];

        Assert.Equal("头", rule.Type);
        Assert.Equal(rule, Assert.Single(RuleEngine.FindMatches(image, lines, [rule])));
        Assert.Equal("0头", RuleEngine.ExtractFinalValue(lines, 301, rule));
        Assert.Equal("2头", RuleEngine.ExtractFinalValue(lines, 302, rule));
    }

    [Fact]
    public void EnpingKillHeadRequiresOnlyOneHeadValuePerIssue()
    {
        OcrRule rule = Assert.Single(Rules, item => item.Id == "恩平杀头");

        Assert.True(rule.SingleValuePerIssue);
        Assert.Equal("0头", RuleEngine.ExtractFinalValue(
            ["恩平杀一头", "249期杀，0头开20"], 249, rule));
        Assert.Null(RuleEngine.ExtractFinalValue(
            ["恩平杀一头", "249期杀，0头、2头开20"], 249, rule));
        Assert.Null(RuleEngine.ExtractFinalValue(
            ["恩平杀一头", "249期杀，0头", "249期杀，2头"], 249, rule));
    }

    [Fact]
    public void FolderBoundXiaohuihuiRuleIsNotAssignedToAnotherFoldersSummary()
    {
        var rule = new OcrRule("小灰灰", "生肖组合", "小灰灰两肖",
            RequiredKeyword: "小灰灰", Folder: "小灰灰");
        string summary = Path.Combine("C:\\结果", "嫣然心水", "乖乖团队", "统计.jpg");
        var localResults = new Dictionary<string, IReadOnlyList<string>>
        {
            [summary] = ["GG团队249期新澳禁肖统计表", "简单爱正正禁虎"]
        };

        Assert.Empty(LocalCandidatePlanner.Build([summary], localResults, [rule]));
    }

    [Fact]
    public void XiaohuihuiHistoryCardIsOneZodiacNotTwo()
    {
        OcrRule single = Assert.Single(Rules, item => item.Id == "小灰灰一肖");
        string[] lines = ["小灰灰荣誉出品", "248杀号龙开猪20√", "249杀号狗开鸭88?"];

        Assert.Equal("狗", RuleEngine.ExtractFinalValue(lines, 249, single));
    }

    [Fact]
    public void GreenAppleRepeatedNumberRowStaysMissingInsteadOfGuessing()
    {
        OcrRule rule = Assert.Single(Rules, item => item.Id == "青苹果");

        Assert.Null(RuleEngine.ExtractFinalValue(
            ["原创青苹果", "301期杀号:20.40.09.21.09.49开00猫准"], 301, rule));
    }
}
