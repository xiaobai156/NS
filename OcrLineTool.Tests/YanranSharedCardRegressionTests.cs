namespace OcrLineTool.Tests;

public sealed class YanranSharedCardRegressionTests
{
    private static IReadOnlyList<OcrRule> Rules => RuleCatalog.Load(Path.Combine(
        ResultFilePaths.ConfigurationDirectory(AppContext.BaseDirectory), "嫣然心水.json"));

    [Theory]
    [InlineData(7, 1)]
    [InlineData(279, 1)]
    [InlineData(301, 9)]
    [InlineData(1001, 0)]
    public void SharedZiyanerCardReachesMediumWhenSmallSplitsTheTailMarker(int issue, int tail)
    {
        var catalog = Rules;
        var rules = catalog.Where(rule => rule.Id is "紫燕儿尾" or "紫燕儿杀一肖").ToArray();
        string path = @"C:\图片\嫣然心水\紫燕儿\shared.jpg";
        string[] small = ["S紫燕儿", $"{issue}期:新奥杀", "鼠】", $"{issue}期:杀一", $"【{tail}尾"];
        var local = new Dictionary<string, IReadOnlyList<string>> { [path] = small };

        LocalCandidatePlan plan = Assert.Single(LocalCandidatePlanner.Build([path], local, rules, issue, catalog));

        Assert.True(plan.IsPrimary);
        Assert.Equal(2, plan.Rules.Count);
        Assert.Contains(plan.Rules, rule => rule.Id == "紫燕儿尾");
        OcrRule tailRule = rules.Single(rule => rule.Id == "紫燕儿尾");
        // Small only admits a candidate. Its incomplete field is never the final value.
        Assert.Null(RuleEngine.ExtractFinalValue(small, issue, tailRule));
        Assert.Equal($"{tail}尾", RuleEngine.ExtractFinalValue(
            ["紫燕儿", $"{issue}期:新澳杀【鼠】", $"{issue}期:杀一尾【{tail}尾】"], issue, tailRule));
        Assert.Null(RuleEngine.ExtractFinalValue(
            ["紫燕儿", $"{issue}期:杀一尾【{tail}尾{(tail + 1) % 10}尾】"], issue, tailRule));
    }

    [Theory]
    [InlineData("紫燕儿", "紫燕儿", "279期:新澳杀【鼠】")]
    [InlineData("紫燕儿", "恩平", "279期:杀一【1尾")]
    [InlineData("恩平", "紫燕儿", "279期:杀一【1尾")]
    [InlineData("紫燕儿", "", "279期:杀一【1尾")]
    public void PartialTailIdentityDoesNotAdmitAnUnrelatedCard(string folder, string title, string row)
    {
        var catalog = Rules;
        OcrRule rule = catalog.Single(rule => rule.Id == "紫燕儿尾");
        Assert.Empty(RuleEngine.FindMatches($@"C:\图片\嫣然心水\{folder}\card.jpg", [title, row], [rule], catalog));
    }

    [Fact]
    public void SummaryAuthorWithoutRecognizedZhengStillHasItsOwnNarrowBand()
    {
        OcrRule rule = Rules.Single(rule => rule.Id == "月来月好");
        OcrLineEvidence[] items =
        [
            Row("一念之间正正", 622),
            Row("月来月好", 672),
            Row("月夜星空正正", 722),
            new("牛狗虎牛猴鼠马蛇羊鸡鼠猴羊蛇", new OcrBox(441, 121, 46, 690))
        ];

        var band = Band(items, rule);

        Assert.NotNull(band);
        Assert.InRange(band.Value.Top, 669, 674);
        Assert.InRange(band.Value.Height, 36, 40);
        Assert.True(band.Value.Top + band.Value.Height < 722);
        Assert.Equal("猴", RuleEngine.ExtractSummaryRowZodiacFromStrip(["月来月好", "禁", "猴"], rule, Rules));
        Assert.Null(RuleEngine.ExtractSummaryRowZodiacFromStrip(
            ["月来月好禁猴", "陈思思禁马"], rule, Rules));
    }

    private static OcrLineEvidence Row(string text, int y) => new(text, new OcrBox(156, y, 220, 36));

    [Fact]
    public void SummaryGeometryWorksWithoutAnyZhengMarksAndStopsBeforeTheClosestRow()
    {
        OcrRule rule = Rules.Single(rule => rule.Id == "月来月好");
        var band = Band([Row("简单爱", 572), Row("一念之间", 622), Row("月来月好", 672),
            Row("月夜星空", 712), Row("潮汕陈龙", 772)], rule);

        Assert.NotNull(band);
        Assert.Equal(674, band.Value.Top);
        Assert.Equal(32, band.Value.Height);
    }

    [Fact]
    public void SummaryBandRejectsAmbiguousMissingOrVerticalAuthorIdentity()
    {
        OcrRule rule = Rules.Single(rule => rule.Id == "月来月好");
        Assert.Null(Band([Row("一念之间正正", 622), Row("月夜星空正正", 722)], rule));
        Assert.Null(Band([Row("月来月好", 672)], rule));
        Assert.Null(Band([Row("月来月好", 622), Row("月来月好", 672), Row("月夜星空", 722)], rule));
        Assert.Null(Band([Row("一念之间", 622), Row("月夜星空", 722),
            new("月来月好", new OcrBox(156, 121, 36, 690))], rule));
        Assert.Null(Band([Row("月来月好", 672),
            new("月夜星空", new OcrBox(156, 722, 220, 36), ViewId: "other-view")], rule));
    }

    private static (int Top, int Height)? Band(IReadOnlyList<OcrLineEvidence> items, OcrRule rule) =>
        SummaryRowRecovery.ComputeSummaryBand(items, rule);
}
