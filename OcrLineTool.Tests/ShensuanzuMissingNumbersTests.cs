using System.IO;
using System.Linq;
using OcrLineTool;
using Xunit;

namespace OcrLineTool.Tests;

// 新澳六合彩资料「神算子【无错45码】付费版」：每期印 45 个号码，没印出来的 4 个才是值。
// 下面的行是这张卡真实的本地 OCR 结果（见 artifacts/shensuanzu-lines.txt），号码段被
// 期号格切成上下两半，段与段之间没有空行，还有相邻号码粘成四位（「3637」「4041」）的情况。
public sealed class ShensuanzuMissingNumbersTests
{
    private static OcrRule Rule => RuleCatalog.Load(Path.Combine(
        ResultFilePaths.ConfigurationDirectory(AppContext.BaseDirectory), "新澳六合彩资料.json"))
        .Single(rule => rule.Id == "神算子四码");

    private static readonly string[] Lines =
    [
        "新澳",
        "48",
        "38",
        "49",
        "09",
        "13",
        "03",
        "刷",
        "42",
        "307期开羊蛇 馬狗馬龍新牛",
        "神算子【无错45码】付费版",
        "01 02 03 04 05 06 07 08 09",
        "10 11 12 13 14 15 16 17 18",
        "19 20 21 22 23 24 25",
        "308期",
        "开??",
        "26 28 29 30 31 32 33 34 35",
        "36 37 38 39 40 42 43 45 46",
        "48 49",
        "01 02 03 04 05 06 07 08 09",
        "10 11 12 13 14 15 16 17 18",
        "19 20 21 22 23 24 25",
        "307期",
        "牛42中",
        "26 27 29 30 31 32 33",
        "34 35",
        "3637 38 39 4041 42",
        "43 45",
        "46 49",
        "02 03 04 05 06 07 08 09 10",
        "11 13 14",
        "15 16 17 18 19 20",
        "21 22 23 24 25 26 27",
        "306期",
        "龍15中",
        "28 29 30 31 32 33 35 36 37",
        "38 40 41 42 43 44 45 46 47",
        "48 49",
        "01 02 03 04 05 06 07 08 09",
        "10 11 12 13 14 15 16 17 18",
        "19 20 21 22 24 25 26",
        "305期",
        "蛇26中",
        "27 28 29 30 31 32 33 34 35",
        "36 37 38 39 40 43 44 45 46",
        "47 49"
    ];

    [Theory]
    [InlineData(308, "27 41 44 47")]
    [InlineData(307, "28 44 47 48")]
    [InlineData(306, "01 12 34 39")]
    [InlineData(305, "23 41 42 48")]
    public void ReturnsTheNumbersThatAreNotPrinted(int issue, string expected)
    {
        Assert.Equal(expected, RuleEngine.ExtractFinalValue(Lines, issue, Rule));
    }

    [Fact]
    public void StaysInsideTheBlockOfTheRequestedIssue()
    {
        // 307 期上面那段属于 308 期，25 属于 307 自己、27 属于 308：相邻段的号码不能串。
        string? value = RuleEngine.ExtractFinalValue(Lines, 307, Rule);

        Assert.Equal("28 44 47 48", value);
        Assert.DoesNotContain("27", value);
        Assert.DoesNotContain("25", value);
    }

    [Fact]
    public void MissingNumbersAreOnesPerIssueNotPerCard()
    {
        // 同一个群当天可能有两期表：每期各算各的，不能把两期并成一张 45 码表。
        string[] mixed = ["神算子【无错45码】付费版", "309期", "破"];
        Assert.NotEqual(
            RuleEngine.ExtractFinalValue(Lines, 308, Rule),
            RuleEngine.ExtractFinalValue([.. mixed, .. Lines], 309, Rule));
    }

    [Fact]
    public void GivesUpWhenTheBlockIsIncomplete()
    {
        // 只印出一半号码（期号格之前那段被裁掉）时不能猜，宁可按缺失处理。
        string[] truncated = ["神算子【无错45码】付费版", "308期", "26 28 29 30 31 32 33 34 35", "48 49"];

        Assert.Null(RuleEngine.ExtractFinalValue(truncated, 308, Rule));
    }

    [Fact]
    public void CardWithoutThePrintedCardinalityIsNotThisRule()
    {
        // 「神算子避开」那张一肖卡也带「神算子」，靠「无错45码」区分：本规则必须要求它，
        // 否则两张卡互相抢。
        Assert.Equal("无错45码", Rule.RequiredKeyword);
        Assert.Equal("号码:4", Rule.Type);
        Assert.Equal("missing_numbers_from_49", Rule.Extractor);
        Assert.DoesNotContain("无错45码", RuleCatalog.Load(Path.Combine(
                ResultFilePaths.ConfigurationDirectory(AppContext.BaseDirectory), "新澳六合彩资料.json"))
            .Where(rule => rule.Id != "神算子四码")
            .Select(rule => rule.RequiredKeyword ?? string.Empty));
    }
}
