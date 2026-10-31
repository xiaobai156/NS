using OcrLineTool;

namespace OcrLineTool.Tests;

// 黄大仙新澳「蕊儿团队」十张同版式卡：作者名只印在图头，数据行是
// 「新菜304期：杀①肖【虎】T00肖」，答案在方括号里，括号右边是开奖结果。
public sealed class RuiErTeamCardTests
{
    private static OcrRule Card(string author, string type, string label) =>
        new(author, type, label,
            RequiredKeyword: author,
            Folder: "蕊儿团队",
            StrictIssueBlock: true,
            Extractor: "rui_er_team_card");

    private static string[] Lines(string author, params string[] rows) =>
        new[] { author, "澳门六合彩", "2026.300期起" }.Concat(rows).ToArray();

    [Fact]
    public void ReadsTheBracketedAnswerOfItsOwnCard()
    {
        OcrRule rule = Card("龙翔", "生肖", "龙翔");
        string[] lines = Lines("龙翔",
            "新菜303期:杀①肖【马】T17虎",
            "新菜304期：杀①肖【虎】T00肖");

        Assert.Equal("虎", RuleEngine.ExtractFinalValue(lines, 304, rule));
        // 305 期的括号是空的，必须判缺失而不是拿上一期的值。
        Assert.Null(RuleEngine.ExtractFinalValue(Lines("龙翔", "新菜305期：杀①肖【】T00肖"), 305, rule));
    }

    [Fact]
    public void OpeningResultOnTheSameRowNeverBecomesTheAnswer()
    {
        OcrRule rule = Card("龙翔", "生肖", "龙翔");
        // 这一行同时有答案「马」和开奖结果「T17虎」，只能取括号里的马。
        string[] lines = Lines("龙翔", "新菜303期:杀①肖【马】T17虎");

        Assert.Equal("马", RuleEngine.ExtractFinalValue(lines, 303, rule));
    }

    [Fact]
    public void TheAuthorNameInTheHeadingIsWhatTellsTwoCardsApart()
    {
        OcrRule longxiang = Card("龙翔", "生肖", "龙翔");
        string[] rainbow = Lines("彩虹公主", "新菜304期：杀①肖【鸡】T00肖√");

        Assert.Null(RuleEngine.ExtractFinalValue(rainbow, 304, longxiang));
        Assert.Equal("鸡", RuleEngine.ExtractFinalValue(rainbow, 304, Card("彩虹公主", "生肖", "彩虹公主")));
    }

    [Fact]
    public void ABracketSplitOntoTheNextLineIsStillRead()
    {
        OcrRule zodiac = Card("碧水蓝天", "生肖", "碧水蓝天");
        OcrRule tail = Card("碧水蓝天", "尾", "碧水蓝天尾");
        string[] lines = Lines("碧水蓝天",
            "新莱302期:杀①肖",
            "【猪+2尾】T45狗",
            "新莱304期：杀①肖【猪+2尾】T00肖√");

        Assert.Equal("猪", RuleEngine.ExtractFinalValue(lines, 302, zodiac));
        Assert.Equal("2尾", RuleEngine.ExtractFinalValue(lines, 302, tail));
        Assert.Equal("猪", RuleEngine.ExtractFinalValue(lines, 304, zodiac));
    }

    [Fact]
    public void AnIssuePrintedAfterTheColonIsStillTheIssue()
    {
        OcrRule zodiac = Card("蕊儿", "生肖", "蕊儿");
        OcrRule tail = Card("蕊儿", "尾", "蕊儿尾");
        // 300 期那行写成「新菜期:300杀①肖」，期号跑到冒号后面。
        string[] lines = Lines("蕊儿", "新菜期:300杀①肖【龙+6尾】T38蛇");

        Assert.Equal("龙", RuleEngine.ExtractFinalValue(lines, 300, zodiac));
        Assert.Equal("6尾", RuleEngine.ExtractFinalValue(lines, 300, tail));
    }

    [Fact]
    public void OneCardCanCarryBothTheSumAndTheHalfHead()
    {
        OcrRule sum = Card("梓涵", "合", "梓涵合");
        OcrRule halfHead = Card("梓涵", "半头", "梓涵半头");
        string[] lines = Lines("梓涵",
            "新莱304期:杀①合【01】T00肖V",
            "新莱304期:杀半头【4头双】T00头");

        Assert.Equal("01合", RuleEngine.ExtractFinalValue(lines, 304, sum));
        Assert.Equal("4头双", RuleEngine.ExtractFinalValue(lines, 304, halfHead));
    }

    [Fact]
    public void AnIssueThatIsNotOnTheCardStaysMissing()
    {
        OcrRule rule = Card("龙翔", "生肖", "龙翔");
        string[] lines = Lines("龙翔", "新菜304期：杀①肖【虎】T00肖");

        Assert.Null(RuleEngine.ExtractFinalValue(lines, 305, rule));
    }
}
