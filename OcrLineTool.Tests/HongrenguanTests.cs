using OcrLineTool;

namespace OcrLineTool.Tests;

public sealed class HongrenguanTests
{
    private static readonly string[] Guangong =
    [
        "红人馆", "红人馆团队之关公",
        "244期关公杀一肖【羊】开鸡✔", "245期关公杀一肖【蛇】开猫✔",
        "241期关公杀一尾【2尾】开49✔", "242期关公杀一尾【5尾】开09✔",
        "243期关公杀一尾【4尾】开21✔", "244期关公杀一尾【8尾】开46✔",
        "245期关公杀一尾【3尾】开00✔"
    ];
    private static readonly string[] Jidian =
    [
        "红人馆", "红人馆团队之极点", "243期极点杀一肖【狗】开狗×",
        "244期极点杀", "一肖【龙】开鸡✔", "245期🍂极点🍂杀一肖【鸡】开猫✔"
    ];
    private static IReadOnlyList<OcrRule> LoadRules() => RuleCatalog.Load(
        Path.Combine(ResultFilePaths.ConfigurationDirectory(AppContext.BaseDirectory), "黄大仙新澳.json"));

    [Theory]
    [InlineData("红人关公肖", "生肖", "关公", "杀一肖", 245, "蛇")]
    [InlineData("红人关公尾", "尾", "关公", "杀一尾", 245, "3尾")]
    [InlineData("红人极点肖", "生肖", "极点", "杀一肖", 245, "鸡")]
    [InlineData("红人关公肖", "生肖", "关公", "杀一肖", 244, "羊")]
    [InlineData("红人关公尾", "尾", "关公", "杀一尾", 244, "8尾")]
    [InlineData("红人极点肖", "生肖", "极点", "杀一肖", 244, "龙")]
    public void ExtractsOnlyTheRequestedIssueAndSection(string label, string type, string author, string section, int issue, string expected)
    {
        OcrRule rule = Assert.Single(LoadRules(), rule => rule.Id == label);
        Assert.Equal(type, rule.Type);
        Assert.Equal("红人馆", rule.Folder);
        Assert.Equal(author, rule.RequiredKeyword);
        Assert.Equal(section, rule.Section);
        string[] lines = author == "关公" ? Guangong : Jidian;
        Assert.Equal(expected, RuleEngine.ExtractValue(lines, issue, rule));
        Assert.Equal(expected, RuleEngine.ExtractFinalValue(lines, issue, rule));
        Assert.Null(RuleEngine.ExtractFinalValue(lines, 246, rule));
    }

    [Fact]
    public void LocksFolderAndAuthorAndSharesGuangongImage()
    {
        IReadOnlyList<OcrRule> rules = LoadRules();
        string guan = @"C:\图片\9.2-黄大仙新澳\红人馆\关公.jpg";
        string ji = @"C:\图片\9.2-黄大仙新澳\红人馆\极点.jpg";
        Assert.Equal(["红人关公肖", "红人关公尾"], RuleEngine.FindMatches(guan, Guangong, rules).Select(rule => rule.Id));
        Assert.Equal(["红人极点肖"], RuleEngine.FindMatches(ji, Jidian, rules).Select(rule => rule.Id));
        Assert.Empty(RuleEngine.FindMatches(@"C:\图片\9.2-黄大仙新澳\其他\图.jpg", Guangong, rules));
        Assert.Empty(RuleEngine.FindMatches(guan, ["红人馆团队之其他", "245期其他杀一肖【牛】", "245期其他杀一尾【6尾】"], rules));
        var results = new Dictionary<string, IReadOnlyList<string>> { [guan] = Guangong, [ji] = Jidian };
        IReadOnlyList<LocalCandidatePlan> plans = LocalCandidatePlanner.Build([guan, ji], results, rules);
        Assert.Equal(2, plans.Count);
        Assert.All(plans, plan => Assert.True(plan.IsPrimary));
        Assert.Equal(2, Assert.Single(plans, plan => plan.Path == guan).Rules.Count);
        Assert.Single(Assert.Single(plans, plan => plan.Path == ji).Rules);
    }

    [Fact]
    public async Task AutomaticAndManualReplayUseTheSameRoutesWithoutDuplicates()
    {
        string folder = Path.Combine(Path.GetTempPath(), $"hongrenguan-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        try
        {
            string zodiac = Path.Combine(folder, "245期-肖-新增.txt");
            string tail = Path.Combine(folder, "245期-尾.txt");
            await File.WriteAllLinesAsync(zodiac, ["原有肖", "生肖次数排行榜"]);
            await File.WriteAllLinesAsync(tail, ["原有尾", "尾数 次数"]);
            string[] lines = ["蛇 红人关公肖", "3尾 红人关公尾", "鸡 红人极点肖"];
            string config = ResultFilePaths.ConfigurationDirectory(AppContext.BaseDirectory);
            for (int run = 0; run < 2; run++)
            {
                DistributionResult result = await ResultDistributor.DistributeAllAsync(
                    @"C:\图片\9.2-黄大仙新澳", 245, lines, folder, config);
                Assert.Empty(result.Errors);
                Assert.Equal(lines.Order(), result.DistributedLines.Order());
                Assert.Equal(lines.Select(line => line + "（已分流）"), ResultDistributor.MarkDistributedLines(lines, result.DistributedLines));
            }
            Assert.Equal(["原有肖", lines[0], lines[2], "", "生肖次数排行榜"], await File.ReadAllLinesAsync(zodiac));
            Assert.Equal(["原有尾", lines[1], "", "尾数 次数"], await File.ReadAllLinesAsync(tail));
            DistributionResult ignored = await ResultDistributor.DistributeAllAsync(
                @"C:\图片\9.2-新澳高手", 245, lines, folder, config);
            Assert.Empty(ignored.DistributedLines);
            Assert.Empty(ignored.Errors);
            Assert.Equal(2, Directory.GetFiles(folder).Length);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
