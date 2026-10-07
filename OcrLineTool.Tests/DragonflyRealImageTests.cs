using System.Text.Json;
using OcrLineTool;
using Xunit.Abstractions;

namespace OcrLineTool.Tests;

public sealed class DragonflyRealImageTests(ITestOutputHelper output)
{
    [DragonflyRealImageFact]
    [Trait("Category", "CUDA")]
    public async Task SharedCardSelectsBothFieldsAndRoutesOneZodiac()
    {
        // Explicit opt-in: an isolated copy of the confirmed three-card folder.
        string root = Environment.GetEnvironmentVariable("OCR_DRAGONFLY_SAMPLES")!;
        string report = Environment.GetEnvironmentVariable("OCR_DRAGONFLY_REPORT")!;
        Directory.CreateDirectory(report);
        var catalog = RuleCatalog.Load(Path.Combine(
            ResultFilePaths.ConfigurationDirectory(AppContext.BaseDirectory), "蜻蜓一套.json"));
        OcrRule[] rules = catalog.Where(rule => rule.Id is "红蜻蜓" or "红蜻蜓杀一肖").ToArray();
        Assert.Equal(2, rules.Length);
        string[] paths = Directory.GetFiles(Path.Combine(root, "一套组合拳"), "*.jpg").Order().ToArray();
        var hashes = paths.ToDictionary(path => path, path => LocalOcrIdentity.Image(path));
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(8));
        var client = new PaddleLocalOcrClient(new SystemProcessRunner(),
            Path.Combine(report, "isolated-cache.json"), LocalOcrDevice.Gpu);
        var small = await client.RecognizeBatchAsync(paths, useCache: false,
            detectionMaxSide: PaddleLocalOcrClient.DetectionMaxSideFor(root), cancellationToken: timeout.Token);
        await File.WriteAllTextAsync(Path.Combine(report, "small.json"), JsonSerializer.Serialize(small));
        Assert.Empty(client.LastImageErrors);
        var plan = Assert.Single(LocalCandidatePlanner.Build(paths, small, rules, 280, catalog));
        Assert.Equal("20261007_185152_d8cee43c_273083.jpg", Path.GetFileName(plan.Path));
        Assert.Equal(2, plan.Rules.Count);
        await client.RecognizeBatchAsync([plan.Path], useCache: false,
            model: PaddleOcrModel.Medium, cancellationToken: timeout.Token);
        Assert.Empty(client.LastImageErrors);
        OcrEvidence evidence = client.LastEvidence[plan.Path].Bind(
            OcrEvidenceIdentity.Capture(plan.Path, plan.Path, "local-primary/medium"));
        await File.WriteAllTextAsync(Path.Combine(report, "medium.json"), JsonSerializer.Serialize(evidence));
        var values = new ResultValues(StringComparer.Ordinal);
        var ledger = new ResultEvidenceLedger();
        MainForm.AddExtractedEvidenceValues(evidence, plan.Rules, 280, values, ledger);
        Assert.Empty(values.Conflicts);
        Assert.Equal("蛇", values.GetValueOrDefault("红蜻蜓杀一肖"));
        Assert.Equal("猪兔鸡猴马牛羊龙鼠", values.GetValueOrDefault("红蜻蜓"));
        OcrRule single = rules.Single(rule => rule.Id == "红蜻蜓杀一肖");
        Assert.Equal("牛", RuleEngine.ExtractFinalValue(evidence, 279, single));
        Assert.Equal("猴", RuleEngine.ExtractFinalValue(evidence, 278, single));
        Assert.Equal("龙", RuleEngine.ExtractFinalValue(evidence, 277, single));
        Assert.Null(RuleEngine.ExtractFinalValue(evidence, 281, single));
        string[] lines = RuleEngine.FormatOutput(rules, values).ToArray();
        string target = Path.Combine(report, "280期-肖-新增.txt");
        await File.WriteAllTextAsync(target, "生肖次数排行榜\r\n");
        var written = await ResultDistributor.DistributeAsync(root, 280, lines, report,
            Path.Combine(ResultFilePaths.ConfigurationDirectory(AppContext.BaseDirectory), "肖新增分发规则.json"));
        Assert.Equal("蛇 红蜻蜓杀一肖", Assert.Single(written));
        Assert.Contains("蛇 红蜻蜓杀一肖", await File.ReadAllTextAsync(target));
        Assert.All(hashes, pair => Assert.Equal(pair.Value, LocalOcrIdentity.Image(pair.Key)));
        await File.WriteAllTextAsync(Path.Combine(report, "results.json"), JsonSerializer.Serialize(new
        {
            Device = "gpu:0", Candidate = plan.Path, Issue = 280, Values = values,
            HistoricalChecks = new { Issue279 = "牛", Issue278 = "猴", Issue277 = "龙", Issue281 = "MISSING" },
            Distributed = written, SourceHashes = hashes
        }, new JsonSerializerOptions { WriteIndented = true }));
        output.WriteLine($"GPU: {paths.Length} source cards; correct shared candidate; {string.Join(" / ", lines)}; isolated distribution passed.");
    }
}

public sealed class DragonflyRealImageFactAttribute : FactAttribute
{
    public DragonflyRealImageFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OCR_DRAGONFLY_SAMPLES"))
            || string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OCR_DRAGONFLY_REPORT")))
            Skip = "Opt-in: supply isolated OCR_DRAGONFLY_SAMPLES and OCR_DRAGONFLY_REPORT.";
    }
}
