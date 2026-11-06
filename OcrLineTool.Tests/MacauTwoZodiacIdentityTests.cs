using System.Text.Json;
using OcrLineTool;

namespace OcrLineTool.Tests;

public sealed class MacauTwoZodiacIdentityTests
{
    private static IReadOnlyList<OcrRule> Rules() => RuleCatalog.Load(Path.Combine(
        ResultFilePaths.ConfigurationDirectory(AppContext.BaseDirectory), "新澳六合彩资料.json"));

    public static IEnumerable<object[]> Cards() => JsonSerializer.Deserialize<Sample[]>(File.ReadAllText(
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "macau-two-zodiac-identity.json")))!
        .Select(sample => new object[] { sample.Owner, sample.Lines });

    public sealed record Sample(string Owner, string[] Lines);

    [Theory]
    [MemberData(nameof(Cards))]
    public void RealCardsOnlyMatchTheirFullIdentityIncludingSingleRuleRetry(string owner, string[] lines)
    {
        var all = Rules();
        string[] ids = ["绿杀", "广东两肖", "福建两肖", "广西两肖", "贵州两肖", "海南两肖",
            "江西两肖", "湖南两肖", "上海两肖", "深圳两肖", "云南两肖", "四川两肖"];
        foreach (var rule in all.Where(rule => ids.Contains(rule.Id)))
        {
            bool expected = rule.Id == owner;
            Assert.Equal(expected, RuleEngine.FindMatches(lines, [rule]).Count == 1);
            foreach (string folder in new[] { "全部图片", rule.Keyword })
            {
                string path = Path.Combine(@"C:\图片\11.6-新澳六合彩资料", folder, "card.jpg");
                Assert.Equal(expected, RuleEngine.FindMatches(path, lines, [rule]).Count == 1);
                Assert.Equal(expected, RuleEngine.FindMatches(path, lines, [rule], all).Count == 1);
            }
        }
    }

    [Theory]
    [InlineData("绿杀", "统计表", "310期绿杀:龙牛")]
    [InlineData("绿杀", "红杀两肖", "310期杀:龙牛")]
    [InlineData("广东两肖", "广东", "310期两肖必杀:龙牛")]
    [InlineData("广东两肖", "广两肖必杀", "310期广东报:龙牛")]
    [InlineData("广西两肖", "广西两肖", "310期广西报:龙牛")]
    public void IncompleteOrForeignNamesCannotUseSummaryOrFolderFallback(string id, string title, string row)
    {
        var rule = Rules().Single(rule => rule.Id == id);
        string[] lines = [title, row];
        Assert.Empty(RuleEngine.FindMatches(lines, [rule]));
        Assert.Empty(RuleEngine.FindMatches(Path.Combine(@"C:\图片", rule.Keyword, "card.jpg"), lines, [rule], [rule]));
    }

    [Fact]
    public void TitlePunctuationAndSplitOcrRemainSupported()
    {
        var rule = Rules().Single(rule => rule.Id == "江西两肖");
        Assert.Single(RuleEngine.FindMatches(["江西", "「必杀两肖」", "310期江西报:兔鸡"], [rule]));
        Assert.Single(RuleEngine.FindMatches(["江西两肖必杀", "310期江西报:兔鸡"], [rule]));
    }

    [Fact]
    public void ATitleCannotBeAssembledAcrossPhysicalRegions()
    {
        var rule = Rules().Single(rule => rule.Id == "广东两肖");
        string[] lines = ["广东", OcrLayoutMarkers.RegionBoundary, "两肖必杀", "310期广西报:羊牛"];
        Assert.Empty(RuleEngine.FindMatches(lines, [rule]));
        Assert.Empty(RuleEngine.FindMatches(@"C:\图片\广东两肖必杀\card.jpg", lines, [rule], Rules()));
    }

    [Fact]
    public void OtherGroupsKeepTheirExistingGreenCardIdentity()
    {
        var rule = RuleCatalog.Load(Path.Combine(ResultFilePaths.ConfigurationDirectory(AppContext.BaseDirectory),
            "新澳高级会员.json")).Single(rule => rule.Id == "六合绿杀两肖");
        Assert.Single(RuleEngine.FindMatches(["六合日报 绿杀两肖", "292期", "绿杀:兔 龙", "开??"], [rule]));
    }
}
