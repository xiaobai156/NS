using OcrLineTool;
using Xunit;

namespace OcrLineTool.Tests;

// Regression coverage for reviewed wrapped number cards whose payload is
// printed after the issue cell, for multi-row next-card leading runs, and for
// the four-head card whose digits are printed without spaces.
[Trait("Category", "ReauditAccuracy")]
public sealed class WrappedTableEvidenceRegressionTests
{
    private static OcrRule Rule(string group, string id) => RuleCatalog.Load(Path.Combine(
        ResultFilePaths.ConfigurationDirectory(AppContext.BaseDirectory), group + ".json"))
        .Single(rule => rule.Id == id);

    [Fact]
    public void PayloadAfterTheIssueRowStillCompletes()
    {
        OcrRule rule = Rule("新澳六合彩资料", "金钱网");
        OcrLineEvidence[] tokens =
        [
            new("金钱网【必杀12个特码】100%准", new OcrBox(60, 100, 600, 60), 0.99, "paddle", "main"),
            new("杀特码：01 02 03 04 05 06 07 08 09", new OcrBox(140, 188, 430, 31), 0.99, "paddle", "main"),
            new("253期", new OcrBox(10, 201, 84, 36), 0.99, "paddle", "main"),
            new("开??", new OcrBox(700, 202, 68, 34), 0.99, "paddle", "main"),
            new("10 11 12", new OcrBox(330, 219, 120, 29), 0.99, "paddle", "main"),
            new("252期", new OcrBox(9, 285, 83, 35), 0.99, "paddle", "main"),
            new("杀特码：04 05 06 07 08 09 10 11 12 13", new OcrBox(141, 272, 430, 31), 0.99, "paddle", "main")
        ];
        var evidence = new OcrEvidence(
            "source", "input", "source-hash", "input-hash", "paddle",
            OcrEvidenceLayout.Partition(tokens), tokens);

        Assert.Equal("01 02 03 04 05 06 07 08 09 10 11 12",
            RuleEngine.ExtractFinalValue(evidence, 253, rule));
    }

    [Fact]
    public void MultiRowNextCardLeadingRunIsNotBorrowed()
    {
        OcrRule rule = Rule("新澳六合彩资料", "龙王");
        var tokens = new List<OcrLineEvidence>
        {
            new("龙王庙【36个中特码】付费版", new OcrBox(92, 107, 690, 77), 0.99, "paddle", "main")
        };
        for (int row = 0; row < 4; row++)
        {
            for (int column = 0; column < 9; column++)
            {
                int number = row * 9 + column + 1;
                tokens.Add(new OcrLineEvidence(
                    number.ToString("00"),
                    new OcrBox(148 + column * 54, 215 + row * 34, 45, 33),
                    0.99, "paddle", "main"));
            }
            if (row == 1)
            {
                tokens.Add(new OcrLineEvidence("253期", new OcrBox(11, 263, 95, 40), 0.99, "paddle", "main"));
                tokens.Add(new OcrLineEvidence("开??", new OcrBox(685, 264, 78, 38), 0.99, "paddle", "main"));
            }
        }
        for (int row = 0; row < 2; row++)
        {
            for (int column = 0; column < 9; column++)
            {
                int number = row * 9 + column + 1;
                tokens.Add(new OcrLineEvidence(
                    number.ToString("00"),
                    new OcrBox(147 + column * 54, 378 + row * 34, 44, 33),
                    0.99, "paddle", "main"));
            }
        }
        tokens.Add(new OcrLineEvidence("252期", new OcrBox(11, 440, 95, 40), 0.99, "paddle", "main"));
        var evidence = new OcrEvidence(
            "source", "input", "source-hash", "input-hash", "paddle",
            OcrEvidenceLayout.Partition(tokens), tokens);

        Assert.Equal(
            string.Join(' ', Enumerable.Range(1, 36).Select(number => number.ToString("00"))),
            RuleEngine.ExtractFinalValue(evidence, 253, rule));
    }

    [Fact]
    public void FourHeadDigitsWithoutSpacesStillComplement()
    {
        OcrRule rule = Rule("新澳六合彩资料", "藏宝头");

        Assert.Equal("4头", RuleEngine.ExtractFinalValue(
            ["藏宝库【无错四头】付费版", "253期", "今晚买【0123】头√", "开??", "252期"],
            253, rule));
        Assert.Null(RuleEngine.ExtractFinalValue(
            ["藏宝库【无错四头】付费版", "253期", "今晚买【0112】头√", "开??", "252期"],
            253, rule));
    }
}
