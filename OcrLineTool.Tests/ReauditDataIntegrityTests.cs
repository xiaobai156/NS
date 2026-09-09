// Source audit acceptance tests; NOT compiled or run in the review environment.
// Reviewed commit: 826117951819219d63e6a4b82b3534a65eee9f92
// All OCR responses are text fixtures or a local HttpMessageHandler; no real OCR or GPU calls.
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using OcrLineTool;
using Xunit;

namespace OcrLineTool.Tests;

[Trait("Category", "ReauditAccuracy")]
public sealed class ReauditDataIntegrityTests
{
    private static OcrRule Rule(string group, string id) => RuleCatalog.Load(
        Path.Combine(ResultFilePaths.ConfigurationDirectory(AppContext.BaseDirectory), group + ".json"))
        .Single(rule => rule.Id == id);

    public static IEnumerable<object[]> Cases()
    {
        yield return new object[] { "R01", "新澳高级会员", "会员特供杀十码", 251, new string[] { "会员特供绝杀10码", "页眉广告 01 02", "251期 绝杀:03 04 05 06 07 08 09 10 准!" }, null! };
        yield return new object[] { "R02", "新澳高级会员", "会员特供杀十码", 251, new string[] { "会员特供绝杀10码", "页眉广告 01 02", "250期 绝杀:03 04 05 06 07 08 09 10 11 12", "251期 绝杀:13 14 15 16 17 18 19 20" }, null! };
        yield return new object[] { "R03", "嫣然心水", "蓝色", 251, new string[] { "蓝色", "250期 特码开在", "01 02 03 04 05 06 07 08 09 10 11 12 13 14 15 16 17 18 19 20 21 22 23 24 25 26 27 28 29 30 31 32 33 34 35 36", "251期 特码开在", "14 15 16 17 18 19 20 21 22 23 24 25 26 27 28 29 30 31 32 33 34 35 36 37 38 39 40 41 42 43 44 45 46 47 48 49" }, "14 15 16 17 18 19 20 21 22 23 24 25 26 27 28 29 30 31 32 33 34 35 36 37 38 39 40 41 42 43 44 45 46 47 48 49" };
        yield return new object[] { "R04", "嫣然心水", "青苹果", 251, new string[] { "251期 青苹果 杀六码 01 02 03 04 05 开猴06中" }, null! };
        yield return new object[] { "R05", "嫣然心水", "青苹果", 251, new string[] { "251期 青苹果 杀六码 01 02 03 04 05", "旁栏参考：06" }, null! };
        yield return new object[] { "R06", "嫣然心水", "南国挽心", 251, new string[] { "南国挽心", "250期 杀狗", "251期 陌上花 杀鸡" }, null! };
        yield return new object[] { "R07", "嫣然心水", "钦差大臣公式一", 251, new string[] { "钦差大臣", "公式一", "251期 待更新", "公式二", "251期 鸡" }, null! };
        yield return new object[] { "R08", "嫣然心水", "南国挽心", 251, new string[] { "统计表", "251期", "南国挽心 待更新 陌上花 禁 鸡" }, null! };
        yield return new object[] { "R09", "新澳高手", "高山流水", 251, new string[] { "高山流水", "250期 精选⑨肖 马蛇龙兔虎牛鼠猪狗", "251期 待更新" }, null! };
        yield return new object[] { "R10", "新澳高手", "亚太一头", 251, new string[] { "251期 亚太地区四头：013" }, null! };
        yield return new object[] { "R11", "新澳高手", "亚太尾", 251, new string[] { "251期 亚太地区八尾：0134567" }, null! };
        yield return new object[] { "R12", "嫣然心水", "小骚货", 251, new string[] { "快乐的骚货 九肖", "251期 九肖待更新", "六肖 马蛇龙兔虎牛", "三肖 鼠猪狗" }, null! };
        yield return new object[] { "R13", "嫣然心水", "小骚货", 251, new string[] { "快乐的骚货 九肖", "251期 九肖 马蛇龙兔虎牛鼠猪狗", "鸡" }, null! };
        yield return new object[] { "R14", "嫣然心水", "小灰灰一肖", 251, new string[] { "251期 小灰灰绝杀一肖 鼠", "牛" }, null! };
        yield return new object[] { "R15", "嫣然心水", "南国挽心", 251, new string[] { "251期 南国挽心 鸡 狗" }, null! };
        yield return new object[] { "R16", "嫣然心水", "沁园春", 251, new string[] { "251期 沁园春 杀二肖 鼠牛虎" }, null! };
        yield return new object[] { "R17", "嫣然心水", "沁园春", 251, new string[] { "251期 沁园春 杀二肖 鼠鼠牛" }, null! };
        yield return new object[] { "R18", "嫣然心水", "青苹果", 251, new string[] { "251期 青苹果 杀六码 01 02 03 04 05 06", "251期 青苹果 杀六码 07 08 09 10 11 12" }, null! };
        yield return new object[] { "R19", "嫣然心水", "杰少九肖", 251, new string[] { "原创杰少", "251期 新澳九肖 马蛇龙兔虎牛鼠猪狗", "251期 新澳九肖 马蛇龙兔虎牛鼠猪鸡" }, null! };
        yield return new object[] { "R20", "新澳六合彩资料", "时点半", 251, new string[] { "250期 十点半集团大围36码 01 02 03 04 05 06 07 08 09 10 11 12 13 14 15 16 17 18 19 20 21 22 23 24 25 26 27 28 29 30 31 32 33 34 35 36" }, null! };
        yield return new object[] { "R21", "嫣然心水", "南国挽心", 251, new string[] { "南国挽心编号251 鼠" }, null! };
        yield return new object[] { "R22", "嫣然心水", "南国挽心", 1001, new string[] { "1001期 南国挽心 待更新", "1002", "鸡" }, null! };
        yield return new object[] { "R23", "新澳六合彩资料", "包公肖肖", 251, new string[] { "包公图", "251期", "￥" }, null! };
        yield return new object[] { "R24", "新澳高级会员", "翩翩公子肖", 251, new string[] { "251期 公子杀一肖：鸡? 准!" }, null! };
        yield return new object[] { "R25", "蜻蜓一套骁腾", "绿格子双杀", 251, new string[] { "251期 绝杀2肖【鸡狗】【鼠牛】" }, null! };
        yield return new object[] { "R26", "蜻蜓一套骁腾", "公式杀两肖肖", 251, new string[] { "251期 (1+1)=杀【狗猴】；最终=杀【鸡牛】" }, "鸡牛" };
        yield return new object[] { "R27", "嫣然心水", "借花献佛", 251, new string[] { "借花献佛", "251期", "9次", "8次 鸡" }, null! };
        yield return new object[] { "C01", "嫣然心水", "南国挽心", 251, new string[] { "251期 南国挽心 鸡" }, "鸡" };
        yield return new object[] { "C02", "新澳高级会员", "会员特供杀十码", 251, new string[] { "会员特供绝杀10码", "251期 绝杀:01 02 03 04 05 06 07 08 09 10" }, "01 02 03 04 05 06 07 08 09 10" };
        yield return new object[] { "C03", "新澳高级会员", "翩翩公子尾", 251, new string[] { "251期 公子送尾数：0 1 2 3 4 5 6 7 9 准!" }, "8尾" };
        yield return new object[] { "C04", "新澳高级会员", "翩翩公子尾", 251, new string[] { "251期 公子送尾数：1 2 3 4 5 6 7 8 准!" }, null! };
        yield return new object[] { "C05", "嫣然心水", "末日降临", 251, new string[] { "249期 末日降临 1段", "50期 末日降临 2段", "50期 末日降临 3段" }, null! };
        yield return new object[] { "C06", "新澳高级会员", "翩翩公子头", 251, new string[] { "251期 公子禁止1头：零头 准!", "251期 公子禁止1头：四头 准!" }, null! };
        yield return new object[] { "C07", "嫣然心水", "彩图", 251, new string[] { "250期:开奖结果:00-00-00-00-00-00特00准", "01 02 03 04 05 06 07 08 09 10 11 12 13 14 15 16 17 18 19 20 21 22 23 24 25 26 27 28 29 30 31 32 33 34 35 36", "251期:开奖结果:88-88-88-88-88-88特88准" }, "01 02 03 04 05 06 07 08 09 10 11 12 13 14 15 16 17 18 19 20 21 22 23 24 25 26 27 28 29 30 31 32 33 34 35 36" };
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void MustNotSynthesizeOrMisattributeAResult(string id, string group, string ruleId,
        int issue, string[] lines, string? expected)
    {
        string? actual = RuleEngine.ExtractFinalValue(lines, issue, Rule(group, ruleId));
        Assert.True(actual == expected,
            $"{id}: expected={expected ?? "<missing>"}; actual={actual ?? "<missing>"}");
    }

    [Fact]
    public void PerRuleStrictFlagMustNotBeSilentlyIgnored()
    {
        Assert.True(Rule("嫣然心水", "小骚货").StrictIssueBlock);
    }

    [Fact]
    public void SameImageCannotBeClaimedByIndependentTemplatesWithoutAnExplicitSharingContract()
    {
        var a = new VisualTemplateDefinition("A", new[] { "规则A" }, new string('0', 256), 0.2, 0.4);
        var b = new VisualTemplateDefinition("B", new[] { "规则B" }, new string('1', 256), 0.5, 0.7);
        var matches = new[] {
            new VisualTemplateMatch("same.png", a, 1, 0),
            new VisualTemplateMatch("same.png", b, 2, 0)
        };
        Assert.False(VisualTemplateMatcher.HasUsableMatches(matches, new[] { a, b },
            new HashSet<string> { "规则A", "规则B" }));
    }

    [Fact]
    public void OneExplicitSharedTemplateCanCoverBothDeclaredRules()
    {
        var shared = new VisualTemplateDefinition("共享", new[] { "肖", "尾" }, new string('0', 256), 0.2, 0.4);
        Assert.True(VisualTemplateMatcher.HasUsableMatches(
            new[] { new VisualTemplateMatch("same.png", shared, 1, 0) }, new[] { shared },
            new HashSet<string> { "肖", "尾" }));
    }

    [Fact]
    public void ParserMustNotUseAnUnrelatedHorizontalColumnToCompleteSixNumbers()
    {
        string response = JsonSerializer.Serialize(new {
            Response = new { TextDetections = new[] {
                new { DetectedText = "251期 青苹果 杀六码 01 02 03 04 05",
                    ItemPolygon = new { X = 0, Y = 10, Width = 400, Height = 20 } },
                new { DetectedText = "旁栏参考：06",
                    ItemPolygon = new { X = 900, Y = 10, Width = 100, Height = 20 } }
            }}
        });
        IReadOnlyList<string> lines = TencentOcrClient.ParseLines(response);
        Assert.Null(RuleEngine.ExtractFinalValue(lines, 251, Rule("嫣然心水", "青苹果")));
    }

    [Fact]
    public void RetrySubsetMustNotTurnASharedFolderIntoAnExclusiveIdentity()
    {
        OcrRule[] full = RuleCatalog.Load(Path.Combine(
            ResultFilePaths.ConfigurationDirectory(AppContext.BaseDirectory), "嫣然心水.json"))
            .Where(rule => rule.Folder == "君君").ToArray();
        OcrRule tail = full.Single(rule => rule.Id == "君军两尾");
        string path = Path.Combine("C:\\图片", "嫣然心水", "君君", "a.jpg");
        string[] lines = { "君君", "251期 杀一肖 兔鸡" };
        Assert.DoesNotContain(tail, RuleEngine.FindMatches(path, lines, full));
        Assert.DoesNotContain(tail, RuleEngine.FindMatches(path, lines, new[] { tail }));
    }

    [Fact]
    public async Task InvalidSingleZodiacFromSavedTextMustNotBeWrittenAsSuccess()
    {
        using var temp = new TempFiles();
        string target = Path.Combine(temp.Root, "251期-肖.txt");
        string config = await temp.CreateDistributionConfig();
        await File.WriteAllTextAsync(target, "其他已有数据\n");
        try {
            await ResultDistributor.DistributeAsync("嫣然心水", 251,
                new[] { "鸡狗 南国挽心" }, temp.Root, config);
        } catch (OcrException) { }
        Assert.DoesNotContain("鸡狗 南国挽心", await File.ReadAllLinesAsync(target));
    }

    [Fact]
    public async Task ExplicitConflictMustNotLeaveAnOwnedOldValueAsAnUnqualifiedSuccess()
    {
        using var temp = new TempFiles();
        string target = Path.Combine(temp.Root, "251期-肖.txt");
        string config = await temp.CreateDistributionConfig();
        await File.WriteAllTextAsync(target, "其他已有数据\n");
        await ResultDistributor.DistributeAsync("嫣然心水", 251,
            new[] { "鸡 南国挽心" }, temp.Root, config);
        await ResultDistributor.DistributeAsync("嫣然心水", 251,
            new[] { "缺失（同一期结果冲突） 南国挽心" }, temp.Root, config);
        Assert.DoesNotContain("鸡 南国挽心", await File.ReadAllLinesAsync(target));
    }

    [Fact]
    public async Task CloudResponseMustNotBeReboundToAReplacementImage()
    {
        using var temp = new TempFiles();
        string path = Path.Combine(temp.Root, "sample.png");
        await File.WriteAllBytesAsync(path, new byte[] { 1, 2, 3 });
        using var handler = new ReplacingResponseHandler(path);
        using var http = new HttpClient(handler);
        var client = new TencentOcrClient(new OcrCredential(OcrProvider.Tencent,
            "test", "TEST_ONLY_ID", "TEST_ONLY_KEY"), http);
        try {
            IReadOnlyList<string> lines = await client.RecognizeAsync(path);
            CloudOcrCacheStore.SaveEntry(temp.Root, "嫣然心水", 251, path, lines, path);
        } catch (OcrException) { }
        Assert.Empty(CloudOcrCacheStore.Load(temp.Root, "嫣然心水", 251));
    }

    private sealed class ReplacingResponseHandler(string imagePath) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            string body = await request.Content!.ReadAsStringAsync(cancellationToken);
            using JsonDocument payload = JsonDocument.Parse(body);
            Assert.Equal("AQID", payload.RootElement.GetProperty("ImageBase64").GetString());
            await File.WriteAllBytesAsync(imagePath, new byte[] { 4, 5, 6 }, cancellationToken);
            string json = JsonSerializer.Serialize(new {
                Response = new { TextDetections = new[] { new { DetectedText = "251期 南国挽心 鸡" } } }
            });
            return new HttpResponseMessage(HttpStatusCode.OK) {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
        }
    }

    private sealed class TempFiles : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "ns-reaudit-" + Guid.NewGuid().ToString("N"));
        public TempFiles() => Directory.CreateDirectory(Root);
        public async Task<string> CreateDistributionConfig()
        {
            string path = Path.Combine(Root, "test-distribution.json");
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new {
                targetFile = "{issue}期-肖.txt",
                sources = new[] { new { sourceGroup = "嫣然心水", labels = new[] { "南国挽心" } } }
            }));
            return path;
        }
        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}
