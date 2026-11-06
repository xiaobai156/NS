using System.Diagnostics;
using System.Drawing;
using System.Text.Json;
using System.Text.RegularExpressions;
using OcrLineTool;

namespace OcrLineTool.Tests;

public sealed class HeikeTailRecoveryTests
{
    private const string GroupPath = @"C:\images\11.6-黑色六合彩\全部图片\card.jpg";
    private static OcrRule Rule => RuleCatalog.Load(Path.Combine(
        ResultFilePaths.ConfigurationDirectory(AppContext.BaseDirectory), "黑色六合彩.json")).Single(r => r.Id == "黑客杀尾");
    private static OcrLineEvidence Token(string text, int x, int y, int w = 80, int h = 36) =>
        new(text, new(x, y, w, h), .99, "paddle/original");
    private static OcrEvidence Evidence(params OcrLineEvidence[] tokens) =>
        new(GroupPath, GroupPath, "hash", "hash", "paddle", OcrEvidenceLayout.Partition(tokens), tokens);
    private static OcrLineEvidence[] Strip(string digits = "012345678", int issue = 310) =>
        [Token($"{issue}期", 10, 10), Token("数据分析" + digits + "尾", 130, 10, 480), Token("开??", 670, 10)];

    [Theory]
    [InlineData("11.6-黑色六合彩", true)]
    [InlineData("311期-黑色六合彩", true)]
    [InlineData("黑色六合彩备份", false)]
    [InlineData("新澳六合彩资料", false)]
    public void OnlyTheNamedRuleAndGroupEnterRecovery(string group, bool allowed)
    {
        string path = $@"C:\images\{group}\全部图片\card.jpg";
        Assert.Equal(allowed, MainForm.AllowsSummaryRowRecoveryCandidate(path, Rule, SummaryRowRecoveryKind.HeikeTail));
        Assert.False(MainForm.AllowsSummaryRowRecoveryCandidate(path, Rule with { Label = "锦囊杀尾" }, SummaryRowRecoveryKind.HeikeTail));
        Assert.True(SummaryRowRecovery.RecoversBeforeCloud(Rule));
        Assert.False(SummaryRowRecovery.RecoversBeforeCloud(Rule with { Extractor = "jinnang_nine_tails" }));
        Assert.False(SummaryRowRecovery.SupportsHeikeTailRecovery(Rule with { Type = "尾" }));
    }

    [Fact]
    public void CropUsesExplicitPeriodGeometryRatherThanBusinessDigitsOrRequestedPosition()
    {
        OcrLineEvidence[] items = [Token("310期", 13, 221, 94), Token("309期", 13, 280, 94, 37),
            Token("123456", 400, 225), Token("310", 500, 228), Token("310期开", 13, 10)];
        Assert.Equal((215, 48), SummaryRowRecovery.ComputeHeikeTailBand(items, 310));
        Assert.Null(SummaryRowRecovery.ComputeHeikeTailBand(items, 311));
        Assert.Null(SummaryRowRecovery.ComputeHeikeTailBand([items[0]], 310));
        Assert.Null(SummaryRowRecovery.ComputeHeikeTailBand([..items, items[0]], 310));
        Assert.Null(SummaryRowRecovery.ComputeHeikeTailBand([items[0], items[1] with { Box = new(13, 241, 94, 36) }], 310));
        foreach (var bad in new[] { items[0] with { Box = null }, items[0] with { Confidence = .4 },
            items[0] with { Confidence = double.NaN }, items[0] with { ViewId = "paddle/compact" },
            items[0] with { Text = "310期数据分析" }, items[0] with { Text = "310期309期" } })
            Assert.Null(SummaryRowRecovery.ComputeHeikeTailBand([bad, items[1]], 310));
        Assert.Null(SummaryRowRecovery.ComputeHeikeTailBand([items[0], items[1] with { ViewId = "other" }], 310));
    }

    [Theory]
    [InlineData("012345678", "9尾")]
    [InlineData("023456789", "1尾")]
    [InlineData("123456789", "0尾")]
    [InlineData("02345678", null)]
    [InlineData("001234567", null)]
    [InlineData("0123456789", null)]
    public void StripRequiresNineDistinctObservedDigits(string digits, string? expected) =>
        Assert.Equal(expected, SummaryRowRecovery.ExtractHeikeTailStrip(Evidence(Strip(digits)), 310, Rule));

    [Fact]
    public void NeighborPeriodMergedWithTextStillStopsTheCrop()
    {
        OcrLineEvidence[] rows = [Token("310期", 0, 85, 80, 30),
            Token("309期:数据分析", 0, 115, 80, 30), Token("308期", 0, 235, 80, 30)];
        Assert.Null(SummaryRowRecovery.ComputeHeikeTailBand(rows, 310));
        rows[1] = rows[1] with { Box = new(0, 145, 80, 30) };
        Assert.Equal((76, 48), SummaryRowRecovery.ComputeHeikeTailBand(rows, 310));
    }

    [Theory]
    [InlineData("unchanged")]
    [InlineData("source-changed")]
    [InlineData("input-changed")]
    public async Task RecoveredInputSurvivesCleanupAndReloadButStillChecksBothHashes(string change)
    {
        using var fixture = new Fixture("valid");
        var result = Assert.Single(await SummaryRowRecovery.TryRecoverBatchAsync(
            fixture.Client, [fixture.Request], [Rule], 310, 1, null, default)).Value;
        Assert.All(fixture.Runner.Strips, path => Assert.False(File.Exists(path)));
        Assert.All(result.StripEvidence!.TokenItems!, item => { Assert.NotNull(item.Box); Assert.NotNull(item.Confidence); });
        string inputs = Path.Combine(fixture.Root, "task-inputs");
        string group = Path.GetDirectoryName(fixture.Request.ImagePath)!;
        var values = new ResultValues(StringComparer.Ordinal);
        var ledger = new ResultEvidenceLedger();
        MainForm.ApplySummaryRowRecoveryResult(fixture.Request, result, inputs, values, ledger);
        var live = ledger.Records[Rule.Id];
        Assert.NotEqual(live.SourceHash, live.InputHash);
        Assert.NotNull(live.MinimumConfidence);
        Assert.Equal(result.InputBytes, File.ReadAllBytes(live.InputPath));
        using (RecognitionStateStore.LockValidEvidenceForPublish([fixture.Request.Rule], values, ledger, new Dictionary<string, string>()))
            await RecognitionStateStore.SaveAsync(fixture.Root, group, 310, [fixture.Request.Rule], values, ledger);
        Directory.Delete(inputs, true);
        var saved = ledger.Records[Rule.Id];
        Assert.True(File.Exists(saved.InputPath));
        Assert.Equal(result.InputBytes, File.ReadAllBytes(saved.InputPath));
        if (change == "source-changed") File.WriteAllBytes(saved.SourcePath, [1, 2, 3]);
        if (change == "input-changed") File.WriteAllBytes(saved.InputPath, [4, 5, 6]);
        var loaded = RecognitionStateStore.Load(fixture.Root, group, 310, [fixture.Request.Rule]);
        if (change == "unchanged") Assert.Equal("9尾", loaded.Values[Rule.Id]);
        else Assert.False(loaded.Values.ContainsKey(Rule.Id));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ChangedSourceOrCropBytesCannotBeApplied(bool changeSource)
    {
        using var fixture = new Fixture("valid");
        var result = Assert.Single(await SummaryRowRecovery.TryRecoverBatchAsync(
            fixture.Client, [fixture.Request], [Rule], 310, 1, null, default)).Value;
        if (changeSource) File.WriteAllBytes(fixture.Request.ImagePath, [1, 2, 3]);
        else result.InputBytes![0] ^= 1;
        var values = new ResultValues(StringComparer.Ordinal);
        var ledger = new ResultEvidenceLedger();
        Assert.Throws<OcrException>(() => MainForm.ApplySummaryRowRecoveryResult(
            fixture.Request, result, Path.Combine(fixture.Root, "task-inputs"), values, ledger));
        Assert.Empty(values);
        Assert.Empty(ledger.Records);
    }

    [Fact]
    public async Task ApplyingDifferentRecoveredSourcesKeepsConflictAfterSaveAndReload()
    {
        using var first = new Fixture("valid");
        using var second = new Fixture("different-value");
        var values = new ResultValues(StringComparer.Ordinal);
        var ledger = new ResultEvidenceLedger();
        string inputs = Path.Combine(first.Root, "task-inputs");
        foreach (var fixture in new[] { first, second, first })
        {
            var result = Assert.Single(await SummaryRowRecovery.TryRecoverBatchAsync(
                fixture.Client, [fixture.Request], [Rule], 310, 1, null, default)).Value;
            MainForm.ApplySummaryRowRecoveryResult(fixture.Request, result, inputs, values, ledger);
        }
        Assert.True(ResultValues.IsConflict(values, Rule.Id));
        string group = Path.GetDirectoryName(first.Request.ImagePath)!;
        await RecognitionStateStore.SaveAsync(first.Root, group, 310, [first.Request.Rule], values, ledger);
        Directory.Delete(inputs, true);
        Assert.True(ResultValues.IsConflict(RecognitionStateStore.Load(first.Root, group, 310, [first.Request.Rule]).Values, Rule.Id));
    }

    [Theory]
    [InlineData("aborted")]
    [InlineData("saved")]
    [InlineData("conflict")]
    public async Task TaskCleanupRevokesOnlyUnpersistedSuccessSoAnAbortedResultCanRetry(string state)
    {
        using var fixture = new Fixture("valid");
        var result = Assert.Single(await SummaryRowRecovery.TryRecoverBatchAsync(
            fixture.Client, [fixture.Request], [Rule], 310, 1, null, default)).Value;
        string inputs = Path.Combine(fixture.Root, "task-inputs");
        var values = new ResultValues(StringComparer.Ordinal);
        var ledger = new ResultEvidenceLedger();
        var reasons = new Dictionary<string, string>();
        MainForm.ApplySummaryRowRecoveryResult(fixture.Request, result, inputs, values, ledger);
        if (state == "saved")
            await RecognitionStateStore.SaveAsync(fixture.Root, Path.GetDirectoryName(fixture.Request.ImagePath)!,
                310, [fixture.Request.Rule], values, ledger);
        if (state == "conflict")
            ledger.ObserveConflict(values, fixture.Request.Rule, result.CreateEvidence(fixture.Request, inputs));
        Assert.Equal(state == "aborted" ? 1 : 0, MainForm.DiscardUnpersistedRecoveryValues(inputs, values, ledger, reasons));
        Directory.Delete(inputs, true);
        if (state == "aborted")
        {
            Assert.False(values.ContainsKey(Rule.Id));
            Assert.Empty(ledger.Records);
            Assert.Contains("重新复抓", reasons[Rule.Id]);
        }
        else if (state == "saved")
        {
            Assert.Equal("9尾", values[Rule.Id]);
            Assert.True(File.Exists(ledger.Records[Rule.Id].InputPath));
        }
        else Assert.True(ResultValues.IsConflict(values, Rule.Id));
    }

    [Fact]
    public async Task UnwritableEvidenceDestinationDoesNotApplyASuccess()
    {
        using var fixture = new Fixture("valid");
        var result = Assert.Single(await SummaryRowRecovery.TryRecoverBatchAsync(
            fixture.Client, [fixture.Request], [Rule], 310, 1, null, default)).Value;
        var values = new ResultValues(StringComparer.Ordinal);
        var ledger = new ResultEvidenceLedger();
        Assert.Throws<OcrException>(() => MainForm.ApplySummaryRowRecoveryResult(
            fixture.Request, result, fixture.Request.ImagePath, values, ledger));
        Assert.Empty(values);
        Assert.Empty(ledger.Records);
    }

    [Fact]
    public void WrongPeriodExtraPeriodMissingGeometryLowConfidenceAndOpeningDigitsCannotSupplyAValue()
    {
        Assert.Null(SummaryRowRecovery.ExtractHeikeTailStrip(Evidence(Strip(issue: 309)), 310, Rule));
        Assert.Null(SummaryRowRecovery.ExtractHeikeTailStrip(Evidence([..Strip(), Token("309期", 10, 80)]), 310, Rule));
        Assert.Null(SummaryRowRecovery.ExtractHeikeTailStrip(Evidence([..Strip(), ..Strip("012345679")]), 310, Rule));
        var row = Strip("02345678");
        row[2] = row[2] with { Text = "馬13中" };
        Assert.Null(SummaryRowRecovery.ExtractHeikeTailStrip(Evidence(row), 310, Rule));
        foreach (var bad in new[] { Strip()[1] with { Confidence = .3 }, Strip()[1] with { Box = null },
            Strip()[1] with { ViewId = "other" }, Strip()[1] with { Box = new(130, 70, 480, 36) } })
            Assert.Null(SummaryRowRecovery.ExtractHeikeTailStrip(Evidence(Strip()[0], bad, Strip()[2]), 310, Rule));
        Assert.Null(SummaryRowRecovery.ExtractHeikeTailStrip(Evidence(Strip()) with { SourcePath = @"C:\新澳六合彩资料\card.jpg" }, 310, Rule));
    }

    [Theory]
    [InlineData(310)] [InlineData(309)] [InlineData(308)] [InlineData(307)]
    [InlineData(306)] [InlineData(305)] [InlineData(304)] [InlineData(303)]
    [InlineData(302)] [InlineData(301)] [InlineData(300)] [InlineData(299)]
    [InlineData(298)] [InlineData(297)] [InlineData(296)] [InlineData(295)]
    public void RecordedMediumStripsPassTheProductionCoordinateExtractor(int issue)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "heike-tail-strips.json")));
        var row = doc.RootElement.GetProperty("rows").EnumerateArray().Single(r => r.GetProperty("issue").GetInt32() == issue);
        var tokens = row.GetProperty("items").EnumerateArray().Select(item =>
        {
            int[] b = item.GetProperty("box").EnumerateArray().Select(n => n.GetInt32()).ToArray();
            return new OcrLineEvidence(item.GetProperty("text").GetString()!, new(b[0], b[1], b[2], b[3]),
                item.GetProperty("confidence").GetDouble(), "paddle/" + item.GetProperty("viewId").GetString());
        }).ToArray();
        Assert.Equal(row.GetProperty("expectedTail").GetString(), SummaryRowRecovery.ExtractHeikeTailStrip(Evidence(tokens), issue, Rule));
        Assert.Null(SummaryRowRecovery.ExtractHeikeTailStrip(Evidence(tokens), issue + 100, Rule));
    }

    [HeikeTailGpuFact]
    [Trait("Category", "CUDA")]
    public async Task FreshGpuRecoveryReadsAllSixteenPeriodsAndRepeats310WithoutProductionWrites()
    {
        string input = Environment.GetEnvironmentVariable("OCR_HEIKE_TAIL_IMAGE")!;
        string report = Environment.GetEnvironmentVariable("OCR_HEIKE_TAIL_REPORT")!;
        string folder = Path.Combine(report, "11.6-黑色六合彩", "全部图片");
        Directory.CreateDirectory(folder);
        string source = Path.Combine(folder, Path.GetFileName(input));
        File.Copy(input, source, overwrite: false);
        string hash = OcrEvidenceIdentity.Capture(input, input, "source").SourceHash;
        var client = new PaddleLocalOcrClient(new SystemProcessRunner(), Path.Combine(report, "isolated-cache.json"), LocalOcrDevice.Gpu);
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        var request = new SummaryRowRecoveryRequest(source, Rule, SummaryRowRecoveryKind.HeikeTail);
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "heike-tail-strips.json")));
        var records = new List<object>();
        foreach (var row in doc.RootElement.GetProperty("rows").EnumerateArray().Append(doc.RootElement.GetProperty("rows")[0]))
        {
            int issue = row.GetProperty("issue").GetInt32();
            string expected = row.GetProperty("expectedTail").GetString()!;
            var result = await SummaryRowRecovery.TryRecoverBatchAsync(client, [request], [Rule], issue, 1, null, timeout.Token);
            string? actual = result.TryGetValue(request, out var recovered) ? recovered.Value : null;
            records.Add(new { Issue = issue, Expected = expected, Actual = actual,
                SourceHash = recovered?.SourceHash, Lines = recovered?.StripLines });
            await File.WriteAllTextAsync(Path.Combine(report, "real-gpu-results.json"), JsonSerializer.Serialize(records, new JsonSerializerOptions { WriteIndented = true }));
            Assert.Empty(client.LastImageErrors);
            Assert.Equal(expected, actual);
            Assert.Equal(hash, recovered!.SourceHash);
            string inputs = Path.Combine(report, "task-inputs");
            var values = new ResultValues(StringComparer.Ordinal);
            var ledger = new ResultEvidenceLedger();
            MainForm.ApplySummaryRowRecoveryResult(request, recovered, inputs, values, ledger);
            await RecognitionStateStore.SaveAsync(report, folder, issue, [Rule], values, ledger);
            Directory.Delete(inputs, true);
            Assert.Equal(expected, RecognitionStateStore.Load(report, folder, issue, [Rule]).Values[Rule.Id]);
        }
        Assert.Empty(await SummaryRowRecovery.TryRecoverBatchAsync(client, [request], [Rule], 311, 1, null, timeout.Token));
        Assert.Equal(hash, OcrEvidenceIdentity.Capture(input, input, "source").SourceHash);
    }

    [Theory]
    [InlineData("valid", 1, 2)]
    [InlineData("wrong-title", 0, 1)]
    [InlineData("old-issue", 0, 2)]
    [InlineData("short", 0, 2)]
    [InlineData("changed-source", 0, 2)]
    [InlineData("other-group", 0, 0)]
    public async Task BatchChecksIdentitySourceVersionAndMediumOnly(string scenario, int expected, int calls)
    {
        using var fixture = new Fixture(scenario);
        var result = await SummaryRowRecovery.TryRecoverBatchAsync(fixture.Client, [fixture.Request], [Rule], 310, 1, null, default);
        Assert.Equal(expected, result.Count);
        Assert.Equal(calls, fixture.Runner.Calls);
        if (expected != 0) Assert.Equal("9尾", Assert.Single(result).Value.Value);
        Assert.All(fixture.Runner.Strips, path => Assert.False(File.Exists(path)));
    }

    [Theory]
    [InlineData("cancel")]
    [InlineData("cuda")]
    public async Task FatalStripErrorsStillPropagate(string scenario)
    {
        using var fixture = new Fixture(scenario);
        Task Run() => SummaryRowRecovery.TryRecoverBatchAsync(fixture.Client, [fixture.Request], [Rule], 310, 1, null, default);
        if (scenario == "cancel") await Assert.ThrowsAnyAsync<OperationCanceledException>(Run);
        else Assert.Equal(PaddleLocalOcrClient.CudaUnavailableCode, (await Assert.ThrowsAsync<OcrException>(Run)).Code);
        Assert.All(fixture.Runner.Strips, path => Assert.False(File.Exists(path)));
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string root = Path.Combine(Path.GetTempPath(), "heike-tail-" + Guid.NewGuid().ToString("N"));
        public string Root => root;
        public SummaryRowRecoveryRequest Request { get; }
        public Runner Runner { get; }
        public PaddleLocalOcrClient Client { get; }
        public Fixture(string scenario)
        {
            string source = Path.Combine(root, scenario == "other-group" ? "新澳六合彩资料" : "11.6-黑色六合彩", "card.png");
            Directory.CreateDirectory(Path.GetDirectoryName(source)!);
            using (var bitmap = new Bitmap(800, 400)) bitmap.Save(source);
            Request = new(source, Rule with { ReadModel = "small" }, SummaryRowRecoveryKind.HeikeTail);
            Runner = new(source, scenario);
            Client = new(Runner, Path.Combine(root, "isolated-cache.json"));
        }
        public void Dispose() => Directory.Delete(root, true);
    }

    private sealed class Runner(string source, string scenario) : IProcessRunner
    {
        public int Calls { get; private set; }
        public List<string> Strips { get; } = [];
        public Task<ProcessResult> RunAsync(ProcessStartInfo info, Action<string>? output, CancellationToken token, Action? reportStarted)
        {
            reportStarted?.Invoke();
            Calls++;
            Assert.Contains("--model medium", info.Arguments);
            Assert.Contains("--device gpu:0", info.Arguments);
            string Argument(string name) => Regex.Match(info.Arguments, name + " \"([^\"]+)\"").Groups[1].Value;
            string path = Assert.Single(File.ReadAllLines(Argument("--list")));
            bool strip = path != source;
            if (strip)
            {
                Strips.Add(path);
                using var image = new Bitmap(path);
                Assert.Equal(new Size(1600, 96), image.Size);
                Assert.Contains("--det-max-side 1440", info.Arguments);
                if (scenario == "cancel") throw new OperationCanceledException();
                if (scenario == "cuda") throw new OcrException("GPU failed", PaddleLocalOcrClient.CudaUnavailableCode);
                if (scenario == "changed-source") File.WriteAllBytes(source, [1, 2, 3]);
            }
            OcrLineEvidence[] tokens = strip
                ? Strip(scenario == "short" ? "02345678" : scenario == "different-value" ? "012345679" : "012345678",
                    scenario == "old-issue" ? 309 : 310)
                : [Token(scenario == "wrong-title" ? "黑客数据灭庄七肖" : "黑客数据数据分析必中九尾", 100, 30, 600),
                    Token("310期", 13, 221, 94), Token("309期", 13, 280, 94, 37)];
            var items = tokens.Select(t => new { text = t.Text, box = new[] { t.Box!.X, t.Box.Y, t.Box.Width, t.Box.Height },
                confidence = t.Confidence, viewId = "original" });
            File.WriteAllText(Argument("--output"), JsonSerializer.Serialize(new {
                results = new[] { new { path, texts = tokens.Select(t => t.Text), items } } }));
            return Task.FromResult(new ProcessResult(true, 0, "", ""));
        }
    }
}

public sealed class HeikeTailGpuFactAttribute : FactAttribute
{
    public HeikeTailGpuFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OCR_HEIKE_TAIL_IMAGE"))
            || string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OCR_HEIKE_TAIL_REPORT")))
            Skip = "Set OCR_HEIKE_TAIL_IMAGE and OCR_HEIKE_TAIL_REPORT for isolated GPU acceptance.";
    }
}
