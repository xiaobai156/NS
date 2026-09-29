using OcrLineTool;

namespace OcrLineTool.Tests;

public class MemberArtsTests
{
    private static OcrRule Rule => RuleCatalog.Load(Path.Combine(
        ResultFilePaths.ConfigurationDirectory(AppContext.BaseDirectory), "新澳高级会员.json"))
        .Single(r => r.Id == "会员琴棋");

    [Theory]
    [InlineData(269, "琴肖 棋肖 画肖", "兔蛇鸡鼠牛狗羊猴猪")]
    [InlineData(1001, "画肖\n书肖\n琴肖", "羊猴猪虎龙马兔蛇鸡")]
    [InlineData(7, "棋肖 書肖 畫肖", "鼠牛狗虎龙马羊猴猪")]
    public void ReadsOnlySelectedIssue(int issue, string payload, string expected)
    {
        string[] lines = ["会员特供【琴棋书画】", $"{issue}期", "会员特供：", payload,
            "开??", $"{issue - 1}期会员特供：琴肖书肖画肖"];
        Assert.Equal(expected, RuleEngine.ExtractFinalValue(lines, issue, Rule));
        Assert.Null(RuleEngine.ExtractFinalValue(lines, issue + 1, Rule));
        Assert.True(RuleEngine.IsFormattedOutputValueValid(Rule, expected));
    }

    // 属性字只是卡面印法，落库/分发都是九个生肖：属性字本身不再算合法值。
    [Fact]
    public void RawArtsCharactersAreNotStored()
    {
        Assert.False(RuleEngine.IsFormattedOutputValueValid(Rule, "琴棋画"));
        Assert.False(RuleEngine.IsFormattedOutputValueValid(Rule, "兔蛇鸡鼠牛狗羊猴"));
    }

    // 缺字时按印在卡上的属性字报数，不拿整张图的其他生肖凑数。
    [Fact]
    public void ReportsPrintedArtsCountWhenTheFieldIsIncomplete()
    {
        string message = RuleEngine.DescribeExtractionFailure(
            ["会员特供【琴棋书画】", "269期", "会员特供：琴肖棋肖", "开??"], 269, Rule);
        Assert.Contains("3个不同琴棋书画字", message);
        Assert.Contains("（识别到2个，去重后2个）", message);
    }

    [Theory]
    [InlineData("琴肖棋肖")]
    [InlineData("琴肖琴肖画肖")]
    [InlineData("琴肖棋肖书肖画肖")]
    [InlineData("琴肖棋肖未知画肖")]
    public void RejectsIncompleteOrInvalidFields(string payload) =>
        Assert.Null(RuleEngine.ExtractFinalValue(["琴棋书画", "269期会员特供：" + payload], 269, Rule));

    [Fact]
    public void KeepsRealConflicts() => Assert.Equal(RuleExtractionStatus.Conflict,
        RuleEngine.ExtractFinalResult(["琴棋书画", "269期会员特供：琴肖棋肖画肖开??",
            "269期会员特供：琴肖书肖画肖开??"], 269, Rule).Status);

    // 大赢家琴棋是另一张卡：目标期行印的是「赢家举荐」，与会员琴棋的「会员特供」是两套字段。
    private static OcrRule DayingjiaRule => RuleCatalog.Load(Path.Combine(
        ResultFilePaths.ConfigurationDirectory(AppContext.BaseDirectory), "新澳高级会员.json"))
        .Single(r => r.Id == "大赢家琴棋");

    [Theory]
    [InlineData(272, "棋肖 书肖 画肖", "鼠牛狗虎龙马羊猴猪")]
    [InlineData(272, "琴肖 棋肖 画肖", "兔蛇鸡鼠牛狗羊猴猪")]
    [InlineData(7, "棋肖 書肖 畫肖", "鼠牛狗虎龙马羊猴猪")]
    public void ReadsDayingjiaWinningRecommendationRows(int issue, string payload, string expected)
    {
        string[] lines = ["大赢家【琴棋书画】付费版", $"{issue}期赢家举荐：{payload}开??",
            $"{issue - 1}期赢家举荐：琴肖书肖画肖猴35中"];
        Assert.Equal(expected, RuleEngine.ExtractFinalValue(lines, issue, DayingjiaRule));
        Assert.True(RuleEngine.IsFormattedOutputValueValid(DayingjiaRule, expected));
    }

    // 两张卡的字段名不能互相串读：会员特供行只喂会员琴棋，赢家举荐行只喂大赢家琴棋。
    [Fact]
    public void KeepsMemberArtsFieldNamesApart()
    {
        Assert.Null(RuleEngine.ExtractFinalValue(["269期赢家举荐：棋肖书肖画肖开??"], 269, Rule));
        Assert.Null(RuleEngine.ExtractFinalValue(["269期会员特供：棋肖书肖画肖开??"], 269, DayingjiaRule));
    }

    // 缺字时同样按印在卡上的属性字报数，且只数大赢家琴棋自己的目标期字段。
    [Fact]
    public void ReportsDayingjiaPrintedArtsCount()
    {
        string message = RuleEngine.DescribeExtractionFailure(
            ["大赢家【琴棋书画】付费版", "272期", "272期赢家举荐：棋肖棋肖书肖开??"], 272, DayingjiaRule);
        Assert.Contains("3个不同琴棋书画字", message);
        Assert.Contains("（识别到3个，去重后2个）", message);
    }
}
