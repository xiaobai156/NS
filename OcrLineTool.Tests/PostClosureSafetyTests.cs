using OcrLineTool;
using Xunit;

namespace OcrLineTool.Tests;

[Trait("Category", "ReauditAccuracy")]
public sealed class PostClosureSafetyTests
{
    private static OcrRule NearbyRule() => new(
        "九宫寻肖", "生肖", "九宫格肖肖",
        AllowNearbyValue: true, StrictIssueBlock: true);

    private static OcrRule XiaotengRule() => new(
        "骁腾杀一肖", "生肖", "骁腾杀肖",
        RequiredKeyword: "杀一肖", Folder: "骁腾系列",
        AllowNearbyValue: true, StrictIssueBlock: true);

    private static OcrRule ProductionHalfWaveRule() => RuleCatalog.Load(Path.Combine(
        ResultFilePaths.ConfigurationDirectory(AppContext.BaseDirectory), "黄大仙新澳.json"))
        .Single(rule => rule.Id == "红红半波");

    [Fact]
    public void StrictNearbyZodiacStopsAtTheNextBareFourDigitIssue()
    {
        Assert.Null(RuleEngine.ExtractFinalValue(
            ["九宫格肖肖", "第1001期", "1002", "虎"], 1001, NearbyRule()));
    }

    [Fact]
    public void StrictNearbyZodiacStillReadsAValueInsideTheSelectedIssueBlock()
    {
        Assert.Equal("虎", RuleEngine.ExtractFinalValue(
            ["九宫格肖肖", "第1001期", "宣传文字", "更多宣传文字", "虎"], 1001, NearbyRule()));
    }

    [Fact]
    public void PositionedEvidenceCannotBorrowTheNextBareIssueZodiac()
    {
        string[] lines = ["九宫格肖肖", "第1001期", "1002", "虎"];
        var evidence = new OcrEvidence(
            "source.png", "input.png", "source-hash", "input-hash", "test",
            lines.Select((text, index) => new OcrLineEvidence(
                text, new OcrBox(0, index * 30, 500, 20), 0.99, "test", "main")).ToArray());
        Assert.Equal(RuleExtractionStatus.Missing,
            RuleEngine.ExtractFinalResult(evidence, 1001, NearbyRule()).Status);
    }

    [Fact]
    public void StrictDragonflySingleValueRejectsTwoLeadingFramesAsConflict()
    {
        RuleExtractionResult result = RuleEngine.ExtractFinalResult(
            ["246期骁腾杀一肖《虎》《兔》"], 246, XiaotengRule());
        Assert.Equal(RuleExtractionStatus.Conflict, result.Status);
        Assert.Null(result.Value);
    }

    [Fact]
    public void StrictDragonflyRepeatedMarkerWithDifferentValuesIsConflict()
    {
        RuleExtractionResult result = RuleEngine.ExtractFinalResult(
            ["246期骁腾杀一肖《虎》骁腾杀一肖《兔》"], 246, XiaotengRule());
        Assert.Equal(RuleExtractionStatus.Conflict, result.Status);
        Assert.Null(result.Value);
    }

    [Fact]
    public void StrictDragonflyRepeatedMarkerWithSameValueRemainsOneSuccess()
    {
        Assert.Equal("虎", RuleEngine.ExtractFinalValue(
            ["246期骁腾杀一肖《虎》骁腾杀一肖《虎》"], 246, XiaotengRule()));
    }

    [Fact]
    public void StrictDragonflySingleValueKeepsOneFramedValue()
    {
        Assert.Equal("虎", RuleEngine.ExtractFinalValue(
            ["246期骁腾杀一肖《虎》"], 246, XiaotengRule()));
    }

    [Fact]
    public void GenericHeadFieldWithTwoActualHeadsIsConflict()
    {
        var rule = new OcrRule("测试头", "头");
        RuleExtractionResult result = RuleEngine.ExtractFinalResult(
            ["251期 测试头 杀一头：1头 2头"], 251, rule);
        Assert.Equal(RuleExtractionStatus.Conflict, result.Status);
    }

    [Fact]
    public void GenericTailFallbackWithTwoActualDigitsIsConflict()
    {
        var rule = new OcrRule("测试尾", "尾");
        RuleExtractionResult result = RuleEngine.ExtractFinalResult(
            ["251期 测试尾 杀一尾 1 2"], 251, rule);
        Assert.Equal(RuleExtractionStatus.Conflict, result.Status);
    }

    [Fact]
    public void GenericTailPairWithTwoCompletePairsIsConflict()
    {
        var rule = new OcrRule("测试双尾", "尾数组合");
        RuleExtractionResult result = RuleEngine.ExtractFinalResult(
            ["251期 测试双尾 1尾+2尾 3尾+4尾"], 251, rule);
        Assert.Equal(RuleExtractionStatus.Conflict, result.Status);
    }

    [Fact]
    public void GenericTailPairKeepsOneCompletePair()
    {
        var rule = new OcrRule("测试双尾", "尾数组合");
        Assert.Equal("1尾+2尾", RuleEngine.ExtractFinalValue(
            ["251期 测试双尾 1尾+2尾"], 251, rule));
    }

    [Fact]
    public void GenericTailPairRejectsTheSameTailTwice()
    {
        var rule = new OcrRule("测试双尾", "尾数组合");
        Assert.Null(RuleEngine.ExtractFinalValue(
            ["251期 测试双尾 1尾+1尾"], 251, rule));
    }

    [Fact]
    public void GenericFiveElementComplementRejectsDuplicateRawEntries()
    {
        var rule = new OcrRule("测试四行", "五行");
        Assert.Null(RuleEngine.ExtractFinalValue(
            ["251期 测试四行 金木水火火"], 251, rule));
    }

    [Fact]
    public void GenericMissingTailRejectsDuplicateRawEntries()
    {
        var rule = new OcrRule("测试九尾", "缺尾");
        Assert.Null(RuleEngine.ExtractFinalValue(
            ["251期 测试九尾 0 1 2 3 4 5 6 7 8 8"], 251, rule));
    }

    [Fact]
    public void GenericMissingHeadRejectsDuplicateRawEntries()
    {
        var rule = new OcrRule("测试四头", "缺头");
        Assert.Null(RuleEngine.ExtractFinalValue(
            ["251期 测试四头 0 0 1 2 3"], 251, rule));
    }

    [Fact]
    public void ProductionHalfWaveTypeUsesTheSameColorParityContractInsideItsConfiguredSection()
    {
        OcrRule rule = ProductionHalfWaveRule();
        Assert.Equal("半波", rule.Type);
        Assert.Equal("杀半波", rule.Section);
        Assert.Equal("绿单", RuleEngine.ExtractFinalValue(
            ["红红半波", "245期杀半波 红红半波【绿单】开"], 245, rule));
        Assert.True(RuleEngine.IsCanonicalValueValid(rule, "绿单"));
    }

    [Fact]
    public void ProductionHalfWaveTypeRejectsUnknownColorsInsideItsConfiguredSection()
    {
        OcrRule rule = ProductionHalfWaveRule();
        Assert.Null(RuleEngine.ExtractFinalValue(
            ["红红半波", "245期杀半波 红红半波【紫双】开"], 245, rule));
    }
}
