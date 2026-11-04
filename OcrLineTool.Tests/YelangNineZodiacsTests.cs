namespace OcrLineTool.Tests;

/// <summary>
/// 黄大仙新澳「野狼九肖王」：整卡是真机 OCR 的 17 期数据，其中 298/299/300/301/305
/// 被断成「期号行 + 『九个生肖』开:…行」两行，行尾还带「开:虎√」这种开奖栏。
/// 开奖栏里的生肖不是答案，先切掉再数九个。
/// </summary>
public sealed class YelangNineZodiacsTests
{
    private static readonly string[] Lines =
    [
        "野狼九肖王",
        "野狼",
        "战队",
        "293期:⑨肖中特『牛鼠狗猴兔虎马龙羊』开:虎√",
        "294期:⑨肖中特『鼠猴兔虎马龙羊猪蛇』开:兔√",
        "295期:⑨肖中特『蛇狗鼠猴兔虎马龙猪』开:马√",
        "296期:⑨肖中特『猪羊蛇狗鼠鸡兔虎马』开:猴X",
        "297期:⑨肖中特『蛇狗鼠兔虎马龙猴牛』开:马√",
        "298期:⑨肖中特",
        "『蛇牛鼠马兔虎龙猴猪』开:猪√",
        "299期:⑨肖中特",
        "『牛鼠马虎龙猴猪狗蛇』开:猴√",
        "300期:⑨肖中特",
        "『兔牛鼠蛇马虎龙猴猪』开:蛇√",
        "301期:⑨肖中特",
        "『牛蛇马虎龙猪羊鸡狗』开:虎√",
        "302期:⑨肖中特『狗鼠猴牛蛇虎龙猪兔』开:狗√",
        "303期:⑨肖中特『兔羊狗鼠猴蛇虎龙猪』开:虎√",
        "304期:⑨肖中特『狗鼠猴蛇虎龙猪兔鸡』开:狗√",
        "305期:⑨肖中特",
        "『牛马狗鼠猴蛇虎龙猪』开:蛇√",
        "306期:⑨肖中特『兔牛狗鼠猪猴蛇虎龙』开:龙√",
        "307期:⑨肖中特『牛狗鼠猪猴蛇虎龙马』开:牛",
        "308期:⑨肖中特『兔羊鸡牛狗猪猴蛇龙』开:√",
    ];

    private static readonly OcrRule Rule = RuleCatalog
        .Load(Path.Combine(ResultFilePaths.ConfigurationDirectory(AppContext.BaseDirectory), "黄大仙新澳.json"))
        .Single(rule => rule.Id == "野狼九肖");

    [Theory]
    [InlineData(293, "牛鼠狗猴兔虎马龙羊")]
    [InlineData(298, "蛇牛鼠马兔虎龙猴猪")]
    [InlineData(305, "牛马狗鼠猴蛇虎龙猪")]
    [InlineData(307, "牛狗鼠猪猴蛇虎龙马")]
    [InlineData(308, "兔羊鸡牛狗猪猴蛇龙")]
    public void OpeningColumnIsDroppedAndNineZodiacsBecomeTheValue(int issue, string expected) =>
        Assert.Equal(expected, RuleEngine.ExtractFinalValue(Lines, issue, Rule));

    [Fact]
    public void CardWithoutThatIssueYieldsNothing() =>
        Assert.Null(RuleEngine.ExtractFinalValue(Lines, 250, Rule));

    [Fact]
    public void RuleIsBoundToTheWildWolfFolderWithItsOwnExtractor()
    {
        Assert.Equal("九肖", Rule.Type);
        Assert.Equal("野狼", Rule.Folder);
        Assert.Equal("野狼", Rule.RequiredKeyword);
        Assert.Equal("nine_zodiacs_before_opening", Rule.Extractor);
    }
}
