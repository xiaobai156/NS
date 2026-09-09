using OcrLineTool;
using Xunit;

namespace OcrLineTool.Tests;

[Trait("Category", "ReauditAccuracy")]
public sealed class FinalLifecycleClosureTests
{
    private static OcrRule Rule(string group, string id) => RuleCatalog.Load(Path.Combine(
        ResultFilePaths.ConfigurationDirectory(AppContext.BaseDirectory), group + ".json"))
        .Single(rule => rule.Id == id);

    [Fact]
    public async Task TemporaryInputIsPersistedBeforeTrustedStateIsSaved()
    {
        using var temp = new Fixture("嫣然心水");
        string source = temp.File("source.png", [1, 2, 3, 4]);
        string cropFolder = Path.Combine(temp.Root, "temporary-crops");
        Directory.CreateDirectory(cropFolder);
        string crop = Path.Combine(cropFolder, "crop.png");
        File.WriteAllBytes(crop, [9, 8, 7, 6]);
        OcrRule rule = Rule("嫣然心水", "南国挽心");
        OcrEvidence evidence = Evidence(source, crop, "251期 南国挽心 鸡");
        var values = new ResultValues(StringComparer.Ordinal);
        var ledger = new ResultEvidenceLedger();
        ledger.Observe(values, rule, "鸡", evidence);

        await RecognitionStateStore.SaveAsync(temp.Root, temp.Group, 251, [rule], values, ledger);
        Directory.Delete(cropFolder, recursive: true);

        RecognitionStateLoad restored = RecognitionStateStore.Load(temp.Root, temp.Group, 251, [rule]);
        Assert.Equal("鸡", restored.Values[rule.Id]);
        ResultEvidenceRecord record = restored.Evidence.Records[rule.Id];
        Assert.True(File.Exists(record.InputPath));
        Assert.NotEqual(Path.GetFullPath(crop), Path.GetFullPath(record.InputPath));
        Assert.Equal(record.InputHash, LocalOcrIdentity.Image(record.InputPath), ignoreCase: true);

        // Same-session retry/publish must also use the persistent evidence path.
        ResultEvidenceRecord live = ledger.Records[rule.Id];
        Assert.Equal(record.InputPath, live.InputPath);
        using IDisposable publish = RecognitionStateStore.LockCurrentEvidenceForPublish([rule], values, ledger);
    }

    [Fact]
    public async Task PersistedCropStillRequiresTheOriginalSourceVersion()
    {
        using var temp = new Fixture("嫣然心水");
        string source = temp.File("source.png", [1, 1, 2, 2]);
        string crop = temp.File("crop.png", [3, 3, 4, 4]);
        OcrRule rule = Rule("嫣然心水", "南国挽心");
        var values = new ResultValues(StringComparer.Ordinal);
        var ledger = new ResultEvidenceLedger();
        ledger.Observe(values, rule, "鸡", Evidence(source, crop, "251期 南国挽心 鸡"));
        await RecognitionStateStore.SaveAsync(temp.Root, temp.Group, 251, [rule], values, ledger);

        File.WriteAllBytes(source, [8, 8, 8, 8]);
        RecognitionStateLoad restored = RecognitionStateStore.Load(temp.Root, temp.Group, 251, [rule]);
        Assert.False(restored.Values.ContainsKey(rule.Id));
    }

    [Fact]
    public void PinnedCandidateIdentityRejectsSourceReplacementAfterCropCreation()
    {
        using var temp = new Fixture("嫣然心水");
        string source = temp.File("source.png", [1, 2, 3]);
        string crop = temp.File("crop.png", [7, 8, 9]);
        OcrEvidenceIdentity pinned = OcrEvidenceIdentity.Capture(source, crop, "candidate");
        File.WriteAllBytes(source, [4, 5, 6]);

        OcrException error = Assert.Throws<OcrException>(() =>
            MainForm.RequirePinnedCandidateIdentity(pinned, source, crop, "cloud/test"));
        Assert.Equal("OCR_IMAGE_CHANGED", error.Code);
    }

    [Fact]
    public void PinnedCandidateIdentityRetainsTheCreationHashes()
    {
        using var temp = new Fixture("嫣然心水");
        string source = temp.File("source.png", [1, 2, 3]);
        string crop = temp.File("crop.png", [7, 8, 9]);
        OcrEvidenceIdentity pinned = OcrEvidenceIdentity.Capture(source, crop, "candidate");

        OcrEvidenceIdentity request = MainForm.RequirePinnedCandidateIdentity(
            pinned, source, crop, "cloud/test");
        Assert.Equal(pinned.SourceHash, request.SourceHash);
        Assert.Equal(pinned.InputHash, request.InputHash);
        Assert.Equal("cloud/test", request.ViewId);
    }

    [Fact]
    public async Task InvalidSuccessIsDroppedWithoutLosingAnotherRulesConflict()
    {
        using var temp = new Fixture("嫣然心水");
        OcrRule conflictRule = Rule("嫣然心水", "南国挽心");
        OcrRule successRule = Rule("嫣然心水", "陌上花");
        string conflictSource = temp.File("conflict.png", [1, 2, 3, 4]);
        string successSource = temp.File("success.png", [5, 6, 7, 8]);
        var values = new ResultValues(StringComparer.Ordinal);
        var ledger = new ResultEvidenceLedger();
        ledger.ObserveConflict(values, conflictRule,
            Evidence(conflictSource, conflictSource, "251期 南国挽心 鸡 狗"));
        ledger.Observe(values, successRule, "鸡",
            Evidence(successSource, successSource, "251期 陌上花 鸡"));
        File.WriteAllBytes(successSource, [9, 9, 9, 9]);
        var missing = new Dictionary<string, string>(StringComparer.Ordinal);

        using (RecognitionStateStore.LockValidEvidenceForPublish(
            [conflictRule, successRule], values, ledger, missing))
        {
            Assert.True(ResultValues.IsConflict(values, conflictRule.Id));
            Assert.False(values.ContainsKey(successRule.Id));
            Assert.Contains(successRule.Id, missing.Keys);
            await RecognitionStateStore.SaveAsync(
                temp.Root, temp.Group, 251, [conflictRule, successRule], values, ledger);
        }

        RecognitionStateLoad restored = RecognitionStateStore.Load(
            temp.Root, temp.Group, 251, [conflictRule, successRule]);
        Assert.True(ResultValues.IsConflict(restored.Values, conflictRule.Id));
        Assert.False(restored.Values.ContainsKey(successRule.Id));
        string[] lines = RuleEngine.FormatOutput(
            [conflictRule, successRule], restored.Values, missing);
        Assert.Contains(lines, line => line == $"缺失（同一期结果冲突，待核对） {conflictRule.OutputLabel}");
    }

    [Fact]
    public async Task ConflictOnlyTrustedStateRemainsUsableForManualRevocationOutput()
    {
        using var temp = new Fixture("嫣然心水");
        OcrRule rule = Rule("嫣然心水", "南国挽心");
        string source = temp.File("source.png", [2, 4, 6, 8]);
        var values = new ResultValues(StringComparer.Ordinal);
        var ledger = new ResultEvidenceLedger();
        ledger.ObserveConflict(values, rule,
            Evidence(source, source, "251期 南国挽心 鸡 狗"));
        await RecognitionStateStore.SaveAsync(temp.Root, temp.Group, 251, [rule], values, ledger);

        string[] output = RecognitionStateStore.BuildTrustedOutputLines(
            temp.Root, temp.Group, 251, [rule]);
        Assert.Equal([$"缺失（同一期结果冲突，待核对） {rule.OutputLabel}"], output);
    }

    private static OcrEvidence Evidence(string source, string input, string text) => new(
        Path.GetFullPath(source), Path.GetFullPath(input),
        LocalOcrIdentity.Image(source), LocalOcrIdentity.Image(input), "test",
        [new(text, new OcrBox(0, 0, 500, 20), 0.99, "test", "main")]);

    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(),
            "ns-final-lifecycle-" + Guid.NewGuid().ToString("N"));
        public string Group { get; }
        public Fixture(string group)
        {
            Directory.CreateDirectory(Root);
            Group = Path.Combine(Root, group);
            Directory.CreateDirectory(Group);
        }
        public string File(string name, byte[] bytes)
        {
            string path = Path.Combine(Group, name);
            System.IO.File.WriteAllBytes(path, bytes);
            return path;
        }
        public void Dispose()
        {
            try { Directory.Delete(Root, recursive: true); } catch { }
        }
    }
}
