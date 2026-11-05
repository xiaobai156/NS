using System.Net;
using System.Text.Json;
using OcrLineTool;

namespace OcrLineTool.Tests;

public sealed class RedDragonflyWaveTests
{
    private static string Config => ResultFilePaths.ConfigurationDirectory(AppContext.BaseDirectory);
    private static OcrRule Rule => RuleCatalog.Load(Path.Combine(Config, "黑色六合彩.json"))
        .Single(rule => rule.Id == "红蜻蜓双波");
    private static string Row(int issue, string pair) => $"{issue}期：【红蜻蜓两波中特】---（{pair}）---开00准主一波：蓝，博一波蓝";
    private static OcrLineEvidence Cell(string text, int x, int y, int width = 390) => new(text, new(x, y, width, 32), .99);
    private static OcrEvidence Evidence(params OcrLineEvidence[] items) => new("source", "input", "hash", "hash", "default", items, items);
    private sealed record Card(string FileName, OcrLineEvidence[] Small, OcrLineEvidence[] Medium);
    private static Card[] Cards => JsonSerializer.Deserialize<Card[]>(File.ReadAllText(
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "red-dragonfly-waves.json")),
        new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

    [Theory]
    [InlineData("红波+蓝波", "绿波")]
    [InlineData("蓝波+红波", "绿波")]
    [InlineData("红波+绿波", "蓝波")]
    [InlineData("绿波+红波", "蓝波")]
    [InlineData("蓝波+绿波", "红波")]
    [InlineData("绿波+蓝波", "红波")]
    [InlineData("藍波＋綠波", "红波")]
    public void InvertsOnlyTheTwoBracketedWaveNames(string pair, string expected)
    {
        Assert.Equal(expected, RuleEngine.ExtractFinalValue([Row(112, pair)], 112, Rule));
        Assert.Equal(expected, RuleEngine.ExtractFinalValue(Evidence(Cell(Row(112, pair), 0, 0, 1200)), 112, Rule));
        Assert.Null(RuleEngine.ExtractFinalValue([Row(112, pair)], 113, Rule));
    }

    [Theory]
    [InlineData("蓝波")]
    [InlineData("蓝波+蓝波")]
    [InlineData("蓝波+绿波+红波")]
    [InlineData("蓝波+绿波未知")]
    [InlineData("蓝波+黄波")]
    [InlineData("蓝+绿")]
    [InlineData("蓝波绿波")]
    [InlineData("蓝波+开00绿波")]
    public void InvalidFieldsRemainMissing(string pair) =>
        Assert.Null(RuleEngine.ExtractFinalValue([Row(309, pair)], 309, Rule));

    [Fact]
    public void BrokenIdentityOrBracketsAndNeighbouringRowsCannotFillTheTarget()
    {
        foreach (string broken in new[]
        {
            Row(309, "蓝波+绿波").Replace("红蜻蜓", "其他人"),
            Row(309, "蓝波+绿波").Replace("（", ""),
            Row(309, "蓝波+绿波").Replace("）", ""),
            Row(309, "蓝波+绿波").Replace("）---开", "）（红波+蓝波）---开"),
            "309期：【红蜻蜓两波中特】---（蓝波+", "绿波）---开00准"
        })
            Assert.Null(RuleEngine.ExtractFinalValue([Row(308, "红波+绿波"), broken], 309, Rule));
        Assert.Null(RuleEngine.ExtractFinalValue(["309期：【红蜻蜓两波中特】", "（蓝波+绿波）"], 309, Rule));
        Assert.Null(RuleEngine.ExtractFinalValue([Row(308, "红波+绿波") + Row(309, "蓝波+绿波")], 309, Rule));
    }

    [Fact]
    public void OnlySamePhysicalRowViewAndRegionCanJoinSplitTokens()
    {
        var title = Cell("309期：【红蜻蜓两波中特】", 15, 100);
        var pair = Cell("（蓝波+绿波）---开00准", 480, 100, 530);
        Assert.Equal("红波", RuleEngine.ExtractFinalValue(Evidence(title, pair), 309, Rule));
        foreach (var other in new[]
        {
            pair with { Box = pair.Box! with { Y = 145 } },
            pair with { Box = pair.Box! with { Y = 45 } },
            pair with { ViewId = "other" }, pair with { RegionId = "other" },
            pair with { Box = null }, pair with { Confidence = .3 },
            pair with { Box = pair.Box! with { X = 1400 } }
        })
            Assert.Null(RuleEngine.ExtractFinalValue(Evidence(title, other), 309, Rule));
        Assert.Null(RuleEngine.ExtractFinalValue(Evidence(Cell("308期\n309期", 15, 100), pair), 309, Rule));
    }

    [Fact]
    public void ConflictingCompleteReadingsStayConflicts()
    {
        string[] rows = [Row(309, "蓝波+绿波"), Row(309, "蓝波+红波")];
        Assert.Equal(RuleExtractionStatus.Conflict, RuleEngine.ExtractFinalResult(rows, 309, Rule).Status);
        Assert.Equal(RuleExtractionStatus.Conflict, RuleEngine.ExtractFinalResult(Evidence(
            Cell(rows[0], 0, 0, 1200), Cell(rows[1], 0, 100, 1200) with { ViewId = "other" }), 309, Rule).Status);
    }

    [Fact]
    public void FreshGpuEvidenceReadsAll17PeriodsAndRejectsTheThreeSiblingCards()
    {
        string[] expected = ["蓝波", "蓝波", "绿波", "蓝波", "蓝波", "蓝波", "绿波", "蓝波", "蓝波",
            "绿波", "红波", "绿波", "蓝波", "红波", "绿波", "蓝波", "红波"];
        foreach (var card in Cards)
        {
            var evidence = Evidence(card.Medium);
            for (int i = 0; i < expected.Length; i++)
                Assert.Equal(card.FileName.EndsWith("82691.jpg") ? expected[i] : null,
                    RuleEngine.ExtractFinalValue(evidence, 293 + i, Rule));
            Assert.Null(RuleEngine.ExtractFinalValue(evidence, 292, Rule));
            Assert.Null(RuleEngine.ExtractFinalValue(evidence, 310, Rule));
        }
        var rules = RuleCatalog.Load(Path.Combine(Config, "黑色六合彩.json"));
        var readings = Cards.ToDictionary(c => Path.Combine(@"C:\图片\11.5-黑色六合彩\红蜻蜓", c.FileName),
            c => (IReadOnlyList<string>)c.Small.Select(i => i.Text).ToArray());
        var plan = Assert.Single(LocalCandidatePlanner.Build(readings.Keys.ToArray(), readings, [Rule], 309, rules));
        Assert.EndsWith("82691.jpg", plan.Path);
        Assert.Equal(Rule.Id, Assert.Single(plan.Rules).Id);
        Assert.Empty(RuleEngine.FindMatches(@"C:\图片\其他\card.jpg", [Row(309, "蓝波+绿波")], [Rule]));
    }

    [Theory]
    [InlineData(OcrProvider.Tencent)]
    [InlineData(OcrProvider.Baidu)]
    public async Task CloudAdaptersAndCacheKeepTheRowGeometry(OcrProvider provider)
    {
        string root = Path.Combine(Path.GetTempPath(), "red-dragonfly-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "red-dragonfly-waves.jpg");
            var tokens = Cards.Single(c => c.FileName.EndsWith("82691.jpg")).Medium;
            string json = provider == OcrProvider.Tencent
                ? JsonSerializer.Serialize(new { Response = new { TextDetections = tokens.Select(i => new {
                    DetectedText = i.Text, ItemPolygon = new { i.Box!.X, i.Box.Y, i.Box.Width, i.Box.Height } }) } })
                : JsonSerializer.Serialize(new { words_result = tokens.Select(i => new {
                    words = i.Text, location = new { left = i.Box!.X, top = i.Box.Y, width = i.Box.Width, height = i.Box.Height } }) });
            using var http = new HttpClient(new FixtureHandler(json));
            var credential = new OcrCredential(provider, "TEST", "TEST_ID", "TEST_SECRET");
            IOcrClient client = provider == OcrProvider.Tencent
                ? new TencentOcrClient(credential, http) : new BaiduOcrClient(credential, http);
            var evidence = await client.RecognizeEvidenceAsync(path);
            CloudOcrCacheStore.SaveEntry(root, "黑色六合彩", 309, evidence);
            var cached = CloudOcrCacheStore.LoadEvidence(root, "黑色六合彩", 309)[path];
            foreach (var result in new[] { evidence, cached })
            {
                Assert.Equal("红波", RuleEngine.ExtractFinalValue(result, 309, Rule));
                Assert.Equal("蓝波", RuleEngine.ExtractFinalValue(result, 294, Rule));
                var values = new ResultValues(StringComparer.Ordinal);
                MainForm.AddExtractedEvidenceValues(result, [Rule], 309, values, new ResultEvidenceLedger());
                Assert.Equal("红波 红蜻蜓双波", Assert.Single(RuleEngine.FormatOutput([Rule], values)));
            }
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void TemplateCannotSelectAnIdenticalCopyOutsideTheSourceFolder()
    {
        string root = Path.Combine(Path.GetTempPath(), "red-dragonfly-template-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "红蜻蜓"));
        try
        {
            string fixture = Path.Combine(AppContext.BaseDirectory, "Fixtures", "red-dragonfly-waves.jpg");
            string outside = Path.Combine(root, "first.jpg");
            string inside = Path.Combine(root, "红蜻蜓", "second.jpg");
            File.Copy(fixture, outside);
            File.Copy(fixture, inside);
            var template = VisualTemplateMatcher.Load(Path.Combine(Config, "黑色六合彩.templates.json"))
                .Templates.Single(t => t.Id == Rule.Id);
            Assert.Equal("红蜻蜓", template.SourceFolder);
            Assert.Empty(VisualTemplateMatcher.Match([outside], [template], 40, device: LocalOcrDevice.Cpu));
            Assert.Equal(inside, Assert.Single(VisualTemplateMatcher.Match(
                [outside, inside], [template], 40, device: LocalOcrDevice.Cpu)).SourcePath);
            var unscoped = template with { Id = "other", RuleIds = ["other"], SourceFolder = null };
            Assert.Single(VisualTemplateMatcher.Match(
                [outside, inside], [template, unscoped], 40, device: LocalOcrDevice.Cpu));
            Assert.Single(VisualTemplateMatcher.Match(
                [outside, inside], [unscoped, template], 40, device: LocalOcrDevice.Cpu));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task WaveRoutingUsesExistingPlacementAndDeduplication()
    {
        string root = Path.Combine(Path.GetTempPath(), "red-dragonfly-routing-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string target = Path.Combine(root, "309期-一波.txt");
            await File.WriteAllTextAsync(target, "绿波 大赢家双波\n\n309期排行\n原统计\n");
            string[] lines = ["红波 红蜻蜓双波", "绿波 黑客半波", "红波蓝波 红蜻蜓双波", "缺失 红蜻蜓双波"];
            for (int i = 0; i < 2; i++)
                Assert.Equal("红波 红蜻蜓双波", Assert.Single(await ResultDistributor.DistributeAsync(
                    "11.5-黑色六合彩", 309, lines, root, Path.Combine(Config, "波分发规则.json"))));
            string text = await File.ReadAllTextAsync(target);
            Assert.Equal(1, text.Split("红波 红蜻蜓双波").Length - 1);
            Assert.Contains("绿波 大赢家双波", text);
            Assert.True(text.IndexOf("红波 红蜻蜓双波", StringComparison.Ordinal) < text.IndexOf("309期排行", StringComparison.Ordinal));
            Assert.Empty(await ResultDistributor.DistributeAsync("蜻蜓一套", 309, lines, root, Path.Combine(Config, "波分发规则.json")));
            await Assert.ThrowsAsync<OcrException>(() => ResultDistributor.DistributeAsync(
                "黑色六合彩", 310, lines, root, Path.Combine(Config, "波分发规则.json")));
            Assert.False(File.Exists(Path.Combine(root, "310期-一波.txt")));
        }
        finally { Directory.Delete(root, true); }
    }

    private sealed class FixtureHandler(string json) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) {
                Content = new StringContent(request.RequestUri!.AbsolutePath.Contains("oauth", StringComparison.Ordinal)
                    ? "{\"access_token\":\"TEST_TOKEN\",\"expires_in\":3600}" : json) });
    }
}
