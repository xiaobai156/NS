using OcrLineTool;

namespace OcrLineTool.Tests;

public sealed class YanranLongTermStabilityTests
{
    private static IReadOnlyList<OcrRule> Rules() => RuleCatalog.Load(Path.Combine(
        ResultFilePaths.ConfigurationDirectory(AppContext.BaseDirectory), "嫣然心水.json"));

    [Fact]
    public void AlianCardKeepsMatchingWhenStableOcrShortensMonthAndConfusesIdentity()
    {
        IReadOnlyList<OcrRule> rules = Rules();
        OcrRule tail = Assert.Single(rules, rule => rule.Id == "阿莲杀尾尾");
        OcrRule zodiac = Assert.Single(rules, rule => rule.Id == "阿莲杀肖肖");
        string[] lines =
        [
            "9份杀料",
            "啊莲",
            "265期禁牛开49[666]",
            "265期禁6尾开49[666]"
        ];
        string path = @"C:\结果\9.24-嫣然心水\阿莲\card.jpg";

        IReadOnlyList<OcrRule> matches = RuleEngine.FindMatches(path, lines, [tail, zodiac], rules);

        Assert.Equal(["阿莲杀尾尾", "阿莲杀肖肖"], matches.Select(rule => rule.Id).OrderBy(id => id));
        Assert.Equal("6尾", RuleEngine.ExtractValue(lines, 265, tail));
        Assert.Equal("牛", RuleEngine.ExtractValue(lines, 265, zodiac));
    }

    [Fact]
    public void AlianSectionAliasRemainsDynamicAcrossAnotherMonth()
    {
        var rule = new OcrRule("阿莲", "尾", Section: "10月份", RequiredKeyword: "10月份", Folder: "阿莲");
        string[] lines = ["10份杀号", "啊莲", "312期禁4尾开？"];

        Assert.Equal(rule, Assert.Single(RuleEngine.FindMatches(
            @"C:\结果\10.1-嫣然心水\阿莲\card.jpg", lines, [rule], [rule])));
        Assert.Equal("4尾", RuleEngine.ExtractValue(lines, 312, rule));
    }

    [Fact]
    public void AlianEvidenceUsesTheSameAliasesAsPlainOcrLines()
    {
        OcrRule rule = Assert.Single(Rules(), item => item.Id == "阿莲杀尾尾");
        var items = new OcrLineEvidence[]
        {
            new("9份杀料", new OcrBox(0, 0, 300, 30), 0.99, "original", "main"),
            new("啊莲", new OcrBox(0, 40, 160, 30), 0.99, "original", "main"),
            new("265期禁6尾开49[666]", new OcrBox(0, 80, 300, 30), 0.99, "original", "main")
        };
        OcrEvidence evidence = new(
            "alian-card.jpg", "alian-card.jpg", "source", "source", "original",
            OcrEvidenceLayout.Partition(items), items);

        Assert.Equal("6尾", RuleEngine.ExtractFinalValue(evidence, 265, rule));
    }

    [Fact]
    public void PurpleButterflyUsesItsIdentityRegionInsteadOfNeighboringAuthor()
    {
        var items = new OcrLineEvidence[]
        {
            new("267期柳叶刀新澳门彩杀一肖【猴】", new OcrBox(0, 0, 500, 30), 0.99, "original", "column-0"),
            new("紫蝴蝶", new OcrBox(700, 0, 180, 30), 0.99, "original", "column-1"),
            new("===【新澳门六合彩】===267期紫蝴蝶今期铁杀=(杀蛇)【对】", new OcrBox(700, 40, 700, 30), 0.99, "original", "column-1")
        };
        OcrEvidence evidence = OcrEvidence.FromCapturedBytes(
            "purple-card.jpg", [1, 2, 3], "original", items);
        var rule = new OcrRule("紫蝴蝶", "生肖", RequiredKeyword: "紫蝴蝶", Folder: "阿尔法天狼星");

        RuleExtractionResult result = RuleEngine.ExtractFinalResult(evidence, 267, rule);

        Assert.Equal(RuleExtractionStatus.Success, result.Status);
        Assert.Equal("蛇", result.Value);
    }
}
