using OcrLineTool;
using Xunit;

namespace OcrLineTool.Tests;

[Trait("Category", "ReauditAccuracy")]
public sealed class FinalResidualClosureTests
{
    private static OcrRule Rule(string group, string id) => RuleCatalog.Load(Path.Combine(
        ResultFilePaths.ConfigurationDirectory(AppContext.BaseDirectory), group + ".json"))
        .Single(rule => rule.Id == id);

    [Fact]
    public void UnknownLabeledTwoNumberLineCannotCompleteTargetNumberField()
    {
        using var temp = new TempFiles("嫣然心水");
        string image = temp.File("source.png", [1, 2, 3, 4]);
        OcrRule rule = Rule("嫣然心水", "青苹果");
        OcrEvidence evidence = OcrEvidence.FromLines(image,
        [
            "251期 青苹果 杀六码 01 03 04 05",
            "其他栏目 10 02"
        ], "opaque");

        RuleExtractionResult result = RuleEngine.ExtractFinalResult(evidence, 251, rule);
        Assert.Equal(RuleExtractionStatus.Missing, result.Status);
        Assert.Null(result.Value);
    }

    [Fact]
    public void PureNumericContinuationStillCompletesTheSameField()
    {
        OcrRule rule = Rule("嫣然心水", "青苹果");
        RuleExtractionResult result = RuleEngine.ExtractFinalResult(
            ["251期 青苹果 杀六码 01 03 04 05", "10 02"], 251, rule);

        Assert.Equal(RuleExtractionStatus.Success, result.Status);
        Assert.Equal("01 03 04 05 10 02", result.Value);
    }

    [Fact]
    public void OwnRepeatedNumberLabelCanContinueTheField()
    {
        OcrRule rule = Rule("嫣然心水", "青苹果");
        RuleExtractionResult result = RuleEngine.ExtractFinalResult(
            ["251期 青苹果 杀六码 01 03 04 05", "杀六码 10 02"], 251, rule);

        Assert.Equal(RuleExtractionStatus.Success, result.Status);
        Assert.Equal("01 03 04 05 10 02", result.Value);
    }

    [Fact]
    public void PublishGuardRejectsAReplacedSuccessfulSource()
    {
        using var temp = new TempFiles("嫣然心水");
        string image = temp.File("source.png", [1, 2, 3, 4]);
        OcrRule rule = Rule("嫣然心水", "南国挽心");
        OcrEvidence evidence = OcrEvidence.FromLines(image, ["251期 南国挽心 鸡"], "test");
        var values = new ResultValues(StringComparer.Ordinal);
        var ledger = new ResultEvidenceLedger();
        ledger.Observe(values, rule, "鸡", evidence);

        File.WriteAllBytes(image, [9, 9, 9, 9]);

        OcrException error = Assert.Throws<OcrException>(() =>
            RecognitionStateStore.LockCurrentEvidenceForPublish([rule], values, ledger));
        Assert.Equal("OCR_IMAGE_CHANGED", error.Code);
    }

    [Fact]
    public void PublishGuardKeepsSuccessfulSourceStableUntilDistributionFinishes()
    {
        using var temp = new TempFiles("嫣然心水");
        string image = temp.File("source.png", [5, 6, 7, 8]);
        OcrRule rule = Rule("嫣然心水", "南国挽心");
        OcrEvidence evidence = OcrEvidence.FromLines(image, ["251期 南国挽心 鸡"], "test");
        var values = new ResultValues(StringComparer.Ordinal);
        var ledger = new ResultEvidenceLedger();
        ledger.Observe(values, rule, "鸡", evidence);

        using (RecognitionStateStore.LockCurrentEvidenceForPublish([rule], values, ledger))
        {
            Assert.Throws<IOException>(() => File.WriteAllBytes(image, [1, 1, 1, 1]));
        }

        File.WriteAllBytes(image, [1, 1, 1, 1]);
        Assert.Equal([1, 1, 1, 1], File.ReadAllBytes(image));
    }

    private sealed class TempFiles : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "ns-final-closure-" + Guid.NewGuid().ToString("N"));
        public string GroupDirectory { get; }

        public TempFiles(string group)
        {
            Directory.CreateDirectory(Root);
            GroupDirectory = Path.Combine(Root, group);
            Directory.CreateDirectory(GroupDirectory);
        }

        public string File(string name, byte[] bytes)
        {
            string path = Path.Combine(GroupDirectory, name);
            System.IO.File.WriteAllBytes(path, bytes);
            return path;
        }

        public void Dispose()
        {
            try { Directory.Delete(Root, recursive: true); } catch { }
        }
    }
}
