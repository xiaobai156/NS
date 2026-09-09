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
    public void DirectionalNumberTableCannotBorrowAnUnknownLabeledRow()
    {
        OcrRule rule = Rule("嫣然心水", "蓝色");
        string first = string.Join(' ', Enumerable.Range(1, 17).Select(n => n.ToString("00")));
        string second = string.Join(' ', Enumerable.Range(18, 17).Select(n => n.ToString("00")));
        RuleExtractionResult result = RuleEngine.ExtractFinalResult(
        [
            "251期 特码开在",
            first,
            second,
            "其他栏目 35 36",
            "250期 特码开在"
        ], 251, rule);

        Assert.Equal(RuleExtractionStatus.Missing, result.Status);
        Assert.Null(result.Value);
    }

    [Fact]
    public void ReviewedCenteredRowCannotBorrowAnUnknownLabeledLeftCell()
    {
        OcrRule rule = Rule("新澳六合彩资料", "杀料");
        RuleExtractionResult result = RuleEngine.ExtractFinalResult(
        [
            "杀料网绝杀10码",
            "其他栏目 01 02",
            "251期 绝杀:03 04 05 06 07 08 09 10"
        ], 251, rule);

        Assert.Equal(RuleExtractionStatus.Missing, result.Status);
        Assert.Null(result.Value);
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
    public void PublishGuardKeepsSourceAndActualInputStableUntilDistributionFinishes()
    {
        using var temp = new TempFiles("嫣然心水");
        string source = temp.File("source.png", [5, 6, 7, 8]);
        string input = temp.File("crop.png", [8, 7, 6, 5]);
        OcrRule rule = Rule("嫣然心水", "南国挽心");
        var evidence = new OcrEvidence(
            source,
            input,
            LocalOcrIdentity.Image(source),
            LocalOcrIdentity.Image(input),
            "test",
            [new("251期 南国挽心 鸡", null, 0.99, "test", "main")]);
        var values = new ResultValues(StringComparer.Ordinal);
        var ledger = new ResultEvidenceLedger();
        ledger.Observe(values, rule, "鸡", evidence);

        using (RecognitionStateStore.LockCurrentEvidenceForPublish([rule], values, ledger))
        {
            Assert.Throws<IOException>(() => File.WriteAllBytes(source, [1, 1, 1, 1]));
            Assert.Throws<IOException>(() => File.WriteAllBytes(input, [2, 2, 2, 2]));
        }

        File.WriteAllBytes(source, [1, 1, 1, 1]);
        File.WriteAllBytes(input, [2, 2, 2, 2]);
        Assert.Equal([1, 1, 1, 1], File.ReadAllBytes(source));
        Assert.Equal([2, 2, 2, 2], File.ReadAllBytes(input));
    }

    [Fact]
    public async Task TrustedStateRejectsAReplacedActualInputView()
    {
        using var temp = new TempFiles("嫣然心水");
        string source = temp.File("source.png", [3, 4, 5, 6]);
        string input = temp.File("crop.png", [6, 5, 4, 3]);
        OcrRule rule = Rule("嫣然心水", "南国挽心");
        var evidence = new OcrEvidence(
            source,
            input,
            LocalOcrIdentity.Image(source),
            LocalOcrIdentity.Image(input),
            "test",
            [new("251期 南国挽心 鸡", null, 0.99, "test", "main")]);
        var values = new ResultValues(StringComparer.Ordinal);
        var ledger = new ResultEvidenceLedger();
        ledger.Observe(values, rule, "鸡", evidence);

        await RecognitionStateStore.SaveAsync(
            temp.Root, temp.GroupDirectory, 251, [rule], values, ledger);
        File.WriteAllBytes(input, [9, 8, 7, 6]);

        RecognitionStateLoad restored = RecognitionStateStore.Load(
            temp.Root, temp.GroupDirectory, 251, [rule]);
        Assert.False(restored.Values.ContainsKey(rule.Id));
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
