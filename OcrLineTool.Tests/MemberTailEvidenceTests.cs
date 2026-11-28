using OcrLineTool;
using System.Text.Json;
using Xunit;

namespace OcrLineTool.Tests;

public sealed class MemberTailEvidenceTests
{
    private const string GroupPath = @"C:\images\11.28-新澳高级会员\全部图片\member-tail.jpg";
    private static OcrRule Rule => RuleCatalog.Load(Path.Combine(
        ResultFilePaths.ConfigurationDirectory(AppContext.BaseDirectory), "新澳高级会员.json"))
        .Single(rule => rule.Id == "翩翩公子尾");

    private static OcrLineEvidence[] Row(int issue = 332, int y = 279,
        string digits = "0 1 2 3 4 5 7", string continuation = "8 9 准！") =>
    [
        new("公子送尾数：" + digits, new(165, y - 16, 443, 31), .99, "paddle/original"),
        new($"{issue}期", new(24, y, 90, 33), .99, "paddle/original"),
        new("开??", new(678, y, 73, 34), .99, "paddle/original"),
        new(continuation, new(314, y + 18, 125, 31), .98, "paddle/original")
    ];

    private static OcrEvidence Evidence(params OcrLineEvidence[] tokens) =>
        new(GroupPath, GroupPath, "source-hash", "input-hash", "paddle",
            OcrEvidenceLayout.Partition(tokens), tokens) { Reader = "paddle/medium" };

    [Theory]
    [InlineData("paddle/original", .99, .98)]
    [InlineData("cloud/tencent", 99, 98)]
    [InlineData("cloud/baidu", .99, .98)]
    public void OriginalMediumGeometrySurvivesPartitioningAndKeepsHistoricalRowsSeparate(
        string view, double confidence, double continuationConfidence)
    {
        OcrLineEvidence[] tokens = [..Row(), ..Row(331, 383, "0 1 2 3 4 6 7")];
        tokens = tokens.Select((item, index) => item with
        {
            ViewId = view,
            Confidence = index % 4 == 3 ? continuationConfidence : confidence
        }).ToArray();
        OcrEvidence evidence = Evidence(tokens);

        Assert.True(Rule.StrictIssueBlock);
        Assert.Equal(RuleExtractionResult.Success("6尾"), RuleEngine.ExtractFinalResult(evidence, 332, Rule));
        Assert.Equal(RuleExtractionResult.Success("5尾"), RuleEngine.ExtractFinalResult(evidence, 331, Rule));
        Assert.Equal(RuleExtractionResult.Missing, RuleEngine.ExtractFinalResult(evidence, 333, Rule));
        Assert.Equal("6尾 翩翩公子尾", Assert.Single(RuleEngine.FormatOutput([Rule],
            new Dictionary<string, string> { [Rule.Id] = "6尾" })));
    }

    [Theory]
    [InlineData(251)]
    [InlineData(402)]
    [InlineData(12345)]
    public void SelectedIssueIsDynamicAndCanBeTheFirstOrHistoricalRow(int issue)
    {
        OcrEvidence evidence = Evidence([..Row(issue), ..Row(issue - 1, 383, "0 1 2 3 4 6 7")]);
        Assert.Equal(RuleExtractionResult.Success("6尾"), RuleEngine.ExtractFinalResult(evidence, issue, Rule));
        Assert.Equal(RuleExtractionResult.Success("5尾"), RuleEngine.ExtractFinalResult(evidence, issue - 1, Rule));
        Assert.Equal(RuleExtractionResult.Missing, RuleEngine.ExtractFinalResult(evidence, issue + 1, Rule));
    }

    [Theory]
    [InlineData("11.28-新澳高级会员", true)]
    [InlineData("402期-新澳高级会员", true)]
    [InlineData("新澳高级会员_402期", true)]
    [InlineData("新澳六合彩资料", false)]
    [InlineData("新澳高级会员备份", false)]
    public void GeometricCompatibilityIsLimitedToTheCanonicalMemberGroup(string group, bool allowed)
    {
        OcrEvidence evidence = Evidence(Row()) with { SourcePath = $@"C:\images\{group}\全部图片\card.jpg" };
        Assert.Equal(allowed ? RuleExtractionResult.Success("6尾") : RuleExtractionResult.Missing,
            RuleEngine.ExtractFinalResult(evidence, 332, Rule));
    }

    [Fact]
    public void OtherRuleIdsAndNonStrictRulesDoNotAcquireTheNewGeometryPath()
    {
        Assert.Equal(RuleExtractionResult.Missing,
            RuleEngine.ExtractFinalResult(Evidence(Row()), 332, Rule with { Label = "其他缺尾" }));
        Assert.Equal(RuleExtractionResult.Missing,
            RuleEngine.ExtractFinalResult(Evidence(Row()), 332, Rule with { StrictIssueBlock = false }));
    }

    [Theory]
    [InlineData("0 1 2 3 4 5 7", "8 准！")]
    [InlineData("0 1 2 3 4 5 7", "8 8 准！")]
    [InlineData("0 1 2 3 4 5 6 7", "8 9 准！")]
    [InlineData("0 1 2 3 4 5 7", "8 ? 准！")]
    public void MissingRepeatedExtraOrUnknownDigitsNeverBecomeAMissingTail(string first, string continuation)
    {
        Assert.Equal(RuleExtractionResult.Missing,
            RuleEngine.ExtractFinalResult(Evidence(Row(digits: first, continuation: continuation)), 332, Rule));
    }

    [Fact]
    public void AnIncompleteTargetCannotBorrowDigitsFromTheNextPeriod()
    {
        OcrEvidence evidence = Evidence([
            ..Row(continuation: "8 准！"), ..Row(331, 383, "0 1 2 3 4 6 7")]);
        Assert.Equal(RuleExtractionResult.Missing, RuleEngine.ExtractFinalResult(evidence, 332, Rule));
        Assert.Equal(RuleExtractionResult.Success("5尾"), RuleEngine.ExtractFinalResult(evidence, 331, Rule));
    }

    [Theory]
    [InlineData("331期")]
    [InlineData("331期：")]
    [InlineData("331期龙03中")]
    [InlineData("【331期】：")]
    public void NeighborIssuePrefixesBoundTheWholeContinuationBox(string neighbor)
    {
        OcrLineEvidence[] tokens = Row();
        var boundary = new OcrLineEvidence(neighbor, new(24, 351, 90, 33), .99, "paddle/original");
        Assert.Equal(RuleExtractionResult.Success("6尾"),
            RuleEngine.ExtractFinalResult(Evidence([..tokens, boundary]), 332, Rule));
        tokens[3] = tokens[3] with { Box = new(314, 310, 125, 31) };
        Assert.Equal(RuleExtractionResult.Missing, RuleEngine.ExtractFinalResult(Evidence([
            ..tokens, boundary]), 332, Rule));
    }

    [Theory]
    [InlineData(100, 297, 125, 31)]
    [InlineData(600, 297, 125, 31)]
    [InlineData(314, 240, 125, 31)]
    [InlineData(314, 339, 125, 31)]
    public void ABoxCrossingAnyCellEdgeCannotBeClippedIntoAValidField(int x, int y, int width, int height)
    {
        OcrLineEvidence[] tokens = Row();
        tokens[3] = tokens[3] with { Box = new(x, y, width, height) };
        Assert.Equal(RuleExtractionResult.Missing, RuleEngine.ExtractFinalResult(Evidence(tokens), 332, Rule));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void PartialCoordinatesNeverFallBackToTextAdjacency(int index)
    {
        OcrLineEvidence[] tokens = Row();
        tokens[index] = tokens[index] with { Box = null };
        Assert.Equal(RuleExtractionResult.Missing, RuleEngine.ExtractFinalResult(Evidence(tokens), 332, Rule));
    }

    [Fact]
    public void DetachedUnpositionedTokensCannotProveIssueOwnership()
    {
        Assert.Equal(RuleExtractionResult.Missing, RuleEngine.ExtractFinalResult(
            Evidence(Row().Select(item => item with { Box = null }).ToArray()), 332, Rule));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(3, false)]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    [InlineData(3, true)]
    public void FieldIssueAndOpeningMustShareTheOriginalViewAndRegion(int index, bool differentRegion)
    {
        OcrLineEvidence[] tokens = Row();
        tokens[index] = differentRegion ? tokens[index] with { RegionId = "other-column" }
            : tokens[index] with { ViewId = "paddle/other-crop" };
        Assert.Equal(RuleExtractionResult.Missing, RuleEngine.ExtractFinalResult(Evidence(tokens), 332, Rule));
    }

    [Theory]
    [InlineData(0, "paddle/original", .79)]
    [InlineData(1, "paddle/original", .79)]
    [InlineData(2, "paddle/original", .79)]
    [InlineData(3, "paddle/original", .79)]
    [InlineData(3, "cloud/tencent", 79)]
    [InlineData(3, "paddle/original", double.NaN)]
    public void LowOrInvalidConfidenceDoesNotBecomeSuccessfulEvidence(int index, string view, double confidence)
    {
        OcrLineEvidence[] tokens = Row().Select(item => item with
        {
            ViewId = view, Confidence = view.Contains("tencent", StringComparison.Ordinal) ? 99 : .99
        }).ToArray();
        tokens[index] = tokens[index] with { Confidence = confidence };
        Assert.Equal(RuleExtractionResult.Missing, RuleEngine.ExtractFinalResult(Evidence(tokens), 332, Rule));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void EveryRequiredBoxMustCarryObservedConfidence(int index)
    {
        OcrLineEvidence[] tokens = Row();
        tokens[index] = tokens[index] with { Confidence = null };
        Assert.Equal(RuleExtractionResult.Missing, RuleEngine.ExtractFinalResult(Evidence(tokens), 332, Rule));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TwoOpeningCandidatesRemainAmbiguousWhenOneHasLowConfidence(bool firstIsLow)
    {
        OcrLineEvidence[] tokens = Row();
        Assert.Equal(RuleExtractionResult.Success("6尾"), RuleEngine.ExtractFinalResult(Evidence(tokens), 332, Rule));
        tokens[2] = tokens[2] with { Confidence = firstIsLow ? .5 : .99 };
        var otherOpening = new OcrLineEvidence("开??", new(800, 279, 73, 34),
            firstIsLow ? .99 : .5, "paddle/original");
        Assert.Equal(RuleExtractionResult.Missing,
            RuleEngine.ExtractFinalResult(Evidence([..tokens, otherOpening]), 332, Rule));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ACompleteOriginalInlineTokenProvesOwnershipAndDifferentInlineValuesConflict(bool positioned)
    {
        var current = new OcrLineEvidence("332期 公子送尾数：0 1 2 3 4 5 7 8 9 准！ 开??",
            positioned ? new(24, 279, 727, 34) : null, .99, "paddle/original");
        Assert.Equal(RuleExtractionResult.Success("6尾"),
            RuleEngine.ExtractFinalResult(Evidence(current), 332, Rule));
        var different = new OcrLineEvidence("332期 公子送尾数：0 1 2 3 4 6 7 8 9 准！ 开??",
            positioned ? new(24, 487, 727, 34) : null, .99, "paddle/original");
        Assert.Equal(RuleExtractionResult.Conflict,
            RuleEngine.ExtractFinalResult(Evidence(current, different), 332, Rule));
    }

    [Fact]
    public void CompleteInlineTokenWithNoConfidenceIsNotTrusted()
    {
        var inline = new OcrLineEvidence("332期 公子送尾数：0 1 2 3 4 5 7 8 9 准！ 开??",
            new(24, 279, 727, 34), null, "paddle/original");
        Assert.Equal(RuleExtractionResult.Missing, RuleEngine.ExtractFinalResult(Evidence(inline), 332, Rule));
    }

    [Fact]
    public void ACompleteInlineTokenCannotHideADifferentPositionedCell()
    {
        var inline = new OcrLineEvidence("332期 公子送尾数：0 1 2 3 4 6 7 8 9 准！ 开??",
            new(24, 487, 727, 34), .99, "paddle/original");
        Assert.Equal(RuleExtractionResult.Conflict,
            RuleEngine.ExtractFinalResult(Evidence([..Row(), inline]), 332, Rule));
    }

    [Fact]
    public void TraditionalLabelAndAccuracyMarkerKeepTheSameNineTailValidation()
    {
        OcrLineEvidence[] tokens = Row(continuation: "8 9 準！");
        tokens[0] = tokens[0] with { Text = "公子送尾數：0 1 2 3 4 5 7" };
        Assert.Equal(RuleExtractionResult.Success("6尾"), RuleEngine.ExtractFinalResult(Evidence(tokens), 332, Rule));
    }

    [Theory]
    [InlineData("公子送尾数：8 9 准！")]
    [InlineData("8 资料 9 准！")]
    public void DuplicateFieldLabelsAndUnknownTextInsideTheDigitsAreCounterEvidence(string continuation)
    {
        Assert.Equal(RuleExtractionResult.Missing,
            RuleEngine.ExtractFinalResult(Evidence(Row(continuation: continuation)), 332, Rule));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void ConflictingCompleteCopiesOfTheSameIssueRemainConflict(int scope)
    {
        OcrLineEvidence[] other = Row(332, 487, "0 1 2 3 4 6 7").Select(item => item with
        {
            ViewId = scope == 1 ? "paddle/other-crop" : item.ViewId,
            RegionId = scope == 2 ? "other-column" : item.RegionId
        }).ToArray();
        Assert.Equal(RuleExtractionResult.Conflict,
            RuleEngine.ExtractFinalResult(Evidence([..Row(), ..other]), 332, Rule));
    }

    [Fact]
    public void AStandaloneRowUsesItsAnchorsAndDoesNotRequireANeighbor()
    {
        Assert.Equal(RuleExtractionResult.Success("6尾"), RuleEngine.ExtractFinalResult(Evidence(Row()), 332, Rule));
        OcrLineEvidence[] tokens = Row();
        tokens[3] = tokens[3] with { Box = new(314, 450, 125, 31) };
        Assert.Equal(RuleExtractionResult.Missing, RuleEngine.ExtractFinalResult(Evidence(tokens), 332, Rule));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void StandaloneRowStillRequiresTheFieldLabelIssueAndOpening(int missing)
    {
        OcrLineEvidence[] tokens = Row();
        if (missing == 0)
            tokens[0] = tokens[0] with { Text = "0 1 2 3 4 5 7" };
        else
            tokens = tokens.Where((_, index) => index != missing).ToArray();
        Assert.Equal(RuleExtractionResult.Missing, RuleEngine.ExtractFinalResult(Evidence(tokens), 332, Rule));
    }

    [Fact]
    public void UnparseableTargetIssueIsMissingInsteadOfThrowing()
    {
        OcrLineEvidence[] tokens = Row();
        tokens[1] = tokens[1] with { Text = "３３２期" };
        Assert.Equal(RuleExtractionResult.Missing, RuleEngine.ExtractFinalResult(Evidence(tokens), 332, Rule));
    }

    [Fact]
    public void UnparseableHistoryDoesNotDiscardAValidTargetAndStillBoundsItsCell()
    {
        OcrLineEvidence[] tokens = Row();
        var neighbor = new OcrLineEvidence("３３１期", new(24, 351, 90, 33), .99, "paddle/original");
        Assert.Equal(RuleExtractionResult.Success("6尾"),
            RuleEngine.ExtractFinalResult(Evidence([..tokens, neighbor]), 332, Rule));
        tokens[3] = tokens[3] with { Box = new(314, 310, 125, 31) };
        Assert.Equal(RuleExtractionResult.Missing,
            RuleEngine.ExtractFinalResult(Evidence([..tokens, neighbor]), 332, Rule));
    }

    [MemberTailGpuFact]
    [Trait("Category", "CUDA")]
    public async Task FreshGpuMediumReadsCurrentAndHistoricalRowsWithoutProductionWrites()
    {
        string input = Environment.GetEnvironmentVariable("OCR_MEMBER_TAIL_IMAGE")!;
        string report = Environment.GetEnvironmentVariable("OCR_MEMBER_TAIL_REPORT")!;
        string folder = Path.Combine(report, "11.28-新澳高级会员", "全部图片");
        Directory.CreateDirectory(folder);
        string source = Path.Combine(folder, Path.GetFileName(input));
        File.Copy(input, source, overwrite: false);
        string hash = OcrEvidenceIdentity.Capture(input, input, "source").SourceHash;
        var client = new PaddleLocalOcrClient(new SystemProcessRunner(),
            Path.Combine(report, "isolated-cache.json"), LocalOcrDevice.Gpu);
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        var readings = await client.RecognizeBatchAsync([source], model: PaddleOcrModels.LocalPrimary,
            titleRatio: 1, useCache: false, cancellationToken: timeout.Token);
        Assert.Empty(client.LastImageErrors);
        OcrEvidence evidence = client.LastEvidence[source];
        var jsonOptions = new JsonSerializerOptions { WriteIndented = true };
        await File.WriteAllTextAsync(Path.Combine(report, "member-tail-evidence.json"),
            JsonSerializer.Serialize(new { Evidence = evidence, evidence.TokenItems }, jsonOptions));
        await File.WriteAllLinesAsync(Path.Combine(report, "member-tail-ocr.txt"), readings[source]);
        Assert.Equal("paddle/medium", evidence.Reader);
        Assert.Contains(RuleEngine.FindMatches(source, readings[source], [Rule], [Rule]), rule => rule.Id == Rule.Id);
        var records = new List<object>();
        var checks = new List<string>();
        foreach (var (issue, expected) in new[] { (332, "6尾"), (331, "5尾"), (330, "3尾"), (329, "7尾"), (333, (string?)null) })
        {
            RuleExtractionResult result = RuleEngine.ExtractFinalResult(evidence, issue, Rule);
            string[] formatted = result.Value is null ? [] : RuleEngine.FormatOutput([Rule],
                new Dictionary<string, string> { [Rule.Id] = result.Value }).ToArray();
            records.Add(new { Issue = issue, Expected = expected, Result = result, Formatted = formatted });
            checks.Add($"{issue}期: expected={expected ?? "Missing"}; actual={result.Value ?? result.Status.ToString()}");
            checks.AddRange(formatted);
            await File.WriteAllTextAsync(Path.Combine(report, "member-tail-results.json"), JsonSerializer.Serialize(records, jsonOptions));
            await File.WriteAllLinesAsync(Path.Combine(report, "member-tail-results.txt"), checks);
            Assert.Equal(expected is null ? RuleExtractionResult.Missing : RuleExtractionResult.Success(expected), result);
        }
        Assert.Equal(hash, evidence.SourceHash);
        Assert.Equal(hash, OcrEvidenceIdentity.Capture(input, input, "source").SourceHash);
    }
}

public sealed class MemberTailGpuFactAttribute : FactAttribute
{
    public MemberTailGpuFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OCR_MEMBER_TAIL_IMAGE"))
            || string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OCR_MEMBER_TAIL_REPORT")))
            Skip = "Set OCR_MEMBER_TAIL_IMAGE and OCR_MEMBER_TAIL_REPORT to run the isolated real GPU check.";
    }
}
