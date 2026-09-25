using System.Diagnostics;
using System.Drawing;
using System.Text.Json;
using System.Text.RegularExpressions;
using OcrLineTool;

namespace OcrLineTool.Tests;

public sealed class SummaryRowBatchTests
{
    [Theory]
    [InlineData((int)SummaryRowRecoveryKind.Summary, false)]
    [InlineData((int)SummaryRowRecoveryKind.IssueZodiac, false)]
    [InlineData((int)SummaryRowRecoveryKind.IssueNumbers, false)]
    [InlineData((int)SummaryRowRecoveryKind.RightBlock, false)]
    [InlineData((int)SummaryRowRecoveryKind.Summary, true)]
    [InlineData((int)SummaryRowRecoveryKind.IssueZodiac, true)]
    [InlineData((int)SummaryRowRecoveryKind.IssueNumbers, true)]
    [InlineData((int)SummaryRowRecoveryKind.RightBlock, true)]
    public async Task BatchesBothStagesAndKeepsEachRulesValidation(int kindValue, bool invalidFirst)
    {
        var kind = (SummaryRowRecoveryKind)kindValue;
        using var files = new BatchFixture(kind, invalidFirst);
        var result = await SummaryRowRecovery.TryRecoverBatchAsync(files.Client, files.Requests,
            files.Rules, 267, 1, null, default);

        Assert.Equal(2, files.Runner.Inputs.Count);
        Assert.All(files.Runner.Inputs, paths => Assert.Equal(2, paths.Length));
        Assert.Equal(invalidFirst ? 1 : 2, result.Count);
        Assert.Equal(kind == SummaryRowRecoveryKind.IssueNumbers ? "04 05 06" : "羊",
            result[files.Requests[1]].Value);
        if (!invalidFirst)
            Assert.Equal(kind == SummaryRowRecoveryKind.IssueNumbers ? "01 02 03" : "虎",
                result[files.Requests[0]].Value);
        Assert.All(files.Runner.Inputs[1], path => Assert.False(File.Exists(path)));
    }

    [Fact]
    public async Task SharedSummarySourceIsLocatedOnceForMultipleAuthors()
    {
        using var files = new BatchFixture(SummaryRowRecoveryKind.Summary);
        SummaryRowRecoveryRequest[] requests =
            [files.Requests[0], files.Requests[1] with { ImagePath = files.Requests[0].ImagePath }];
        var result = await SummaryRowRecovery.TryRecoverBatchAsync(files.Client, requests,
            files.Rules, 267, 1, null, default);
        Assert.Equal(2, result.Count);
        Assert.Single(files.Runner.Inputs[0]);
        Assert.Equal(2, files.Runner.Inputs[1].Length);
    }

    [Fact]
    public async Task RightBlockRetainsItsStandardDetectionFallback()
    {
        using var files = new BatchFixture(SummaryRowRecoveryKind.RightBlock);
        files.Runner.RequireStandardDetection = true;
        var result = await SummaryRowRecovery.TryRecoverBatchAsync(files.Client, files.Requests,
            files.Rules, 267, 1, 960, default);
        Assert.Equal(2, result.Count);
        Assert.Equal(3, files.Runner.Inputs.Count);
        Assert.Contains("--det-max-side 960", files.Runner.Arguments[0]);
        Assert.DoesNotContain("--det-max-side", files.Runner.Arguments[1]);
        Assert.Contains("--det-max-side 1440", files.Runner.Arguments[2]);
    }

    // 期行补读：嫣然心水的定位读带 det-max 960（0.55 横向压缩视图），密集表在该
    // 视图里可能连目标期号锚点都读不到（268 期杰少杀一肖），此时整条补读连行都裁不
    // 出来。仍未解决的请求要用原图坐标系再定位一次，行条取值仍是标准 medium 1440。
    [Fact]
    public async Task IssueRowRecoveryRelocatesOnTheOriginalGeometryWhenTheCompactViewLosesTheAnchor()
    {
        using var files = new BatchFixture(SummaryRowRecoveryKind.IssueZodiac);
        files.Runner.RequireStandardDetection = true;
        var result = await SummaryRowRecovery.TryRecoverBatchAsync(files.Client, files.Requests,
            files.Rules, 267, 1, 960, default);

        Assert.Equal(2, result.Count);
        Assert.Equal(3, files.Runner.Inputs.Count);
        Assert.Contains("--det-max-side 960", files.Runner.Arguments[0]);
        Assert.DoesNotContain("--det-max-side", files.Runner.Arguments[1]);
        Assert.Contains("--det-max-side 1440", files.Runner.Arguments[2]);
    }

    [Fact]
    public async Task ARecoverableBatchFailureIsIsolatedWithoutLosingValidPeers()
    {
        using var files = new BatchFixture(SummaryRowRecoveryKind.IssueZodiac);
        files.Runner.FailFirstBatch = true;
        var result = await SummaryRowRecovery.TryRecoverBatchAsync(files.Client, files.Requests,
            files.Rules, 267, 1, null, default);
        Assert.Equal(2, result.Count);
        Assert.Equal(new[] { 2, 1, 1, 2 }, files.Runner.Inputs.Select(paths => paths.Length));
    }

    [Fact]
    public async Task OneCorruptSourceDoesNotPreventOtherCrops()
    {
        using var files = new BatchFixture(SummaryRowRecoveryKind.IssueZodiac);
        File.WriteAllBytes(files.Requests[0].ImagePath, [1, 2, 3]);
        var result = await SummaryRowRecovery.TryRecoverBatchAsync(files.Client, files.Requests,
            files.Rules, 267, 1, null, default);
        Assert.Equal(files.Requests[1], Assert.Single(result).Key);
        Assert.Single(files.Runner.Inputs[1]);
    }

    [Fact]
    public async Task ChangedSourceCannotAcquireTheOldStripsValue()
    {
        using var files = new BatchFixture(SummaryRowRecoveryKind.IssueZodiac);
        files.Runner.OnStrips = () => File.WriteAllBytes(files.Requests[0].ImagePath, [9, 9, 9]);
        var result = await SummaryRowRecovery.TryRecoverBatchAsync(files.Client, files.Requests,
            files.Rules, 267, 1, null, default);
        Assert.Single(result);
        Assert.Contains(files.Requests[1], result.Keys);
        Assert.All(files.Runner.Inputs[1], path => Assert.False(File.Exists(path)));
    }

    [Theory]
    [InlineData("cancel")]
    [InlineData("cuda")]
    public async Task FatalStripFailurePropagatesAndCleansEveryCrop(string kind)
    {
        using var files = new BatchFixture(SummaryRowRecoveryKind.IssueZodiac);
        files.Runner.OnStrips = () =>
        {
            if (kind == "cancel") throw new OperationCanceledException();
            throw new OcrException("device failed", PaddleLocalOcrClient.CudaUnavailableCode);
        };
        Task Call() => SummaryRowRecovery.TryRecoverBatchAsync(files.Client, files.Requests,
            files.Rules, 267, 1, null, default);
        if (kind == "cancel")
            await Assert.ThrowsAnyAsync<OperationCanceledException>(Call);
        else
            Assert.Equal(PaddleLocalOcrClient.CudaUnavailableCode, (await Assert.ThrowsAsync<OcrException>(Call)).Code);
        Assert.Equal(2, files.Runner.Inputs.Count);
        Assert.All(files.Runner.Inputs[1], path => Assert.False(File.Exists(path)));
    }

    private sealed class BatchFixture : IDisposable
    {
        private readonly string root = Path.Combine(Path.GetTempPath(), "recovery-batch-" + Guid.NewGuid().ToString("N"));
        public BatchRunner Runner { get; }
        public PaddleLocalOcrClient Client { get; }
        public SummaryRowRecoveryRequest[] Requests { get; }
        public OcrRule[] Rules { get; }

        public BatchFixture(SummaryRowRecoveryKind kind, bool invalidFirst = false)
        {
            Directory.CreateDirectory(root);
            Rules = kind == SummaryRowRecoveryKind.IssueNumbers
                ? [new("batch-A", "号码:3"), new("batch-B", "号码:3")]
                : [new("简单爱", "生肖", StrictIssueBlock: true), new("陈思思", "生肖", StrictIssueBlock: true)];
            Requests = Rules.Select((rule, index) =>
            {
                string path = Path.Combine(root, $"source-{index}.png");
                using var bitmap = new Bitmap(400, 400);
                using (var graphics = Graphics.FromImage(bitmap)) graphics.Clear(Color.FromArgb(255, 230 + index, 220));
                bitmap.Save(path);
                return new SummaryRowRecoveryRequest(path, rule, kind);
            }).ToArray();
            Runner = new BatchRunner(Requests, kind, invalidFirst);
            Client = new PaddleLocalOcrClient(Runner, Path.Combine(root, "cache.json"));
        }

        public void Dispose() => Directory.Delete(root, true);
    }

    private sealed class BatchRunner(SummaryRowRecoveryRequest[] requests, SummaryRowRecoveryKind kind, bool invalidFirst) : IProcessRunner
    {
        public List<string[]> Inputs { get; } = [];
        public List<string> Arguments { get; } = [];
        public bool RequireStandardDetection { get; set; }
        public bool FailFirstBatch { get; set; }
        public Action? OnStrips { get; set; }

        public Task<ProcessResult> RunAsync(ProcessStartInfo info, Action<string>? output, CancellationToken token)
        {
            string Argument(string name) => Regex.Match(info.Arguments, name + " \"([^\"]+)\"").Groups[1].Value;
            string[] paths = File.ReadAllLines(Argument("--list"));
            Inputs.Add(paths);
            Arguments.Add(info.Arguments);
            if (FailFirstBatch && Inputs.Count == 1)
                throw new OcrException("recoverable input failure");
            bool strips = paths.All(path => requests.All(request => request.ImagePath != path));
            if (strips)
            {
                Assert.Contains("--model medium", info.Arguments);
                Assert.Contains("--det-max-side 1440", info.Arguments);
                OnStrips?.Invoke();
            }
            var results = paths.Select((path, index) =>
            {
                string[] texts;
                if (!strips && RequireStandardDetection && info.Arguments.Contains("--det-max-side 960", StringComparison.Ordinal))
                    texts = ["no target row"];
                else if (!strips)
                    texts = kind == SummaryRowRecoveryKind.Summary ? ["简单爱 正正正", "陈思思 正正正"] : ["267期", "268期"];
                else if (invalidFirst && index == 0)
                    texts = ["unreadable"];
                else
                    texts = kind switch
                    {
                        SummaryRowRecoveryKind.Summary => [index == 0 ? "简单爱 正正正禁虎" : "陈思思 正正正禁羊"],
                        SummaryRowRecoveryKind.IssueNumbers => [index == 0 ? "267期禁01.02.03" : "267期禁04.05.06"],
                        _ => [index == 0 ? "267期禁虎" : "267期禁羊"]
                    };
                var items = texts.Select((text, row) => new { text, box = new[] { 20, 100 + row * 50, 180, 24 }, confidence = 0.99, viewId = "original" });
                return new { path, texts, items };
            }).Reverse().ToArray();
            File.WriteAllText(Argument("--output"), JsonSerializer.Serialize(new { results }));
            return Task.FromResult(new ProcessResult(true, 0, "", ""));
        }
    }
}
