namespace OcrLineTool.Tests;

public sealed class GgTeamSummaryTests
{
    private static OcrRule Rule(string id) => RuleCatalog.Load(Path.Combine(
        ResultFilePaths.ConfigurationDirectory(AppContext.BaseDirectory), "嫣然心水.json")).Single(rule => rule.Id == id);

    [Theory]
    [InlineData("陈思思", "猪")]
    [InlineData("简单爱", "猪")]
    [InlineData("欧阳肖", "猪")]
    [InlineData("苏柒若", "狗")]
    [InlineData("月来月好", "马")]
    public void TitleWithoutBiaoStillReadsOnlyTheAuthorsOwnRow(string id, string expected)
    {
        string[] lines = ["GG团队304期新澳禁肖统计", "YGG正正正正禁羊", "欧阳正正正正禁猪",
            "陈思思正正三禁猪", "苏柒若正正正禁狗", "简单爱正正正禁猪", "月来月好正正禁马"];
        Assert.Equal(expected, RuleEngine.ExtractFinalValue(lines, 304, Rule(id)));
        Assert.Null(RuleEngine.ExtractFinalValue(lines, 305, Rule(id)));
    }

    [Theory]
    [InlineData("陈思思")]
    [InlineData("简单爱")]
    [InlineData("欧阳肖")]
    [InlineData("苏柒若")]
    [InlineData("月来月好")]
    public void MissingOwnZodiacNeverUsesYggOrTheMergedColumn(string id)
    {
        OcrRule rule = Rule(id);
        string[] lines = ["GG团队304期新澳禁肖统计", "YGG正正正正禁羊",
            rule.Keyword + "正正正禁", "蛇鸡猪狗兔猪狗牛猪猪猴马蛇"];
        Assert.Equal(RuleExtractionStatus.Missing, RuleEngine.ExtractFinalResult(lines, 304, rule).Status);
        Assert.Equal(RuleExtractionStatus.Missing, RuleEngine.ExtractFinalResult(
            Evidence([Row(lines[0], 0), Row(lines[1], 70), Row(lines[2], 210),
                new(lines[3], new OcrBox(419, 150, 40, 560))]), 304, rule).Status);
    }

    [Theory]
    [InlineData("陈思思")]
    [InlineData("简单爱")]
    [InlineData("欧阳肖")]
    [InlineData("苏柒若")]
    [InlineData("月来月好")]
    public void DifferentCompleteOwnRowsRemainAConflict(string id)
    {
        OcrRule rule = Rule(id);
        string[] lines = ["GG团队304期新澳禁肖统计", rule.Keyword + "正正禁马", rule.Keyword + "正正禁狗"];
        Assert.Equal(RuleExtractionStatus.Conflict, RuleEngine.ExtractFinalResult(lines, 304, rule).Status);
        Assert.Equal(RuleExtractionStatus.Conflict, RuleEngine.ExtractFinalResult(
            Evidence(lines.Select((line, i) => Row(line, i * 60)).ToArray()), 304, rule).Status);
    }

    [Fact]
    public void MissingAnchoredRegionNeverFallsBackToAnotherAuthorsRegion()
    {
        OcrRule rule = Rule("苏柒若");
        Assert.Equal(RuleExtractionStatus.Missing, RuleEngine.ExtractFinalResult(Evidence([
            Row("GG团队304期新澳禁肖统计", 0), Row("苏柒若正正禁", 120),
            new("304期YGG正正禁羊", new OcrBox(20, 240, 250, 30), RegionId: "peer")]), 304, rule).Status);
    }

    [Fact]
    public void AValueInAnIndependentViewCannotCompleteAnAuthorRow()
    {
        Assert.Equal(RuleExtractionStatus.Missing, RuleEngine.ExtractFinalResult(Evidence([
            Row("GG团队304期新澳禁肖统计", 0), Row("苏柒若正正禁", 120),
            new("狗", new OcrBox(300, 120, 30, 30), ViewId: "other")]), 304, Rule("苏柒若")).Status);
    }

    [Theory]
    [InlineData("苏柒若正正禁", null)]
    [InlineData("苏柒若正正禁狗", "狗")]
    public void UnpositionedSummaryRegionsKeepTheAuthorAndPeriodBoundary(string ownRow, string? expected)
    {
        OcrRule rule = Rule("苏柒若");
        OcrEvidence evidence = Evidence([
            new("GG团队304期新澳禁肖统计", RegionId: "title"),
            new(ownRow, RegionId: "own"),
            new("304期YGG正正禁羊", RegionId: "peer")]);
        Assert.Equal(expected, RuleEngine.ExtractFinalValue(evidence, 304, rule));
        Assert.Null(RuleEngine.ExtractFinalValue(evidence, 305, rule));
    }

    [Fact]
    public void UnpositionedSplitCellsCannotJoinAcrossRegions()
    {
        Assert.Null(RuleEngine.ExtractFinalValue(Evidence([
            new("GG团队304期新澳禁肖统计", RegionId: "title"),
            new("苏柒若正正", RegionId: "own"),
            new("禁", RegionId: "peer"), new("羊", RegionId: "peer")]), 304, Rule("苏柒若")));
    }

    [Fact]
    public void AVerticalSingleZodiacCannotBeAssignedByItsCenter()
    {
        Assert.Equal(RuleExtractionStatus.Missing, RuleEngine.ExtractFinalResult(Evidence([
            Row("GG团队304期新澳禁肖统计", 0), Row("苏柒若正正禁", 210),
            new("羊", new OcrBox(300, 100, 30, 250))]), 304, Rule("苏柒若")).Status);
    }

    [Fact]
    public void SeparateCellsOnTheSamePhysicalRowCanSupplyTheOwnValue()
    {
        Assert.Equal("狗", RuleEngine.ExtractFinalValue(Evidence([
            Row("GG团队304期新澳禁肖统计", 0), Row("苏柒若正正", 120),
            new("禁", new OcrBox(285, 120, 30, 30), RegionId: "marker"),
            new("狗", new OcrBox(325, 120, 30, 30), RegionId: "values")]), 304, Rule("苏柒若")));
    }

    [Fact]
    public void SeparatePhysicalRowsCannotCompleteTheAuthorsField()
    {
        Assert.Null(RuleEngine.ExtractFinalValue(Evidence([
            Row("GG团队304期新澳禁肖统计", 0), Row("苏柒若正正", 120),
            new("禁", new OcrBox(285, 180, 30, 30)),
            new("羊", new OcrBox(325, 240, 30, 30))]), 304, Rule("苏柒若")));
    }

    [Theory]
    [InlineData("苏柒若正正禁YGG禁羊")]
    [InlineData("苏柒若正正YGG禁羊")]
    public void UnconfiguredPeerOnTheSameOcrLineCannotSupplyTheValue(string row)
    {
        OcrRule rule = Rule("苏柒若");
        Assert.Null(RuleEngine.ExtractFinalValue(["GG团队304期新澳禁肖统计", row], 304, rule));
        Assert.Null(RuleEngine.ExtractSummaryRowZodiacFromStrip([row], rule, [rule]));
    }

    [Fact]
    public void SplitFieldCannotCrossAWaitingOrPeerAuthorSuffix()
    {
        OcrRule rule = Rule("苏柒若");
        foreach (string suffix in new[] { "待更新", "正正YGG" })
        {
            string[] row = [rule.Keyword + suffix, "禁", "羊"];
            Assert.Null(RuleEngine.ExtractFinalValue(["GG团队304期新澳禁肖统计", .. row], 304, rule));
            Assert.Null(RuleEngine.ExtractSummaryRowZodiacFromStrip(row, rule, [rule]));
        }
    }

    [Theory]
    [InlineData("GG团队304期新澳禁肖统计", "战绩：从303期开始记录", "狗")]
    [InlineData("304期新澳禁肖统计", "战绩：从303期开始记录", "狗")]
    [InlineData("GG团队303期新澳禁肖统计", "304期", null)]
    [InlineData("GG团队303期新澳禁肖统计 304期", "", null)]
    [InlineData("GG团队303期新澳禁肖统计", "战绩：从304期开始记录", null)]
    public void SummaryTitleOwnsThePeriodInsteadOfUnrelatedPeriodText(string title, string extra, string? expected)
    {
        string[] lines = [title, extra, "苏柒若正正禁狗"];
        OcrRule rule = Rule("苏柒若");
        Assert.Equal(expected, RuleEngine.ExtractFinalValue(lines, 304, rule));
        Assert.Equal(expected, RuleEngine.ExtractFinalValue(
            Evidence(lines.Select((line, i) => Row(line, i * 60)).ToArray()), 304, rule));
    }

    [Fact]
    public void ANewSummaryTitleCanOpenItsOwnPeriodBlock()
    {
        string[] lines = ["GG团队303期新澳禁肖统计", "苏柒若正正禁羊",
            "GG团队304期新澳禁肖统计", "苏柒若正正禁狗"];
        Assert.Equal("狗", RuleEngine.ExtractFinalValue(lines, 304, Rule("苏柒若")));
    }

    [Fact]
    public void SplitSummaryTitleCannotBorrowAPeriodAcrossARegionBoundary()
    {
        Assert.Null(RuleEngine.ExtractFinalValue(
            ["GG团队304期新澳", OcrLayoutMarkers.RegionBoundary, "禁", "肖统计表", "苏柒若正正禁狗"],
            304, Rule("苏柒若")));
    }

    private static OcrLineEvidence Row(string text, int y) => new(text, new OcrBox(20, y, 250, 30));
    private static OcrEvidence Evidence(OcrLineEvidence[] items) => new(
        @"C:\图片\嫣然心水\乖乖团队\summary.jpg", "summary.jpg", "hash", "hash", "default", items, items);
}
