using System.Text.Json;
using OcrLineTool;
using Xunit;

namespace OcrLineTool.Tests;

// Acceptance coverage for the 0aee0482 residual review (B01-B07).
[Trait("Category", "ReauditAccuracy")]
public sealed class LatestCommitReauditProposedTests
{
    private static OcrRule Rule(string group, string id) => RuleCatalog.Load(Path.Combine(
        ResultFilePaths.ConfigurationDirectory(AppContext.BaseDirectory), group + ".json"))
        .Single(rule => rule.Id == id);

    // ---------- B01: issue ownership ----------

    [Theory]
    [InlineData("251期 青苹果 杀六码 01 03 04 05", "2002")]
    [InlineData("251期 青苹果 杀六码 01 03 04", "201102")]
    [InlineData("251期 青苹果 杀六码 01 03 04 05", "3002")]
    public void OutOfRangeBareNumbersCannotCompleteATargetField(string issueLine, string bare)
    {
        OcrRule rule = Rule("嫣然心水", "青苹果");

        Assert.Null(RuleEngine.ExtractFinalValue([issueLine, bare], 251, rule));
    }

    [Fact]
    public void DistantBareNumberDoesNotVetoACompleteTargetField()
    {
        OcrRule rule = Rule("新澳六合彩资料", "杀料");
        string[] lines =
        [
            "杀料网绝杀10码",
            "01 02",
            "251期 绝杀:03 04 05 06 07 08 09 10",
            "250期",
            "1002"
        ];

        Assert.Equal("01 02 03 04 05 06 07 08 09 10",
            RuleEngine.ExtractFinalValue(lines, 251, rule));
    }

    [Fact]
    public void ReviewedCompactPairContinuationStillWorks()
    {
        var rule = new OcrRule("杀料", "号码:10");

        Assert.Equal("02 15 20 26 29 31 37 43 44 45", RuleEngine.ExtractFinalValue(
            ["241期", "杀→0215202629313743", "4445", "开？？", "240期"],
            241,
            rule));
    }

    [Fact]
    public void ReviewedWrapCardStillIgnoresTheNextCardsLeadingRow()
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

    // ---------- B02: raw field and brackets ----------

    [Fact]
    public void IncompleteExtraBracketCannotBeDropped()
    {
        OcrRule rule = Rule("嫣然心水", "青苹果");

        Assert.Null(RuleEngine.ExtractFinalValue(
            ["251期 青苹果 杀六码【01 02 03 04 05 06】【07】"], 251, rule));
    }

    [Fact]
    public void NumbersOutsideASingleBracketAreCounted()
    {
        OcrRule rule = Rule("嫣然心水", "青苹果");

        Assert.Null(RuleEngine.ExtractFinalValue(
            ["251期 青苹果 杀六码【01 02 03 04 05 06】07 08"], 251, rule));
    }

    [Fact]
    public void ForeignLabelAfterOwnKeywordCannotClaimTheBracket()
    {
        OcrRule rule = Rule("嫣然心水", "青苹果");

        Assert.Null(RuleEngine.ExtractFinalValue(
            ["251期 青苹果 杀六码 待更新 其他栏目【01 02 03 04 05 06】"], 251, rule));
    }

    [Fact]
    public void UnknownBracketContentCannotBeFilteredAway()
    {
        OcrRule rule = Rule("嫣然心水", "青苹果");

        Assert.Null(RuleEngine.ExtractFinalValue(
            ["251期 青苹果 杀六码【待定】【01 02 03 04 05 06】"], 251, rule));
    }

    [Fact]
    public void OwnedSingleBracketStillWorks()
    {
        OcrRule rule = Rule("嫣然心水", "青苹果");

        Assert.Equal("01 02 03 04 05 06", RuleEngine.ExtractFinalValue(
            ["251期 青苹果 杀六码【01 02 03 04 05 06】"], 251, rule));
    }

    [Fact]
    public void PureNumericContinuationStillCompletesTheSameField()
    {
        OcrRule rule = Rule("嫣然心水", "青苹果");

        Assert.Equal("01 03 04 05 10 02", RuleEngine.ExtractFinalValue(
            ["251期 青苹果 杀六码 01 03 04 05", "10 02"], 251, rule));
    }

    // ---------- B03: full field and same-issue comparison ----------

    [Fact]
    public void ExtraNumericRowInsideTheFieldRegionInvalidatesTheCount()
    {
        OcrRule rule = Rule("新澳六合彩资料", "杀料");
        string[] lines =
        [
            "杀料网绝杀10码",
            "01 02",
            "251期 绝杀:03 04 05 06 07 08",
            "09 10",
            "11 12",
            "250期"
        ];

        Assert.Null(RuleEngine.ExtractFinalValue(lines, 251, rule));
    }

    [Fact]
    public void DuplicateExtraRowInsideTheFieldRegionInvalidatesTheCount()
    {
        OcrRule rule = Rule("新澳六合彩资料", "杀料");
        string[] lines =
        [
            "杀料网绝杀10码",
            "01 02",
            "251期 绝杀:03 04 05 06 07 08",
            "09 10",
            "01 02",
            "250期"
        ];

        Assert.Null(RuleEngine.ExtractFinalValue(lines, 251, rule));
    }

    [Fact]
    public void SurroundAndOrdinarySameIssueAnswersFormAConflict()
    {
        OcrRule rule = Rule("新澳六合彩资料", "杀料");
        string[] lines =
        [
            "杀料网绝杀10码",
            "01 02",
            "251期 绝杀:03 04 05 06 07 08 09 10",
            "开？？",
            "251期 绝杀:11 12 13 14 15 16 17 18 19 20"
        ];

        Assert.Equal(RuleExtractionStatus.Conflict,
            RuleEngine.ExtractFinalResult(lines, 251, rule).Status);
    }

    // ---------- B04: peer boundary keeps original characters ----------

    [Fact]
    public void PeerBoundaryKeepsBracketsForConflictDetection()
    {
        OcrRule rule = Rule("嫣然心水", "齐天大圣");
        Assert.NotNull(rule.PeerKeywords);
        Assert.NotEmpty(rule.PeerKeywords!);

        Assert.Equal(RuleExtractionStatus.Conflict, RuleEngine.ExtractFinalResult(
            ["251期 齐天大圣【1头】【2头】"], 251, rule).Status);
        Assert.Equal(RuleExtractionStatus.Conflict, RuleEngine.ExtractFinalResult(
            ["251期 齐天大圣 杀一头【1】【2】"], 251, rule).Status);
        Assert.Equal(RuleExtractionStatus.Conflict, RuleEngine.ExtractFinalResult(
            ["251期 齐天大圣 杀一头 1头 2头"], 251, rule).Status);
        Assert.Equal(RuleExtractionStatus.Conflict, RuleEngine.ExtractFinalResult(
            ["251期 齐天大圣 杀一头：【1头】【2头】"], 251, rule).Status);
    }

    [Fact]
    public void SingleOwnedHeadStillSucceeds()
    {
        OcrRule rule = Rule("嫣然心水", "齐天大圣");

        Assert.Equal("1头", RuleEngine.ExtractFinalValue(
            ["251期 齐天大圣 杀一头【1头】"], 251, rule));
    }

    // ---------- B05: body columns vs banners ----------

    [Fact]
    public void MixedWidthHeadersCannotMergeBodyColumns()
    {
        OcrRule rule = Rule("嫣然心水", "青苹果");
        (string Text, OcrBox? Box)[] pieces =
        [
            ("青苹果资料", new OcrBox(0, 0, 1000, 20)),
            ("资料总标题", new OcrBox(0, 25, 650, 20)),
            ("251期 青苹果 杀六码 01 02 03 04 05", new OcrBox(0, 60, 400, 20)),
            ("06", new OcrBox(700, 90, 20, 20))
        ];
        IReadOnlyList<OcrLineEvidence> items = TencentOcrClient.ParseEvidenceItems(TencentJson(pieces));
        var evidence = new OcrEvidence("source", "input", "source-hash", "input-hash", "test", items);

        string bodyRegion = items.Single(item => item.Text == "251期 青苹果 杀六码 01 02 03 04 05").RegionId;
        string tailRegion = items.Single(item => item.Text == "06").RegionId;
        Assert.NotEqual(bodyRegion, tailRegion);
        Assert.Equal(RuleExtractionStatus.Missing,
            RuleEngine.ExtractFinalResult(evidence, 251, rule).Status);
    }

    // ---------- B06: persistence failure lifecycle ----------

    [Fact]
    public async Task UnpersistableSuccessIsReportedAndNotReusable()
    {
        using var temp = new TempFiles("嫣然心水");
        string source = temp.File("source.png", [1, 2, 3, 4]);
        string input = temp.File("crop.png", [4, 3, 2, 1]);
        OcrRule rule = Rule("嫣然心水", "南国挽心");
        var evidence = new OcrEvidence(
            source, input,
            LocalOcrIdentity.Image(source), LocalOcrIdentity.Image(input),
            "test",
            [new OcrLineEvidence("251期 南国挽心 鸡", null, 0.99, "test", "main")]);
        var values = new ResultValues(StringComparer.Ordinal);
        var ledger = new ResultEvidenceLedger();
        ledger.Observe(values, rule, "鸡", evidence);

        string evidenceDirectory = ResultFilePaths.RecognitionEvidenceDirectory(temp.Root, temp.Group, 251);
        Directory.CreateDirectory(Path.GetDirectoryName(evidenceDirectory)!);
        File.WriteAllText(evidenceDirectory, "blocked");

        using (RecognitionStateStore.LockCurrentEvidenceForPublish([rule], values, ledger))
        {
            RecognitionStateStore.RecognitionStateSaveOutcome outcome =
                await RecognitionStateStore.SaveAsync(
                    temp.Root, temp.GroupDirectory, 251, [rule], values, ledger);
            Assert.Contains(rule.Id, outcome.FailedSuccesses.Keys);
        }

        Assert.False(values.ContainsKey(rule.Id));
        RecognitionStateLoad restored = RecognitionStateStore.Load(
            temp.Root, temp.GroupDirectory, 251, [rule]);
        Assert.False(restored.Values.ContainsKey(rule.Id));
    }

    [Fact]
    public async Task FirstConflictCheckpointSurvivesAFailedSecondPersist()
    {
        using var temp = new TempFiles("嫣然心水");
        string source = temp.File("source.png", [1, 2, 3, 4]);
        OcrRule conflictRule = Rule("嫣然心水", "南国挽心");
        OcrRule successRule = Rule("嫣然心水", "青苹果");
        string hash = LocalOcrIdentity.Image(source);
        var conflictEvidence = new OcrEvidence(
            source, source, hash, hash, "test",
            [new OcrLineEvidence("251期 南国挽心 鸡", null, 0.99, "test", "main")]);
        var successEvidence = new OcrEvidence(
            source, source, hash, hash, "test",
            [new OcrLineEvidence("251期 青苹果 杀六码 01 02 03 04 05 06", null, 0.99, "test", "main")]);

        var values = new ResultValues(StringComparer.Ordinal);
        var ledger = new ResultEvidenceLedger();
        ledger.ObserveConflict(values, conflictRule, conflictEvidence);
        ledger.Observe(values, successRule, "01 02 03 04 05 06", successEvidence);

        // Block the evidence snapshot so the success fails to persist, then
        // abort before the second state write.
        string evidenceDirectory = ResultFilePaths.RecognitionEvidenceDirectory(temp.Root, temp.Group, 251);
        Directory.CreateDirectory(Path.GetDirectoryName(evidenceDirectory)!);
        File.WriteAllText(evidenceDirectory, "blocked");

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            RecognitionStateStore.SaveAsync(
                temp.Root, temp.GroupDirectory, 251, [conflictRule, successRule], values, ledger,
                beforeSecondPersist: () => throw new OperationCanceledException()));

        RecognitionStateLoad restored = RecognitionStateStore.Load(
            temp.Root, temp.GroupDirectory, 251, [conflictRule, successRule]);
        Assert.True(ResultValues.IsConflict(restored.Values, conflictRule.Id));
        Assert.False(restored.Values.ContainsKey(successRule.Id));
    }

    // ---------- B07: extractor revision on success evidence ----------

    [Fact]
    public void OldUnversionedSuccessIsNotRestoredButConflictKeeps()
    {
        using var temp = new TempFiles("嫣然心水");
        string source = temp.File("source.png", [7, 7, 7, 7]);
        string hash = LocalOcrIdentity.Image(source);
        OcrRule successRule = Rule("嫣然心水", "南国挽心");
        OcrRule conflictRule = Rule("嫣然心水", "青苹果");

        string statePath = ResultFilePaths.ForRecognitionState(temp.Root, temp.GroupDirectory, 251);
        Directory.CreateDirectory(Path.GetDirectoryName(statePath)!);
        var document = new
        {
            Version = 1,
            Group = temp.Group,
            Issue = 251,
            Results = new object[]
            {
                new
                {
                    RuleId = successRule.Id,
                    RuleType = successRule.Type,
                    OutputLabel = successRule.OutputLabel,
                    RuleSignature = RecognitionStateStore.RuleSignature(successRule),
                    Value = "鸡",
                    Status = "success",
                    SourcePath = source,
                    InputPath = source,
                    SourceHash = hash,
                    InputHash = hash,
                    ViewId = "test",
                    RegionIds = new[] { "test/main" },
                    MinimumConfidence = (double?)0.99
                },
                new
                {
                    RuleId = conflictRule.Id,
                    RuleType = conflictRule.Type,
                    OutputLabel = conflictRule.OutputLabel,
                    RuleSignature = RecognitionStateStore.RuleSignature(conflictRule),
                    Value = "",
                    Status = "conflict",
                    SourcePath = Path.Combine(temp.GroupDirectory, "gone.png"),
                    InputPath = Path.Combine(temp.GroupDirectory, "gone.png"),
                    SourceHash = "old",
                    InputHash = "old",
                    ViewId = "test",
                    RegionIds = new[] { "test/main" },
                    MinimumConfidence = (double?)null
                }
            }
        };
        File.WriteAllText(statePath, JsonSerializer.Serialize(document));

        RecognitionStateLoad restored = RecognitionStateStore.Load(
            temp.Root, temp.GroupDirectory, 251, [successRule, conflictRule]);

        Assert.False(restored.Values.ContainsKey(successRule.Id));
        Assert.True(ResultValues.IsConflict(restored.Values, conflictRule.Id));
    }

    [Fact]
    public async Task CurrentRevisionSuccessStillRestores()
    {
        using var temp = new TempFiles("嫣然心水");
        string source = temp.File("source.png", [5, 5, 5, 5]);
        string hash = LocalOcrIdentity.Image(source);
        OcrRule rule = Rule("嫣然心水", "南国挽心");
        var evidence = new OcrEvidence(
            source, source, hash, hash, "test",
            [new OcrLineEvidence("251期 南国挽心 鸡", null, 0.99, "test", "main")]);
        var values = new ResultValues(StringComparer.Ordinal);
        var ledger = new ResultEvidenceLedger();
        ledger.Observe(values, rule, "鸡", evidence);

        await RecognitionStateStore.SaveAsync(
            temp.Root, temp.GroupDirectory, 251, [rule], values, ledger);
        RecognitionStateLoad restored = RecognitionStateStore.Load(
            temp.Root, temp.GroupDirectory, 251, [rule]);

        Assert.Equal("鸡", restored.Values[rule.Id]);
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

    private sealed class TempFiles : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "ns-reaudit-" + Guid.NewGuid().ToString("N"));
        public string Group { get; }
        public string GroupDirectory { get; }

        public TempFiles(string group)
        {
            Group = group;
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
