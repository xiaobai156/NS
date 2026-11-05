using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
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
    public void WholeCardReadingSurvivesTheEvidenceAggregator()
    {
        // 现场 309 期报「未通过4个两位数号码校验」：整页能算出 4 码，但证据聚合还要
        // 求逐行单独也算出同一个值，单行永远算不出，正确结果就被丢掉。走证据入口
        // （生产真实路径）必须同样拿到补集。
        var evidence = new OcrEvidence(
            "card.jpg", "card.jpg", "source-hash", "input-hash", "local-primary/medium",
            Lines.Select(line => new OcrLineEvidence(line, null, null, "local-primary/medium", "main")).ToArray());

        RuleExtractionResult result = RuleEngine.ExtractFinalResult(evidence, 308, Rule);

        Assert.Equal(RuleExtractionStatus.Success, result.Status);
        Assert.Equal("27 41 44 47", result.Value);
    }

    private static OcrLineEvidence[] RecordedTokens(string view)
    {
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "shensuanzu-309.json")));
        return document.RootElement.EnumerateArray().Single(item => item.GetProperty("Name").GetString() == view)
            .GetProperty("Items").EnumerateArray().Select(item =>
            {
                int[] box = item.GetProperty("Box").EnumerateArray().Select(value => value.GetInt32()).ToArray();
                return new OcrLineEvidence(item.GetProperty("Text").GetString()!,
                    new OcrBox(box[0], box[1], box[2], box[3]), 0.99, view, "main");
            }).ToArray();
    }

    private static OcrEvidence Positioned(OcrLineEvidence[] tokens) => new(
            "source", "input", "source-hash", "input-hash", "paddle",
            OcrEvidenceLayout.Partition(tokens), tokens);

    [Theory]
    [InlineData("crop", 309, "14 33 34 36")]
    [InlineData("crop", 308, "27 41 44 47")]
    [InlineData("crop", 307, "28 44 47 48")]
    [InlineData("crop", 306, "01 12 34 39")]
    [InlineData("original", 309, "14 33 34 36")]
    [InlineData("original", 308, "27 41 44 47")]
    [InlineData("original", 307, "28 44 47 48")]
    [InlineData("original", 306, "01 12 34 39")]
    public void RecordedCoordinatesSurviveProductionPartitioning(string view, int issue, string expected)
    {
        // Exact medium OCR text and boxes from the failing crop and original.
        RuleExtractionResult result = RuleEngine.ExtractFinalResult(Positioned(RecordedTokens(view)), issue, Rule);

        Assert.Equal(RuleExtractionStatus.Success, result.Status);
        Assert.Equal(expected, result.Value);
    }

    [Theory]
    [InlineData("01 02 03 04 05 06 07 08", false)]
    [InlineData("01 02 03 04 05 06 07 08 08", false)]
    [InlineData("01 02 03 04 05 06 07 08 09 09", false)]
    [InlineData("01 02 03 04 05 06 07 08 09 14", false)]
    [InlineData("1 02 03 04 05 06 07 08 09", false)]
    [InlineData("50 02 03 04 05 06 07 08 09", false)]
    [InlineData("010203040506070809", true)]
    public void WholeCellRequiresExactlyFortyFiveValidUniquePairs(string firstRow, bool valid)
    {
        OcrLineEvidence[] tokens = RecordedTokens("crop");
        tokens[1] = tokens[1] with { Text = firstRow };
        Assert.Equal(valid ? "14 33 34 36" : null,
            RuleEngine.ExtractFinalValue(Positioned(tokens), 309, Rule));
    }

    [Fact]
    public void MissingTopRowsCannotBorrowFromFollowingPeriod()
    {
        OcrLineEvidence[] tokens = RecordedTokens("crop").Where(item => item.Box!.Y >= 187).ToArray();
        Assert.Null(RuleEngine.ExtractFinalValue(Positioned(tokens), 309, Rule));
    }

    [Fact]
    public void WholeBoxMustStayInsideTheTargetPeriod()
    {
        OcrLineEvidence[] tokens = RecordedTokens("crop");
        // The centre is inside 309, but the box crosses the next period boundary.
        tokens[8] = tokens[8] with { Box = new OcrBox(138, 325, 96, 35) };
        Assert.Null(RuleEngine.ExtractFinalValue(Positioned(tokens), 309, Rule));
    }

    [Fact]
    public void DifferentViewsAndExplicitRegionsCannotCompleteOneAnother()
    {
        OcrLineEvidence[] tokens = RecordedTokens("crop");
        OcrLineEvidence[] otherView = tokens.Select((item, index) =>
            index == 8 ? item with { ViewId = "other" } : item).ToArray();
        OcrLineEvidence[] otherRegion = tokens.Select((item, index) =>
            index == 8 ? item with { RegionId = "other" } : item).ToArray();
        Assert.Null(RuleEngine.ExtractFinalValue(Positioned(otherView), 309, Rule));
        Assert.Null(RuleEngine.ExtractFinalValue(Positioned(otherRegion), 309, Rule));
    }

    [Fact]
    public void DifferentCompleteReadingsConflict()
    {
        OcrLineEvidence[] tokens = RecordedTokens("crop");
        OcrLineEvidence[] other = tokens.Select((item, index) => item with
        {
            ViewId = "other",
            Text = index == 2 ? item.Text.Replace("13", "14") : item.Text
        }).ToArray();
        Assert.Equal(RuleExtractionStatus.Conflict,
            RuleEngine.ExtractFinalResult(Positioned([.. tokens, .. other]), 309, Rule).Status);
    }

    [Theory]
    [InlineData("309期")]
    [InlineData("308期")]
    [InlineData("开??")]
    public void MissingOwnershipEvidenceStaysMissing(string removed)
    {
        OcrLineEvidence[] tokens = RecordedTokens("crop").Where(item => item.Text != removed).ToArray();
        Assert.Null(RuleEngine.ExtractFinalValue(Positioned(tokens), 309, Rule));
    }

    [Fact]
    public void TargetIssueIsDynamic()
    {
        OcrLineEvidence[] tokens = RecordedTokens("crop").Select(item => item with
        {
            Text = Regex.Replace(item.Text, @"\d{3}(?=期)", match => (int.Parse(match.Value) - 99).ToString())
        }).ToArray();
        Assert.Equal("14 33 34 36", RuleEngine.ExtractFinalValue(Positioned(tokens), 210, Rule));
        Assert.Null(RuleEngine.ExtractFinalValue(Positioned(tokens), 309, Rule));
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
