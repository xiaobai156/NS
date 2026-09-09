using OcrLineTool;
using Xunit;

namespace OcrLineTool.Tests;

[Trait("Category", "ReauditAccuracy")]
public sealed class FinalClosureTests
{
    private static OcrRule Rule(string group, string id) => RuleCatalog.Load(Path.Combine(
        ResultFilePaths.ConfigurationDirectory(AppContext.BaseDirectory), group + ".json"))
        .Single(rule => rule.Id == id);

    [Fact]
    public void PaidCloudFallbackMustCompareAllRulesOnThatCandidate()
    {
        OcrRule zodiac = Rule("嫣然心水", "紫燕儿杀一肖");
        OcrRule tail = Rule("嫣然心水", "紫燕儿尾");
        IReadOnlyList<OcrRule> candidateRules = [zodiac, tail];
        var accepted = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [zodiac.Id] = "鸡"
        };

        IReadOnlyList<OcrRule> compare = MainForm.RulesForAlreadyRequestedCloudFallback(
            candidateRules, accepted);

        Assert.Equal(candidateRules.Select(rule => rule.Id), compare.Select(rule => rule.Id));
    }

    [Fact]
    public void NoCloudFallbackIsPlannedWhenEveryCandidateRuleAlreadySucceeded()
    {
        OcrRule zodiac = Rule("嫣然心水", "紫燕儿杀一肖");
        OcrRule tail = Rule("嫣然心水", "紫燕儿尾");
        IReadOnlyList<OcrRule> candidateRules = [zodiac, tail];
        var accepted = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [zodiac.Id] = "鸡",
            [tail.Id] = "8尾"
        };

        Assert.Empty(MainForm.RulesForAlreadyRequestedCloudFallback(candidateRules, accepted));
    }

    [Fact]
    public void ManualDistributionSourceRequiresEvidenceBackedState()
    {
        using var temp = new TempState("嫣然心水");
        OcrRule rule = Rule("嫣然心水", "南国挽心");

        OcrException error = Assert.Throws<OcrException>(() =>
            RecognitionStateStore.BuildTrustedOutputLines(
                temp.Root, temp.GroupDirectory, 251, [rule]));

        Assert.Equal("OCR_STATE_REQUIRED", error.Code);
    }

    [Fact]
    public async Task ManualDistributionLinesAreRebuiltFromTrustedStateNotTxtText()
    {
        using var temp = new TempState("嫣然心水");
        OcrRule rule = Rule("嫣然心水", "南国挽心");
        string image = temp.File("source.png", [1, 2, 3, 4, 5]);
        OcrEvidence evidence = OcrEvidence.FromLines(image, ["251期 南国挽心 鸡"], "test");
        var values = new ResultValues(StringComparer.Ordinal);
        var ledger = new ResultEvidenceLedger();
        string value = Assert.IsType<string>(RuleEngine.ExtractFinalValue(evidence, 251, rule));
        ledger.Observe(values, rule, value, evidence);
        await RecognitionStateStore.SaveAsync(
            temp.Root, temp.GroupDirectory, 251, [rule], values, ledger);

        // A display file may be edited or stale; it is intentionally irrelevant
        // to the trusted manual-distribution source.
        string display = ResultFilePaths.ForGroup(temp.Root, temp.GroupDirectory, 251);
        Directory.CreateDirectory(Path.GetDirectoryName(display)!);
        await System.IO.File.WriteAllTextAsync(display, "狗 南国挽心\n");

        string[] output = RecognitionStateStore.BuildTrustedOutputLines(
            temp.Root, temp.GroupDirectory, 251, [rule]);
        Assert.Contains("鸡 南国挽心", output);
        Assert.DoesNotContain("狗 南国挽心", output);
    }

    [Fact]
    public async Task RuleContractChangeInvalidatesOldTrustedState()
    {
        using var temp = new TempState("嫣然心水");
        OcrRule original = Rule("嫣然心水", "南国挽心");
        string image = temp.File("source.png", [9, 8, 7, 6]);
        OcrEvidence evidence = OcrEvidence.FromLines(image, ["251期 南国挽心 鸡"], "test");
        var values = new ResultValues(StringComparer.Ordinal);
        var ledger = new ResultEvidenceLedger();
        ledger.Observe(values, original, "鸡", evidence);
        await RecognitionStateStore.SaveAsync(
            temp.Root, temp.GroupDirectory, 251, [original], values, ledger);

        OcrRule changed = original with { Section = "变更后的栏目契约" };
        RecognitionStateLoad restored = RecognitionStateStore.Load(
            temp.Root, temp.GroupDirectory, 251, [changed]);

        Assert.Empty(restored.Values);
    }

    private sealed class TempState : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "ns-final-closure-" + Guid.NewGuid().ToString("N"));
        public string GroupDirectory { get; }

        public TempState(string group)
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
