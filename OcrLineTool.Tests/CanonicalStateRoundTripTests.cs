using OcrLineTool;
using Xunit;

namespace OcrLineTool.Tests;

[Trait("Category", "ReauditAccuracy")]
public sealed class CanonicalStateRoundTripTests
{
    [Theory]
    [InlineData("号码:6", "01 02 03 04 05 06", "01,02,03,04,05,06")]
    [InlineData("五行", "金木水火", "土行")]
    [InlineData("单五行", "水", "水行")]
    [InlineData("尾数组合", "1尾+2尾", "1尾 2尾")]
    public async Task CanonicalBusinessValueSurvivesSaveLoadAndFormatting(
        string type, string canonical, string formatted)
    {
        using var temp = new Fixture();
        var rule = new OcrRule("测试规则", type, "测试输出");
        string source = temp.File("source.png", [1, 2, 3, 4, 5]);
        OcrEvidence evidence = OcrEvidence.FromLines(source, ["251期 测试规则"], "test");
        var values = new ResultValues(StringComparer.Ordinal);
        var ledger = new ResultEvidenceLedger();
        ledger.Observe(values, rule, canonical, evidence);

        await RecognitionStateStore.SaveAsync(
            temp.Root, temp.Group, 251, [rule], values, ledger);
        RecognitionStateLoad restored = RecognitionStateStore.Load(
            temp.Root, temp.Group, 251, [rule]);

        Assert.Equal(canonical, restored.Values[rule.Id]);
        string output = Assert.Single(RuleEngine.FormatOutput([rule], restored.Values));
        Assert.Equal($"{formatted} {rule.OutputLabel}", output);
    }

    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = Path.Combine(
            Path.GetTempPath(), "ns-canonical-roundtrip-" + Guid.NewGuid().ToString("N"));
        public string Group { get; }

        public Fixture()
        {
            Directory.CreateDirectory(Root);
            Group = Path.Combine(Root, "测试组");
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
