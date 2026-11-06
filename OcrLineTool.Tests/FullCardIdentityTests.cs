using System.Text.Json;
using OcrLineTool;

namespace OcrLineTool.Tests;

public sealed class FullCardIdentityTests
{
    private static readonly string[] Ids = ["超级赢家半波波", "黄大仙半波", "唐朝杀肖", "唐朝杀尾", "唐朝杀头", "唐朝杀行"];

    private static IReadOnlyList<OcrRule> Rules(string group) => RuleCatalog.Load(Path.Combine(
        ResultFilePaths.ConfigurationDirectory(AppContext.BaseDirectory), group + ".json"));

    public sealed record Card(string Group, string Owner, string[] Lines);

    public static IEnumerable<object[]> Cards() => JsonSerializer.Deserialize<Card[]>(File.ReadAllText(
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "full-card-identities.json")))!
        .Select(card => new object[] { card.Group, card.Owner, card.Lines });

    [Theory]
    [MemberData(nameof(Cards))]
    public void RealOcrOnlyAdmitsTheOwnerInAllCandidateEntrypoints(string group, string owner, string[] lines)
    {
        var all = Rules(group);
        foreach (var rule in all.Where(rule => Ids.Contains(rule.Id)))
        {
            bool expected = rule.Id == owner;
            Assert.Equal(expected, RuleEngine.FindMatches(lines, [rule]).Any());
            foreach (string folder in new[] { "全部图片", rule.Keyword })
            {
                string path = Path.Combine(@"C:\图片", "11.6-" + group, folder, "card.jpg");
                Assert.Equal(expected, RuleEngine.FindMatches(path, lines, [rule]).Any());
                Assert.Equal(expected, RuleEngine.FindMatches(path, lines, [rule], all).Any());
                var plans = LocalCandidatePlanner.Build([path],
                    new Dictionary<string, IReadOnlyList<string>> { [path] = lines }, [rule], 310, all);
                Assert.Equal(expected, plans.Any(plan => plan.Rules.Any(r => r.Id == rule.Id)));
            }
        }
    }

    [Theory]
    [InlineData("新澳六合彩资料", "超级赢家半波波", "彩虹网【精杀半波】正版")]
    [InlineData("新澳六合彩资料", "超级赢家半波波", "其他作者【绝杀半波】")]
    [InlineData("新澳六合彩资料", "超级赢家半波波", "大赢家【精杀半波】")]
    [InlineData("新澳六合彩资料", "黄大仙半波", "红人馆禁半个波色付费版")]
    [InlineData("新澳六合彩资料", "黄大仙半波", "其他作者杀半个波色")]
    [InlineData("新澳六合彩资料", "黄大仙半波", "黄大仙禁半个波色")]
    [InlineData("嫣然六合彩大全", "唐朝杀肖", "唐朝杀一波")]
    [InlineData("嫣然六合彩大全", "唐朝杀尾", "唐朝杀一肖")]
    [InlineData("嫣然六合彩大全", "唐朝杀头", "唐朝杀一尾")]
    [InlineData("嫣然六合彩大全", "唐朝杀行", "唐朝杀一头")]
    [InlineData("新澳六合彩资料", "绿杀", "红杀两肖")]
    [InlineData("新澳六合彩资料", "广东两肖", "广西两肖必杀")]
    public void SummaryAndMatchingFolderCannotReintroduceForeignCards(string group, string id, string title)
    {
        var all = Rules(group);
        var rule = all.Single(r => r.Id == id);
        string[] lines = [title, "统计表", "310期" + id + ":龙牛 红单 1头 9尾 木"];
        string path = Path.Combine(@"C:\图片", "11.6-" + group, rule.Keyword, "card.jpg");
        Assert.Empty(RuleEngine.FindMatches(lines, [rule]));
        Assert.Empty(RuleEngine.FindMatches(path, lines, [rule], all));
        Assert.Empty(LocalCandidatePlanner.Build([path], new Dictionary<string, IReadOnlyList<string>>
            { [path] = lines }, [rule], 310, all));
    }

    [Theory]
    [InlineData("新澳六合彩资料", "超级赢家半波波", "大赢家", "绝杀半波")]
    [InlineData("新澳六合彩资料", "黄大仙半波", "黄大仙", "杀半个波色")]
    [InlineData("嫣然六合彩大全", "唐朝杀头", "唐朝", "杀一头")]
    public void SplitTitlesStayWithinTheSamePhysicalRegion(string group, string id, string author, string kind)
    {
        var rule = Rules(group).Single(r => r.Id == id);
        Assert.Single(RuleEngine.FindMatches([author, "【" + kind + "】"], [rule]));
        Assert.Empty(RuleEngine.FindMatches([author, OcrLayoutMarkers.RegionBoundary, kind], [rule]));
    }

    [Fact]
    public void TraditionalTitlesAndKnownPrintedBrandRemainAccepted()
    {
        Assert.Single(RuleEngine.FindMatches(["贏 大贏家【絕殺半波】付費版"],
            [Rules("新澳六合彩资料").Single(r => r.Id == "超级赢家半波波")]));
        Assert.Single(RuleEngine.FindMatches(["黃大仙--殺半個波色"],
            [Rules("新澳六合彩资料").Single(r => r.Id == "黄大仙半波")]));
        Assert.Single(RuleEngine.FindMatches(["唐朝殺一頭"],
            [Rules("嫣然六合彩大全").Single(r => r.Id == "唐朝杀头")]));
    }

    [Fact]
    public void KnownZodiacTitleMisreadRequiresItsOwnCompleteTypedField()
    {
        var rule = Rules("嫣然六合彩大全").Single(r => r.Id == "唐朝杀肖");
        Assert.Single(RuleEngine.FindMatches(["唐朝杀一当", "310期唐朝绝杀羊羊羊肖稳杀开??"], [rule]));
        Assert.Empty(RuleEngine.FindMatches(["唐朝杀一当", "310期其他作者绝杀羊羊羊肖稳杀开??"], [rule]));
        Assert.Empty(RuleEngine.FindMatches(["唐朝杀一当", "310期唐朝绝杀绿稳杀开??"], [rule]));
        Assert.Empty(RuleEngine.FindMatches(["唐朝杀一当", "唐朝杀一波", "310期唐朝绝杀羊羊羊肖稳杀开??"], [rule]));
        Assert.Empty(RuleEngine.FindMatches(["唐朝杀一当", "唐朝绝杀", OcrLayoutMarkers.RegionBoundary, "羊羊羊肖稳杀"], [rule]));
    }

    [Fact]
    public void GenericAuthorSummaryRulesKeepTheirExistingFallback()
    {
        var rule = new OcrRule("本人", "生肖");
        string path = @"C:\图片\普通群\全部图片\card.jpg";
        Assert.Single(LocalCandidatePlanner.Build([path], new Dictionary<string, IReadOnlyList<string>>
            { [path] = new[] { "310期禁肖统计表", "另一位:兔" } }, [rule], 310));
    }
}
