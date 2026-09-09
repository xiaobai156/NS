using System.Net;
using System.Text;
using System.Text.Json;
using OcrLineTool;
using Xunit;

namespace OcrLineTool.Tests;

[Trait("Category", "ReauditAccuracy")]
public sealed class ResidualLifecycleRegressionTests
{
    private static OcrRule Rule(string group, string id) => RuleCatalog.Load(Path.Combine(
        ResultFilePaths.ConfigurationDirectory(AppContext.BaseDirectory), group + ".json"))
        .Single(rule => rule.Id == id);

    [Fact]
    public async Task ConflictOnlyStateSurvivesSaveLoadAndCanRevokeOwnedDistribution()
    {
        using var temp = new TempGroup("嫣然心水");
        OcrRule rule = Rule("嫣然心水", "南国挽心");
        string image = temp.File("source.png", [1, 2, 3, 4]);
        OcrEvidence chicken = OcrEvidence.FromLines(image, ["251期 南国挽心 鸡"], "primary");
        OcrEvidence dog = OcrEvidence.FromLines(image, ["251期 南国挽心 狗"], "fallback");
        var values = new ResultValues(StringComparer.Ordinal);
        var ledger = new ResultEvidenceLedger();
        ledger.Observe(values, rule, "鸡", chicken);
        ledger.Observe(values, rule, "狗", dog);
        Assert.True(ResultValues.IsConflict(values, rule.Id));

        await RecognitionStateStore.SaveAsync(
            temp.Root, temp.GroupDirectory, 251, [rule], values, ledger);
        RecognitionStateLoad restored = RecognitionStateStore.Load(
            temp.Root, temp.GroupDirectory, 251, [rule]);
        Assert.True(ResultValues.IsConflict(restored.Values, rule.Id));
        Assert.Equal("conflict", restored.Evidence.Records[rule.Id].Status);

        string[] conflictLines = RecognitionStateStore.BuildTrustedOutputLines(
            temp.Root, temp.GroupDirectory, 251, [rule]);
        Assert.Contains("缺失（同一期结果冲突，待核对） 南国挽心", conflictLines);

        string targetDirectory = Path.Combine(temp.Root, "target");
        Directory.CreateDirectory(targetDirectory);
        string targetPath = Path.Combine(targetDirectory, "data-251.txt");
        await File.WriteAllTextAsync(targetPath, "");
        string config = Path.Combine(temp.Root, "test分发规则.json");
        await File.WriteAllTextAsync(config, JsonSerializer.Serialize(new
        {
            targetFile = "data-{issue}.txt",
            sources = new[] { new { sourceGroup = "嫣然心水", labels = new[] { "南国挽心" } } }
        }));

        await ResultDistributor.DistributeAsync(
            temp.GroupDirectory, 251, ["鸡 南国挽心"], targetDirectory, config);
        Assert.Contains("鸡 南国挽心", await File.ReadAllTextAsync(targetPath));

        await ResultDistributor.DistributeAsync(
            temp.GroupDirectory, 251, conflictLines, targetDirectory, config);
        Assert.DoesNotContain("鸡 南国挽心", await File.ReadAllTextAsync(targetPath));
    }

    [Theory]
    [InlineData("嫣然心水", "青苹果", "01 02 03 04 05 06", "01,02,03,04,05,06 青苹果")]
    [InlineData("新澳六合彩资料", "天机阁五行", "金木水火", "土 天机阁五行")]
    [InlineData("新澳六合彩资料", "妈祖两尾", "1尾+2尾", "1尾 2尾 妈祖两尾")]
    public async Task CanonicalValuesRoundTripThroughTrustedState(
        string group, string ruleId, string canonicalValue, string expectedOutput)
    {
        using var temp = new TempGroup(group);
        OcrRule rule = Rule(group, ruleId);
        string image = temp.File("source.png", [9, 8, 7, 6]);
        OcrEvidence evidence = OcrEvidence.FromLines(image, [$"251期 {rule.Keyword}"], "test");
        var values = new ResultValues(StringComparer.Ordinal);
        var ledger = new ResultEvidenceLedger();
        ledger.Observe(values, rule, canonicalValue, evidence);

        await RecognitionStateStore.SaveAsync(
            temp.Root, temp.GroupDirectory, 251, [rule], values, ledger);
        RecognitionStateLoad restored = RecognitionStateStore.Load(
            temp.Root, temp.GroupDirectory, 251, [rule]);
        Assert.Equal(canonicalValue, restored.Values[rule.Id]);
        Assert.Contains(expectedOutput, RecognitionStateStore.BuildTrustedOutputLines(
            temp.Root, temp.GroupDirectory, 251, [rule]));
    }

    [Fact]
    public void ExistingConflictCannotBeResolvedByRetryCloudCache()
    {
        using var temp = new TempGroup("嫣然心水");
        OcrRule rule = Rule("嫣然心水", "南国挽心");
        string image = temp.File("source.png", [7, 7, 7]);
        OcrEvidence cached = OcrEvidence.FromLines(image, ["251期 南国挽心 鸡"], "cache");
        var values = new ResultValues(StringComparer.Ordinal);
        ResultValues.AddTo(values, rule.Id, "鸡");
        ResultValues.AddTo(values, rule.Id, "狗");
        Assert.True(ResultValues.IsConflict(values, rule.Id));

        Assert.False(MainForm.CanReuseRetryCloudEvidence(
            values, [rule], cached, 251));
        Assert.True(ResultValues.IsConflict(values, rule.Id));
    }

    [Fact]
    public void CloudCacheNeverRebindsOldEvidenceToAReplacedImage()
    {
        using var temp = new TempGroup("嫣然心水");
        string image = temp.File("source.jpg", [1, 2, 3, 4]);
        byte[] requestBytes = File.ReadAllBytes(image);
        OcrEvidence evidence = OcrEvidence.FromCapturedBytes(
            image, requestBytes, "tencent",
            [new("251期 南国挽心 鸡", new OcrBox(10, 10, 200, 20), 0.99, "tencent", "main")]);

        File.WriteAllBytes(image, [5, 6, 7, 8]);
        CloudOcrCacheStore.SaveEntry(temp.Root, "嫣然心水", 251, evidence);

        Assert.Empty(CloudOcrCacheStore.LoadEvidence(temp.Root, "嫣然心水", 251));
        Assert.Empty(CloudOcrCacheStore.Load(temp.Root, "嫣然心水", 251));
    }

    [Fact]
    public void StructuredCloudCacheKeepsEvidenceMetadataAndIdentity()
    {
        using var temp = new TempGroup("嫣然心水");
        string image = temp.File("source.jpg", [2, 4, 6, 8]);
        byte[] bytes = File.ReadAllBytes(image);
        OcrEvidence evidence = OcrEvidence.FromCapturedBytes(
            image, bytes, "tencent",
            [new("251期 南国挽心 鸡", new OcrBox(12, 30, 220, 22), 98.7, "tencent", "main")]);

        CloudOcrCacheStore.SaveEntry(temp.Root, "嫣然心水", 251, evidence);
        OcrEvidence restored = Assert.Single(
            CloudOcrCacheStore.LoadEvidence(temp.Root, "嫣然心水", 251)).Value;
        OcrLineEvidence item = Assert.Single(restored.Items);
        Assert.Equal(evidence.SourceHash, restored.SourceHash);
        Assert.Equal(evidence.InputHash, restored.InputHash);
        Assert.Equal(new OcrBox(12, 30, 220, 22), item.Box);
        Assert.Equal(98.7, item.Confidence);
    }

    [Fact]
    public void PrecheckKeyChangesWhenEitherSourceOrInputVersionChanges()
    {
        using var temp = new TempGroup("嫣然心水");
        string source = temp.File("source.jpg", [1, 1, 1]);
        string input = temp.File("crop.png", [2, 2, 2]);
        OcrEvidenceIdentity first = OcrEvidenceIdentity.Capture(source, input, "precheck");
        string firstKey = MainForm.CloudEvidenceKey(first);

        File.WriteAllBytes(source, [3, 3, 3]);
        OcrEvidenceIdentity changedSource = OcrEvidenceIdentity.Capture(source, input, "precheck");
        Assert.NotEqual(firstKey, MainForm.CloudEvidenceKey(changedSource));

        File.WriteAllBytes(input, [4, 4, 4]);
        OcrEvidenceIdentity changedInput = OcrEvidenceIdentity.Capture(source, input, "precheck");
        Assert.NotEqual(MainForm.CloudEvidenceKey(changedSource), MainForm.CloudEvidenceKey(changedInput));
    }

    [Fact]
    public async Task BaiduRealRequestUsesPositionalAccurateEndpointAndProbability()
    {
        using var temp = new TempGroup("嫣然心水");
        string image = temp.File("source.jpg", [1, 2, 3]);
        var handler = new BaiduHandler();
        using var http = new HttpClient(handler);
        var client = new BaiduOcrClient(
            new OcrCredential(OcrProvider.Baidu, "T", "id", "secret"), http);

        OcrEvidence evidence = await client.RecognizeEvidenceAsync(image);
        Assert.Contains("/rest/2.0/ocr/v1/accurate?", handler.OcrUri!.AbsoluteUri);
        Assert.Contains("probability=true", handler.OcrBody!);
        OcrLineEvidence item = Assert.Single(evidence.Items);
        Assert.Equal(new OcrBox(10, 20, 180, 30), item.Box);
        Assert.Equal(0.96, item.Confidence);
    }

    private sealed class BaiduHandler : HttpMessageHandler
    {
        public Uri? OcrUri { get; private set; }
        public string? OcrBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath == "/oauth/2.0/token")
            {
                return Json(new { access_token = "token", expires_in = 3600 });
            }

            OcrUri = request.RequestUri;
            OcrBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            return Json(new
            {
                words_result = new[]
                {
                    new
                    {
                        words = "251期 南国挽心 鸡",
                        location = new { left = 10, top = 20, width = 180, height = 30 },
                        probability = new { average = 0.96 }
                    }
                }
            });
        }

        private static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json")
        };
    }

    private sealed class TempGroup : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "ns-lifecycle-" + Guid.NewGuid().ToString("N"));
        public string GroupDirectory { get; }

        public TempGroup(string group)
        {
            Directory.CreateDirectory(Root);
            GroupDirectory = Path.Combine(Root, group);
            Directory.CreateDirectory(GroupDirectory);
        }

        public string File(string name, byte[] bytes)
        {
            string path = Path.Combine(GroupDirectory, name);
            System.IO.File.WriteAllBytes(path, bytes);
            return path;
        }

        public void Dispose()
        {
            try { Directory.Delete(Root, recursive: true); } catch { }
        }
    }
}
