using OcrLineTool;

namespace OcrLineTool.Tests;

public class MemberArtsTests
{
    private static OcrRule Rule => RuleCatalog.Load(Path.Combine(
        ResultFilePaths.ConfigurationDirectory(AppContext.BaseDirectory), "新澳高级会员.json"))
        .Single(r => r.Id == "会员琴棋");

    [Theory]
    [InlineData(269, "琴肖 棋肖 画肖", "琴棋画")]
    [InlineData(1001, "画肖\n书肖\n琴肖", "画书琴")]
    [InlineData(7, "棋肖 書肖 畫肖", "棋书画")]
    public void ReadsOnlySelectedIssue(int issue, string payload, string expected)
    {
        string[] lines = ["会员特供【琴棋书画】", $"{issue}期", "会员特供：", payload,
            "开??", $"{issue - 1}期会员特供：琴肖书肖画肖"];
        Assert.Equal(expected, RuleEngine.ExtractFinalValue(lines, issue, Rule));
        Assert.Null(RuleEngine.ExtractFinalValue(lines, issue + 1, Rule));
        Assert.True(RuleEngine.IsFormattedOutputValueValid(Rule, expected));
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
}
