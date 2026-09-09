using System.Text.Json;
using OcrLineTool;
using Xunit;

namespace OcrLineTool.Tests;

[Trait("Category", "ReauditAccuracy")]
public sealed class ResidualAuditRound2Tests
{
    private static OcrRule Rule(string group, string id) => RuleCatalog.Load(Path.Combine(
        ResultFilePaths.ConfigurationDirectory(AppContext.BaseDirectory), group + ".json"))
        .Single(rule => rule.Id == id);

    [Fact]
    public void DistantFourDigitBareIssueCannotRepairGenericSixNumberField()
    {
        OcrRule rule = Rule("嫣然心水", "青苹果");

        Assert.Null(RuleEngine.ExtractFinalValue(
            [
                "1001期 青苹果 杀六码 01 03 04 05",
                "1102"
            ],
            1001,
            rule));
    }

    [Fact]
    public void DirectionalTableCannotSplitDistantBareIssueIntoTwoMissingNumbers()
    {
        OcrRule rule = Rule("嫣然心水", "蓝色");
        string body = string.Join(' ', Enumerable.Range(1, 36)
            .Where(number => number is not (2 or 11))
            .Select(number => number.ToString("00")));

        Assert.Null(RuleEngine.ExtractFinalValue(
            ["1001期 特码开在", body, "1102"],
            1001,
            rule));
    }

    [Fact]
    public void ForeignGenericKillBracketCannotClaimCurrentNumberField()
    {
        OcrRule rule = Rule("嫣然心水", "青苹果");

        Assert.Null(RuleEngine.ExtractFinalValue(
            [
                "251期 青苹果 杀六码 待更新",
                "其他栏目杀码【01 02 03 04 05 06】"
            ],
            251,
            rule));
    }

    [Fact]
    public void MediumWidthBannerCannotBridgeTwoBodyColumns()
    {
        OcrRule rule = Rule("嫣然心水", "青苹果");
        (string Text, OcrBox? Box)[] pieces =
        [
            ("青苹果资料", new OcrBox(0, 0, 650, 20)),
            ("251期 青苹果 杀六码 01 02 03 04 05", new OcrBox(0, 40, 400, 20)),
            ("06", new OcrBox(700, 70, 20, 20))
        ];
        IReadOnlyList<OcrLineEvidence> items = TencentOcrClient.ParseEvidenceItems(TencentJson(pieces));
        var evidence = new OcrEvidence("source", "input", "source-hash", "input-hash", "test", items);

        Assert.Equal(
            RuleExtractionStatus.Missing,
            RuleEngine.ExtractFinalResult(evidence, 251, rule).Status);
        Assert.True(items.Select(item => item.RegionId).Distinct(StringComparer.Ordinal).Count() >= 2);
    }

    [Fact]
    public void TreasureHeadDuplicateRawValuesCannotBecomeMissingHead()
    {
        OcrRule rule = Rule("新澳六合彩资料", "藏宝头");

        Assert.Null(RuleEngine.ExtractFinalValue(
            ["251期 藏宝库无错四头付费版 今晚买:0 1 2 3 3头"],
            251,
            rule));
    }

    [Fact]
    public void TreasureHeadExactlyFourDistinctValuesStillWorks()
    {
        OcrRule rule = Rule("新澳六合彩资料", "藏宝头");

        Assert.Equal("4头", RuleEngine.ExtractFinalValue(
            ["251期 藏宝库无错四头付费版 今晚买:0 1 2 3头"],
            251,
            rule));
    }

    [Fact]
    public void OwnedCompleteBracketStillWorks()
    {
        OcrRule rule = Rule("嫣然心水", "青苹果");

        Assert.Equal("01 02 03 04 05 06", RuleEngine.ExtractFinalValue(
            ["251期 青苹果 杀六码【01 02 03 04 05 06】"],
            251,
            rule));
    }

    [Fact]
    public void ReviewedCompactPairContinuationStillWorksForThreeDigitIssue()
    {
        var rule = new OcrRule("杀料", "号码:10");

        Assert.Equal("02 15 20 26 29 31 37 43 44 45", RuleEngine.ExtractFinalValue(
            ["241期", "杀→0215202629313743", "4445", "开？？", "240期"],
            241,
            rule));
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
}
