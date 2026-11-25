using OcrLineTool;

namespace OcrLineTool.Tests;

// 蜻蜓一套「神算子双波」：卡面每行「329期神算双波【蓝波绿波】开0000准」，
// 只按本期这一行方头括号里的两个不同波色反推缺的那个。
public sealed class ShensuanTwoWaveTests
{
    private static string Config => ResultFilePaths.ConfigurationDirectory(AppContext.BaseDirectory);
    private static OcrRule Rule => RuleCatalog.Load(Path.Combine(Config, "蜻蜓一套.json"))
        .Single(rule => rule.Id == "神算子双波");
    private static string Row(int issue, string pair) => $"{issue}期神算双波【{pair}】开0000准";
    private static OcrLineEvidence Cell(string text, int x, int y, int width = 390) => new(text, new(x, y, width, 32), .99);
    private static OcrEvidence Evidence(params OcrLineEvidence[] items) => new("source", "input", "hash", "hash", "default", items, items);

    [Fact]
    public void RuleKeepsItsWaveContract()
    {
        Assert.Equal("波", Rule.Type);
        Assert.Equal("神算子双波", Rule.OutputLabel);
        Assert.Equal("shensuan_two_waves", Rule.Extractor);
        Assert.Equal("神算子", Rule.Folder);
        Assert.True(Rule.StrictIssueBlock);
    }

    [Theory]
    [InlineData("红波蓝波", "绿波")]
    [InlineData("蓝波红波", "绿波")]
    [InlineData("红波绿波", "蓝波")]
    [InlineData("绿波红波", "蓝波")]
    [InlineData("蓝波绿波", "红波")]
    [InlineData("绿波蓝波", "红波")]
    [InlineData("藍波綠波", "红波")]
    public void InvertsOnlyTheTwoBracketedWaveNames(string pair, string expected)
    {
        Assert.Equal(expected, RuleEngine.ExtractFinalValue([Row(329, pair)], 329, Rule));
        Assert.Equal(expected, RuleEngine.ExtractFinalValue(Evidence(Cell(Row(329, pair), 0, 0, 1200)), 329, Rule));
        Assert.Null(RuleEngine.ExtractFinalValue([Row(329, pair)], 330, Rule));
    }

    [Theory]
    [InlineData("蓝波")]
    [InlineData("蓝波蓝波")]
    [InlineData("蓝波绿波红波")]
    [InlineData("蓝波+绿波")]
    [InlineData("蓝波、绿波")]
    [InlineData("蓝波绿波未知")]
    [InlineData("蓝波黄波")]
    [InlineData("蓝绿")]
    [InlineData("")]
    public void InvalidFieldsRemainMissing(string pair) =>
        Assert.Null(RuleEngine.ExtractFinalValue([Row(329, pair)], 329, Rule));

    [Fact]
    public void BrokenIdentityOrBracketsAndNeighbouringRowsCannotFillTheTarget()
    {
        foreach (string broken in new[]
        {
            Row(329, "蓝波绿波").Replace("神算双波", "其他双波"),
            Row(329, "蓝波绿波").Replace("【", ""),
            Row(329, "蓝波绿波").Replace("】", ""),
            Row(329, "蓝波绿波").Replace("】开", "】【红波蓝波】开"),
            "329期神算双波【蓝波", "绿波】开0000准"
        })
            Assert.Null(RuleEngine.ExtractFinalValue([Row(328, "红波绿波"), broken], 329, Rule));
        Assert.Null(RuleEngine.ExtractFinalValue([Row(328, "红波绿波") + Row(329, "蓝波绿波")], 329, Rule));
    }

    [Fact]
    public void OnlySamePhysicalRowViewAndRegionCanJoinSplitTokens()
    {
        var title = Cell("329期神算双波", 15, 100);
        var pair = Cell("【蓝波绿波】开0000准", 480, 100, 530);
        Assert.Equal("红波", RuleEngine.ExtractFinalValue(Evidence(title, pair), 329, Rule));
        foreach (var other in new[]
        {
            pair with { Box = pair.Box! with { Y = 145 } },
            pair with { Box = pair.Box! with { Y = 45 } },
            pair with { ViewId = "other" },
            pair with { RegionId = "other" },
            pair with { Box = null },
            pair with { Confidence = .3 },
            pair with { Box = pair.Box! with { X = 1400 } }
        })
            Assert.Null(RuleEngine.ExtractFinalValue(Evidence(title, other), 329, Rule));
        Assert.Null(RuleEngine.ExtractFinalValue(Evidence(Cell("328期\n329期", 15, 100), pair), 329, Rule));
    }

    [Fact]
    public void ConflictingCompleteReadingsStayConflicts()
    {
        string[] rows = [Row(329, "蓝波绿波"), Row(329, "蓝波红波")];
        Assert.Equal(RuleExtractionStatus.Conflict, RuleEngine.ExtractFinalResult(rows, 329, Rule).Status);
        Assert.Equal(RuleExtractionStatus.Conflict, RuleEngine.ExtractFinalResult(Evidence(
            Cell(rows[0], 0, 0, 1200), Cell(rows[1], 0, 100, 1200) with { ViewId = "other" }), 329, Rule).Status);
    }

    [Fact]
    public void PeriodHistoryOnTheSameCardSuppliesEveryPastRowButNotTheTarget()
    {
        // 一张卡从 307 期排到 329 期，中间缺 311、314、317、322 期。
        (int Issue, string Pair)[] history =
        [
            (307, "红波蓝波"), (308, "红波绿波"), (309, "红波蓝波"), (310, "红波绿波"),
            (312, "红波绿波"), (313, "蓝波绿波"), (315, "蓝波绿波"), (316, "蓝波红波"),
            (318, "绿波蓝波"), (319, "绿波红波"), (320, "绿波蓝波"), (321, "红波蓝波"),
            (323, "红波蓝波"), (324, "红波绿波"), (325, "蓝波绿波"), (326, "红波绿波"),
            (327, "红波蓝波"), (328, "红波绿波"), (329, "蓝波绿波")
        ];
        string[] lines = history.Select(row => Row(row.Issue, row.Pair)).ToArray();
        string[] expected =
        [
            "绿波", "蓝波", "绿波", "蓝波", "蓝波", "红波", "红波", "绿波",
            "红波", "蓝波", "红波", "绿波", "绿波", "蓝波", "红波", "蓝波",
            "绿波", "蓝波", "红波"
        ];
        Assert.Equal(expected.Length, history.Length);
        for (int i = 0; i < history.Length; i++)
            Assert.Equal(expected[i], RuleEngine.ExtractFinalValue(lines, history[i].Issue, Rule));
        Assert.Null(RuleEngine.ExtractFinalValue(lines, 311, Rule));
        Assert.Null(RuleEngine.ExtractFinalValue(lines, 330, Rule));
    }

    [Fact]
    public void OnlyTheShensuanziSubfolderBelongsToThisRule()
    {
        Assert.Single(RuleEngine.FindMatches(@"C:\图片\11.25-蜻蜓一套\神算子\card.jpg", [Row(329, "蓝波绿波")], [Rule]));
        Assert.Empty(RuleEngine.FindMatches(@"C:\图片\11.25-蜻蜓一套\各种杀\card.jpg", [Row(329, "蓝波绿波")], [Rule]));
        Assert.Empty(RuleEngine.FindMatches(@"C:\图片\其他群\card.jpg", [Row(329, "蓝波绿波")], [Rule]));
    }

    [Fact]
    public void TheRuleTravelsWithTheWaveDistributionSource()
    {
        string text = File.ReadAllText(Path.Combine(Config, "波分发规则.json"));
        Assert.Contains("神算子双波", text, StringComparison.Ordinal);
        Assert.Contains("蜻蜓一套", text, StringComparison.Ordinal);
    }
}
