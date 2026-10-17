using System.Diagnostics;
using System.Drawing;
using System.Text.Json;
using System.Text.RegularExpressions;
using OcrLineTool;

namespace OcrLineTool.Tests;

public sealed class JieshaoRowRecoveryTests
{
    private static OcrRule Zodiac => new("杰少", "生肖", "杰少杀一肖", "杀①肖", "原创杰少", "杰少");
    private static OcrRule Tail => new("杰少", "尾", "杰少禁一尾", "禁一尾", "原创杰少", "杰少");

    [Theory]
    [InlineData(269, "valid", "猴")]
    [InlineData(302, "valid", "猴")]
    [InlineData(302, "wrong-issue", null)]
    [InlineData(302, "wrong-section", null)]
    [InlineData(302, "neighbour", null)]
    [InlineData(302, "empty", null)]
    [InlineData(302, "wrong-author", null)]
    [InlineData(302, "wrong-view", null)]
    [InlineData(302, "conflict", null)]
    [InlineData(302, "changed", null)]
    [InlineData(268, "opening-zodiac", "狗")]
    public async Task CompactSmallOnlyLocatesAndMediumMustValidateTheOriginalStrip(int issue, string mode, string? expected)
    {
        string root = Path.Combine(Path.GetTempPath(), "jieshao-recovery-" + Guid.NewGuid().ToString("N"));
        string directory = Path.Combine(root, "嫣然心水", "杰少");
        Directory.CreateDirectory(directory);
        try
        {
            string source = Path.Combine(directory, "source.png");
            using (var bitmap = new Bitmap(600, 400)) bitmap.Save(source);
            var runner = new LocatorRunner(source, issue, mode);
            var client = new PaddleLocalOcrClient(runner, Path.Combine(root, "cache.json"));
            var request = new SummaryRowRecoveryRequest(source, Zodiac, SummaryRowRecoveryKind.IssueZodiac);
            var result = await SummaryRowRecovery.TryRecoverBatchAsync(client, [request], [Zodiac, Tail], issue, 1, 960, default);
            Assert.Equal(expected, result.GetValueOrDefault(request)?.Value);
            Assert.Contains(runner.Arguments, argument => argument.Contains("--model small"));
            if (expected is not null)
            {
                Assert.Equal(LocalOcrIdentity.Image(source), result[request].SourceHash);
                Assert.Contains(expected, string.Concat(result[request].StripLines));
                Assert.DoesNotContain("虎", string.Concat(result[request].StripLines));
            }
            Assert.All(runner.Strips, path => Assert.False(File.Exists(path)));
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData("cuda")]
    [InlineData("cancel")]
    public async Task SmallLocatorInfrastructureFailuresPropagate(string mode)
    {
        string root = Path.Combine(Path.GetTempPath(), "jieshao-failure-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string path = Path.Combine(root, "source.png");
            using (var bitmap = new Bitmap(600, 400)) bitmap.Save(path);
            var client = new PaddleLocalOcrClient(new LocatorRunner(path, 302, mode), Path.Combine(root, "cache.json"));
            Task Call() => SummaryRowRecovery.TryRecoverBatchAsync(client,
                [new(path, Zodiac, SummaryRowRecoveryKind.IssueZodiac)], [Zodiac], 302, 1, 960, default);
            if (mode == "cuda")
                Assert.Equal(PaddleLocalOcrClient.CudaUnavailableCode, (await Assert.ThrowsAsync<OcrException>(Call)).Code);
            else
                await Assert.ThrowsAnyAsync<OperationCanceledException>(Call);
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData(269)]
    [InlineData(302)]
    public void MissingForbiddenTailNeverBorrowsTheAdditionalTail(int issue)
    {
        string[] lines = ["原创杰少", $"{issue - 1}期新澳禁一尾{{5尾}}开40兔对", $"{issue}期新澳加杀尾{{4尾}}开50猫对"];
        Assert.Null(RuleEngine.ExtractFinalValue(lines, issue, Tail));
        Assert.Equal($"已识别到第{issue}期，但未在“禁一尾”区段找到该期数据", RuleEngine.DescribeExtractionFailure(lines, issue, Tail));
        Assert.Equal("5尾", RuleEngine.ExtractFinalValue(["原创杰少", $"{issue}期新澳禁一尾{{5尾}}开50猫对"], issue, Tail));
    }

    private sealed class LocatorRunner(string source, int issue, string mode) : IProcessRunner
    {
        public List<string> Arguments { get; } = [];
        public List<string> Strips { get; } = [];
        public Task<ProcessResult> RunAsync(
            ProcessStartInfo info, Action<string>? output, CancellationToken token, Action? reportStarted)
        {
            reportStarted?.Invoke();
            Arguments.Add(info.Arguments);
            string Argument(string name) => Regex.Match(info.Arguments, name + " \"([^\"]+)\"").Groups[1].Value;
            string path = File.ReadAllLines(Argument("--list")).Single();
            bool strip = path != source;
            bool small = info.Arguments.Contains("--model small");
            if (small && mode == "cuda") throw new OcrException("device failed", PaddleLocalOcrClient.CudaUnavailableCode);
            if (small && mode == "cancel") throw new OperationCanceledException();
            string[] texts = ["medium lost the period column"];
            if (small)
                texts = [mode == "wrong-author" ? "原创青苹果" : "原创杰少", $"{issue - 1}期新澳杀①肖：狗", $"{issue}期新澳杀①肖：虎"];
            if (strip)
            {
                Strips.Add(path);
                Assert.False(small);
                Assert.Contains("--det-max-side 1440", info.Arguments);
                using var bitmap = new Bitmap(path);
                Assert.Equal(1200, bitmap.Width); // Full original width, not the compact X coordinate.
                texts = mode switch
                {
                    "wrong-issue" => [$"{issue - 1}期新澳杀①肖：猴开猫50准"],
                    "wrong-section" => [$"{issue}期新澳加杀肖：猴开猫50准"],
                    "neighbour" => [$"{issue}期新澳杀①肖：", $"{issue + 1}期新澳杀①肖：猴开猫50准"],
                    "empty" => [],
                    "conflict" => [$"{issue}期新澳杀①肖：猴", $"{issue}期新澳杀①肖：鼠"],
                    "opening-zodiac" => [$"{issue}期新澳杀①肖：狗开兔40准"],
                    _ => [$"{issue}期新澳杀①肖：猴开猫50准"]
                };
                if (mode == "changed") File.WriteAllBytes(source, [1, 2, 3]);
            }
            var items = texts.Select((text, index) => new { text, box = new[] { 10, 30 + index * 60, 270, 28 }, confidence = .99,
                viewId = small ? (index == 0 ? "header" : mode == "wrong-view" ? "top" : "compact") : "original" });
            File.WriteAllText(Argument("--output"), JsonSerializer.Serialize(new { results = new[] {new { path, texts, items }} }));
            return Task.FromResult(new ProcessResult(true, 0, "", ""));
        }
    }
}
