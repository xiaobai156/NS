using System.Text.Json;
using OcrLineTool;
using Xunit.Abstractions;

namespace OcrLineTool.Tests;

public sealed class TreasureRealImageTests(ITestOutputHelper output)
{
    [TreasureRealImageTheory]
    [InlineData(LocalOcrDevice.Cpu)]
    [InlineData(LocalOcrDevice.Gpu)]
    public async Task ConfirmedCardUsesTemplateCropAndDynamicPeriods(LocalOcrDevice device)
    {
        string image = Environment.GetEnvironmentVariable("OCR_TREASURE_SAMPLE")!;
        string report = Environment.GetEnvironmentVariable("OCR_TREASURE_REPORT")!;
        Directory.CreateDirectory(report);
        var catalog = VisualTemplateMatcher.Load(VisualTemplateMatcher.ConfigPath(AppContext.BaseDirectory, "新澳高级会员"));
        var match = Assert.Single(VisualTemplateMatcher.Match([image], catalog, device: device));
        Assert.Equal("藏宝九肖", match.Template.Id);
        string crop = Path.Combine(report, device + "-crop.png");
        VisualTemplateMatcher.CreateCrop(match, crop, includeRemainingRows: true);
        OcrRule rule = RuleCatalog.Load(Path.Combine(ResultFilePaths.ConfigurationDirectory(AppContext.BaseDirectory),
            "新澳高级会员.json")).Single(rule => rule.Id == "藏宝九肖");
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(8));
        var client = new PaddleLocalOcrClient(new SystemProcessRunner(), Path.Combine(report, device + "-cache.json"), device);
        await client.RecognizeBatchAsync([crop], useCache: false, model: PaddleOcrModel.Medium, cancellationToken: timeout.Token);
        Assert.Empty(client.LastImageErrors);
        OcrEvidence evidence = client.LastEvidence[crop].Bind(OcrEvidenceIdentity.Capture(image, crop, "local-primary/medium"));
        await File.WriteAllTextAsync(Path.Combine(report, device + "-evidence.json"), JsonSerializer.Serialize(
            new { Evidence = evidence, Tokens = evidence.TokenItems }));
        var lines = new List<string>();
        foreach (var (issue, expected) in new[] { (267, "东西北"), (266, "东南北"), (265, "东西北"), (264, "西南北") })
        {
            var values = new ResultValues(StringComparer.Ordinal);
            var ledger = new ResultEvidenceLedger();
            MainForm.AddExtractedEvidenceValues(evidence, [rule], issue, values, ledger);
            Assert.Empty(values.Conflicts);
            Assert.Equal(expected, values.GetValueOrDefault(rule.Id));
            string line = Assert.Single(RuleEngine.FormatOutput([rule], values));
            Assert.Equal(expected + " 藏宝九肖", line);
            lines.Add($"{issue}: {line}");
            output.WriteLine($"{device}: {issue}: {line}; template distance={match.Distance}");
        }
        Assert.Null(RuleEngine.ExtractFinalValue(evidence, 268, rule));
        await File.WriteAllLinesAsync(Path.Combine(report, device + "-results.txt"), lines);
    }
}

public sealed class TreasureRealImageTheoryAttribute : TheoryAttribute
{
    public TreasureRealImageTheoryAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OCR_TREASURE_SAMPLE"))
            || string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OCR_TREASURE_REPORT")))
            Skip = "Opt-in: supply the confirmed OCR_TREASURE_SAMPLE and isolated OCR_TREASURE_REPORT.";
    }
}
