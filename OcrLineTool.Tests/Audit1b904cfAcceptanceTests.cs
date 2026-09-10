using System.Text.Json;
using OcrLineTool;
using Xunit;

namespace OcrLineTool.Tests;

// Acceptance coverage for the 1b904cf residual review (C01-C05).
[Trait("Category", "ReauditAccuracy")]
public sealed class Audit1b904cfAcceptanceTests
{
    private static OcrRule Rule(string group, string id) => RuleCatalog.Load(Path.Combine(
        ResultFilePaths.ConfigurationDirectory(AppContext.BaseDirectory), group + ".json"))
        .Single(rule => rule.Id == id);

    // ---------- C01: issue classification and compatibility ----------

    [Fact]
    public void RepeatedSelectedBareIssueIsAlsoABoundary()
    {
        OcrRule rule = Rule("嫣然心水", "青苹果");

        Assert.Null(RuleEngine.ExtractFinalValue(
            ["2302期 青苹果 杀六码 01 03 04 05", "2302"], 2302, rule));
    }

    [Fact]
    public void OpeningTextAloneCannotAuthorizeAnotherBareIssue()
    {
        OcrRule rule = Rule("嫣然心水", "青苹果");

        Assert.Null(RuleEngine.ExtractFinalValue(
            ["2301期 青苹果 杀六码 01 03 04 05 开？？", "2302"], 2301, rule));
    }

    [Fact]
    public void UnrelatedBareRowCannotVetoAnOrdinaryCompleteTarget()
    {
        OcrRule rule = Rule("新澳六合彩资料", "杀料");

        Assert.Equal("01 02 03 04 05 06 07 08 09 10", RuleEngine.ExtractFinalValue(
            ["杀料网绝杀10码", "251期 绝杀:01 02 03 04 05 06 07 08 09 10", "250期", "1002"],
            251, rule));
    }

    [Fact]
    public void ExplicitSpacedNumberContinuationStillWorks()
    {
        OcrRule rule = Rule("嫣然心水", "青苹果");

        Assert.Equal("01 03 04 05 10 02", RuleEngine.ExtractFinalValue(
            ["251期 青苹果 杀六码 01 03 04 05", "10 02"], 251, rule));
    }

    [Fact]
    public void BareBoundaryWithoutOpeningStillStopsContinuation()
    {
        OcrRule rule = Rule("嫣然心水", "青苹果");

        Assert.Null(RuleEngine.ExtractFinalValue(
            ["251期 青苹果 杀六码 01 03 04 05", "2002"], 251, rule));
    }

    // ---------- C02: raw field counter-evidence ----------

    [Theory]
    [InlineData("251期 青苹果 杀六码 07 08【01 02 03 04 05 06】")]
    [InlineData("251期 青苹果 杀六码【01 02 03 04 05 06】7")]
    [InlineData("251期 青苹果 杀六码【01 02 03 04 05 06】X")]
    [InlineData("251期 青苹果 杀六码 其他栏目杀码【01 02 03 04 05 06】")]
    [InlineData("251期 青苹果 杀六码【01 02 03】其他栏目【04 05 06】")]
    public void RawFieldCounterEvidenceCannotBeDiscarded(string line)
    {
        OcrRule rule = Rule("嫣然心水", "青苹果");

        Assert.Equal(RuleExtractionStatus.Missing,
            RuleEngine.ExtractFinalResult([line], 251, rule).Status);
    }

    [Fact]
    public void SingleCompleteOwnedBracketStillWorks()
    {
        OcrRule rule = Rule("嫣然心水", "青苹果");

        Assert.Equal("01 02 03 04 05 06", RuleEngine.ExtractFinalValue(
            ["251期 青苹果 杀六码【01 02 03 04 05 06】"], 251, rule));
    }

    [Fact]
    public void ExistingTrailingPairRejectionIsPreserved()
    {
        OcrRule rule = Rule("嫣然心水", "青苹果");

        Assert.Null(RuleEngine.ExtractFinalValue(
            ["251期 青苹果 杀六码【01 02 03 04 05 06】07 08"], 251, rule));
    }

    // ---------- C03: conflicting complete answers ----------

    [Fact]
    public void TwoDifferentCompleteBracketsAreConflictNotMissing()
    {
        OcrRule rule = Rule("嫣然心水", "青苹果");

        Assert.Equal(RuleExtractionStatus.Conflict, RuleEngine.ExtractFinalResult(
            ["251期 青苹果 杀六码【01 02 03 04 05 06】【07 08 09 10 11 12】"],
            251, rule).Status);
    }

    [Fact]
    public void BracketConflictMustReachTheLedgerAndSuppressPriorSuccess()
    {
        OcrRule rule = Rule("嫣然心水", "青苹果");
        string[] conflicting =
            ["251期 青苹果 杀六码【01 02 03 04 05 06】【07 08 09 10 11 12】"];
        string hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData([1, 2, 3, 4]));
        var evidence = new OcrEvidence(
            "source.png", "source.png", hash, hash, "test",
            [new OcrLineEvidence("251期 青苹果 杀六码 01 02 03 04 05 06", null, 0.99, "test", "main")]);
        var values = new ResultValues(StringComparer.Ordinal);
        var ledger = new ResultEvidenceLedger();
        ledger.Observe(values, rule, "01 02 03 04 05 06", evidence);
        Assert.True(values.ContainsKey(rule.Id));

        RuleExtractionResult result = RuleEngine.ExtractFinalResult(conflicting, 251, rule);
        Assert.Equal(RuleExtractionStatus.Conflict, result.Status);
        ledger.ObserveConflict(values, rule, evidence);

        Assert.False(values.ContainsKey(rule.Id));
        Assert.True(ResultValues.IsConflict(values, rule.Id));
    }

    [Fact]
    public async Task BracketConflictSurvivesSaveAndRestore()
    {
        OcrRule rule = Rule("嫣然心水", "青苹果");
        string root = Path.Combine(Path.GetTempPath(), "ns-conflict-" + Guid.NewGuid().ToString("N"));
        string groupDirectory = Path.Combine(root, "嫣然心水");
        Directory.CreateDirectory(groupDirectory);
        try
        {
            string source = Path.Combine(groupDirectory, "source.png");
            await File.WriteAllBytesAsync(source, [5, 5, 5, 5]);
            string hash = LocalOcrIdentity.Image(source);
            var evidence = new OcrEvidence(
                source, source, hash, hash, "test",
                [new OcrLineEvidence("251期 青苹果 杀六码 01 02 03 04 05 06", null, 0.99, "test", "main")]);
            var values = new ResultValues(StringComparer.Ordinal);
            var ledger = new ResultEvidenceLedger();
            ledger.Observe(values, rule, "01 02 03 04 05 06", evidence);

            string[] conflicting =
                ["251期 青苹果 杀六码【01 02 03 04 05 06】【07 08 09 10 11 12】"];
            RuleExtractionResult result = RuleEngine.ExtractFinalResult(conflicting, 251, rule);
            Assert.Equal(RuleExtractionStatus.Conflict, result.Status);
            ledger.ObserveConflict(values, rule, evidence);

            await RecognitionStateStore.SaveAsync(root, groupDirectory, 251, [rule], values, ledger);
            RecognitionStateLoad restored = RecognitionStateStore.Load(root, groupDirectory, 251, [rule]);

            Assert.False(restored.Values.ContainsKey(rule.Id));
            Assert.True(ResultValues.IsConflict(restored.Values, rule.Id));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    // ---------- C04: no count-based truncation after the opening marker ----------

    [Theory]
    [InlineData("11 12")]
    [InlineData("01 02")]
    public void OpeningSeparatorDoesNotHideSurplusAtEndOfCurrentField(string surplus)
    {
        OcrRule rule = Rule("新澳六合彩资料", "杀料");
        string[] lines =
        [
            "杀料网绝杀10码",
            "01 02",
            "251期 绝杀:03 04 05 06 07 08",
            "09 10",
            "开？？",
            surplus
        ];

        Assert.Equal(RuleExtractionStatus.Missing,
            RuleEngine.ExtractFinalResult(lines, 251, rule).Status);
    }

    [Fact]
    public void OpeningSeparatorSurplusAlsoFailsWithPositionedEvidence()
    {
        OcrRule rule = Rule("新澳六合彩资料", "杀料");
        IReadOnlyList<OcrLineEvidence> items = OcrEvidenceLayout.Partition(
        [
            new("杀料网绝杀10码", new OcrBox(0, 0, 200, 20), 0.99, "test", "main"),
            new("01 02", new OcrBox(0, 30, 100, 20), 0.99, "test", "main"),
            new("251期 绝杀:03 04 05 06 07 08", new OcrBox(0, 60, 300, 20), 0.99, "test", "main"),
            new("09 10", new OcrBox(0, 90, 100, 20), 0.99, "test", "main"),
            new("开？？", new OcrBox(0, 120, 100, 20), 0.99, "test", "main"),
            new("11 12", new OcrBox(0, 150, 100, 20), 0.99, "test", "main")
        ]);
        var evidence = new OcrEvidence("source", "input", "source-hash", "input-hash", "test", items);

        Assert.Equal(RuleExtractionStatus.Missing,
            RuleEngine.ExtractFinalResult(evidence, 251, rule).Status);
    }

    [Fact]
    public void NextCardsLeadingRowBeforeItsIssueIsStillExcluded()
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

    // ---------- C05: header stack cannot widen the body columns ----------

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HeaderStackCannotChangeBodyColumnOwnership(bool withThirdHeader)
    {
        OcrRule rule = Rule("嫣然心水", "青苹果");
        var pieces = new List<(string Text, OcrBox? Box)>
        {
            ("主标题", new OcrBox(0, 0, 1000, 20)),
            ("二级标题", new OcrBox(0, 30, 880, 20))
        };
        if (withThirdHeader)
            pieces.Add(("三级标题", new OcrBox(0, 60, 600, 20)));
        pieces.Add(("251期 青苹果 杀六码 01 02 03 04 05", new OcrBox(0, 120, 400, 20)));
        pieces.Add(("06", new OcrBox(650, 160, 20, 20)));

        IReadOnlyList<OcrLineEvidence> items = TencentOcrClient.ParseEvidenceItems(TencentJson(pieces));
        var evidence = new OcrEvidence("source", "input", "source-hash", "input-hash", "test", items);

        string bodyRegion = items.Single(item => item.Text == "251期 青苹果 杀六码 01 02 03 04 05").RegionId;
        string tailRegion = items.Single(item => item.Text == "06").RegionId;
        Assert.NotEqual(bodyRegion, tailRegion);
        Assert.Equal(RuleExtractionStatus.Missing,
            RuleEngine.ExtractFinalResult(evidence, 251, rule).Status);
    }

    // ---------- extractor revision after the semantics change ----------

    [Fact]
    public async Task PreviousExtractorRevisionSuccessIsNotRestored()
    {
        string root = Path.Combine(Path.GetTempPath(), "ns-revision-" + Guid.NewGuid().ToString("N"));
        string groupDirectory = Path.Combine(root, "嫣然心水");
        Directory.CreateDirectory(groupDirectory);
        try
        {
            string source = Path.Combine(groupDirectory, "source.png");
            await File.WriteAllBytesAsync(source, [9, 9, 9, 9]);
            string hash = LocalOcrIdentity.Image(source);
            OcrRule rule = Rule("嫣然心水", "南国挽心");
            string statePath = ResultFilePaths.ForRecognitionState(root, groupDirectory, 251);
            Directory.CreateDirectory(Path.GetDirectoryName(statePath)!);
            var document = new
            {
                Version = 1,
                Group = "嫣然心水",
                Issue = 251,
                Results = new object[]
                {
                    new
                    {
                        RuleId = rule.Id,
                        RuleType = rule.Type,
                        OutputLabel = rule.OutputLabel,
                        RuleSignature = RecognitionStateStore.RuleSignature(rule),
                        Value = "鸡",
                        Status = "success",
                        SourcePath = source,
                        InputPath = source,
                        SourceHash = hash,
                        InputHash = hash,
                        ViewId = "test",
                        RegionIds = new[] { "test/main" },
                        MinimumConfidence = (double?)0.99,
                        ExtractorRevision = 1
                    }
                }
            };
            File.WriteAllText(statePath, JsonSerializer.Serialize(document));

            RecognitionStateLoad restored = RecognitionStateStore.Load(root, groupDirectory, 251, [rule]);
            Assert.False(restored.Values.ContainsKey(rule.Id));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
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
