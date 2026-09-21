using OcrLineTool;
using System.Diagnostics;
using System.Drawing;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace OcrLineTool.Tests;

public sealed class SummaryRowRecoveryTests
{
    [Theory]
    [InlineData("summary", 1)]
    [InlineData("summary", 2)]
    [InlineData("issue", 1)]
    [InlineData("issue", 2)]
    [InlineData("numbers", 1)]
    [InlineData("numbers", 2)]
    [InlineData("right", 1)]
    [InlineData("right", 2)]
    public async Task RecoveryPropagatesDeviceFailureAndCancellationButKeepsOrdinaryMissing(string route, int failAt)
    {
        string folder = Path.Combine(Path.GetTempPath(), "recovery-errors-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            string image = Path.Combine(folder, "sample.png");
            using (var bitmap = new Bitmap(400, 400)) bitmap.Save(image);
            var rule = Rules.Single(r => r.Id == "简单爱");
            foreach (string kind in new[] { "cuda", "ordinary", "cancel" })
            {
                var runner = new RecoveryErrorRunner(route, failAt, kind);
                var client = new PaddleLocalOcrClient(runner, Path.Combine(folder, kind + ".json"));
                Task<SummaryRowRecoveryResult?> Call() => route switch
                {
                    "summary" => SummaryRowRecovery.TryRecoverAsync(client, image, rule, Rules, 1, null, default),
                    "right" => SummaryRowRecovery.TryRecoverRightBlockIssueRowAsync(client, image, 257, 1, null, default),
                    "numbers" => SummaryRowRecovery.TryRecoverIssueRowNumbersAsync(client, image, 257, rule, 1, null, default),
                    _ => SummaryRowRecovery.TryRecoverIssueRowAsync(client, image, 257, 1, null, default)
                };
                if (kind == "cuda")
                    Assert.Equal(PaddleLocalOcrClient.CudaUnavailableCode, (await Assert.ThrowsAsync<OcrException>(Call)).Code);
                else if (kind == "cancel")
                    await Assert.ThrowsAnyAsync<OperationCanceledException>(Call);
                else
                    Assert.Null(await Call());
                Assert.Equal(failAt, runner.Calls);
            }
        }
        finally { Directory.Delete(folder, true); }
    }

    private sealed class RecoveryErrorRunner(string route, int failAt, string kind) : IProcessRunner
    {
        public int Calls { get; private set; }
        public Task<ProcessResult> RunAsync(ProcessStartInfo info, Action<string>? output, CancellationToken token)
        {
            if (++Calls == failAt)
                throw kind switch
                {
                    "cuda" => new OcrException("GPU failed", PaddleLocalOcrClient.CudaUnavailableCode),
                    "cancel" => new OperationCanceledException(),
                    _ => new OcrException("image failed")
                };
            string Argument(string name) => Regex.Match(info.Arguments, name + " \"([^\"]+)\"").Groups[1].Value;
            string path = File.ReadAllLines(Argument("--list")).Single();
            string[] texts = route == "summary" ? ["简单爱 正正正", "陈思思 正正正"] : ["257期", "258期"];
            var items = texts.Select((text, i) => new { text, box = new[] { 20, 100 + i * 50, 180, 24 }, confidence = 0.99, viewId = "original" });
            File.WriteAllText(Argument("--output"), JsonSerializer.Serialize(new { results = new[] { new { path, texts, items } } }));
            return Task.FromResult(new ProcessResult(true, 0, "", ""));
        }
    }

    private static IReadOnlyList<OcrRule> Rules => RuleCatalog.Load(Path.Combine(
        ResultFilePaths.ConfigurationDirectory(AppContext.BaseDirectory), "嫣然心水.json"));

    [Fact]
    public void StripWithOnlyTheOwnRowYieldsItsSingleZodiac()
    {
        IReadOnlyList<OcrRule> rules = Rules;
        OcrRule simple = rules.Single(rule => rule.Id == "简单爱");

        Assert.Equal("虎", RuleEngine.ExtractSummaryRowZodiacFromStrip(["简单爱 正正正禁虎"], simple, rules));
        Assert.Equal("虎", RuleEngine.ExtractSummaryRowZodiacFromStrip(["简单爱 正正正", "禁", "虎"], simple, rules));
        Assert.Equal("虎", RuleEngine.ExtractSummaryRowZodiacFromStrip(["简单爱 正正正 禁 虎"], simple, rules));
    }

    [Fact]
    public void StripContainingAnotherKnownAuthorIsRejected()
    {
        IReadOnlyList<OcrRule> rules = Rules;
        OcrRule simple = rules.Single(rule => rule.Id == "简单爱");

        Assert.Null(RuleEngine.ExtractSummaryRowZodiacFromStrip(
            ["陈思思 正正正 禁蛇", "简单爱 正正正 禁虎"], simple, rules));
    }

    [Fact]
    public void StripWithoutASingleZodiacBehindItsOwnForbiddenMarkIsRejected()
    {
        IReadOnlyList<OcrRule> rules = Rules;
        OcrRule simple = rules.Single(rule => rule.Id == "简单爱");

        Assert.Null(RuleEngine.ExtractSummaryRowZodiacFromStrip(["简单爱 正正正"], simple, rules));
        Assert.Null(RuleEngine.ExtractSummaryRowZodiacFromStrip(["简单爱 正正正禁"], simple, rules));
        Assert.Null(RuleEngine.ExtractSummaryRowZodiacFromStrip(["简单爱 正正正禁虎兔"], simple, rules));
        Assert.Null(RuleEngine.ExtractSummaryRowZodiacFromStrip(["简单爱 正正正 简单爱 正正正禁虎"], simple, rules));
    }

    [Fact]
    public void StripWithoutAnyIdentityIsRejected()
    {
        IReadOnlyList<OcrRule> rules = Rules;
        OcrRule simple = rules.Single(rule => rule.Id == "简单爱");

        Assert.Null(RuleEngine.ExtractSummaryRowZodiacFromStrip(["正正正禁虎"], simple, rules));
        Assert.Null(RuleEngine.ExtractSummaryRowZodiacFromStrip([], simple, rules));
    }

    [Fact]
    public void BandStaysInsideTheRowPitch()
    {
        (int Top, int Height)? band = SummaryRowRecovery.ComputeBand([100, 146, 192], 146);

        Assert.NotNull(band);
        Assert.Equal(128, band!.Value.Top);
        Assert.Equal(36, band.Value.Height);
    }

    [Fact]
    public void BandRequiresMeasurableRows()
    {
        Assert.Null(SummaryRowRecovery.ComputeBand([100], 100));
        Assert.Null(SummaryRowRecovery.ComputeBand([], 0));
        Assert.Null(SummaryRowRecovery.ComputeBand([100, 100], 100));
    }

    private static OcrLineEvidence Row(string text, int y, int height = 30) =>
        new(text, new OcrBox(20, y, 300, height), 0.99, "original", "main");

    [Fact]
    public void IssueRowBandSpansTheTargetRowUntilTheNextIssueRow()
    {
        OcrLineEvidence[] rows =
        [
            Row("245期禁01.13.25", 100),
            Row("246期禁07.19.31", 146),
            Row("247期", 192),
            Row("禁", 192),
            Row("06.18.30", 192),
            Row("248期", 238),
            Row("257期", 284),
            Row("月禁", 284),
            Row("41.29.05", 284)
        ];

        (int Top, int Height)? band = SummaryRowRecovery.ComputeIssueRowBand(rows, 247);

        Assert.NotNull(band);
        Assert.Equal(188, band!.Value.Top);
        Assert.Equal(46, band.Value.Height);
    }

    [Fact]
    public void IssueRowBandCoversTheValueBelowTheLastIssueRow()
    {
        OcrLineEvidence[] rows =
        [
            Row("255期禁10.34.46", 100),
            Row("256期禁11.23.35", 146),
            Row("257期", 192),
            Row("月禁", 192),
            Row("41.29.05", 192)
        ];

        (int Top, int Height)? band = SummaryRowRecovery.ComputeIssueRowBand(rows, 257);

        Assert.NotNull(band);
        Assert.Equal(188, band!.Value.Top);
        Assert.Equal(71, band.Value.Height);
    }

    [Fact]
    public void IssueRowBandRequiresExactlyOneTargetRow()
    {
        OcrLineEvidence[] rows =
        [
            Row("255期禁10.34.46", 100),
            Row("257期禁41.29.05", 146),
            Row("257期月禁41.29.05", 192)
        ];

        Assert.Null(SummaryRowRecovery.ComputeIssueRowBand(rows, 257));
        Assert.Null(SummaryRowRecovery.ComputeIssueRowBand(rows, 258));
        Assert.Null(SummaryRowRecovery.ComputeIssueRowBand([Row("257期月禁41.29.05", 100)], 257));
    }
}
