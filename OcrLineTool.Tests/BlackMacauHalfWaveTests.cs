using OcrLineTool;
using System.Net;
using System.Text.Json;

namespace OcrLineTool.Tests;

public sealed class BlackMacauHalfWaveTests
{
    private const string RedSingle = "数据半波【红波双蓝波单蓝波双绿波单绿波双】";
    private const string GreenDouble = "数据半波【红波单红波双蓝波单蓝波双绿波单】";
    private static OcrRule Wave => RuleCatalog.Load(Path.Combine(
        ResultFilePaths.ConfigurationDirectory(AppContext.BaseDirectory), "黑色六合彩.json"))
        .Single(rule => rule.Id == "黑客半波");

    private static OcrLineEvidence Item(string text, int x, int y, int width = 450, int height = 20) =>
        new(text, new(x, y, width, height));

    private static OcrEvidence Evidence(params OcrLineEvidence[] items) =>
        new(@"C:图片黑色六合彩card.jpg", "fixture", "hash", "hash", "default", items, items);

    private static OcrLineEvidence[] Row(int issue, int y, params OcrLineEvidence[] field) =>
        [Item($"{issue}期", 0, y, 80, 30), ..field, Item("开??", 650, y, 100, 30)];

    [Theory]
    [InlineData(RedSingle)]
    [InlineData(GreenDouble)]
    public void UnpositionedPrecedingFieldHasNoProvableIssue(string field)
    {
        foreach (string[] lines in new string[][]
        {
            [field, "298期", "开??"],
            ["馬13中", field, "298期", "开??"],
            ["297期", "298期", field, "298期", "开??"],
            ["297期", "猴23中", "开??", field, "298期", "开??"]
        })
            Assert.Null(RuleEngine.ExtractFinalValue(lines, 298, Wave));
    }

    [Fact]
    public void EqualTextWithDifferentCoordinatesHasDifferentOwnership()
    {
        var own = Evidence(Row(298, 100, Item(RedSingle, 120, 90)));
        var previous = Evidence(Row(298, 100, Item(RedSingle, 120, 20)));
        Assert.Equal(own.Lines, previous.Lines);
        Assert.Equal("红单", RuleEngine.ExtractFinalValue(own, 298, Wave));
        Assert.Null(RuleEngine.ExtractFinalValue(previous, 298, Wave));
    }

    [Fact]
    public void Real298CardGeometryReadsOnlyTheTargetHalfWaveCell()
    {
        var card = Evidence([
            Item("数据半波【红波双蓝波单蓝波双", 132, 119, 505, 34),
            Item("298期", 13, 136, 93, 38),
            Item("开??", 683, 133, 79, 42),
            Item("绿波单绿波双】", 255, 156, 249, 32),
            Item("数据半波【红波单", 132, 213, 281, 36),
            Item("红波双蓝波单", 421, 214, 215, 34),
            Item("297期", 13, 231, 93, 37),
            Item("馬13中", 670, 229, 107, 40),
            Item("蓝波双绿波单】", 253, 247, 252, 37),
        ]);

        Assert.Equal("红单", RuleEngine.ExtractFinalValue(card, 298, Wave));
        Assert.Equal("绿双", RuleEngine.ExtractFinalValue(card, 297, Wave));
        Assert.Null(RuleEngine.ExtractFinalValue(card, 299, Wave));
    }

    [Theory]
    [InlineData("298期")]
    [InlineData("【298期】")]
    [InlineData("[298期]")]
    public void BothWrappedLinesMustBelongToTheIssueCell(string issue)
    {
        var e = Evidence([
            Item("黑客数据 GPT半波预测", 100, 0),
            Item("数据半波【红波双蓝波单蓝波双", 120, 80),
            Item(issue, 0, 95, 80, 30), Item("开??", 650, 95, 100, 30),
            Item("绿波单绿波双】", 240, 120, 250),
            ..Row(297, 195, Item(GreenDouble, 120, 185))]);
        Assert.Equal("红单", RuleEngine.ExtractFinalValue(e, 298, Wave));
        Assert.Equal("绿双", RuleEngine.ExtractFinalValue(e, 297, Wave));
        Assert.Null(RuleEngine.ExtractFinalValue(e, 299, Wave));
    }

    [Fact]
    public void MissingCellNeverFallsBackToTextOrAnAdjacentRow()
    {
        var e = Evidence([..Row(297, 100, Item(GreenDouble, 120, 90)), ..Row(298, 200)]);
        Assert.Null(RuleEngine.ExtractFinalValue(e, 298, Wave));
        var wrongColumn = Evidence(Row(298, 100, Item(RedSingle, 760, 90)));
        Assert.Null(RuleEngine.ExtractFinalValue(wrongColumn, 298, Wave));
        var noOpening = Evidence(Item("298期", 0, 100, 80, 30), Item(RedSingle, 120, 90));
        Assert.Null(RuleEngine.ExtractFinalValue(noOpening, 298, Wave));
    }

    [Fact]
    public void IndependentViewsCannotCompleteEachOthersBracket()
    {
        var e = Evidence([..Row(298, 100, Item("数据半波【红波双蓝波单蓝波双", 120, 90)),
            Item("绿波单绿波双】", 240, 120, 250) with { ViewId = "other" }]);
        Assert.Null(RuleEngine.ExtractFinalValue(e, 298, Wave));
    }

    [Fact]
    public void IndependentRegionsCannotCompleteEachOthersBracket()
    {
        var e = Evidence([..Row(298, 100, Item("数据半波【红波双蓝波单蓝波双", 120, 90)),
            Item("绿波单绿波双】", 240, 120, 250) with { RegionId = "other" }]);
        Assert.Null(RuleEngine.ExtractFinalValue(e, 298, Wave));
    }

    [Fact]
    public void IssueAnchorCannotBorrowACompleteFieldFromAnotherRegion()
    {
        var items = Row(298, 100, Item(RedSingle, 120, 90));
        items[0] = items[0] with { RegionId = "left" };
        items[1] = items[1] with { RegionId = "right" };
        items[2] = items[2] with { RegionId = "right" };
        Assert.Null(RuleEngine.ExtractFinalValue(Evidence(items), 298, Wave));
    }

    [Theory]
    [InlineData("297期：", 50, 75)]
    [InlineData("297期马13中", 50, 75)]
    [InlineData("【297期】：", 50, 75)]
    [InlineData("299期：", 150, 135)]
    public void NeighborIssuePrefixesStillBoundTheTargetCell(string neighbor, int y, int fieldY)
    {
        var evidence = Evidence([
            Item(neighbor, 0, y, 80, 30), Item(RedSingle, 120, fieldY),
            Item("马13中", 650, y, 100, 30), ..Row(298, 100)]);
        Assert.Null(RuleEngine.ExtractFinalValue(evidence, 298, Wave));
    }

    [Fact]
    public void UnresolvedMergedIssueFieldCannotDisappearBehindAnotherSuccess()
    {
        OcrLineEvidence[] split = [
            Item("298期数据半波【红波双蓝波单蓝波双开??", 0, 100, 750, 30),
            Item("绿波单绿波双】", 240, 135, 250)];
        Assert.Equal(RuleExtractionStatus.Missing, RuleEngine.ExtractFinalResult(Evidence(split), 298, Wave).Status);
        Assert.Equal(RuleExtractionStatus.Missing, RuleEngine.ExtractFinalResult(Evidence([
            ..split, Item("298期" + GreenDouble, 0, 200, 750, 30)]), 298, Wave).Status);
    }

    [Fact]
    public void UnresolvedOldIssueDoesNotBlockTheCompleteTarget()
    {
        Assert.Equal("绿双", RuleEngine.ExtractFinalValue(Evidence([
            Item("297期数据半波【红波双蓝波单", 0, 100, 750, 30),
            Item("298期" + GreenDouble, 0, 200, 750, 30)]), 298, Wave));
    }

    [Theory]
    [InlineData(OcrProvider.Tencent, RuleExtractionStatus.Success, false)]
    [InlineData(OcrProvider.Baidu, RuleExtractionStatus.Success, false)]
    [InlineData(OcrProvider.Tencent, RuleExtractionStatus.Missing, false)]
    [InlineData(OcrProvider.Baidu, RuleExtractionStatus.Missing, false)]
    [InlineData(OcrProvider.Tencent, RuleExtractionStatus.Conflict, false)]
    [InlineData(OcrProvider.Baidu, RuleExtractionStatus.Conflict, false)]
    [InlineData(OcrProvider.Tencent, RuleExtractionStatus.Success, true)]
    [InlineData(OcrProvider.Baidu, RuleExtractionStatus.Success, true)]
    [InlineData(OcrProvider.Tencent, RuleExtractionStatus.Conflict, true)]
    [InlineData(OcrProvider.Baidu, RuleExtractionStatus.Conflict, true)]
    public async Task CloudClientsAndCacheKeepOriginalCellGeometry(OcrProvider provider, RuleExtractionStatus scenario, bool mergedPrefix)
    {
        string root = Path.Combine(Path.GetTempPath(), "half-wave-cloud-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string path = Path.Combine(root, "sample.png");
            await File.WriteAllBytesAsync(path, [1, 2, 3]);
            OcrLineEvidence[] tokens = [
                Item("数据半波【红波双蓝波单蓝波双", 132, 119, 505, 34),
                Item("298期", 13, 136, 93, 38), Item("开??", 683, 133, 79, 42),
                Item("绿波单绿波双】", 255, 156, 249, 32),
                ..Row(297, 250, Item(GreenDouble, 120, 240))];
            if (mergedPrefix)
                tokens = [Item("298期数据半波【红波双蓝波单蓝波双开??", 0, 119, 762, 34), ..tokens.Skip(3)];
            if (scenario == RuleExtractionStatus.Missing)
                tokens = tokens.Where(item => item.Box!.Y > 200 || !item.Text.Contains("波", StringComparison.Ordinal)).ToArray();
            if (scenario == RuleExtractionStatus.Conflict)
                tokens = [..tokens, Item("298期" + GreenDouble, 0, 350, 760, 30)];
            RuleExtractionStatus expected = mergedPrefix ? RuleExtractionStatus.Missing : scenario;
            string json = provider == OcrProvider.Tencent
                ? JsonSerializer.Serialize(new { Response = new { TextDetections = tokens.Select(i => new {
                    DetectedText = i.Text, ItemPolygon = new { i.Box!.X, i.Box.Y, i.Box.Width, i.Box.Height } }) } })
                : JsonSerializer.Serialize(new { words_result = tokens.Select(i => new {
                    words = i.Text, location = new { left = i.Box!.X, top = i.Box.Y, width = i.Box.Width, height = i.Box.Height } }) });
            using var http = new HttpClient(new FixtureHandler(json));
            var credential = new OcrCredential(provider, "TEST", "TEST_ID", "TEST_SECRET");
            IOcrClient client = provider == OcrProvider.Tencent
                ? new TencentOcrClient(credential, http) : new BaiduOcrClient(credential, http);
            OcrEvidence result = await client.RecognizeEvidenceAsync(path);
            Assert.Equal(expected, RuleEngine.ExtractFinalResult(result, 298, Wave).Status);
            Assert.Equal(expected == RuleExtractionStatus.Success ? "红单" : null, RuleEngine.ExtractFinalValue(result, 298, Wave));
            Assert.Equal(tokens.Select(item => item.Box), result.TokenItems!.Select(item => item.Box));
            CloudOcrCacheStore.SaveEntry(root, "黑色六合彩", 298, result);
            OcrEvidence cached = CloudOcrCacheStore.LoadEvidence(root, "黑色六合彩", 298)[path];
            Assert.Equal(result.TokenItems, cached.TokenItems);
            Assert.Equal(expected, RuleEngine.ExtractFinalResult(cached, 298, Wave).Status);
            Assert.Equal(expected == RuleExtractionStatus.Success ? "红单" : null, RuleEngine.ExtractFinalValue(cached, 298, Wave));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private sealed class FixtureHandler(string json) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) {
                Content = new StringContent(request.RequestUri!.AbsolutePath.Contains("oauth", StringComparison.Ordinal)
                    ? "{\"access_token\":\"TEST_TOKEN\",\"expires_in\":3600}" : json) });
    }

    [Fact]
    public void PartialGeometryCannotFallBackToAdjacentText()
    {
        var items = Row(298, 100, Item(RedSingle, 120, 20));
        items[^1] = items[^1] with { Box = null };
        Assert.Null(RuleEngine.ExtractFinalValue(Evidence(items), 298, Wave));
    }

    [Theory]
    [InlineData("298期数据半波【红波双蓝波单蓝波双绿波单绿波双")]
    [InlineData("298期数据半波红波双蓝波单蓝波双绿波单绿波双】")]
    [InlineData("298期数据半波【红波双蓝波单蓝波双绿波单绿波双】未知")]
    public void IncompleteOrUnknownContainerCannotBecomeSuccess(string text)
    {
        Assert.Null(RuleEngine.ExtractFinalValue([text], 298, Wave));
    }

    [Theory]
    [InlineData("红开??波双蓝波单蓝波双绿波单绿波双")]
    [InlineData("红波双开??蓝波单开??蓝波双绿波单绿波双")]
    public void OpeningPlaceholderCannotRepairWordsOrJoinMultipleBlocks(string payload)
    {
        Assert.Null(RuleEngine.ExtractFinalValue([$"298期数据半波【{payload}】"], 298, Wave));
    }

    [Theory]
    [InlineData("红波双蓝波单蓝波双绿波单")]
    [InlineData("红波单红波双蓝波单蓝波双绿波单绿波双")]
    [InlineData("红波双蓝波单蓝波双绿波单绿波双绿波双")]
    [InlineData("红波双蓝波单蓝波双绿波单绿波双未知")]
    [InlineData("红波双蓝波单蓝波双绿波单绿波双开00中")]
    public void ExactFiveDistinctKindsAreRequired(string payload)
    {
        string field = $"数据半波【{payload}】";
        Assert.Null(RuleEngine.ExtractFinalValue([$"298期{field}"], 298, Wave));
        Assert.Null(RuleEngine.ExtractFinalValue(Evidence(Row(298, 100, Item(field, 120, 90))), 298, Wave));
    }

    [Fact]
    public void SameIssueDifferentCompleteFieldsAreConflicts()
    {
        Assert.Equal(RuleExtractionStatus.Conflict, RuleEngine.ExtractFinalResult(
            [$"298期{RedSingle}", $"298期{GreenDouble}"], 298, Wave).Status);
        Assert.Equal(RuleExtractionStatus.Conflict, RuleEngine.ExtractFinalResult(Evidence([
            ..Row(298, 100, Item(RedSingle, 120, 90)),
            ..Row(298, 200, Item(GreenDouble, 120, 190))]), 298, Wave).Status);
    }

    [Fact]
    public void InlineIssueAndWrappedIssuePrefixKeepTheirOwnPayload()
    {
        foreach (string prefix in new[] { "298期", "【298期】", "[298期]" })
            Assert.Equal("红单", RuleEngine.ExtractFinalValue(
                [$"{prefix}数据半波【红波双蓝波单蓝波双开??", "绿波单绿波双】"], 298, Wave));
        Assert.Equal("红单", RuleEngine.ExtractFinalValue(
            [$"297期{GreenDouble}", $"298期{RedSingle}"], 298, Wave));
    }
}
