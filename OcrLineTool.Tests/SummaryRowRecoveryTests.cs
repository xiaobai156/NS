using OcrLineTool;

namespace OcrLineTool.Tests;

public sealed class SummaryRowRecoveryTests
{
    private static IReadOnlyList<OcrRule> Rules => RuleCatalog.Load(Path.Combine(
        ResultFilePaths.ConfigurationDirectory(AppContext.BaseDirectory), "嫣然心水.json"));

    [Fact]
    public void StripWithOnlyTheOwnRowYieldsItsSingleZodiac()
    {
        IReadOnlyList<OcrRule> rules = Rules;
        OcrRule simple = rules.Single(rule => rule.Id == "简单爱");

        Assert.Equal("虎", RuleEngine.ExtractSummaryRowZodiacFromStrip(["简单爱 正正正禁虎"], simple, rules));
        Assert.Equal("虎", RuleEngine.ExtractSummaryRowZodiacFromStrip(["简单爱 正正正", "禁", "虎"], simple, rules));
        Assert.Equal("虎", RuleEngine.ExtractSummaryRowZodiacFromStrip(["简单爱 正正正 禁 虎"], simple, rules));
    }

    [Fact]
    public void StripContainingAnotherKnownAuthorIsRejected()
    {
        IReadOnlyList<OcrRule> rules = Rules;
        OcrRule simple = rules.Single(rule => rule.Id == "简单爱");

        Assert.Null(RuleEngine.ExtractSummaryRowZodiacFromStrip(
            ["陈思思 正正正 禁蛇", "简单爱 正正正 禁虎"], simple, rules));
    }

    [Fact]
    public void StripWithoutASingleZodiacBehindItsOwnForbiddenMarkIsRejected()
    {
        IReadOnlyList<OcrRule> rules = Rules;
        OcrRule simple = rules.Single(rule => rule.Id == "简单爱");

        Assert.Null(RuleEngine.ExtractSummaryRowZodiacFromStrip(["简单爱 正正正"], simple, rules));
        Assert.Null(RuleEngine.ExtractSummaryRowZodiacFromStrip(["简单爱 正正正禁"], simple, rules));
        Assert.Null(RuleEngine.ExtractSummaryRowZodiacFromStrip(["简单爱 正正正禁虎兔"], simple, rules));
        Assert.Null(RuleEngine.ExtractSummaryRowZodiacFromStrip(["简单爱 正正正 简单爱 正正正禁虎"], simple, rules));
    }

    [Fact]
    public void StripWithoutAnyIdentityIsRejected()
    {
        IReadOnlyList<OcrRule> rules = Rules;
        OcrRule simple = rules.Single(rule => rule.Id == "简单爱");

        Assert.Null(RuleEngine.ExtractSummaryRowZodiacFromStrip(["正正正禁虎"], simple, rules));
        Assert.Null(RuleEngine.ExtractSummaryRowZodiacFromStrip([], simple, rules));
    }

    [Fact]
    public void BandStaysInsideTheRowPitch()
    {
        (int Top, int Height)? band = SummaryRowRecovery.ComputeBand([100, 146, 192], 146);

        Assert.NotNull(band);
        Assert.Equal(128, band!.Value.Top);
        Assert.Equal(36, band.Value.Height);
    }

    [Fact]
    public void BandRequiresMeasurableRows()
    {
        Assert.Null(SummaryRowRecovery.ComputeBand([100], 100));
        Assert.Null(SummaryRowRecovery.ComputeBand([], 0));
        Assert.Null(SummaryRowRecovery.ComputeBand([100, 100], 100));
    }

    private static OcrLineEvidence Row(string text, int y, int height = 30) =>
        new(text, new OcrBox(20, y, 300, height), 0.99, "original", "main");

    [Fact]
    public void IssueRowBandSpansTheTargetRowUntilTheNextIssueRow()
    {
        OcrLineEvidence[] rows =
        [
            Row("245期禁01.13.25", 100),
            Row("246期禁07.19.31", 146),
            Row("247期", 192),
            Row("禁", 192),
            Row("06.18.30", 192),
            Row("248期", 238),
            Row("257期", 284),
            Row("月禁", 284),
            Row("41.29.05", 284)
        ];

        (int Top, int Height)? band = SummaryRowRecovery.ComputeIssueRowBand(rows, 247);

        Assert.NotNull(band);
        Assert.Equal(188, band!.Value.Top);
        Assert.Equal(46, band.Value.Height);
    }

    [Fact]
    public void IssueRowBandCoversTheValueBelowTheLastIssueRow()
    {
        OcrLineEvidence[] rows =
        [
            Row("255期禁10.34.46", 100),
            Row("256期禁11.23.35", 146),
            Row("257期", 192),
            Row("月禁", 192),
            Row("41.29.05", 192)
        ];

        (int Top, int Height)? band = SummaryRowRecovery.ComputeIssueRowBand(rows, 257);

        Assert.NotNull(band);
        Assert.Equal(188, band!.Value.Top);
        Assert.Equal(71, band.Value.Height);
    }

    [Fact]
    public void IssueRowBandRequiresExactlyOneTargetRow()
    {
        OcrLineEvidence[] rows =
        [
            Row("255期禁10.34.46", 100),
            Row("257期禁41.29.05", 146),
            Row("257期月禁41.29.05", 192)
        ];

        Assert.Null(SummaryRowRecovery.ComputeIssueRowBand(rows, 257));
        Assert.Null(SummaryRowRecovery.ComputeIssueRowBand(rows, 258));
        Assert.Null(SummaryRowRecovery.ComputeIssueRowBand([Row("257期月禁41.29.05", 100)], 257));
    }
}
