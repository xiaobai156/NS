using System.Text.Json;
using OcrLineTool;
using Xunit.Abstractions;

namespace OcrLineTool.Tests;

public sealed class October7RealImageTests(ITestOutputHelper output)
{
    [October7RealImageFact]
    [Trait("Category", "CUDA")]
    public async Task HongrenguanAndAiwantingUseConfirmedSourceCards()
    {
        string samples = Environment.GetEnvironmentVariable("OCR_OCT7_SAMPLES")!;
        string report = Environment.GetEnvironmentVariable("OCR_OCT7_REPORT")!;
        Directory.CreateDirectory(report);
        string yanran = Path.Combine(samples, "10.7-嫣然心水");
        string hong = Path.Combine(samples, "10.7-新澳六合彩资料", "全部图片", "20261007_164159_02b811f3_648202.jpg");
        var yanranRules = RuleCatalog.Load(Path.Combine(ResultFilePaths.ConfigurationDirectory(AppContext.BaseDirectory), "嫣然心水.json"));
        var ai = yanranRules.Single(rule => rule.Id == "爱晚亭");
        var hongRule = RuleCatalog.Load(Path.Combine(ResultFilePaths.ConfigurationDirectory(AppContext.BaseDirectory), "新澳六合彩资料.json"))
            .Single(rule => rule.Id == "红人馆");
        var templates = VisualTemplateMatcher.Load(VisualTemplateMatcher.ConfigPath(AppContext.BaseDirectory, "新澳六合彩资料"));
        var matched = Assert.Single(VisualTemplateMatcher.Match([hong], templates, device: LocalOcrDevice.Gpu));
        Assert.Contains("红人馆", matched.Template.RuleIds);
        string crop = Path.Combine(report, "hong-crop.png");
        VisualTemplateMatcher.CreateCrop(matched, crop, includeRemainingRows: true);
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(8));
        var client = new PaddleLocalOcrClient(new SystemProcessRunner(), Path.Combine(report, "isolated-cache.json"), LocalOcrDevice.Gpu);
        string[] paths = Directory.GetFiles(Path.Combine(yanran, "爱晚亭"), "*.jpg").Order().ToArray();
        var small = await client.RecognizeBatchAsync(paths, useCache: false,
            detectionMaxSide: PaddleLocalOcrClient.DetectionMaxSideFor(yanran), cancellationToken: timeout.Token);
        await File.WriteAllTextAsync(Path.Combine(report, "ai-small.json"), JsonSerializer.Serialize(small));
        Assert.Empty(client.LastImageErrors);
        var plans = LocalCandidatePlanner.Build(paths, small, [ai], 280, yanranRules);
        string aiImage = Path.Combine(yanran, "爱晚亭", "20261007_185942_aaa2aa2d_91542.jpg");
        await client.RecognizeBatchAsync([aiImage, crop, hong], useCache: false,
            model: PaddleOcrModel.Medium, cancellationToken: timeout.Token);
        Assert.Empty(client.LastImageErrors);
        var evidence = client.LastEvidence.ToDictionary(pair => pair.Key, pair => pair.Value.Bind(
            OcrEvidenceIdentity.Capture(pair.Key == crop ? hong : pair.Key, pair.Key, "local-primary/medium")));
        await File.WriteAllTextAsync(Path.Combine(report, "medium.json"), JsonSerializer.Serialize(evidence));
        await File.WriteAllTextAsync(Path.Combine(report, "tokens.json"), JsonSerializer.Serialize(
            evidence.ToDictionary(pair => pair.Key, pair => pair.Value.TokenItems)));
        var results = new List<object>();
        foreach (var (path, rule) in new[] { (aiImage, ai), (crop, hongRule), (hong, hongRule) })
        {
            var values = new ResultValues(StringComparer.Ordinal);
            var ledger = new ResultEvidenceLedger();
            MainForm.AddExtractedEvidenceValues(evidence[path], [rule], 280, values, ledger);
            results.Add(new { Path = path, Values = values, Conflicts = values.Conflicts,
                TextValue = RuleEngine.ExtractFinalValue(evidence[path].Lines, 280, rule) });
        }
        await File.WriteAllTextAsync(Path.Combine(report, "results.json"), JsonSerializer.Serialize(
            new { Plans = plans, Results = results }, new JsonSerializerOptions { WriteIndented = true }));
        Assert.Equal(aiImage, Assert.Single(plans).Path);
        Assert.Equal("35 32 44 42 37", RuleEngine.ExtractFinalValue(evidence[aiImage], 280, ai));
        Assert.Equal("01 03 04 05 06 07 10 12 13 15 16 17 18 20 22 23 25 27 28 29 30 31 32 34 35 36 37 39 40 42 43 44 45 47 48 49",
            RuleEngine.ExtractFinalValue(evidence[crop], 280, hongRule));
        output.WriteLine("Fresh GPU candidate, medium extraction and template crop passed for both cards.");
    }
}

public sealed class October7RealImageFactAttribute : FactAttribute
{
    public October7RealImageFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OCR_OCT7_SAMPLES"))
            || string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OCR_OCT7_REPORT")))
            Skip = "Opt-in: supply isolated OCR_OCT7_SAMPLES and OCR_OCT7_REPORT.";
    }
}
