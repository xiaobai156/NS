using OcrLineTool;

namespace OcrLineTool.Tests;

public sealed class ResidualAuditRound3ProposedTests
{
    [Fact]
    public void BareIssueLikeRowsDoNotCompleteThePreviousNumberField()
    {
        var rule = new OcrRule("祥瑞阁", "号码:36", StrictIssueBlock: true);
        string[] lines =
        [
            "祥瑞阁王中王包围36码",
            "祥瑞阁王中王包围36码 01 03 04 05 06 07 08 09 10 12 13 14 15 16 17 18 19",
            "1001期",
            "20 21 22 23 24 25 26 27 28 29 30 31 32 33 34 35 36",
            "开？？",
            "1102"
        ];

        Assert.Null(RuleEngine.ExtractFinalValue(lines, 1001, rule));
    }

    [Fact]
    public void MultipleNumberBracketsRemainAConflict()
    {
        var rule = new OcrRule("青苹果", "号码:6");
        string[] lines = ["251期 青苹果 杀六码【01 02 03 04 05 06】【07 08】"];

        Assert.Equal(RuleExtractionStatus.Missing,
            RuleEngine.ExtractFinalResult(lines, 251, rule).Status);
    }

    [Theory]
    [InlineData("杀一头【1头】【2头】")]
    [InlineData("杀一头 1头 2头")]
    [InlineData("杀一头【1】【2】")]
    [InlineData("杀一头：【1头】【2头】")]
    public void HeadCandidatesUseTheWholeField(string payload)
    {
        var rule = new OcrRule("齐天大圣", "头");
        Assert.Equal(RuleExtractionStatus.Conflict,
            RuleEngine.ExtractFinalResult([$"251期 齐天大圣 {payload}"], 251, rule).Status);
    }

    [Fact]
    public void MultipleWideHeadersCannotMergeBodyColumns()
    {
        OcrLineEvidence[] items =
        [
            new("青苹果资料", new(0, 0, 650, 20)),
            new("资料总标题", new(0, 25, 650, 20)),
            new("251期 青苹果 杀六码 01 02 03 04 05", new(0, 60, 400, 20)),
            new("06", new(700, 90, 20, 20))
        ];

        IReadOnlyList<OcrLineEvidence> partitioned = OcrEvidenceLayout.Partition(items);
        Assert.Contains(partitioned, item => item.RegionId == "column-0");
        Assert.Contains(partitioned, item => item.RegionId == "column-1");
        Assert.NotEqual(
            partitioned.Where(item => item.Text == "251期 青苹果 杀六码 01 02 03 04 05").Single().RegionId,
            partitioned.Where(item => item.Text == "06").Single().RegionId);
    }
}
