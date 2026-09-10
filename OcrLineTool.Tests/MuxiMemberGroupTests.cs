using OcrLineTool;
using Xunit;

namespace OcrLineTool.Tests;

// 慕熙会员群 36特围等子文件夹的首批资料（用户 253 期卡面，本地版式锁定）。
public sealed class MuxiMemberGroupTests
{
    private static OcrRule[] Rules() => RuleCatalog.Load(
        Path.Combine(ResultFilePaths.ConfigurationDirectory(AppContext.BaseDirectory), "慕熙会员群.json"))
        .ToArray();

    private static OcrRule Rule(string id) => Assert.Single(Rules(), rule => rule.Id == id);

    [Fact]
    public void LoadsTheNewGroupRules()
    {
        OcrRule[] rules = Rules();

        Assert.Equal(11, rules.Length);
        Assert.All(rules, rule => Assert.Equal("慕熙会员群", RuleCatalog.GroupNameForFolder(
            AppContext.BaseDirectory,
            Path.Combine(@"C:\图片", "9.10-慕熙会员群"))));
        Assert.Contains(rules, rule => rule.Id == "红色散流" && rule.Type == "号码:36" && rule.Folder == "36特围");
        Assert.Contains(rules, rule => rule.Id == "紫色爆中" && rule.Type == "号码:36");
        Assert.Contains(rules, rule => rule.Id == "无错" && rule.Type == "号码:36");
        Assert.Contains(rules, rule => rule.Id == "大妮子" && rule.Type == "生肖" && rule.RequiredKeyword == "大妮");
        Assert.Contains(rules, rule => rule.Id == "玫瑰香" && rule.Type == "生肖");
        Assert.Contains(rules, rule => rule.Id == "必杀十码" && rule.Type == "号码:10");
        Assert.Contains(rules, rule => rule.Id == "发财九肖" && rule.Type == "九肖");
        Assert.Contains(rules, rule => rule.Id == "红色" && rule.Type == "缺肖");
        Assert.Contains(rules, rule => rule.Id == "另版兄弟" && rule.Type == "生肖组合" && rule.TenZodiacCombo);
        Assert.Contains(rules, rule => rule.Id == "大围" && rule.Type == "号码:36" && rule.Folder == "大围");
        Assert.Contains(rules, rule => rule.Id == "黄杀" && rule.Type == "生肖组合" && rule.Folder == "黄杀");
    }

    [Fact]
    public void ThirtySixNumberCardsMatchTheirOwnMarkerAndExtractAllNumbers()
    {
        string folder = @"C:\结果\9.10-慕熙会员群\36特围";
        OcrRule red = Rule("红色散流");
        OcrRule purple = Rule("紫色爆中");
        OcrRule flawless = Rule("无错");
        string[] redLines =
        [
            "253期:36码中特开:發00准",
            "01.02.03.04.05.08.09.10.11.12.13.14",
            "15.16.17.18.19.20.21.22.24.26.27.29",
            "30.31.32.35.36.37.38.42.44.46.47.48"
        ];
        string[] purpleLines =
        [
            "253期爆中36码开：？00中",
            "[01.04.05.06.07.08.09.10.11.16.17.18]",
            "[19.21.25.26.27.28.29.30.31.32.36.37]",
            "[38.39.40.41.42.43.44.45.46.47.48.49]",
            "252期爆中36码开：鸡22中",
            "[02.04.07.08.09.10.11.12.13.17.18.19]"
        ];
        string[] flawlessLines =
        [
            "253期无错36码开:肖00准",
            "38.39.40.41.42.43.44.45.46.47.48.49",
            "14.15.16.17.18.19.20.21.22.23.24.25",
            "26.27.28.29.30.31.32.33.34.35.36.37"
        ];

        Assert.Single(RuleEngine.FindMatches(Path.Combine(folder, "a.jpg"), redLines, [red], Rules()));
        Assert.Empty(RuleEngine.FindMatches(Path.Combine(folder, "a.jpg"), redLines, [purple], Rules()));
        Assert.Single(RuleEngine.FindMatches(Path.Combine(folder, "b.jpg"), purpleLines, [purple], Rules()));
        Assert.Single(RuleEngine.FindMatches(Path.Combine(folder, "c.jpg"), flawlessLines, [flawless], Rules()));

        Assert.Equal(
            "01 02 03 04 05 08 09 10 11 12 13 14 15 16 17 18 19 20 21 22 24 26 27 29 "
            + "30 31 32 35 36 37 38 42 44 46 47 48",
            RuleEngine.ExtractFinalValue(redLines, 253, red));
        Assert.Equal(
            "01 04 05 06 07 08 09 10 11 16 17 18 19 21 25 26 27 28 29 30 31 32 36 37 "
            + "38 39 40 41 42 43 44 45 46 47 48 49",
            RuleEngine.ExtractFinalValue(purpleLines, 253, purple));
        Assert.Equal(
            "38 39 40 41 42 43 44 45 46 47 48 49 14 15 16 17 18 19 20 21 22 23 24 25 "
            + "26 27 28 29 30 31 32 33 34 35 36 37",
            RuleEngine.ExtractFinalValue(flawlessLines, 253, flawless));
    }

    [Fact]
    public void DanniAndRoseReadTheirKillZodiac()
    {
        OcrRule danni = Rule("大妮子");
        OcrRule rose = Rule("玫瑰香");

        Assert.Equal("狗", RuleEngine.ExtractFinalValue(
            ["【大妮儿】NEW心水", "新澳彩新澳彩杀肖", "253期杀肖【狗】开猫中"], 253, danni));
        Assert.Equal("羊", RuleEngine.ExtractFinalValue(
            ["【玫瑰香】NEW心水", "新澳彩新澳彩杀肖", "253期杀肖【羊】开猫中"], 253, rose));
        Assert.Single(RuleEngine.FindMatches(
            @"C:\结果\9.10-慕熙会员群\大妮儿\a.jpg",
            ["【大妮儿】NEW心水", "253期杀肖【狗】开猫中"], [danni], Rules()));
        Assert.Single(RuleEngine.FindMatches(
            @"C:\结果\9.10-慕熙会员群\大妮儿\a.jpg",
            ["【大妮子】NEW心水", "253期杀肖【狗】开猫中"], [danni], Rules()));
    }

    [Fact]
    public void BishaShimaReadsTenNumbersFromTheTraditionalOpeningColumn()
    {
        Assert.Equal("40 23 22 43 27 11 28 07 08 06", RuleEngine.ExtractFinalValue(
            [
                "253期：【40.23.22.43.27.11.28.07.08.06】開？00准",
                "252期：（08.43.25.42.06.12.11.32.26.21）開鸡22准"
            ], 253, Rule("必杀十码")));
    }

    [Fact]
    public void FucaiNineZodiacsReadsTheRowBelowItsHeader()
    {
        Assert.Equal("虎鼠鸡马猴兔羊狗蛇", RuleEngine.ExtractFinalValue(
            ["253期：【发财九肖】开:? 00准", "虎鼠鸡马猴兔羊狗蛇"], 253, Rule("发财九肖")));
    }

    [Fact]
    public void RedElevenZodiacsInferTheMissingOne()
    {
        Assert.Equal("鼠", RuleEngine.ExtractFinalValue(
            ["253期: (马牛兔龙虎羊狗猪鸡蛇猴) 开? 00中"], 253, Rule("红色")));
    }

    [Fact]
    public void TheOtherVersionBrothersInferTheMissingPair()
    {
        Assert.Equal("兔虎", RuleEngine.ExtractFinalValue(
            ["253期【猴鼠羊龙狗鸡马牛蛇猪】√"], 253, Rule("另版兄弟")));
    }

    [Fact]
    public void DaweiReadsTheSpecialEnclosureTable()
    {
        string[] lines =
        [
            "253期【特围36码】 开? 00准",
            "马:01.49.13 鸡:10.22.34 猴:35.23.11",
            "鼠:31.43.07 狗:09.21.45 羊:12.36.24",
            "蛇:02.26.38 猪:44.08.32 虎:29.05.41",
            "牛:06.18.30 兔:04.28.16 龙:27.03.39"
        ];

        Assert.Equal(
            "01 49 13 10 22 34 35 23 11 31 43 07 09 21 45 12 36 24 02 26 38 44 08 32 "
            + "29 05 41 06 18 30 04 28 16 27 03 39",
            RuleEngine.ExtractFinalValue(lines, 253, Rule("大围")));
    }

    [Fact]
    public void PrimaryOnlyRulesSkipBackupPlans()
    {
        OcrRule danni = Rule("大妮子");
        Assert.True(danni.PrimaryOnly);
        Assert.True(Rule("玫瑰香").PrimaryOnly);
        Assert.False(Rule("黄杀").PrimaryOnly);

        string card = @"C:\结果\9.10-慕熙会员群\大妮儿\a.jpg";
        string summary = @"C:\结果\9.10-慕熙会员群\大妮儿\b.jpg";
        var results = new Dictionary<string, IReadOnlyList<string>>
        {
            [card] = ["【大妮儿】NEW心水", "253期杀肖【狗】开猫中"],
            [summary] = ["佰何团队与您同行", "2026年第253期", "大妮儿：禁肖[狗]08"]
        };

        IReadOnlyList<LocalCandidatePlan> plans = LocalCandidatePlanner.Build(
            [card, summary], results, [danni], 253, [danni]);

        LocalCandidatePlan plan = Assert.Single(plans);
        Assert.True(plan.IsPrimary);
    }

    [Fact]
    public void HuangKillReadsTwoZodiacs()
    {
        Assert.Equal("牛猪", RuleEngine.ExtractFinalValue(
            ["黄牌杀二肖", "253期: 牛猪 ??"], 253, Rule("黄杀")));
    }
}
