using System.Text.Json;
using OcrLineTool;
using Xunit;

namespace OcrLineTool.Tests;

[Trait("Category", "ReauditAccuracy")]
public sealed class F18EvidenceTests
{
    private static OcrRule Rule(string group, string id) => RuleCatalog.Load(Path.Combine(
        ResultFilePaths.ConfigurationDirectory(AppContext.BaseDirectory), group + ".json"))
        .Single(rule => rule.Id == id);

    [Fact]
    public void OpaqueSeparateLinesCannotBeJoinedToCompleteSixNumbers()
    {
        using var temp = new TempEvidenceFiles("嫣然心水");
        string image = temp.File("sample.png", new byte[] { 1, 2, 3, 4 });
        OcrEvidence evidence = OcrEvidence.FromLines(image,
        [
            "251期 青苹果 杀六码 01 02 03 04 05",
            "06"
        ], "legacy-cloud");

        Assert.Null(RuleEngine.ExtractFinalValue(evidence, 251, Rule("嫣然心水", "青苹果")));
        Assert.Contains(evidence.Lines, OcrLayoutMarkers.IsBoundary);
    }

    [Fact]
    public void PositionedSameColumnRowsCanStillFormOneReviewedField()
    {
        using var temp = new TempEvidenceFiles("嫣然心水");
        string image = temp.File("sample.png", new byte[] { 1, 2, 3, 4 });
        string hash = LocalOcrIdentity.Image(image);
        var evidence = new OcrEvidence(
            image, image, hash, hash, "test",
            OcrEvidenceLayout.Partition(
            [
                new("251期 青苹果 杀六码 01 02 03 04 05", new OcrBox(10, 10, 420, 20), 0.99, "test", "main"),
                new("06", new OcrBox(10, 40, 30, 20), 0.98, "test", "main")
            ]));

        Assert.Equal("01 02 03 04 05 06",
            RuleEngine.ExtractFinalValue(evidence, 251, Rule("嫣然心水", "青苹果")));
    }

    [Fact]
    public void DifferentRenderedViewsCannotBeConcatenatedIntoOneAnswer()
    {
        using var temp = new TempEvidenceFiles("嫣然心水");
        string image = temp.File("sample.png", new byte[] { 9, 8, 7 });
        string hash = LocalOcrIdentity.Image(image);
        var evidence = new OcrEvidence(image, image, hash, hash, "paddle",
        [
            new("251期 青苹果 杀六码 01 02 03 04 05", new OcrBox(0, 0, 400, 20), 0.97, "paddle/header", "main"),
            new("06", new OcrBox(0, 30, 30, 20), 0.98, "paddle/compact", "main")
        ]);

        Assert.Null(RuleEngine.ExtractFinalValue(evidence, 251, Rule("嫣然心水", "青苹果")));
    }

    [Fact]
    public void TwoPhysicalRegionsWithDifferentValidValuesAreAConflict()
    {
        using var temp = new TempEvidenceFiles("嫣然心水");
        string image = temp.File("sample.png", new byte[] { 6, 6, 6 });
        string hash = LocalOcrIdentity.Image(image);
        var evidence = new OcrEvidence(image, image, hash, hash, "test",
        [
            new("251期 南国挽心 鸡", new OcrBox(0, 10, 220, 20), 0.99, "test", "column-0"),
            new("251期 南国挽心 狗", new OcrBox(800, 10, 220, 20), 0.99, "test", "column-1")
        ]);

        Assert.Null(RuleEngine.ExtractFinalValue(evidence, 251, Rule("嫣然心水", "南国挽心")));
    }

    [Fact]
    public void TencentEvidenceRetainsGeometryConfidenceAndPhysicalRegions()
    {
        string json = JsonSerializer.Serialize(new
        {
            Response = new
            {
                TextDetections = new object[]
                {
                    new { DetectedText = "251期 青苹果 杀六码 01 02 03 04 05", Confidence = 98.5,
                        ItemPolygon = new { X = 0, Y = 10, Width = 400, Height = 20 } },
                    new { DetectedText = "旁栏参考：06", Confidence = 76.0,
                        ItemPolygon = new { X = 900, Y = 10, Width = 120, Height = 20 } }
                }
            }
        });

        IReadOnlyList<OcrLineEvidence> items = TencentOcrClient.ParseEvidenceItems(json);
        Assert.All(items, item => Assert.NotNull(item.Box));
        Assert.Contains(items, item => item.Confidence == 98.5);
        Assert.Equal(2, items.Select(item => item.RegionId).Distinct().Count());
    }

    [Fact]
    public void BaiduEvidenceUsesLocationAndProbabilityWhenProvided()
    {
        string json = JsonSerializer.Serialize(new
        {
            words_result = new object[]
            {
                new
                {
                    words = "251期 南国挽心 鸡",
                    location = new { left = 12, top = 34, width = 210, height = 25 },
                    probability = new { average = 0.963 }
                }
            }
        });

        OcrLineEvidence item = Assert.Single(BaiduOcrClient.ParseEvidenceItems(json));
        Assert.Equal(new OcrBox(12, 34, 210, 25), item.Box);
        Assert.Equal(0.963, item.Confidence);
    }

    [Fact]
    public void BindingRejectsAChangedSourceImage()
    {
        using var temp = new TempEvidenceFiles("嫣然心水");
        string image = temp.File("sample.png", new byte[] { 1, 2, 3 });
        OcrEvidenceIdentity identity = OcrEvidenceIdentity.Capture(image, image, "original");
        OcrEvidence response = OcrEvidence.FromLines(image, ["251期 南国挽心 鸡"], "cloud");
        File.WriteAllBytes(image, new byte[] { 4, 5, 6 });

        OcrException error = Assert.Throws<OcrException>(() => response.Bind(identity));
        Assert.Equal("OCR_IMAGE_CHANGED", error.Code);
    }

    [Fact]
    public async Task StructuredStateRestoresOnlyWhileItsSourceHashStillMatches()
    {
        using var temp = new TempEvidenceFiles("嫣然心水");
        string image = temp.File("sample.png", new byte[] { 1, 3, 5, 7 });
        OcrRule rule = Rule("嫣然心水", "南国挽心");
        OcrEvidence evidence = OcrEvidence.FromLines(image, ["251期 南国挽心 鸡"], "test");
        var values = new ResultValues(StringComparer.Ordinal);
        var ledger = new ResultEvidenceLedger();
        string value = Assert.IsType<string>(RuleEngine.ExtractFinalValue(evidence, 251, rule));
        ledger.Observe(values, rule, value, evidence);

        await RecognitionStateStore.SaveAsync(
            temp.Root, temp.GroupDirectory, 251, [rule], values, ledger);
        RecognitionStateLoad restored = RecognitionStateStore.Load(
            temp.Root, temp.GroupDirectory, 251, [rule]);
        Assert.Equal("鸡", restored.Values[rule.Id]);
        Assert.Equal("success", restored.Evidence.Records[rule.Id].Status);
        Assert.Equal(LocalOcrIdentity.Image(image), restored.Evidence.Records[rule.Id].SourceHash);

        File.WriteAllBytes(image, new byte[] { 2, 4, 6, 8 });
        RecognitionStateLoad afterReplacement = RecognitionStateStore.Load(
            temp.Root, temp.GroupDirectory, 251, [rule]);
        Assert.False(afterReplacement.Values.ContainsKey(rule.Id));
    }

    [Fact]
    public async Task AFormattedValueWithoutEvidenceIsNeverPersistedAsTrustedState()
    {
        using var temp = new TempEvidenceFiles("嫣然心水");
        OcrRule rule = Rule("嫣然心水", "南国挽心");
        var values = new ResultValues(StringComparer.Ordinal) { [rule.Id] = "鸡" };
        var ledger = new ResultEvidenceLedger();

        await RecognitionStateStore.SaveAsync(
            temp.Root, temp.GroupDirectory, 251, [rule], values, ledger);
        RecognitionStateLoad restored = RecognitionStateStore.Load(
            temp.Root, temp.GroupDirectory, 251, [rule]);
        Assert.Empty(restored.Values);
    }

    private sealed class TempEvidenceFiles : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "ns-evidence-" + Guid.NewGuid().ToString("N"));
        public string GroupDirectory { get; }

        public TempEvidenceFiles(string group)
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
