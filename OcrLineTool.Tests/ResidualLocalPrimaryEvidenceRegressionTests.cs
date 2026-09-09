using OcrLineTool;
using Xunit;

namespace OcrLineTool.Tests;

[Trait("Category", "ReauditAccuracy")]
public sealed class ResidualLocalPrimaryEvidenceRegressionTests
{
    private static OcrRule Rule(string group, string id) => RuleCatalog.Load(Path.Combine(
        ResultFilePaths.ConfigurationDirectory(AppContext.BaseDirectory), group + ".json"))
        .Single(rule => rule.Id == id);

    [Fact]
    public void ApplyingOneEvidenceWithTwoCompleteValuesMarksConflict()
    {
        string root = Path.Combine(Path.GetTempPath(), "ns-local-primary-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string image = Path.Combine(root, "source.png");
        File.WriteAllBytes(image, [1, 2, 3, 4]);
        try
        {
            OcrRule rule = Rule("嫣然心水", "南国挽心");
            OcrEvidence evidence = OcrEvidence.FromLines(image,
            [
                "251期 南国挽心 鸡",
                "251期 南国挽心 狗"
            ], "local-primary-test");
            var values = new ResultValues(StringComparer.Ordinal);
            var ledger = new ResultEvidenceLedger();

            MainForm.AddExtractedEvidenceValues(evidence, [rule], 251, values, ledger);

            Assert.True(ResultValues.IsConflict(values, rule.Id));
            Assert.False(values.ContainsKey(rule.Id));
            Assert.Equal("conflict", ledger.Records[rule.Id].Status);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    [Fact]
    public void SameRunCloudEvidenceCannotBeReusedAfterSourceReplacement()
    {
        string root = Path.Combine(Path.GetTempPath(), "ns-local-primary-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string image = Path.Combine(root, "source.png");
        File.WriteAllBytes(image, [4, 3, 2, 1]);
        try
        {
            byte[] bytes = File.ReadAllBytes(image);
            OcrEvidence evidence = OcrEvidence.FromCapturedBytes(image, bytes, "local-primary/cloud",
            [
                new("251期 南国挽心 鸡", new OcrBox(10, 10, 180, 20), 0.98, "cloud", "main")
            ]);

            Assert.True(MainForm.IsEvidenceCurrent(evidence));
            File.WriteAllBytes(image, [9, 9, 9, 9]);
            Assert.False(MainForm.IsEvidenceCurrent(evidence));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }
}
