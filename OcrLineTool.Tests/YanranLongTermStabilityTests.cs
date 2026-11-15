using OcrLineTool;

namespace OcrLineTool.Tests;

public sealed class YanranLongTermStabilityTests
{
    private static IReadOnlyList<OcrRule> Rules() => RuleCatalog.Load(Path.Combine(
        ResultFilePaths.ConfigurationDirectory(AppContext.BaseDirectory), "嫣然心水.json"));

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
        var rule = Assert.Single(Rules(), rule => rule.Id == "紫蝴蝶");

        RuleExtractionResult result = RuleEngine.ExtractFinalResult(evidence, 267, rule);

        Assert.Equal(RuleExtractionStatus.Success, result.Status);
        Assert.Equal("蛇", result.Value);
    }

    [Fact]
    public void PurpleButterflyUnpositionedCloudTextIgnoresNeighborIssueRows()
    {
        string[] lines =
        [
            "267期柳叶刀新澳门彩杀一肖【猴】",
            "===【新澳门六合彩】===267期紫蝴蝶今期铁杀=(杀蛇)【对】"
        ];
        var rule = Assert.Single(Rules(), rule => rule.Id == "紫蝴蝶");

        RuleExtractionResult result = RuleEngine.ExtractFinalResult(lines, 267, rule);

        Assert.Equal(RuleExtractionStatus.Success, result.Status);
        Assert.Equal("蛇", result.Value);
    }

    [Fact]
    public void PurpleButterflySplitIdentityAndIssueRowsIgnoreThePreviousAuthor()
    {
        string[] lines =
        [
            "267期柳叶刀新澳门彩杀一肖猴放心杀OK",
            "紫蝴蝶入",
            "【新澳门六合彩】===267期",
            "紫蝴蝶今期铁杀=（杀蛇）【对】",
            "中中中"
        ];
        var rule = Assert.Single(Rules(), rule => rule.Id == "紫蝴蝶");

        RuleExtractionResult result = RuleEngine.ExtractFinalResult(lines, 267, rule);

        Assert.Equal(RuleExtractionStatus.Success, result.Status);
        Assert.Equal("蛇", result.Value);
    }

    [Theory]
    [InlineData(268, "兔")]
    [InlineData(1001, "牛")]
    public void PurpleButterflyScopesItsSectionAcrossLayoutsAndDynamicIssues(int issue, string expected)
    {
        var rule = Assert.Single(Rules(), item => item.Id == "紫蝴蝶");
        string[][] layouts =
        [
            [$"{issue}期柳叶刀杀一肖猴", "紫蝴蝶", $"{issue}期紫蝴蝶今期铁杀=(杀{expected})对"],
            [$"{issue}期柳叶刀杀一肖猴", "紫蝴蝶", $"新澳门六合彩{issue}期", $"紫蝴蝶今期铁杀=(杀{expected})对"],
            [$"{issue}期紫蝴", $"蝶今期铁杀=(杀{expected})对"],
            ["紫蝴蝶", $"{issue}期紫蝴蝶今期铁杀=(杀{expected})对", "柳叶刀", $"{issue}期柳叶刀杀一肖猴"]
        ];
        for (int layoutIndex = 0; layoutIndex < layouts.Length; layoutIndex++)
        {
            string[] lines = layouts[layoutIndex];
            Assert.True(expected == RuleEngine.ExtractFinalValue(lines, issue, rule), $"layout={layoutIndex}");
            foreach (bool positioned in new[] { false, true })
            {
                var items = lines.Select((text, index) => new OcrLineEvidence(text,
                    positioned ? new OcrBox(0, index * 40, 900, 30) : null, 0.99, "test", "main")).ToArray();
                var evidence = new OcrEvidence("card", "card", "hash", "hash", "test", items, items);
                Assert.True(expected == RuleEngine.ExtractFinalValue(evidence, issue, rule), $"layout={layoutIndex}, positioned={positioned}");
            }
        }
    }

    [Fact]
    public void PurpleButterflyKeepsOwnConflictsAndCannotBorrowAnotherIssueOrAuthor()
    {
        var rule = Assert.Single(Rules(), item => item.Id == "紫蝴蝶");
        string[][] invalid =
        [
            ["268期柳叶刀杀一肖猴", "267期紫蝴蝶今期铁杀=(杀兔)对"],
            ["268期柳叶刀杀一肖猴", "紫蝴蝶", "268期紫蝴蝶今期铁杀=(杀?)"],
            ["紫蝴蝶", "268期紫蝴蝶今期铁杀=(杀兔牛)对"]
        ];
        foreach (string[] lines in invalid)
            Assert.Null(RuleEngine.ExtractFinalValue(lines, 268, rule));
        string[] conflicting = ["紫蝴蝶", "268期紫蝴蝶今期铁杀=(杀兔)对", "268期", "紫蝴蝶今期铁杀=(杀牛)对"];
        Assert.Equal(RuleExtractionStatus.Conflict, RuleEngine.ExtractFinalResult(conflicting, 268, rule).Status);
    }
}
