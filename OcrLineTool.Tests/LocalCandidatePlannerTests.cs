using OcrLineTool;

namespace OcrLineTool.Tests;

public sealed class LocalCandidatePlannerTests
{
    [Fact]
    public void LiangWeiweiLockedFolderUsesOnlyThePrimaryImage()
    {
        var rule = new OcrRule("梁微微", "生肖", Folder: "梁薇薇");
        string dataImage = @"C:\结果\9.2-嫣然心水\梁薇薇\原图.jpg";
        string summaryImage = @"C:\结果\9.2-嫣然心水\汇总\统计.jpg";
        var results = new Dictionary<string, IReadOnlyList<string>>
        {
            [dataImage] = ["245期杀兔", "(梁微微娱乐)"],
            [summaryImage] = ["245期禁肖统计表", "梁薇薇禁兔", "烟草味禁羊"]
        };

        IReadOnlyList<LocalCandidatePlan> plans = LocalCandidatePlanner.Build([dataImage, summaryImage], results, [rule]);

        LocalCandidatePlan primary = Assert.Single(plans);
        Assert.True(primary.IsPrimary);
        Assert.Equal(dataImage, primary.Path);
        Assert.Equal(rule, Assert.Single(primary.Rules));
    }

    [Fact]
    public void LiangWeiweiLockedFolderStillWinsWhenLocalOcrMissesTheFooter()
    {
        var rule = new OcrRule("梁微微", "生肖", Folder: "梁薇薇");
        string dataImage = @"C:\结果\9.2-嫣然心水\梁薇薇\原图.jpg";
        string summaryImage = @"C:\结果\9.2-嫣然心水\汇总\统计.jpg";
        var results = new Dictionary<string, IReadOnlyList<string>>
        {
            [dataImage] = ["244期杀兔开46中", "245期杀猴开18中", "246期杀鼠开？"],
            [summaryImage] = ["246期禁肖统计表", "梁薇薇禁羊"]
        };

        IReadOnlyList<LocalCandidatePlan> plans = LocalCandidatePlanner.Build([dataImage, summaryImage], results, [rule]);

        LocalCandidatePlan plan = Assert.Single(plans);
        Assert.True(plan.IsPrimary);
        Assert.Equal(dataImage, plan.Path);
    }

    [Fact]
    public void PrefersAnExplicitTitleAndKeepsTheFolderMatchAsFallback()
    {
        var rule = new OcrRule("爱晚亭", "生肖");
        string folderFallback = Path.Combine("C:\\结果", "爱晚亭", "20260830_100000.jpg");
        string explicitTitle = Path.Combine("C:\\结果", "其他来源", "20260830_110000.jpg");
        var results = new Dictionary<string, IReadOnlyList<string>>
        {
            [folderFallback] = ["242期杀蛇开？"],
            [explicitTitle] = ["爱晚亭", "242期杀虎开？"]
        };

        IReadOnlyList<LocalCandidatePlan> plans = LocalCandidatePlanner.Build(
            [folderFallback, explicitTitle], results, [rule]);

        Assert.Equal(2, plans.Count);
        Assert.True(plans[0].IsPrimary);
        Assert.Equal(explicitTitle, plans[0].Path);
        Assert.False(plans[1].IsPrimary);
        Assert.Equal(folderFallback, plans[1].Path);
    }

    [Fact]
    public void UsesSeparateFolderFallbackImagesWhenEachContainsADifferentType()
    {
        var rules = new[]
        {
            new OcrRule("小灰灰", "单生肖", "小灰灰一肖"),
            new OcrRule("小灰灰", "生肖组合", "小灰灰两肖"),
            new OcrRule("小灰灰", "尾数组合", "小灰灰两尾")
        };
        string zodiac = Path.Combine("C:\\结果", "小灰灰", "1.jpg");
        string twoZodiacs = Path.Combine("C:\\结果", "小灰灰", "2.jpg");
        string twoTails = Path.Combine("C:\\结果", "小灰灰", "3.jpg");
        var results = new Dictionary<string, IReadOnlyList<string>>
        {
            [zodiac] = ["242期杀蛇开？"],
            [twoZodiacs] = ["242期绝杀二肖", "【兔+鸡】开：？"],
            [twoTails] = ["242期精杀两尾", "【1 4】尾开？"]
        };

        IReadOnlyList<LocalCandidatePlan> plans = LocalCandidatePlanner.Build(
            [zodiac, twoZodiacs, twoTails], results, rules);

        LocalCandidatePlan[] primary = plans.Where(plan => plan.IsPrimary).ToArray();
        Assert.Equal(3, primary.Length);
        Assert.All(primary, plan => Assert.Single(plan.Rules));
        Assert.Equal(rules.Select(rule => rule.Id).Order(), primary.SelectMany(plan => plan.Rules).Select(rule => rule.Id).Order());
    }

    [Fact]
    public void CombinesRulesWhoseBestMatchIsTheSameImage()
    {
        var rules = new[]
        {
            new OcrRule("君军", "生肖", "君军（一）"),
            new OcrRule("君军", "生肖", "君军（二）")
        };
        string image = Path.Combine("C:\\结果", "君君", "sample.jpg");
        var results = new Dictionary<string, IReadOnlyList<string>>
        {
            [image] = ["君军", "242期杀牛开？"]
        };

        LocalCandidatePlan plan = Assert.Single(LocalCandidatePlanner.Build([image], results, rules));

        Assert.True(plan.IsPrimary);
        Assert.Equal(rules.Select(rule => rule.Id), plan.Rules.Select(rule => rule.Id));
    }

    [Fact]
    public void PrefersTheLatestFileWhenEquivalentExplicitMatchesExist()
    {
        var rule = new OcrRule("木桃", "生肖");
        string earlier = Path.Combine("C:\\结果", "来源", "20260830_100000.jpg");
        string later = Path.Combine("C:\\结果", "来源", "20260830_120000.jpg");
        var results = new Dictionary<string, IReadOnlyList<string>>
        {
            [earlier] = ["木桃", "242期杀蛇开？"],
            [later] = ["木桃", "242期杀蛇开？"]
        };

        IReadOnlyList<LocalCandidatePlan> plans = LocalCandidatePlanner.Build(
            [earlier, later], results, [rule]);

        Assert.Equal(later, plans[0].Path);
        Assert.True(plans[0].IsPrimary);
        Assert.Equal(earlier, plans[1].Path);
        Assert.False(plans[1].IsPrimary);
    }

    [Fact]
    public void PrefersASummaryAndKeepsTheDataImageAsFallback()
    {
        var rule = new OcrRule("简单爱", "生肖");
        string dataImage = Path.Combine("C:\\结果", "来源", "20260830_100000.jpg");
        string summaryImage = Path.Combine("C:\\结果", "来源", "20260830_120000.jpg");
        var results = new Dictionary<string, IReadOnlyList<string>>
        {
            [dataImage] = ["简单爱", "242期禁马"],
            [summaryImage] = ["GG团队242期统计表", "简单爱"]
        };

        IReadOnlyList<LocalCandidatePlan> plans = LocalCandidatePlanner.Build(
            [dataImage, summaryImage], results, [rule]);

        Assert.Equal(summaryImage, plans[0].Path);
        Assert.True(plans[0].IsPrimary);
        Assert.Equal(dataImage, plans[1].Path);
        Assert.False(plans[1].IsPrimary);
    }

    [Fact]
    public void UsesTheOutputLabelToMakeTheSummaryPrimary()
    {
        var rule = new OcrRule("守信承诺经营者", "生肖", "月来月好");
        string dataImage = Path.Combine("C:\\结果", "来源", "20260830_100000.jpg");
        string summaryImage = Path.Combine("C:\\结果", "来源", "20260830_120000.jpg");
        var results = new Dictionary<string, IReadOnlyList<string>>
        {
            [dataImage] = ["守信承诺经营者", "242期禁羊"],
            [summaryImage] = ["GG团队242期新澳禁肖统计表", "月来月好正正禁羊"]
        };

        IReadOnlyList<LocalCandidatePlan> plans = LocalCandidatePlanner.Build(
            [dataImage, summaryImage], results, [rule]);

        Assert.Equal(2, plans.Count);
        Assert.Equal(summaryImage, plans[0].Path);
        Assert.True(plans[0].IsPrimary);
        Assert.Equal(dataImage, plans[1].Path);
        Assert.False(plans[1].IsPrimary);
    }

    [Fact]
    public void CombinesMultipleZodiacRulesIntoOnePrimarySummaryRequest()
    {
        OcrRule[] rules =
        [
            new OcrRule("简单爱", "生肖"),
            new OcrRule("守信承诺经营者", "生肖", "月来月好")
        ];
        string simpleData = Path.Combine("C:\\结果", "来源", "简单爱.jpg");
        string monthlyData = Path.Combine("C:\\结果", "来源", "月来月好.jpg");
        string summaryImage = Path.Combine("C:\\结果", "来源", "汇总.jpg");
        var results = new Dictionary<string, IReadOnlyList<string>>
        {
            [simpleData] = ["简单爱", "242期禁马"],
            [monthlyData] = ["守信承诺经营者", "242期禁羊"],
            [summaryImage] = ["GG团队242期新澳禁肖统计表", "简单爱正正禁马", "月来月好正正禁羊"]
        };

        IReadOnlyList<LocalCandidatePlan> plans = LocalCandidatePlanner.Build(
            [simpleData, monthlyData, summaryImage], results, rules);

        LocalCandidatePlan primary = Assert.Single(plans, plan => plan.IsPrimary);
        Assert.Equal(summaryImage, primary.Path);
        Assert.Equal(rules.Select(rule => rule.Id), primary.Rules.Select(rule => rule.Id));
        Assert.Equal(2, plans.Count(plan => !plan.IsPrimary));
    }

    [Fact]
    public void UsesSummaryAsFallbackWhenLocalOcrMissesZodiacNames()
    {
        OcrRule[] rules =
        [
            new OcrRule("简单爱", "生肖"),
            new OcrRule("守信承诺经营者", "生肖", "月来月好"),
            new OcrRule("陈思思", "生肖")
        ];
        string summaryImage = Path.Combine("C:\\结果", "来源", "汇总.jpg");
        var results = new Dictionary<string, IReadOnlyList<string>>
        {
            [summaryImage] = ["GG团队243期新澳禁肖统计表"]
        };

        IReadOnlyList<LocalCandidatePlan> plans = LocalCandidatePlanner.Build(
            [summaryImage], results, rules);

        LocalCandidatePlan primary = Assert.Single(plans);
        Assert.True(primary.IsPrimary);
        Assert.Equal(rules.Select(rule => rule.Id), primary.Rules.Select(rule => rule.Id));
    }

    [Fact]
    public void UsesSummaryAsFallbackWhenLocalOcrMissesStatisticZodiacTitle()
    {
        OcrRule rule = new("品鉴", "统计生肖");
        string summaryImage = Path.Combine("C:\\结果", "来源", "汇总.jpg");
        var results = new Dictionary<string, IReadOnlyList<string>>
        {
            [summaryImage] = ["生肖统计表"]
        };

        LocalCandidatePlan plan = Assert.Single(LocalCandidatePlanner.Build(
            [summaryImage], results, [rule]));

        Assert.True(plan.IsPrimary);
        Assert.Equal(rule, Assert.Single(plan.Rules));
    }

    [Fact]
    public void DoesNotAssignAStatisticRuleToASummaryImageFromAnotherFolder()
    {
        OcrRule rule = new("借花献佛", "统计生肖", Folder: "六扇门");
        string publicSummary = Path.Combine("C:\\结果", "公共统计", "汇总.jpg");
        var results = new Dictionary<string, IReadOnlyList<string>>
        {
            [publicSummary] = ["新澳彩247期", "统计表", "3次：", "牛"]
        };

        Assert.Empty(LocalCandidatePlanner.Build([publicSummary], results, [rule]));
    }

    [Fact]
    public void SeparatesYanranBlueAndColorImagesInsideTheSharedThirtySixNumberFolder()
    {
        OcrRule[] rules =
        [
            new("蓝色", "号码:36", "蓝色", RequiredKeyword: "特码开在", Folder: "36码"),
            new("彩图", "号码:36", "彩图", RequiredKeyword: "开奖结果", Folder: "36码")
        ];
        string blue = Path.Combine("C:\\结果", "36码", "1.jpg");
        string color = Path.Combine("C:\\结果", "36码", "2.jpg");
        string[] numbers = Enumerable.Range(1, 36).Select(number => number.ToString("00")).ToArray();
        var results = new Dictionary<string, IReadOnlyList<string>>
        {
            [blue] = ["244期:特码开在", .. numbers, "243期:特码开在"],
            [color] = ["243期:开奖结果", .. numbers, "244期:开奖结果"]
        };

        LocalCandidatePlan[] primary = LocalCandidatePlanner.Build([blue, color], results, rules)
            .Where(plan => plan.IsPrimary)
            .ToArray();

        Assert.Collection(
            primary,
            plan =>
            {
                Assert.Equal(blue, plan.Path);
                Assert.Equal("蓝色", Assert.Single(plan.Rules).Id);
            },
            plan =>
            {
                Assert.Equal(color, plan.Path);
                Assert.Equal("彩图", Assert.Single(plan.Rules).Id);
            });
    }

    [Fact]
    public void FindsHuangdaxianKaiGeWhenTheCardUsesLaiBeforeTheOpeningResult()
    {
        OcrRule rule = Assert.Single(LoadRules("黄大仙新澳.json"), rule => rule.Id == "68凯哥");
        string image = Path.Combine("C:\\结果", "68", "凯哥.jpg");
        var results = new Dictionary<string, IReadOnlyList<string>>
        {
            [image] = ["68团队原创之凯哥", "246期凯哥杀五码【03,10,29,33,46】来√"]
        };

        LocalCandidatePlan plan = Assert.Single(LocalCandidatePlanner.Build([image], results, [rule]));

        Assert.Equal(image, plan.Path);
        Assert.Equal(rule, Assert.Single(plan.Rules));
    }

    [Fact]
    public void FindsXiaotengKillZodiacWhenTheTitleAndCurrentIssueValueAreSplit()
    {
        OcrRule rule = Assert.Single(LoadRules("蜻蜓一套骁腾.json"), rule => rule.Id == "骁腾杀肖");
        string image = Path.Combine("C:\\结果", "骁腾系列", "杀肖.jpg");
        var results = new Dictionary<string, IReadOnlyList<string>>
        {
            [image] = ["横财杀喜肖", "245期：杀一肖《马》开：牛18准", "246期：杀一肖", "《龙》开：發00准"]
        };

        LocalCandidatePlan plan = Assert.Single(LocalCandidatePlanner.Build([image], results, [rule]));

        Assert.Equal(image, plan.Path);
        Assert.Equal(rule, Assert.Single(plan.Rules));
    }

    [Fact]
    public void FindsYongBuQiKillHeadUsingTheActualCardTitle()
    {
        OcrRule rule = Assert.Single(LoadRules("嫣然心水.json"), rule => rule.Id == "永卟弃杀头");
        string image = Path.Combine("C:\\结果", "乖乖团队", "永卟弃.jpg");
        var results = new Dictionary<string, IReadOnlyList<string>>
        {
            [image] = ["新澳门六合彩", "永卟弃", "245期中0123头", "246期中0234头"]
        };

        LocalCandidatePlan plan = Assert.Single(LocalCandidatePlanner.Build([image], results, [rule]));

        Assert.Equal(image, plan.Path);
        Assert.Equal(rule, Assert.Single(plan.Rules));
    }

    private static IReadOnlyList<OcrRule> LoadRules(string fileName) => RuleCatalog.Load(Path.Combine(
        ResultFilePaths.ConfigurationDirectory(AppContext.BaseDirectory), fileName));
}
