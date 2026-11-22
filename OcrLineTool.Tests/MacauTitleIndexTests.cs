using OcrLineTool;

namespace OcrLineTool.Tests;

public class MacauTitleIndexTests
{
    private static OcrEvidence Card(string file, params (string Text, int Y)[] rows) =>
        new(file, file, file, file, "header", rows.Select(x => new OcrLineEvidence(x.Text, new OcrBox(30, x.Y, 140, 24))).ToArray());
    private static VisualTemplateDefinition Template(string id) => new(id, [id], "", 0, .6);

    [Fact]
    public void EarlierMessageOwnsTheRuleEvenWhenOnlyTheLaterCardHasAVisualMatchOrReadableValue()
    {
        var t = Template("三怪肖肖"); var rule = new OcrRule("三怪禁肖图", "生肖", t.Id);
        var index = new MacauTitleIndex([t], new Dictionary<string, OcrRule> { { rule.Id, rule } });
        var second = "20261122_070001_02b811f3_101.jpg"; var first = "20261122_070001_02b811f3_99.jpg";
        index.Read(Card(second, ("三怪禁肖图", 130), ("第326期", 240), ("牛", 350)), 800, 326);
        index.Read(Card(first, ("三怪禁肖图", 130), ("第326期", 240)), 800, 326);
        index.Read(Card("98.jpg", ("三怪禁肖图", 130), ("第325期", 240)), 800, 326);
        Assert.Equal(first, Assert.Single(index.Combine([new(second, t, 3, 0, second)])).SourcePath);
        index.Read(Card("未知顺序.jpg", ("三怪禁肖图", 130)), 800, 326);
        Assert.Empty(index.Combine([]));
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
}
