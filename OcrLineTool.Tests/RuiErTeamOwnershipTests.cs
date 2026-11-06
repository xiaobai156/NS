using OcrLineTool;
using System.Text.Json;

namespace OcrLineTool.Tests;

public sealed class RuiErTeamOwnershipTests
{
    private static OcrRule Rule(string name = "安之若素", string type = "生肖") =>
        new(name, type, RequiredKeyword: name, Folder: "蕊儿团队", Extractor: "rui_er_team_card");
    private static OcrLineEvidence T(string text, int x, int y, int w = 150, int h = 20) =>
        new(text, new(x, y, w, h), .99);
    private static OcrEvidence E(params OcrLineEvidence[] items) =>
        new("fixture", "fixture", "hash", "hash", "default", items, items);
    private static OcrLineEvidence[] Card(string name, string row, int x = 0, int y = 0) =>
        [T(name, x + 170, y, 100, 35), T("澳门六合彩", x + 150, y + 40, 150, 40),
         T(row, x + 20, y + 120, 260)];

    [Fact]
    public void SideBySideAndStackedAuthorsCannotSupplyEachOther()
    {
        var own = Card("安之若素", "310期杀①肖【龙+7尾】");
        var right = Card("蕊儿", "310期杀①肖【蛇+1尾】", 330);
        var below = Card("碧水蓝天", "310期杀①肖【羊+6尾】", 0, 300);
        Assert.Equal("龙", RuleEngine.ExtractFinalValue(E([..own, ..right, ..below]), 310, Rule()));
        Assert.Equal("7尾", RuleEngine.ExtractFinalValue(E([..own, ..right, ..below]), 310, Rule(type: "尾")));
        own[2] = own[2] with { Text = "310期杀①肖【+尾】" };
        Assert.Null(RuleEngine.ExtractFinalValue(E([..own, ..right, ..below]), 310, Rule()));
    }

    [Fact]
    public void AnUnknownLowerAuthorStillStopsTheFirstCard()
    {
        var own = Card("安之若素", "310期杀①肖【+尾】");
        var foreign = Card("任意新作者", "310期杀①肖【蛇+1尾】", 0, 300);
        Assert.Null(RuleEngine.ExtractFinalValue(E([..own, ..foreign]), 310, Rule()));
        Assert.Null(RuleEngine.ExtractFinalValue(E([..own, foreign[0], foreign[2]]), 310, Rule()));
    }

    [Theory]
    [InlineData("蕊儿")]
    [InlineData("【蕊儿】")]
    [InlineData("[蕊儿]")]
    public void AShortLowerHeadingStopsTheCardEvenIfItsLogoWasMissed(string name)
    {
        var own = Card("安之若素", "310期杀①肖【+尾】");
        Assert.Null(RuleEngine.ExtractFinalValue(E([..own,
            T(name, 170, 300, 100, 20), T("310期杀①肖【蛇+1尾】", 20, 420, 260)]), 310, Rule()));
        Assert.Null(RuleEngine.ExtractFinalValue(["安之若素", "澳门六合彩", "310期杀①肖【+尾】",
            name, "310期杀①肖【蛇+1尾】"], 310, Rule()));
    }

    [Theory]
    [InlineData("tencent", 75, false)]
    [InlineData("primary/tencent", 95, true)]
    [InlineData("paddle", 1.5, false)]
    public void ConfidenceUsesTheProviderScale(string view, double confidence, bool valid)
    {
        var tokens = Card("安之若素", "310期杀①肖【龙+7尾】")
            .Select(t => t with { ViewId = view, Confidence = confidence }).ToArray();
        Assert.Equal(valid ? "龙" : null, RuleEngine.ExtractFinalValue(E(tokens), 310, Rule()));
    }

    [Fact]
    public void AuthorFromAnotherViewOrRegionDoesNotAuthorizeAValue()
    {
        var card = Card("安之若素", "310期杀①肖【龙+7尾】");
        Assert.Null(RuleEngine.ExtractFinalValue(E(card.Select(t => t == card[2]
            ? t with { ViewId = "other" } : t).ToArray()), 310, Rule()));
        Assert.Null(RuleEngine.ExtractFinalValue(E(card.Select(t => t == card[2]
            ? t with { RegionId = "other" } : t).ToArray()), 310, Rule()));
        Assert.Null(RuleEngine.ExtractFinalValue(E(card.Select(t => t with { Box = null }).ToArray()), 310, Rule()));
    }

    [Fact]
    public void MissingOrLowConfidencePayloadCannotBecomeSuccess()
    {
        var card = Card("安之若素", "310期杀①肖【龙+7尾】");
        card[2] = card[2] with { Confidence = .3 };
        Assert.Null(RuleEngine.ExtractFinalValue(E(card), 310, Rule()));
        card[2] = card[2] with { Confidence = double.NaN };
        Assert.Null(RuleEngine.ExtractFinalValue(E(card), 310, Rule()));
        card[2] = card[2] with { Confidence = .99, Text = "310期杀①肖" };
        Assert.Null(RuleEngine.ExtractFinalValue(E([..card, T("309期杀①肖【蛇+1尾】", 20, 145, 260)]), 310, Rule()));
    }

    [Fact]
    public void TwoOwnCardsWithDifferentValuesConflict()
    {
        Assert.Equal(RuleExtractionStatus.Conflict, RuleEngine.ExtractFinalResult(E([
            ..Card("安之若素", "310期杀①肖【龙+7尾】"),
            ..Card("安之若素", "310期杀①肖【虎+7尾】", 330)]), 310, Rule()).Status);
    }

    [Theory]
    [InlineData("【龙+7尾】【蛇+1尾】")]
    [InlineData("【龙+7尾】【蛇+1尾")]
    [InlineData("【龙+7尾]")]
    public void ExtraOrBrokenPayloadsCannotBecomeFirstAnswerSuccess(string payload)
    {
        string row = "310期杀①肖" + payload;
        Assert.Null(RuleEngine.ExtractFinalValue(E(Card("安之若素", row)), 310, Rule()));
        Assert.Null(RuleEngine.ExtractFinalValue(["安之若素", "澳门六合彩", row], 310, Rule()));
    }

    [Fact]
    public void SummaryRequiresNewMacauTargetIssueAndExactAuthor()
    {
        var rows = new[] { T("旧澳统计310期", 50, 0, 100), T("新澳统计310期", 400, 0, 100),
            T("【安之若素05:01】蛇", 20, 40, 170), T("【安之若素05:01】龙", 370, 40, 170),
            T("【蕊儿杀肖05:00】鸡", 370, 65, 170) };
        Assert.Equal("龙", RuleEngine.ExtractFinalValue(E(rows), 310, Rule()));
        Assert.Null(RuleEngine.ExtractFinalValue(E(rows), 309, Rule()));
        Assert.Null(RuleEngine.ExtractFinalValue(E(rows), 310, Rule(type: "尾")));
        Assert.Null(RuleEngine.ExtractFinalValue(E(rows), 310, Rule("安之若")));
        rows[3] = rows[3] with { Text = "【安之若素05:01】" };
        Assert.Null(RuleEngine.ExtractFinalValue(E(rows), 310, Rule()));
    }

    [Fact]
    public void MissingOldTableTitleDoesNotPullItsValuesIntoNewTable()
    {
        Assert.Null(RuleEngine.ExtractFinalValue(E(T("新澳统计310期", 400, 0, 100),
            T("【安之若素05:01】蛇", 20, 40, 170), T("【蕊儿杀肖05:00】鸡", 370, 65, 170)), 310, Rule()));
    }

    [Fact]
    public void ANewTableTitleClosesThePreviousPeriod()
    {
        Assert.Null(RuleEngine.ExtractFinalValue(E(T("新澳统计310期", 400, 0, 100),
            T("新澳统计309期", 400, 30, 100), T("【安之若素05:01】蛇", 370, 65, 170)), 310, Rule()));
    }

    [Theory]
    [InlineData("新澳统计309期")]
    [InlineData("旧澳统计309期")]
    [InlineData("旧澳统计")]
    [InlineData("新澳统计")]
    public void UncertainPeriodTitlesStillCloseThePreviousTable(string title)
    {
        Assert.Null(RuleEngine.ExtractFinalValue(E(T("新澳统计310期", 400, 0, 100),
            T(title, 400, 30, 100) with { Confidence = .3 },
            T("【安之若素05:01】蛇", 370, 65, 170)), 310, Rule()));
    }

    [Fact]
    public void VerticalMergedZodiacColumnCannotSupplyAnAuthor()
    {
        Assert.Null(RuleEngine.ExtractFinalValue(E(T("新澳统计310期", 400, 0, 100),
            T("【安之若素", 370, 40, 80), T("05:01】", 460, 40, 50), T("蛇鸡牛", 520, 30, 20, 75)),
            310, Rule()));
    }

    [Fact]
    public void AuthorsOwnIncompleteCounterCannotBeReplacedByARightHandCounter()
    {
        Assert.Null(RuleEngine.ExtractFinalValue(E(T("新澳统计310期", 400, 0, 100),
            T("【安之若素05:01】", 370, 40, 170), T("05:00】蛇", 540, 40, 60)), 310, Rule()));
        Assert.Equal("龙", RuleEngine.ExtractFinalValue(E(T("新澳统计310期", 400, 0, 100),
            T("【安之若素05:01】", 370, 40, 170), T("龙", 540, 40, 20)), 310, Rule()));
    }

    [Fact]
    public void SharedFolderDoesNotEnableGenericWholeWidthRecovery()
    {
        Assert.True(SummaryRowRecovery.RecoversBeforeCloud(Rule()));
        Assert.True(RuiErTeamRecovery.Supports(Rule()));
    }

    [Fact]
    public void RealGpuEvidenceUsesEachAuthorsOwn310Values()
    {
        var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
            "Fixtures", "ruier-owned-310.json")));
        var expected = new Dictionary<string, string> {
            ["龙翔"]="蛇", ["彩虹公主"]="鸡", ["蓝色空间"]="猴", ["战神飘扬"]="鸡",
            ["安之若素"]="龙", ["安之若素尾"]="7尾", ["风雨同路"]="牛", ["风雨同路尾"]="6尾",
            ["碧水蓝天"]="羊", ["碧水蓝天尾"]="6尾", ["蕊儿"]="蛇", ["蕊儿尾"]="1尾",
            ["梓涵合"]="12合", ["梓涵半头"]="4头单" };
        var rules = RuleCatalog.Load(Path.Combine(ResultFilePaths.ConfigurationDirectory(AppContext.BaseDirectory),
            "黄大仙新澳.json"));
        var observed = new HashSet<string>();
        foreach (var row in fixture.RootElement.EnumerateArray())
        {
            string id = row.GetProperty("rule").GetString()!;
            var rule = rules.Single(r => r.Id == id);
            var tokens = row.GetProperty("Tokens").Deserialize<OcrLineEvidence[]>()!;
            var result = RuleEngine.ExtractFinalResult(E(tokens), 310, rule);
            // The author's other card types are valid candidates but cannot supply this field.
            if (result.Status == RuleExtractionStatus.Missing) continue;
            Assert.True(expected[id] == result.Value, $"{id}: expected {expected[id]}, actual {result.Value}");
            Assert.Equal(RuleExtractionStatus.Success, result.Status);
            observed.Add(id);
            Assert.Equal(RuleExtractionStatus.Missing, RuleEngine.ExtractFinalResult(E(tokens), 311, rule).Status);
        }
        Assert.Equal(expected.Keys.Order(), observed.Order());
    }
}
