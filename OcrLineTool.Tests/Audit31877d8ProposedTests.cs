using System.Text.Json;
using OcrLineTool;
using Xunit;

namespace OcrLineTool.Tests;

// Acceptance coverage for the 31877d8 residual review (D01-D05).
[Trait("Category", "ReauditAccuracy")]
public sealed class Audit31877d8ProposedTests
{
    private static OcrRule Rule(string group, string id) => RuleCatalog.Load(Path.Combine(
        ResultFilePaths.ConfigurationDirectory(AppContext.BaseDirectory), group + ".json"))
        .Single(rule => rule.Id == id);

    // ---------- D01: boundaries stay boundaries ----------

    [Fact]
    public void BareIssueAfterOpeningWithLaterIssueIsNotData()
    {
        OcrRule rule = Rule("嫣然心水", "青苹果");

        Assert.Null(RuleEngine.ExtractFinalValue(
            ["2301期 青苹果 杀六码 01 03 04 05 开？？", "2302", "2303期"], 2301, rule));
    }

    [Theory]
    [InlineData("【2302】")]
    [InlineData("[2302]")]
    [InlineData("2302")]
    public void BracketedBareIssueIsStillABoundary(string bareLine)
    {
        OcrRule rule = Rule("嫣然心水", "青苹果");

        Assert.Null(RuleEngine.ExtractFinalValue(
            ["2301期 青苹果 杀六码 01 03 04 05", bareLine], 2301, rule));
    }

    [Fact]
    public void ReviewedCompactEqualSelectedIssueIsNotData()
    {
        OcrRule rule = Rule("新澳六合彩资料", "杀料");
        string[] lines =
        [
            "杀料网绝杀10码",
            "01 03",
            "2302期 绝杀:04 05 06 07 08 09",
            "开？？",
            "2302"
        ];

        Assert.Equal(RuleExtractionStatus.Missing,
            RuleEngine.ExtractFinalResult(lines, 2302, rule).Status);
    }

    // ---------- D02: ownerless continuations keep counter-evidence ----------

    [Theory]
    [InlineData("07 08【01 02 03 04 05 06】")]
    [InlineData("【01 02 03 04 05 06】7")]
    public void MultilineOwnerlessContinuationKeepsCounterEvidence(string continuation)
    {
        OcrRule rule = Rule("嫣然心水", "青苹果");

        Assert.Equal(RuleExtractionStatus.Missing, RuleEngine.ExtractFinalResult(
            ["251期 青苹果 杀六码", continuation], 251, rule).Status);
    }

    [Fact]
    public void MultilineContinuationCounterEvidenceFailsWithPositionedEvidence()
    {
        OcrRule rule = Rule("嫣然心水", "青苹果");
        OcrLineEvidence[] items =
        [
            new("251期 青苹果 杀六码", new OcrBox(0, 0, 300, 20), 0.99, "tencent", "main"),
            new("07 08【01 02 03 04 05 06】", new OcrBox(0, 30, 300, 20), 0.99, "tencent", "main")
        ];
        var evidence = new OcrEvidence("source", "input", "source-hash", "input-hash", "tencent",
            OcrEvidenceLayout.Partition(items));

        Assert.Equal(RuleExtractionStatus.Missing,
            RuleEngine.ExtractFinalResult(evidence, 251, rule).Status);
    }

    [Fact]
    public void NoBracketTrailingUnknownCharIsRejected()
    {
        OcrRule rule = Rule("嫣然心水", "青苹果");

        Assert.Equal(RuleExtractionStatus.Missing, RuleEngine.ExtractFinalResult(
            ["251期 青苹果 杀六码 01 02 03 04 05 06X"], 251, rule).Status);
    }

    [Fact]
    public void ContinuationControlsStillWork()
    {
        OcrRule rule = Rule("嫣然心水", "青苹果");

        Assert.Equal("01 02 03 04 05 06", RuleEngine.ExtractFinalValue(
            ["251期 青苹果 杀六码【01 02 03 04 05 06】"], 251, rule));
        Assert.Equal("01 03 04 05 10 02", RuleEngine.ExtractFinalValue(
            ["251期 青苹果 杀六码 01 03 04 05", "10 02"], 251, rule));
    }

    // ---------- D03: strict containers use the shared checker ----------

    [Fact]
    public void StrictTwoIncompleteContainersCannotBeSummed()
    {
        OcrRule rule = Rule("新澳高级会员", "会员特供杀十码");
        Assert.True(rule.StrictIssueBlock);

        Assert.Equal(RuleExtractionStatus.Missing, RuleEngine.ExtractFinalResult(
            ["会员特供绝杀10码", "251期 绝杀【01 02 03 04 05】【06 07 08 09 10】"],
            251, rule).Status);
    }

    [Fact]
    public void StrictTwoDifferentCompleteContainersAreConflict()
    {
        OcrRule rule = Rule("新澳高级会员", "会员特供杀十码");

        Assert.Equal(RuleExtractionStatus.Conflict, RuleEngine.ExtractFinalResult(
            ["会员特供绝杀10码",
             "251期 绝杀【01 02 03 04 05 06 07 08 09 10】【11 12 13 14 15 16 17 18 19 20】"],
            251, rule).Status);
    }

    [Fact]
    public void StrictContainerConflictSuppressesPriorSuccess()
    {
        OcrRule rule = Rule("新澳高级会员", "会员特供杀十码");
        string[] values = ["05 07 10 16 26 33 35 37 42 49"];
        var result = new ResultValues(StringComparer.Ordinal);
        var ledger = new ResultEvidenceLedger();
        string hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData([1, 2, 3]));
        var evidence = new OcrEvidence("source", "input", hash, hash, "test",
            [new OcrLineEvidence("251期 绝杀:05 07 10 16 26 33 35 37 42 49", null, 0.99, "test", "main")]);
        ledger.Observe(result, rule, values[0], evidence);

        RuleExtractionResult conflict = RuleEngine.ExtractFinalResult(
            ["会员特供绝杀10码",
             "251期 绝杀【01 02 03 04 05 06 07 08 09 10】【11 12 13 14 15 16 17 18 19 20】"],
            251, rule);
        Assert.Equal(RuleExtractionStatus.Conflict, conflict.Status);
        ledger.ObserveConflict(result, rule, evidence);

        Assert.False(result.ContainsKey(rule.Id));
        Assert.True(ResultValues.IsConflict(result, rule.Id));
    }

    [Fact]
    public void StrictOrdinaryNumbersStillWork()
    {
        OcrRule rule = Rule("新澳高级会员", "会员特供杀十码");

        Assert.Equal("05 07 10 16 26 33 35 37 42 49", RuleEngine.ExtractFinalValue(
            ["会员特供绝杀10码", "251期 绝杀:05 07 10 16 26 33 35 37 42 49"], 251, rule));
    }

    // ---------- D04: following issue does not authorize surplus ----------

    [Theory]
    [InlineData("11 12")]
    [InlineData("01 02")]
    public void SurplusAfterOpeningWithFollowingIssueIsNotData(string surplus)
    {
        OcrRule rule = Rule("新澳六合彩资料", "杀料");
        string[] lines =
        [
            "杀料网绝杀10码",
            "01 02",
            "251期 绝杀:03 04 05 06 07 08",
            "09 10",
            "开？？",
            surplus,
            "250期"
        ];

        Assert.Equal(RuleExtractionStatus.Missing,
            RuleEngine.ExtractFinalResult(lines, 251, rule).Status);
    }

    [Fact]
    public void SurplusAfterOpeningFailsWithPositionedEvidence()
    {
        OcrRule rule = Rule("新澳六合彩资料", "杀料");
        IReadOnlyList<OcrLineEvidence> items = OcrEvidenceLayout.Partition(
        [
            new("杀料网绝杀10码", new OcrBox(0, 0, 200, 20), 0.99, "test", "main"),
            new("01 02", new OcrBox(0, 30, 100, 20), 0.99, "test", "main"),
            new("251期 绝杀:03 04 05 06 07 08", new OcrBox(0, 60, 300, 20), 0.99, "test", "main"),
            new("09 10", new OcrBox(0, 90, 100, 20), 0.99, "test", "main"),
            new("开？？", new OcrBox(0, 120, 100, 20), 0.99, "test", "main"),
            new("11 12", new OcrBox(0, 150, 100, 20), 0.99, "test", "main"),
            new("250期", new OcrBox(0, 180, 100, 20), 0.99, "test", "main")
        ]);
        var evidence = new OcrEvidence("source", "input", "source-hash", "input-hash", "test", items);

        Assert.Equal(RuleExtractionStatus.Missing,
            RuleEngine.ExtractFinalResult(evidence, 251, rule).Status);
    }

    [Fact]
    public void WrappedReviewedCardStillExcludesTheNextCardsLeadingRow()
    {
        OcrRule rule = Rule("新澳六合彩资料", "内幕");
        string[] card =
        [
            rule.Keyword,
            "02030405060709101213141617",
            "246期",
            "18202223242627282933343536",
            "开？？",
            "37383940414246474849",
            "02030405060709111213141520",
            "245期",
            "010203040506070809101112131415161718192021222324252627282930313233343536",
            "牛18错"
        ];

        Assert.Equal(
            "02 03 04 05 06 07 09 10 12 13 14 16 17 18 20 22 23 24 26 27 28 29 33 34 35 36 37 38 39 40 41 42 46 47 48 49",
            RuleEngine.ExtractFinalValue(card, 246, rule));
    }

    // ---------- D05: header stack cannot bridge body columns ----------

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(6)]
    public void HeaderStackCannotChangeBodyColumnOwnership(int headerCount)
    {
        OcrRule rule = Rule("嫣然心水", "青苹果");
        int[] headerWidths = [1000, 880, 800, 550, 500, 450];
        var pieces = new List<(string Text, OcrBox? Box)>();
        for (int index = 0; index < headerCount; index++)
            pieces.Add(($"标题{index + 1}", new OcrBox(0, index * 20, headerWidths[index], 20)));
        pieces.Add(("251期 青苹果 杀六码 01 02 03 04 05", new OcrBox(0, 150, 350, 20)));
        pieces.Add(("06", new OcrBox(600, 190, 20, 20)));

        IReadOnlyList<OcrLineEvidence> items = TencentOcrClient.ParseEvidenceItems(TencentJson(pieces));
        var evidence = new OcrEvidence("source", "input", "source-hash", "input-hash", "test", items);

        string bodyRegion = items.Single(item => item.Text == "251期 青苹果 杀六码 01 02 03 04 05").RegionId;
        string tailRegion = items.Single(item => item.Text == "06").RegionId;
        Assert.NotEqual(bodyRegion, tailRegion);
        Assert.Equal(RuleExtractionStatus.Missing,
            RuleEngine.ExtractFinalResult(evidence, 251, rule).Status);
    }

    // ---------- helpers ----------

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
