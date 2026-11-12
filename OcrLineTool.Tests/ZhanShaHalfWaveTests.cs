using OcrLineTool;
using System.Text.Json;

namespace OcrLineTool.Tests;

public sealed class ZhanShaHalfWaveTests
{
    private const string Source = @"C:\图片\11.12-新澳高手\斩杀系列\card.jpg";
    private static IReadOnlyList<OcrRule> Rules => RuleCatalog.Load(Path.Combine(
        ResultFilePaths.ConfigurationDirectory(AppContext.BaseDirectory), "新澳高手.json"));
    private static OcrRule Rule => Rules.Single(rule => rule.Id == "斩杀半波");
    private static readonly string[] Current =
    [
        "斩杀半波", "314期(红单)√", "315期(红双)√", "316期(红单)√",
        "317期(研究)√", "318期(研究)√", "319期(研究)√", "320期(研究)√", "321期(研究)√"
    ];

    [Fact]
    public void FreshSmallAndMediumFixturePassesCandidateAndFinalEvidencePaths()
    {
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory, "Fixtures", "zhansha-halfwave-316.json")));
        foreach (string model in new[] { "Small", "Medium" })
        {
            var evidence = new OcrEvidence(Source, Source, "s", "i", "default",
                fixture.RootElement.GetProperty(model).Deserialize<OcrLineEvidence[]>()!);
            Assert.Single(RuleEngine.FindMatches(Source, evidence.Lines, [Rule], Rules));
            Assert.Equal("红单", RuleEngine.ExtractFinalValue(evidence, 316, Rule));
            foreach (int issue in Enumerable.Range(317, 5))
                Assert.Null(RuleEngine.ExtractFinalValue(evidence, issue, Rule));
        }
    }

    [Fact]
    public void TitledRealCardIsSelectedAndOnlyItsPublishedPeriodsHaveValues()
    {
        var rules = Rules;
        Assert.Equal("斩杀半波", Assert.Single(RuleEngine.FindMatches(Source, Current, rules, rules)).Id);
        Assert.Single(LocalCandidatePlanner.Build([Source],
            new Dictionary<string, IReadOnlyList<string>> { [Source] = Current }, [Rule], 316, rules));
        Assert.Equal("红单", RuleEngine.ExtractValue(Current, 316, Rule));
        Assert.Equal("红单", RuleEngine.ExtractFinalValue(Current, 316, Rule));
        Assert.Equal("红双", RuleEngine.ExtractFinalValue(Current, 315, Rule));
        foreach (int issue in new[] { 313, 317, 318, 319, 320, 321, 322 })
            Assert.Null(RuleEngine.ExtractFinalValue(Current, issue, Rule));
    }

    [Theory]
    [InlineData("316期(红单)√", "红单")]
    [InlineData("316期（藍雙）×", "蓝双")]
    [InlineData("316期(研究)√", null)]
    [InlineData("316期()√", null)]
    [InlineData("316期(红单蓝双)√", null)]
    [InlineData("316期(红单红单)√", null)]
    [InlineData("316期(研究红单)√", null)]
    [InlineData("316期(红)√", null)]
    [InlineData("316期(红单)317期(蓝双)", null)]
    public void TitledCardNeedsNoHistoryButRequiresOneCompleteTargetField(string row, string? expected)
    {
        string[] lines = ["斩杀半波", row];
        Assert.Equal(expected, RuleEngine.ExtractFinalValue(lines, 316, Rule));
    }

    [Fact]
    public void NeitherOtherTitlesNorAnotherFolderOrGroupCanSupplyTheValue()
    {
        foreach (string title in new[] { "斩杀一行", "斩杀两尾", "斩杀两肖", "斩杀一头", "其他作者半波", "斩杀单波" })
        {
            string[] lines = [title, "314期(红单)√", "315期(蓝双)√", "316期(红单)√",
                "317期(绿单)√", "318期(红双)√"];
            Assert.Empty(RuleEngine.FindMatches(Source, lines, [Rule], Rules));
            Assert.Null(RuleEngine.ExtractFinalValue(lines, 316, Rule));
        }
        Assert.Empty(RuleEngine.FindMatches(Source.Replace("斩杀系列", "其他目录"), Current, [Rule], Rules));
        Assert.Empty(RuleEngine.FindMatches(Source.Replace("新澳高手", "其他群"), Current, [Rule], Rules));
        Assert.Null(RuleEngine.ExtractFinalValue(["斩杀半波", "315期(蓝单)", "斩杀一头", "316期(红单)"], 316, Rule));
    }

    [Fact]
    public void TitlelessHistoryStillWorksWithoutWeakeningItsThreshold()
    {
        string[] history = ["312期(红单)√", "313期(蓝双)√", "314期(绿单)√", "315期(红双)√", "316期(蓝单)√"];
        Assert.Single(RuleEngine.FindMatches(Source, history, [Rule], Rules));
        Assert.Equal("蓝单", RuleEngine.ExtractFinalValue(history, 316, Rule));
        Assert.Empty(RuleEngine.FindMatches(Source, Current.Skip(1), [Rule], Rules));
        Assert.Null(RuleEngine.ExtractFinalValue(Current.Skip(1), 316, Rule));
    }

    [Fact]
    public void RepeatedIssueDisagreementAndSplitRowsNeverBorrowValues()
    {
        Assert.Equal(RuleExtractionStatus.Conflict, RuleEngine.ExtractFinalResult(
            ["斩杀半波", "316期(红单)", "316期(蓝双)"], 316, Rule).Status);
        Assert.Null(RuleEngine.ExtractFinalValue(["斩杀半波", "316期", "红单", "317期(蓝双)"], 316, Rule));
        Assert.Null(RuleEngine.ExtractFinalValue(["斩杀半波", "315期(红单)", "316期(研究)", "317期(蓝双)"], 316, Rule));
    }

    [Fact]
    public void FinalEvidenceRechecksIdentityWithoutBorrowingAnotherRegionOrView()
    {
        OcrEvidence Evidence(params OcrLineEvidence[] items) => new(Source, Source, "s", "i", "default", items);
        Assert.Equal("红单", RuleEngine.ExtractFinalValue(Evidence(
            new("斩杀半波"), new("316期(红单)")), 316, Rule));
        foreach (var other in new[] { new OcrLineEvidence("316期(红单)", RegionId: "other"),
            new OcrLineEvidence("316期(红单)", ViewId: "other") })
            Assert.Null(RuleEngine.ExtractFinalValue(Evidence(new("斩杀半波"), other), 316, Rule));
        Assert.Null(RuleEngine.ExtractFinalValue(Evidence(new("斩杀一头"), new("316期(红单)")), 316, Rule));
    }

    [Fact]
    public void ForeignHeadingCannotBeDiscardedBySplittingItIntoAnotherRegion()
    {
        var items = new List<OcrLineEvidence> { new("其他作者半波", RegionId: "title") };
        items.AddRange(Enumerable.Range(312, 5).Select(issue => new OcrLineEvidence($"{issue}期(红单)√")));
        var evidence = new OcrEvidence(Source, Source, "s", "i", "default", items);
        Assert.Null(RuleEngine.ExtractFinalValue(evidence, 316, Rule));
    }

    [Fact]
    public void PositionedCellsJoinOnlyWithinTheSamePhysicalRow()
    {
        var evidence = new OcrEvidence(Source, Source, "s", "i", "default",
            [new("斩杀半波", new(30, 10, 100, 20)), new("316期", new(20, 50, 50, 20)),
                new("(红单)√", new(72, 50, 70, 20))]);
        Assert.Equal("红单", RuleEngine.ExtractFinalValue(evidence, 316, Rule));
        Assert.Null(RuleEngine.ExtractFinalValue(evidence with { Items =
            [evidence.Items[0], evidence.Items[1], evidence.Items[2] with { Box = new(72, 90, 70, 20) }] }, 316, Rule));
        Assert.Null(RuleEngine.ExtractFinalValue(evidence with { SourcePath = Source.Replace("斩杀系列", "其他") }, 316, Rule));
    }

    [Fact]
    public void AnUnpublishedTargetCannotBeReplacedByAnotherViewsAnswer()
    {
        var evidence = new OcrEvidence(Source, Source, "s", "i", "default",
            [new("斩杀半波"), new("316期(研究)√"),
                new("斩杀半波", ViewId: "other"), new("316期(红单)√", ViewId: "other")]);
        Assert.Null(RuleEngine.ExtractFinalValue(evidence, 316, Rule));
    }

    [Theory]
    [InlineData("其他作者半波")]
    [InlineData("斩杀一头")]
    public void AHeadingMergedIntoAHistoryRowDoesNotTransferIdentity(string foreignTitle)
    {
        foreach (string[] lines in new[] {
            new[] { "斩杀半波", $"315期(蓝双)√ {foreignTitle}", "316期(红单)√" },
            new[] { "312期(红单)√", "313期(蓝双)√", $"314期(红单)√ {foreignTitle}",
                "315期(绿单)√", "316期(红单)√" } })
        {
            Assert.Empty(RuleEngine.FindMatches(Source, lines, [Rule], Rules));
            Assert.Null(RuleEngine.ExtractFinalValue(lines, 316, Rule));
        }
        OcrLineEvidence[] tokens = [new("斩杀半波", new(10, 0, 100, 20)),
            new("315期(蓝双)√", new(10, 40, 120, 20)), new(foreignTitle, new(140, 40, 120, 20)),
            new("316期(红单)√", new(140, 70, 120, 20))];
        var evidence = new OcrEvidence(Source, Source, "s", "i", "default", OcrEvidenceLayout.Partition(tokens), tokens);
        Assert.Null(RuleEngine.ExtractFinalValue(evidence, 316, Rule));
    }

    [Theory]
    [InlineData("研究", RuleExtractionStatus.Missing)]
    [InlineData("蓝双", RuleExtractionStatus.Conflict)]
    [InlineData("红单", RuleExtractionStatus.Success)]
    public void TitlelessViewCounterevidenceMustNotBeIgnored(string target, RuleExtractionStatus expected)
    {
        string[] otherView = ["312期(红单)√", "313期(蓝双)√", "314期(绿单)√",
            "315期(红双)√", $"316期({target})√"];
        OcrLineEvidence[] items = [new("斩杀半波"), new("316期(红单)√"),
            .. otherView.Select(row => new OcrLineEvidence(row, ViewId: "compact"))];
        Assert.Equal(expected, RuleEngine.ExtractFinalResult(new OcrEvidence(Source, Source, "s", "i", "default", items), 316, Rule).Status);
        var unrelated = new OcrEvidence(Source, Source, "s", "i", "default",
            [new("斩杀半波"), new("315期(红单)√"), .. items.Skip(2)]);
        Assert.Null(RuleEngine.ExtractFinalValue(unrelated, 316, Rule));
    }

    [Theory]
    [InlineData("研究")]
    [InlineData("")]
    [InlineData("红")]
    public void LegacyHistoryMayHaveOneUnresolvedRowWithoutLoweringTheThreshold(string historical)
    {
        string[] lines = ["312期(红单)√", "313期(蓝双)√", $"314期({historical})√", "315期(绿单)√", "316期(红单)√"];
        Assert.Single(RuleEngine.FindMatches(Source, lines, [Rule], Rules));
        Assert.Equal("红单", RuleEngine.ExtractFinalValue(lines, 316, Rule));
        Assert.Null(RuleEngine.ExtractFinalValue(lines, 314, Rule));
    }
}
