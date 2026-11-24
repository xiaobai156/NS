using OcrLineTool;

namespace OcrLineTool.Tests;

public class MacauTitleIndexTests
{
    private static OcrEvidence Card(string file, params (string Text, int Y)[] rows) =>
        new(file, file, file, file, "header", rows.Select(x => new OcrLineEvidence(x.Text, new OcrBox(30, x.Y, 140, 24))).ToArray());
    private static OcrEvidence SingleViewEvidence(string path, params string[] lines)
    {
        OcrEvidence evidence = OcrEvidence.FromLines(path, lines);
        OcrLineEvidence[] items = lines.Select((line, index) =>
            new OcrLineEvidence(line, new OcrBox(30, 50 + index * 30, 140, 24), 0.99, "test", "card")).ToArray();
        return evidence with { Items = items, TokenItems = items };
    }
    private static VisualTemplateDefinition Template(string id) => new(id, [id], "", 0, .6);

    [Fact]
    public void SingleCardRuleAcceptsAnyTitleVerifiedCurrentIssueCard()
    {
        var t = Template(MacauTitleIndex.SingleCardRule); var rule = new OcrRule("三怪禁肖图", "生肖", t.Id);
        var index = new MacauTitleIndex([t], new Dictionary<string, OcrRule> { { rule.Id, rule } });
        var second = "20261122_070001_02b811f3_101.jpg";
        var first = "20261122_070001_02b811f3_99.jpg";
        var third = "20261122_070001_02b811f3_102.jpg";
        index.Read(Card(second, ("三怪禁肖图", 130), ("第326期", 240), ("牛", 350)), 800, 326);
        index.Read(Card(first, ("三怪禁肖图", 130), ("第326期", 240)), 800, 326);
        index.Read(Card(third, ("三怪禁肖图", 130), ("第326期", 240)), 800, 326);
        index.Read(Card("98.jpg", ("三怪禁肖图", 130), ("第325期", 240)), 800, 326);
        IReadOnlyList<VisualTemplateMatch> candidates = index.Combine(
            [new(second, t, 3, 0, second), new(first, t, 3, 0, first), new(third, t, 3, 0, third), new("98.jpg", t, 3, 0, "98")]);
        Assert.Equal(new[] { second, first }, candidates.Select(match => match.SourcePath));

        var unknownOrderIndex = new MacauTitleIndex([t], new Dictionary<string, OcrRule> { { rule.Id, rule } });
        unknownOrderIndex.Read(Card("unknown-order.jpg", ("三怪禁肖图", 130)), 800, 326);
        Assert.Equal("unknown-order.jpg", Assert.Single(unknownOrderIndex.Combine([])).SourcePath);
    }

    [Fact]
    public void ExplicitWrongIssueRejectsSingleCardEvenWhenItsTitleWasUnreadable()
    {
        var t = Template(MacauTitleIndex.SingleCardRule);
        var rule = new OcrRule("三怪禁肖图", "生肖", t.Id);
        var index = new MacauTitleIndex([t], new Dictionary<string, OcrRule> { { rule.Id, rule } });
        index.Read(Card("old.jpg", ("第325期", 130)), 800, 326);

        Assert.Empty(index.Combine([new("old.jpg", t, 3, 0, "old-hash")]));

        var other = Template("其他资料");
        var otherRules = new Dictionary<string, OcrRule>
        {
            [t.Id] = rule,
            [other.Id] = new("其他资料", "生肖", other.Id)
        };
        var isolatedIndex = new MacauTitleIndex([t, other], otherRules);
        isolatedIndex.Read(Card("old.jpg", ("第325期", 130)), 800, 326);
        Assert.Single(isolatedIndex.Combine([new("old.jpg", other, 3, 0, "old-hash")]));
    }

    [Fact]
    public void FullRegionalTitleIsRequiredAndBodyOrAmbiguousTitlesCannotOverrideOwnership()
    {
        var gd = Template("广东两肖"); var gx = Template("广西两肖");
        var index = new MacauTitleIndex([gd, gx], new Dictionary<string, OcrRule> { { gd.Id, new("广东两肖必杀", "生肖组合", gd.Id) }, { gx.Id, new("广西两肖必杀", "生肖组合", gx.Id) } });
        index.Read(Card("a.jpg", ("广西两肖必杀", 120), ("广东两肖必杀", 330)), 800, 326);
        index.Read(Card("b.jpg", ("两肖", 120)), 800, 326);
        index.Read(Card("c.jpg", ("广西两肖必杀", 120), ("广东两肖必杀", 150)), 800, 326);
        var only = Assert.Single(index.Combine([new("a.jpg", gd, 10, 0), new("c.jpg", gx, 8, 0)]));
        Assert.Equal(("a.jpg", gx.Id), (only.SourcePath, only.Template.Id));
    }

    [Fact]
    public void ContestedVisualCandidateMustBeConfirmedByItsExactTitle()
    {
        var a = Template("甲资料");
        var b = Template("乙资料");
        var rules = new Dictionary<string, OcrRule>
        {
            [a.Id] = new("甲资料", "生肖", a.Id),
            [b.Id] = new("乙资料", "生肖", b.Id)
        };
        var index = new MacauTitleIndex([a, b], rules);
        var contested = new VisualTemplateMatch(
            "card.jpg", b, 4, 0, "hash", RequiresTitleVerification: true);

        Assert.Empty(index.Combine([contested]));

        index.Read(Card("card.jpg", ("甲资料", 130)), 800, 326);

        VisualTemplateMatch reclassified = Assert.Single(index.Combine([contested]));
        Assert.Equal(a.Id, reclassified.Template.Id);
        Assert.False(reclassified.RequiresTitleVerification);

        var verifiedIndex = new MacauTitleIndex([a, b], rules);
        verifiedIndex.Read(Card("card.jpg", ("乙资料", 130), ("第326期", 240)), 800, 326);
        VisualTemplateMatch verified = Assert.Single(verifiedIndex.Combine([contested]));
        Assert.Equal(b.Id, verified.Template.Id);
        Assert.False(verified.RequiresTitleVerification);

        var unreadableIndex = new MacauTitleIndex([a, b], rules);
        unreadableIndex.Read(Card("card.jpg", ("无关标题", 130)), 800, 326);
        Assert.Empty(unreadableIndex.Combine([contested]));
    }

    [Fact]
    public void SingleCardSkipsMissingCandidateAndAcceptsOnlyTheFirstValidValue()
    {
        string folder = Path.Combine(Path.GetTempPath(), "macau-single-card-" + Guid.NewGuid().ToString("N"), "新澳六合彩资料");
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, "card.jpg");
        try
        {
            File.WriteAllText(path, "fixture");
            OcrRule rule = RuleCatalog.Load(Path.Combine(
                ResultFilePaths.ConfigurationDirectory(AppContext.BaseDirectory), "新澳六合彩资料.json"))
                .Single(item => item.Id == MacauTitleIndex.SingleCardRule);
            Assert.Equal("蛇", RuleEngine.ExtractFinalValue(
                [rule.Keyword, "蛇", "第326期", "上期开奖结果:09 14 03 17 33 40 46"], 326, rule));
            var values = new ResultValues(StringComparer.Ordinal);
            var ledger = new ResultEvidenceLedger();

            MainForm.AddExtractedEvidenceValues(
                SingleViewEvidence(path, rule.Keyword, "第326期", "上期开奖结果:09 14 03 17 33 40 46"), [rule], 326, values, ledger);
            Assert.False(values.ContainsKey(rule.Id));

            MainForm.AddExtractedEvidenceValues(
                SingleViewEvidence(path, rule.Keyword, "蛇", "第326期", "上期开奖结果:09 14 03 17 33 40 46"), [rule], 326, values, ledger);
            MainForm.AddExtractedEvidenceValues(
                SingleViewEvidence(path, rule.Keyword, "兔", "第326期", "上期开奖结果:09 14 03 17 33 40 46"), [rule], 326, values, ledger);

            Assert.Equal("蛇", values[rule.Id]);
            Assert.False(ResultValues.IsConflict(values, rule.Id));
            Assert.False(MainForm.ShouldProcessCandidateRule(rule, values));

            var conflicted = new ResultValues(StringComparer.Ordinal);
            ResultValues.MarkConflict(conflicted, rule.Id);
            Assert.False(MainForm.ShouldProcessCandidateRule(rule, conflicted));
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(folder)!, recursive: true);
        }
    }
}
