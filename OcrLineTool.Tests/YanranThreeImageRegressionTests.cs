using OcrLineTool;

namespace OcrLineTool.Tests;

public sealed class YanranThreeImageRegressionTests
{
    private static IReadOnlyList<OcrRule> Rules => RuleCatalog.Load(Path.Combine(
        ResultFilePaths.ConfigurationDirectory(AppContext.BaseDirectory), "嫣然心水.json"));

    [Fact]
    public void EnpingKillHeadIsTheMissingHeadOfTheCurrentFourHeadRow()
    {
        OcrRule rule = Assert.Single(Rules, item => item.Id == "恩平杀头");
        string image = Path.Combine("C:\\结果", "嫣然心水", "恩平", "当前图.jpg");
        string[] lines = ["恩平澳彩四头，251期止58期错7", "266期， 0124头"];

        Assert.Equal("缺头", rule.Type);
        Assert.Equal("恩平澳彩四头", rule.RequiredKeyword);
        Assert.False(rule.SingleValuePerIssue);
        Assert.Equal(rule, Assert.Single(RuleEngine.FindMatches(image, lines, [rule])));
        Assert.Equal("3头", RuleEngine.ExtractFinalValue(lines, 266, rule));
    }

    [Fact]
    public void EnpingKillHeadStaysMissingOnInvalidHeadRows()
    {
        OcrRule rule = Assert.Single(Rules, item => item.Id == "恩平杀头");
        string title = "恩平澳彩四头，251期止58期错7";

        Assert.Equal("0头", RuleEngine.ExtractFinalValue([title, "265期， 1234头"], 265, rule));
        Assert.Null(RuleEngine.ExtractFinalValue([title, "265期， 123头"], 265, rule));
        Assert.Null(RuleEngine.ExtractFinalValue([title, "265期， 0122头"], 265, rule));
        Assert.Null(RuleEngine.ExtractFinalValue([title, "265期， 01230头"], 265, rule));
        Assert.Null(RuleEngine.ExtractFinalValue([title, "264期， 0124头"], 265, rule));
    }

    [Fact]
    public void EnpingKillHeadIgnoresTheOneHeadCardAndTheFourElementCard()
    {
        OcrRule rule = Assert.Single(Rules, item => item.Id == "恩平杀头");
        string image = Path.Combine("C:\\结果", "嫣然心水", "恩平", "别的卡.jpg");
        string[] fourElements = ["恩平：澳彩四行", "265期， 金木水火", "264期， 金木水火✅21"];

        // 每期只有一个头的旧卡不再支持，标题对不上就不命中
        Assert.Empty(RuleEngine.FindMatches(image, ["恩平杀一头", "265期杀，0头开20"], [rule]));
        // 四行卡与四头标题只差一字，required_keyword 保留一字容错，因此可能被选为候选，
        // 但目标期行没有 0～4 的四个头，取值必须保持缺失，不能借邻期结果数字凑数
        Assert.Null(RuleEngine.ExtractFinalValue(fourElements, 265, rule));
        Assert.Null(RuleEngine.ExtractFinalValue(fourElements, 264, rule));
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
