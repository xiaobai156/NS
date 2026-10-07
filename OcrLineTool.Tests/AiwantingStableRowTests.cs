using OcrLineTool;
using System.Diagnostics;
using System.Drawing;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace OcrLineTool.Tests;

public sealed class AiwantingStableRowTests
{
    private static IReadOnlyList<OcrRule> Rules => RuleCatalog.Load(Path.Combine(
        ResultFilePaths.ConfigurationDirectory(AppContext.BaseDirectory), "嫣然心水.json"));
    private static OcrRule Rule => Rules.Single(rule => rule.Id == "爱晚亭");
    private const string PathInFolder = @"C:\图片\11.8-嫣然心水\爱晚亭\新图片.jpg";

    [Theory]
    [InlineData(7, false)]
    [InlineData(281, true)]
    [InlineData(319, false)]
    public void OneClearRowIsEnoughRegardlessOfOldRowsOrTitle(int issue, bool title)
    {
        string[] lines = [title ? "爱晚亭新澳门杀码" : "",
            "201期：水印遮挡", "202期：01.02.0", $"{issue:D3}期：04.18.27.36.49.开00"];
        Assert.Contains(RuleEngine.FindMatches(PathInFolder, lines, [Rule], Rules), item => item.Id == Rule.Id);
        Assert.Equal("04 18 27 36 49", RuleEngine.ExtractFinalValue(lines, issue, Rule));
    }

    [Fact]
    public void NamedKillNumberCardCanBeSelectedEvenWhenAllValuesNeedRereading()
    {
        string[] lines = ["爱晚亭新澳门杀码", "281期：04.18.27.3"];
        Assert.Contains(RuleEngine.FindMatches(PathInFolder, lines, [Rule], Rules), item => item.Id == Rule.Id);
        Assert.Null(RuleEngine.ExtractFinalValue(lines, 281, Rule));
    }

    [Theory]
    [InlineData("281期：04.18.27.36.开49")]
    [InlineData("281期：04.18.27.36.36.开49")]
    [InlineData("281期：04.18.27.36.50.开49")]
    [InlineData("280期：04.18.27.36.49.开00")]
    public void InvalidCurrentRowNeverBorrowsOpeningOrOldValues(string row)
    {
        Assert.Null(RuleEngine.ExtractFinalValue(["爱晚亭", row], 281, Rule));
    }

    [Fact]
    public void TwoDifferentValidAnswersRemainAConflict()
    {
        string[] lines = ["281期：04.18.27.36.49.开00", "281期：04.18.27.36.48.开00"];
        Assert.Equal(RuleExtractionStatus.Conflict, RuleEngine.ExtractFinalResult(lines, 281, Rule).Status);
    }

    [Theory]
    [InlineData("爱晚亭您的计算结果", "281期：04.18.27.36.49.开00")]
    [InlineData("爱晚亭新澳门杀1肖", "281期：虎开猴23")]
    [InlineData("紫燕儿", "281期：04.18.27.36.49.开00")]
    public void OtherCardsCannotUseTheFiveNumberRule(string title, string row)
    {
        Assert.DoesNotContain(RuleEngine.FindMatches(PathInFolder, [title, row], [Rule], Rules), item => item.Id == Rule.Id);
    }

    [Fact]
    public async Task MediumReviewIsFolderScopedAndDoesNotReplaceASiblingPlan()
    {
        string root = Path.Combine(Path.GetTempPath(), "ai-row-" + Guid.NewGuid().ToString("N"));
        try
        {
            string folder = Path.Combine(root, "11.8-嫣然心水", "爱晚亭");
            string Create(string dir, string name)
            {
                Directory.CreateDirectory(dir);
                string path = Path.Combine(dir, name + ".png");
                using var bitmap = new Bitmap(400, 400);
                bitmap.Save(path);
                return path;
            }
            string card = Create(folder, "card"), stats = Create(folder, "stats"), zodiac = Create(folder, "zodiac");
            string foreign = Create(Path.Combine(root, "11.8-嫣然心水", "他人"), "card");
            var runner = new RowRunner(path => Path.GetFileNameWithoutExtension(path) switch
            {
                "stats" => ["爱晚亭您的计算结果", "281期：04.18.27.36.49.开00"],
                "zodiac" => ["爱晚亭杀1肖", "281期虎开猴23"],
                _ => ["281期：04.18.27.36.49.开00"]
            });
            var client = new PaddleLocalOcrClient(runner, Path.Combine(root, "cache.json"));
            var sibling = new LocalCandidatePlan(zodiac, [Rules.Single(rule => rule.Id == "爱晚亭杀肖")], true);
            var selected = await LocalCandidatePlanner.ReviewRowCandidatesAsync(
                [card, stats, zodiac, foreign], [sibling], [Rule], Rules, 281, client, default);
            Assert.Equal(card, Assert.Single(selected).Path);
            Assert.DoesNotContain(foreign, runner.Paths);
            Assert.Equal(3, runner.Paths.Count);
            Assert.All(runner.Models, model => Assert.Equal("medium", model));
            int calls = runner.Models.Count;
            Assert.Empty(await LocalCandidatePlanner.ReviewRowCandidatesAsync(
                [card], selected, [Rule], Rules, 281, client, default));
            Assert.Equal(calls, runner.Models.Count);
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData("281期：04.18.27.36.49.开00", "04 18 27 36 49")]
    [InlineData("281期：04.18.27.36.开49", null)]
    [InlineData("280期：04.18.27.36.49.开00", null)]
    public async Task CropRecoveryRechecksIssueAndQuantity(string strip, string? expected)
    {
        string root = Path.Combine(Path.GetTempPath(), "ai-strip-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string path = Path.Combine(root, "card.png");
            using (var bitmap = new Bitmap(400, 400)) bitmap.Save(path);
            var runner = new RowRunner(input => input == path ? ["281期"] : [strip]);
            var client = new PaddleLocalOcrClient(runner, Path.Combine(root, "cache.json"));
            var request = new SummaryRowRecoveryRequest(path, Rule, SummaryRowRecoveryKind.IssueNumbers);
            var result = await SummaryRowRecovery.TryRecoverBatchAsync(client, [request], Rules, 281, 1, null, default);
            Assert.Equal(2, runner.Models.Count);
            if (expected is null) Assert.Empty(result);
            else Assert.Equal(expected, Assert.Single(result).Value.Value);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void RowCropUsesOnlyTheSameViewAndNeverIncludesANeighbouringIssue()
    {
        OcrLineEvidence[] items = [new("281期", new(10, 100, 80, 24), ViewId: "original"),
            new("04.18.27.36.49", new(95, 102, 200, 24), ViewId: "original"),
            new("280期", new(10, 80, 80, 24), ViewId: "header")];
        Assert.Equal((96, 34), SummaryRowRecovery.ComputeNumberTableRowBand(items, 281));
        Assert.Null(SummaryRowRecovery.ComputeNumberTableRowBand(
            items.Append(new("280期", new(10, 80, 80, 24), ViewId: "original")).ToArray(), 281));
        Assert.Null(SummaryRowRecovery.ComputeNumberTableRowBand(
            items.Append(items[0]).ToArray(), 281));
    }

    [Fact]
    public void SplitCellsNeedSamePhysicalRowAndView()
    {
        OcrLineEvidence[] items = [new("281期：", new(10, 100, 80, 24)),
            new("04.18.27.36.49", new(94, 101, 200, 24)), new("开00", new(298, 100, 50, 24))];
        var evidence = new OcrEvidence("sample", "sample", "hash", "hash", "original", items);
        Assert.Equal("04 18 27 36 49", RuleEngine.ExtractFinalValue(RuleEngine.RowCandidateLines(evidence), 281, Rule));
        Assert.Equal("04 18 27 36 49", RuleEngine.ExtractFinalValue(evidence, 281, Rule));
        var wrongView = evidence with { Items = [items[0], items[1] with { ViewId = "other" }, items[2]] };
        Assert.Null(RuleEngine.ExtractFinalValue(RuleEngine.RowCandidateLines(wrongView), 281, Rule));
    }

    private sealed class RowRunner(Func<string, string[]> textFor) : IProcessRunner
    {
        internal List<string> Paths { get; } = [];
        internal List<string> Models { get; } = [];
        public Task<ProcessResult> RunAsync(ProcessStartInfo info, Action<string>? output,
            CancellationToken token, Action? started)
        {
            started?.Invoke();
            string Argument(string name) => Regex.Match(info.Arguments, name + " \\\"([^\\\"]+)\\\"").Groups[1].Value;
            Models.Add(Regex.Match(info.Arguments, @"--model\s+""?(\w+)").Groups[1].Value);
            var paths = File.ReadAllLines(Argument("--list"));
            Paths.AddRange(paths);
            var results = paths.Select(path => new { path, texts = textFor(path), items = textFor(path)
                .Select((text, i) => new { text, box = new[] { 10, 100 + i * 45, 300, 24 }, confidence = 0.99, viewId = "original" }) });
            File.WriteAllText(Argument("--output"), JsonSerializer.Serialize(new { results }));
            return Task.FromResult(new ProcessResult(true, 0, "", ""));
        }
    }
}
