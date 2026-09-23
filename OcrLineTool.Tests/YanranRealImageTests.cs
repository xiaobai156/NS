using System.Text.Json;
using OcrLineTool;
using Xunit.Abstractions;

namespace OcrLineTool.Tests;

public sealed class YanranRealImageTests(ITestOutputHelper output)
{
    // Explicit opt-in only. The directory is supplied by the operator; ordinary
    // regression runs never read a daily folder or start a model.
    [YanranRealImageTheory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task CandidateThroughEvidenceUsesTheConfirmedImages(int deviceNumber)
    {
        string root = Environment.GetEnvironmentVariable("OCR_YANRAN_SAMPLE_DIRECTORY")!;
        string report = Environment.GetEnvironmentVariable("OCR_YANRAN_REPORT_DIRECTORY")!;
        var device = (LocalOcrDevice)deviceNumber;
        Directory.CreateDirectory(report);
        var expected = new Dictionary<string, (string File, string Value)>
        {
            ["雁塔题名杀头"] = ("20260922_121805_85442.jpg", "2头"),
            ["雁塔题名半波"] = ("20260922_121805_85442.jpg", "蓝双"),
            ["欧阳半波"] = ("20260922_195308_85582.jpg", "绿双"),
            ["紫燕儿杀一肖"] = ("20260922_153900_85501.jpg", "猪")
        };
        var catalog = RuleCatalog.Load(Path.Combine(
            ResultFilePaths.ConfigurationDirectory(AppContext.BaseDirectory), "嫣然心水.json"));
        OcrRule[] rules = catalog.Where(rule => expected.ContainsKey(rule.Id)).ToArray();
        // Sibling images participate only to verify that these four rules choose
        // their own sources. No other rule produces a result.
        string[] paths = rules.Select(rule => rule.Folder!).Distinct()
            .SelectMany(folder => Directory.GetFiles(Path.Combine(root, folder), "*.jpg"))
            .Order(StringComparer.OrdinalIgnoreCase).ToArray();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(12));
        var client = new PaddleLocalOcrClient(new SystemProcessRunner(),
            Path.Combine(report, device + "-unused-cache.json"), device);
        var small = await client.RecognizeBatchAsync(paths, useCache: false,
            detectionMaxSide: PaddleLocalOcrClient.DetectionMaxSideFor(root), cancellationToken: timeout.Token);
        await File.WriteAllTextAsync(Path.Combine(report, device + "-small.json"), JsonSerializer.Serialize(small));
        Assert.Empty(client.LastImageErrors);
        var plans = LocalCandidatePlanner.Build(paths, small, rules, 265, catalog);
        foreach (var rule in rules)
        {
            var plan = Assert.Single(plans, plan => plan.Rules.Any(item => item.Id == rule.Id));
            output.WriteLine($"{device} candidate {rule.Id}: {Path.GetFileName(plan.Path)}");
            Assert.Equal(expected[rule.Id].File, Path.GetFileName(plan.Path));
        }
        await client.RecognizeBatchAsync(plans.Select(plan => plan.Path).Distinct().ToArray(),
            useCache: false, model: PaddleOcrModel.Medium, cancellationToken: timeout.Token);
        Assert.Empty(client.LastImageErrors);
        await File.WriteAllTextAsync(Path.Combine(report, device + "-evidence.json"), JsonSerializer.Serialize(
            client.LastEvidence.Select(pair => new { Path = pair.Key, Evidence = pair.Value, Tokens = pair.Value.TokenItems })));
        var values = new ResultValues(StringComparer.Ordinal);
        var ledger = new ResultEvidenceLedger();
        foreach (var plan in plans)
        {
            var evidence = client.LastEvidence[plan.Path].Bind(
                OcrEvidenceIdentity.Capture(plan.Path, plan.Path, "local-primary/medium"));
            MainForm.AddExtractedEvidenceValues(evidence, plan.Rules, 265, values, ledger);
        }
        foreach (var rule in rules)
        {
            output.WriteLine($"{device} result {rule.Id}: {values.GetValueOrDefault(rule.Id, "MISSING")}, conflict={values.Conflicts.Contains(rule.Id)}");
        }
        Assert.Empty(values.Conflicts);
        foreach (var rule in rules)
        {
            Assert.Equal(expected[rule.Id].Value, values.GetValueOrDefault(rule.Id));
            Assert.Equal(expected[rule.Id].File, Path.GetFileName(ledger.Records[rule.Id].SourcePath));
        }
        string[] lines = GroupResultFormatter.Format(rules, RuleEngine.FormatOutput(rules, values));
        await File.WriteAllLinesAsync(Path.Combine(report, device + "-results.txt"), lines);
    }
}

public sealed class YanranRealImageTheoryAttribute : TheoryAttribute
{
    public YanranRealImageTheoryAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OCR_YANRAN_SAMPLE_DIRECTORY"))
            || string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OCR_YANRAN_REPORT_DIRECTORY")))
            Skip = "Opt-in: provide OCR_YANRAN_SAMPLE_DIRECTORY and isolated OCR_YANRAN_REPORT_DIRECTORY.";
    }
}
