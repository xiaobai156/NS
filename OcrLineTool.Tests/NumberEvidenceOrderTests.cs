using OcrLineTool;

namespace OcrLineTool.Tests;

public sealed class NumberEvidenceOrderTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NumberEvidenceComparesCompleteSetsWithoutChangingOutputOrder(bool differentNumber)
    {
        OcrRule rule = RuleCatalog.Load(Path.Combine(ResultFilePaths.ConfigurationDirectory(AppContext.BaseDirectory),
            "新澳六合彩资料.json")).Single(rule => rule.Id == "红人馆");
        string first = string.Join(' ', Enumerable.Range(1, 36).Select(number => number.ToString("00")));
        string second = string.Join(' ', Enumerable.Range(1, 36).Reverse()
            .Select(number => (differentNumber && number == 36 ? 49 : number).ToString("00")));
        var evidence = new OcrEvidence("source", "input", "hash", "hash", "view",
        [
            new($"280期 {first} 开??", new OcrBox(0, 0, 700, 30), ViewId: "a"),
            new($"280期 {second} 开??", new OcrBox(0, 0, 700, 30), ViewId: "b")
        ]);
        var result = RuleEngine.ExtractFinalResult(evidence, 280, rule);
        Assert.Equal(differentNumber ? RuleExtractionStatus.Conflict : RuleExtractionStatus.Success, result.Status);
        if (!differentNumber)
            Assert.Equal(first, result.Value);
    }
}
