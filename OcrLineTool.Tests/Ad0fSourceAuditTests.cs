using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using OcrLineTool;
using Xunit;

namespace OcrLineTool.Tests;

[Trait("Category", "ReauditAccuracy")]
public sealed class Ad0fSourceAuditTests
{
    private static OcrRule Rule(string group, string id) => RuleCatalog.Load(Path.Combine(
        ResultFilePaths.ConfigurationDirectory(AppContext.BaseDirectory), group + ".json"))
        .Single(rule => rule.Id == id);

    public static IEnumerable<object[]> UnsafeSamples()
    {
        yield return ["F01-wrapped-previous-row", "新澳六合彩资料", "杀料", 251,
            new[] { "杀料网绝杀10码", "250期 绝杀:03 04 05 06 07 08 09 10", "11 12", "251期 绝杀:13 14 15 16 17 18 19 20" }];
        yield return ["F03-inline-unowned", "嫣然心水", "青苹果", 251,
            new[] { "251期 青苹果 杀六码 01 02 03 04 05 其他栏目06" }];
        yield return ["F03-foreign-complete-bracket", "嫣然心水", "青苹果", 251,
            new[] { "251期 青苹果 杀六码 待更新", "其他栏目【01 02 03 04 05 06】" }];
        yield return ["F03-heading-then-bare-numbers", "嫣然心水", "蓝色", 251,
            new[] { "251期 特码开在", Numbers(Enumerable.Range(1, 34)), "旁栏", "35 36", "250期 特码开在" }];
        yield return ["F04-target-author-after-peer", "嫣然心水", "南国挽心", 251,
            new[] { "251期 陌上花 鸡 南国挽心 待更新" }];
        yield return ["F06-invalid-suffix-after-four-heads", "新澳高手", "亚太一头", 251,
            new[] { "251期 亚太地区四头：01239" }];
        yield return ["F08-two-tail-fields", "嫣然心水", "凌志", 251,
            new[] { "251期 凌志 杀一尾【1尾】【2尾】" }];
        yield return ["F08-two-sum-values", "嫣然心水", "青玉", 251,
            new[] { "251期 青玉 杀一合 03合 04合" }];
        yield return ["F11-directional-bare-next-issue", "嫣然心水", "蓝色", 1001,
            new[] { "1001期 特码开在", Numbers(Enumerable.Range(1, 36).Where(n => n != 2 && n != 10)), "1002" }];
        yield return ["F11-bare-issue-outside-delta-ten", "嫣然心水", "青苹果", 1001,
            new[] { "1001期 青苹果 杀六码 01 03 04 05", "1012" }];
    }

    [Theory]
    [MemberData(nameof(UnsafeSamples))]
    public async Task UnsafeCandidatesDoNotBecomeRestorableSuccess(
        string caseId, string group, string id, int issue, string[] lines)
    {
        using var temp = new Fixture(group);
        OcrRule rule = Rule(group, id);
        IReadOnlyList<OcrLineEvidence> items = TencentOcrClient.ParseEvidenceItems(
            TencentJson(lines.Select((text, index) =>
                (text, (OcrBox?)new OcrBox(0, index * 40, Math.Max(30, text.Length * 10), 20)))));
        OcrEvidence evidence = OcrEvidence.FromCapturedBytes(temp.Image, temp.Bytes, "tencent", items);
        RecognitionStateLoad restored = await RoundTrip(temp, rule, issue, evidence);
        Assert.False(restored.Values.ContainsKey(rule.Id),
            caseId + ": unsafe candidate survived parser -> extraction -> state round trip");
    }

    [Theory]
    [InlineData("tencent")]
    [InlineData("baidu")]
    public async Task UnknownGeometryCannotReenableTheMouseCowShortFragment(string provider)
    {
        using var temp = new Fixture("嫣然心水");
        OcrRule rule = Rule("嫣然心水", "小灰灰一肖");
        string[] texts = ["251期 小灰灰绝杀一肖 鼠", "牛"];
        IReadOnlyList<OcrLineEvidence> items = provider == "tencent"
            ? TencentOcrClient.ParseEvidenceItems(TencentJson(texts.Select(t => (t, (OcrBox?)null))))
            : BaiduOcrClient.ParseEvidenceItems(JsonSerializer.Serialize(new
            {
                words_result = texts.Select(text => new { words = text }).ToArray()
            }));
        OcrEvidence evidence = OcrEvidence.FromCapturedBytes(temp.Image, temp.Bytes, provider, items);
        RecognitionStateLoad restored = await RoundTrip(temp, rule, 251, evidence);
        Assert.False(restored.Values.ContainsKey(rule.Id));
    }

    [Fact]
    public async Task AFullWidthBannerCannotMergeTwoBodyColumns()
    {
        using var temp = new Fixture("嫣然心水");
        OcrRule rule = Rule("嫣然心水", "青苹果");
        (string, OcrBox?)[] pieces =
        [
            ("青苹果资料", new OcrBox(0, 0, 1000, 20)),
            ("251期 青苹果 杀六码 01 02 03 04 05", new OcrBox(0, 40, 400, 20)),
            ("06", new OcrBox(900, 70, 20, 20))
        ];
        IReadOnlyList<OcrLineEvidence> items = TencentOcrClient.ParseEvidenceItems(TencentJson(pieces));
        OcrEvidence evidence = OcrEvidence.FromCapturedBytes(temp.Image, temp.Bytes, "tencent", items);
        RecognitionStateLoad restored = await RoundTrip(temp, rule, 251, evidence);
        Assert.False(restored.Values.ContainsKey(rule.Id));
    }

    [Theory]
    [InlineData("嫣然心水", "青苹果", "251期 青苹果 杀六码 01 02 03 04 05 06", "01 02 03 04 05 06")]
    [InlineData("嫣然心水", "南国挽心", "251期 南国挽心 鸡", "鸡")]
    [InlineData("嫣然心水", "凌志", "251期 凌志 杀一尾【1尾】", "1尾")]
    [InlineData("新澳高手", "亚太一头", "251期 亚太地区四头：0123", "4头")]
    public async Task NormalCompleteFieldsRemainRestorable(
        string group, string id, string text, string expected)
    {
        using var temp = new Fixture(group);
        OcrRule rule = Rule(group, id);
        var items = TencentOcrClient.ParseEvidenceItems(TencentJson([(text, (OcrBox?)new OcrBox(0, 0, 500, 20))]));
        var evidence = OcrEvidence.FromCapturedBytes(temp.Image, temp.Bytes, "tencent", items);
        RecognitionStateLoad restored = await RoundTrip(temp, rule, 251, evidence);
        Assert.True(restored.Values.TryGetValue(rule.Id, out string? actual));
        Assert.Equal(expected, actual);
    }

    private static async Task<RecognitionStateLoad> RoundTrip(
        Fixture temp, OcrRule rule, int issue, OcrEvidence evidence)
    {
        var values = new ResultValues(StringComparer.Ordinal);
        var ledger = new ResultEvidenceLedger();
        RuleExtractionResult result = RuleEngine.ExtractFinalResult(evidence, issue, rule);
        if (result.Status == RuleExtractionStatus.Success)
            ledger.Observe(values, rule, result.Value!, evidence);
        else if (result.Status == RuleExtractionStatus.Conflict)
            ledger.ObserveConflict(values, rule, evidence);
        using (RecognitionStateStore.LockCurrentEvidenceForPublish([rule], values, ledger))
            await RecognitionStateStore.SaveAsync(temp.Root, temp.Group, issue, [rule], values, ledger);
        return RecognitionStateStore.Load(temp.Root, temp.Group, issue, [rule]);
    }

    private static string TencentJson(IEnumerable<(string Text, OcrBox? Box)> pieces)
    {
        object[] detections = pieces.Select(piece =>
        {
            var item = new Dictionary<string, object?>
            {
                ["DetectedText"] = piece.Text,
                ["Confidence"] = 99
            };
            if (piece.Box is OcrBox box)
                item["ItemPolygon"] = new { box.X, box.Y, box.Width, box.Height };
            return (object)item;
        }).ToArray();
        return JsonSerializer.Serialize(new { Response = new { TextDetections = detections } });
    }

    private static string Numbers(IEnumerable<int> numbers) =>
        string.Join(' ', numbers.Select(n => n.ToString("00")));

    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "ns-ad0f-review-" + Guid.NewGuid().ToString("N"));
        public string Group { get; }
        public string Image { get; }
        public byte[] Bytes { get; } = [1, 2, 3, 4];
        public Fixture(string group)
        {
            Group = Path.Combine(Root, group);
            Directory.CreateDirectory(Group);
            Image = Path.Combine(Group, "source.png");
            File.WriteAllBytes(Image, Bytes);
        }
        public void Dispose()
        {
            try { Directory.Delete(Root, recursive: true); } catch (IOException) { }
        }
    }
}
