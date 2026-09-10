using OcrLineTool;
using Xunit;

namespace OcrLineTool.Tests;

// Regression coverage for 新澳高级会员 wrapped number cards whose cells are
// physically separated into columns by the layout partitioner.
[Trait("Category", "ReauditAccuracy")]
public sealed class MemberWrappedCardEvidenceTests
{
    private static OcrRule Rule(string id) => RuleCatalog.Load(Path.Combine(
        ResultFilePaths.ConfigurationDirectory(AppContext.BaseDirectory), "新澳高级会员.json"))
        .Single(rule => rule.Id == id);

    [Fact]
    public void WrappedBracketTableCardSurvivesColumnPartitioning()
    {
        OcrRule rule = Rule("表弟");
        // Same geometry as the real card: title, wrapped bracket lines in the
        // middle, the issue cell on the left and the opening cell on the right.
        OcrLineEvidence[] tokens =
        [
            new("【表弟主36码】√", new OcrBox(237, 107, 369, 60), 0.99, "paddle", "main"),
            new("36码【01 02 03 04 05 06 07 08 09", new OcrBox(132, 192, 507, 32), 0.99, "paddle", "main"),
            new("10 11 12 13 14 15 16 17 18 19 20", new OcrBox(137, 233, 500, 30), 0.99, "paddle", "main"),
            new("253期", new OcrBox(12, 250, 91, 38), 0.99, "paddle", "main"),
            new("开??", new OcrBox(692, 248, 70, 43), 0.99, "paddle", "main"),
            new("21 22 23 24 25 26 27 28 29 30 31", new OcrBox(136, 271, 498, 32), 0.99, "paddle", "main"),
            new("32 33 34 35 36】", new OcrBox(255, 313, 247, 31), 0.99, "paddle", "main")
        ];
        var evidence = new OcrEvidence(
            "source", "input", "source-hash", "input-hash", "paddle",
            OcrEvidenceLayout.Partition(tokens), tokens);

        Assert.Equal(
            "01 02 03 04 05 06 07 08 09 10 11 12 13 14 15 16 17 18 19 20 21 22 23 24 25 26 27 28 29 30 31 32 33 34 35 36",
            RuleEngine.ExtractFinalValue(evidence, 253, rule));
    }

    [Fact]
    public void SplitIssueAndOpeningCellsCannotHideWrappedPayload()
    {
        OcrRule rule = Rule("翩翩公子杀十码");
        // Payload printed left of the issue cell and right of the opening cell.
        OcrLineEvidence[] tokens =
        [
            new("公子神算杀10码： 01 02 03", new OcrBox(177, 263, 420, 31), 0.99, "paddle", "main"),
            new("253期", new OcrBox(25, 279, 89, 33), 0.99, "paddle", "main"),
            new("开??", new OcrBox(678, 279, 73, 34), 0.99, "paddle", "main"),
            new("04 05 06 07 08 09 10 准！", new OcrBox(164, 298, 427, 30), 0.99, "paddle", "main")
        ];
        var evidence = new OcrEvidence(
            "source", "input", "source-hash", "input-hash", "paddle",
            OcrEvidenceLayout.Partition(tokens), tokens);

        Assert.Equal("01 02 03 04 05 06 07 08 09 10",
            RuleEngine.ExtractFinalValue(evidence, 253, rule));
    }
}
