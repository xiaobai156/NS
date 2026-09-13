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
}
