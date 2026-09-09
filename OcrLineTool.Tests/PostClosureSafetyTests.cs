using OcrLineTool;
using Xunit;

namespace OcrLineTool.Tests;

[Trait("Category", "ReauditAccuracy")]
public sealed class PostClosureSafetyTests
{
    private static OcrRule NearbyRule() => new(
        "九宫寻肖",
        "生肖",
        "九宫格肖肖",
        AllowNearbyValue: true,
        StrictIssueBlock: true);

    [Fact]
    public void StrictNearbyZodiacStopsAtTheNextBareFourDigitIssue()
    {
        OcrRule rule = NearbyRule();

        Assert.Null(RuleEngine.ExtractFinalValue(
            ["九宫格肖肖", "第1001期", "1002", "虎"], 1001, rule));
    }

    [Fact]
    public void StrictNearbyZodiacStillReadsAValueInsideTheSelectedIssueBlock()
    {
        OcrRule rule = NearbyRule();

        Assert.Equal("虎", RuleEngine.ExtractFinalValue(
            ["九宫格肖肖", "第1001期", "宣传文字", "更多宣传文字", "虎"], 1001, rule));
    }

    [Fact]
    public void PositionedEvidenceCannotBorrowTheNextBareIssueZodiac()
    {
        OcrRule rule = NearbyRule();
        string[] lines = ["九宫格肖肖", "第1001期", "1002", "虎"];
        var evidence = new OcrEvidence(
            "source.png",
            "input.png",
            "source-hash",
            "input-hash",
            "test",
            lines.Select((text, index) => new OcrLineEvidence(
                text,
                new OcrBox(0, index * 30, 500, 20),
                0.99,
                "test",
                "main")).ToArray());

        Assert.Equal(
            RuleExtractionStatus.Missing,
            RuleEngine.ExtractFinalResult(evidence, 1001, rule).Status);
    }
}
