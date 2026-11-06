using System.Text.Json;
using System.Net;
using OcrLineTool;

namespace OcrLineTool.Tests;

public sealed class FiveCardTableTests
{
    private static string Group(string id) => id == "唐朝杀头" ? "嫣然六合彩大全" : "黑色六合彩";
    private static OcrRule Rule(string id) => RuleCatalog.Load(Path.Combine(
        ResultFilePaths.ConfigurationDirectory(AppContext.BaseDirectory), Group(id) + ".json")).Single(r => r.Id == id);
    private static OcrLineEvidence Token(string text, int x, int y, int w = 480, int h = 24) =>
        new(text, new(x, y, w, h), .99);
    private static OcrEvidence Evidence(string id, params OcrLineEvidence[] tokens) =>
        new($@"C:\结果\11.6-{Group(id)}\全部图片\sample.jpg", "sample", "hash", "hash", "default",
            OcrEvidenceLayout.Partition(tokens), tokens);
    private static OcrLineEvidence[] Row(int issue, int y, string field) =>
        [Token($"{issue}期", 0, y, 80, 30), Token(field, 130, y), Token("开??", 660, y, 100, 30)];

    [Theory]
    [InlineData("黑客杀尾", "数据分析", "必中九尾")]
    [InlineData("黑客杀两肖", "数据分析", "绝杀两肖")]
    [InlineData("黑客九肖", "数据最准之", "九肖")]
    public void AuthorAndExactColumnMatchAcrossNeutralHeader(string id, string neutral, string column)
    {
        Assert.Single(RuleEngine.FindMatches(["黑客数据", neutral, column, "310期"], [Rule(id)]));
        Assert.Empty(RuleEngine.FindMatches(["其他资料", neutral, column, "310期"], [Rule(id)]));
        Assert.Empty(RuleEngine.FindMatches(["黑客数据", "数据分析", "灭庄七肖", "310期黑客数据七肖鼠牛虎兔龙蛇马"], [Rule(id)]));
    }

    [Theory]
    [InlineData("唐朝杀头", "唐朝绝杀一头一头一头稳杀", "1头")]
    [InlineData("天宫杀头", "天宫杀（4）头", "4头")]
    [InlineData("黑客杀尾", "数据分析 0 1 2 3 4 5 6 7 8 尾", "9尾")]
    [InlineData("黑客杀两肖", "数据杀特肖：牛狗", "牛狗")]
    [InlineData("黑客九肖", "数据分析 猴鸡龙猪蛇兔羊虎马", "猴鸡龙猪蛇兔羊虎马")]
    public void ReadsOwnCellAndRejectsMissingTarget(string id, string field, string expected)
    {
        var e = Evidence(id, [..Row(310, 100, field), ..Row(309, 200, field)]);
        Assert.Equal(expected, RuleEngine.ExtractFinalValue(e, 310, Rule(id)));
        Assert.Equal(expected, RuleEngine.ExtractFinalValue(e, 309, Rule(id)));
        Assert.Null(RuleEngine.ExtractFinalValue(e, 311, Rule(id)));
        Assert.Null(RuleEngine.ExtractFinalValue(Evidence(id,
            [..Row(310, 100, ""), ..Row(309, 200, field)]), 310, Rule(id)));
    }

    [Theory]
    [InlineData("唐朝杀头", "唐朝绝杀一头一头四头稳杀")]
    [InlineData("唐朝杀头", "唐朝绝杀一头一头一头稳")]
    [InlineData("天宫杀头", "天宫杀（4 3）头")]
    [InlineData("天宫杀头", "天宫杀（5）头")]
    [InlineData("黑客杀尾", "数据分析 0 1 2 3 4 5 6 7 尾")]
    [InlineData("黑客杀尾", "数据分析 0 1 2 3 4 5 6 7 7 8 尾")]
    [InlineData("黑客杀两肖", "数据杀特肖：牛牛狗")]
    [InlineData("黑客杀两肖", "数据杀特肖：牛狗?犬")]
    [InlineData("黑客九肖", "数据分析 猴鸡龙猪蛇兔羊虎虎")]
    [InlineData("黑客九肖", "黑客数据七肖猴鸡龙猪蛇兔羊")]
    public void InvalidFieldsStayMissing(string id, string field) =>
        Assert.Null(RuleEngine.ExtractFinalValue(Evidence(id, Row(310, 100, field)), 310, Rule(id)));

    [Fact]
    public void NineZodiacsWrapOnlyInsideOneCell()
    {
        const string id = "黑客九肖";
        OcrLineEvidence[] tokens = [Token("310期", 0, 100, 80, 30),
            Token("数据分析猴鸡龙猪蛇兔羊", 130, 85), Token("虎马", 300, 120, 80),
            Token("开??", 660, 100, 100, 30), ..Row(309, 200, "数据分析鼠兔猪狗马龙鸡蛇猴")];
        Assert.Equal("猴鸡龙猪蛇兔羊虎马", RuleEngine.ExtractFinalValue(Evidence(id, tokens), 310, Rule(id)));
        foreach (var replacement in new[] { tokens[2] with { ViewId = "other" }, tokens[2] with { RegionId = "other" },
            tokens[2] with { Box = new(300, 160, 80, 24) }, tokens[2] with { Confidence = .4 }, tokens[2] with { Box = null } })
        {
            var copy = tokens.ToArray(); copy[2] = replacement;
            Assert.Null(RuleEngine.ExtractFinalValue(Evidence(id, copy), 310, Rule(id)));
        }
    }

    [Fact]
    public void ConflictingCompleteViewsStayConflict()
    {
        const string id = "天宫杀头";
        var tokens = Row(310, 100, "天宫杀（4）头").Concat(Row(310, 100, "天宫杀（3）头")
            .Select(t => t with { ViewId = "second" })).ToArray();
        Assert.Equal(RuleExtractionStatus.Conflict, RuleEngine.ExtractFinalResult(Evidence(id, tokens), 310, Rule(id)).Status);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CompleteMergedRowsAndConflictsAreNotLost(bool positioned)
    {
        const string id = "天宫杀头";
        var inline = Token("310期 天宫杀（4）头 开??", 0, 100, 770, 35) with { ViewId = "inline" };
        if (!positioned) inline = inline with { Box = null };
        Assert.Equal("4头", RuleEngine.ExtractFinalValue(Evidence(id, inline), 310, Rule(id)));
        Assert.Equal(RuleExtractionStatus.Conflict, RuleEngine.ExtractFinalResult(Evidence(id,
            [inline, ..Row(310, 200, "天宫杀（3）头")]), 310, Rule(id)).Status);
        Assert.Null(RuleEngine.ExtractFinalValue(Evidence(id,
            [inline with { Text = "310期 天宫杀（4" }, ..Row(310, 200, "天宫杀（3）头")]), 310, Rule(id)));
        Assert.Null(RuleEngine.ExtractFinalValue(Evidence(id,
            inline with { Text = "310期天宫杀（4）头开??309期天宫杀（3）头开??" }), 310, Rule(id)));
    }

    [Theory]
    [InlineData("local-primary/cloud/Tencent/tencent", 40d)]
    [InlineData("tencent", 101d)]
    [InlineData("baidu", .4d)]
    [InlineData("baidu", 1.1d)]
    [InlineData("paddle/original", double.NaN)]
    [InlineData("tencent", double.PositiveInfinity)]
    [InlineData("paddle/original", -1d)]
    public void ProviderConfidenceScalesRejectBadScores(string view, double score)
    {
        const string id = "黑客杀尾";
        var row = Row(310, 100, "数据分析012345678尾");
        foreach (int at in new[] { 0, 1 })
        {
            var copy = row.Select(t => t with { ViewId = view, Confidence = view.Contains("tencent", StringComparison.OrdinalIgnoreCase) ? 99 : .99 }).ToArray();
            copy[at] = copy[at] with { Confidence = score };
            Assert.Null(RuleEngine.ExtractFinalValue(Evidence(id, copy), 310, Rule(id)));
        }
        Assert.Null(RuleEngine.ExtractFinalValue(Evidence(id,
            Token("310期数据分析012345678尾开??", 0, 100, 770, 35) with { ViewId = view, Confidence = score }), 310, Rule(id)));
    }

    [Theory]
    [InlineData("tencent", 99d)]
    [InlineData("baidu", .99d)]
    public void ProviderConfidenceScalesAcceptReliableScores(string view, double score)
    {
        const string id = "黑客杀尾";
        var row = Row(310, 100, "数据分析012345678尾").Select(t => t with { ViewId = view, Confidence = score }).ToArray();
        Assert.Equal("9尾", RuleEngine.ExtractFinalValue(Evidence(id, row), 310, Rule(id)));
    }

    [Theory]
    [InlineData("唐朝杀头", 310, "1头")]
    [InlineData("唐朝杀头", 309, "4头")]
    [InlineData("唐朝杀头", 303, "3头")]
    [InlineData("天宫杀头", 310, "4头")]
    [InlineData("天宫杀头", 308, "2头")]
    [InlineData("天宫杀头", 301, "0头")]
    [InlineData("黑客杀两肖", 310, "牛狗")]
    [InlineData("黑客杀两肖", 309, "兔狗")]
    [InlineData("黑客杀两肖", 301, "龙马")]
    [InlineData("黑客九肖", 310, "猴鸡龙猪蛇兔羊虎马")]
    [InlineData("黑客九肖", 308, "兔狗马鼠猪羊龙虎牛")]
    [InlineData("黑客九肖", 301, "牛蛇龙虎鼠猴猪马鸡")]
    [InlineData("黑客九肖", 309, null)] // Medium omitted the red horse; the opening horse must not replace it.
    [InlineData("黑客九肖", 307, null)] // Medium read 牛 as 生.
    [InlineData("黑客杀尾", 310, null)] // Medium omitted 1; never infer a missing printed digit.
    [InlineData("黑客杀尾", 304, "1尾")]
    [InlineData("黑客杀尾", 295, "0尾")]
    public void FreshMediumEvidenceReadsCompleteRowsAndRejectsOmissions(string id, int issue, string? expected)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "five-card-310.json")));
        var card = doc.RootElement.EnumerateArray().First(c => c.GetProperty("label").GetString() == id
            && c.TryGetProperty("fresh", out var fresh) && fresh.GetBoolean());
        var tokens = card.GetProperty("items").EnumerateArray().Select(item =>
        {
            var b = item.GetProperty("box").EnumerateArray().Select(v => v.GetInt32()).ToArray();
            return new OcrLineEvidence(item.GetProperty("text").GetString()!, new(b[0], b[1], b[2], b[3]),
                item.GetProperty("confidence").GetDouble(), item.GetProperty("viewId").GetString()!);
        }).ToArray();
        Assert.Equal(expected, RuleEngine.ExtractFinalValue(Evidence(id, tokens), issue, Rule(id)));
    }

    [Theory]
    [InlineData(OcrProvider.Tencent, false)]
    [InlineData(OcrProvider.Baidu, false)]
    [InlineData(OcrProvider.Tencent, true)]
    [InlineData(OcrProvider.Baidu, true)]
    public async Task CloudGeometryAndCachedEvidenceUseTheSameCellContract(OcrProvider provider, bool merged)
    {
        string root = Path.Combine(Path.GetTempPath(), "five-card-cloud-" + Guid.NewGuid().ToString("N"));
        var cases = new[] {
            ("唐朝杀头", "唐朝绝杀一头一头一头稳杀", "1头"),
            ("天宫杀头", "天宫杀（4）头", "4头"),
            ("黑客杀尾", "数据分析012345678尾", "9尾"),
            ("黑客杀两肖", "数据杀特肖牛狗", "牛狗"),
            ("黑客九肖", "数据分析猴鸡龙猪蛇兔羊虎马", "猴鸡龙猪蛇兔羊虎马") };
        try
        {
            foreach (var (id, field, expected) in cases)
            {
                string path = Path.Combine(root, "11.6-" + Group(id), id + ".png");
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                await File.WriteAllBytesAsync(path, [1, 2, 3]);
                var tokens = merged ? new[] { Token("310期" + field + "开??", 0, 100, 770, 35) } : Row(310, 100, field);
                string json = provider == OcrProvider.Tencent
                    ? JsonSerializer.Serialize(new { Response = new { TextDetections = tokens.Select(i => new {
                        DetectedText = i.Text, ItemPolygon = new { i.Box!.X, i.Box.Y, i.Box.Width, i.Box.Height } }) } })
                    : JsonSerializer.Serialize(new { words_result = tokens.Select(i => new {
                        words = i.Text, location = new { left = i.Box!.X, top = i.Box.Y, width = i.Box.Width, height = i.Box.Height } }) });
                using var http = new HttpClient(new FixtureHandler(json));
                var credential = new OcrCredential(provider, "TEST", "TEST_ID", "TEST_SECRET");
                IOcrClient client = provider == OcrProvider.Tencent
                    ? new TencentOcrClient(credential, http) : new BaiduOcrClient(credential, http);
                var result = await client.RecognizeEvidenceAsync(path);
                var values = new ResultValues(StringComparer.Ordinal);
                MainForm.AddExtractedEvidenceValues(result, [Rule(id)], 310, values, new ResultEvidenceLedger());
                Assert.Equal(expected, values[id]);
                CloudOcrCacheStore.SaveEntry(root, Group(id), 310, result);
                var cached = CloudOcrCacheStore.LoadEvidence(root, Group(id), 310)[path];
                Assert.Equal(expected, RuleEngine.ExtractFinalValue(cached, 310, Rule(id)));
                Assert.Equal(tokens.Select(t => t.Box), cached.TokenItems!.Select(t => t.Box));
            }
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    private sealed class FixtureHandler(string json) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) {
                Content = new StringContent(request.RequestUri!.AbsolutePath.Contains("oauth", StringComparison.Ordinal)
                    ? "{\"access_token\":\"TEST_TOKEN\",\"expires_in\":3600}" : json) });
    }

    [Theory]
    [InlineData("唐朝杀头", 310, "1头")]
    [InlineData("唐朝杀头", 309, "4头")]
    [InlineData("唐朝杀头", 303, "3头")]
    [InlineData("天宫杀头", 310, "4头")]
    [InlineData("天宫杀头", 308, "2头")]
    [InlineData("天宫杀头", 301, "0头")]
    [InlineData("黑客杀两肖", 310, "牛狗")]
    [InlineData("黑客杀尾", 310, "9尾")]
    [InlineData("黑客九肖", 310, "猴鸡龙猪蛇兔羊虎马")]
    public void RecordedCoordinatesRecoverFailures(string id, int issue, string expected)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "five-card-310.json")));
        var card = doc.RootElement.EnumerateArray().First(c => c.GetProperty("label").GetString() == id);
        var tokens = card.GetProperty("items").EnumerateArray().Select(item =>
        {
            var b = item.GetProperty("Box").EnumerateArray().Select(v => v.GetInt32()).ToArray();
            return new OcrLineEvidence(item.GetProperty("Text").GetString()!, new(b[0], b[1], b[2], b[3]),
                item.GetProperty("Confidence").GetDouble(), item.GetProperty("ViewId").GetString()!);
        }).ToArray();
        Assert.Equal(expected, RuleEngine.ExtractFinalValue(Evidence(id, tokens), issue, Rule(id)));
    }
}
