using OcrLineTool;
using Xunit;

namespace OcrLineTool.Tests;

[Trait("Category", "ReauditAccuracy")]
public sealed class IssueBoundaryConsistencyTests
{
    private static OcrRule NearbyZodiacRule() => new(
        "水哥杀一肖",
        "生肖",
        "水哥肖",
        AllowNearbyValue: true,
        StrictIssueBlock: true);

    [Fact]
    public void NearbySingleZodiacDoesNotCrossFourDigitBareIssueBoundary()
    {
        Assert.Null(RuleEngine.ExtractFinalValue(
            ["水哥杀一肖", "1001期", "1000", "狗"],
            1001,
            NearbyZodiacRule()));
    }

    [Fact]
    public void NearbySingleZodiacDoesNotBorrowBackwardAcrossFourDigitBareIssueBoundary()
    {
        Assert.Null(RuleEngine.ExtractFinalValue(
            ["狗", "1000", "说明", "1001期"],
            1001,
            NearbyZodiacRule()));
    }

    [Fact]
    public void SingleHeadDoesNotCrossFourDigitBareIssueBoundary()
    {
        var rule = new OcrRule("测试单头", "头", SingleValuePerIssue: true);

        Assert.Null(RuleEngine.ExtractFinalValue(
            ["1001期", "说明", "1000", "2头"],
            1001,
            rule));
    }

    [Fact]
    public void ZodiacSummaryDoesNotCrossFourDigitBareIssueBoundary()
    {
        var rule = new OcrRule("苏柒若", "生肖");

        Assert.Null(RuleEngine.ExtractFinalValue(
            ["禁肖统计表", "1001期", "1000", "苏柒若正正禁狗"],
            1001,
            rule));
    }

    [Fact]
    public void FrequencyTableDoesNotCrossFourDigitBareIssueBoundary()
    {
        var rule = new OcrRule("借花献佛", "统计生肖");

        Assert.Null(RuleEngine.ExtractFinalValue(
            ["1001期", "1000", "9次 狗"],
            1001,
            rule));
    }

    [Fact]
    public void FrequencyContinuationDoesNotCrossFourDigitBareIssueBoundary()
    {
        var rule = new OcrRule("借花献佛", "统计生肖");

        Assert.Null(RuleEngine.ExtractFinalValue(
            ["1001期", "9次：", "1000", "狗"],
            1001,
            rule));
    }
}
