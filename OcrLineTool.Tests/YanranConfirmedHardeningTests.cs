using OcrLineTool;

namespace OcrLineTool.Tests;

public sealed class YanranConfirmedHardeningTests
{
    private static IReadOnlyList<OcrRule> Rules => RuleCatalog.Load(Path.Combine(
        ResultFilePaths.ConfigurationDirectory(AppContext.BaseDirectory), "嫣然心水.json"));

    public static TheoryData<string, string, string> Rows => new()
    {
        { "青苹果", "杀六码:11.12.41.17.09.10开00猫准", "11 12 41 17 09 10" },
        { "紫蝴蝶", "紫蝴蝶今期铁杀=(杀羊)【对】", "羊" },
        { "长安之星", "长安之星新澳门六合彩杀码【36,12】开00", "36 12" },
        { "依然公主", "依然公主杀一合 11合 开00对", "11合" },
        { "游牧草民", "游牧草民杀一合 01合 开00对", "01合" },
        { "南国挽心", "南国挽心杀一肖 猪 开猫对", "猪" },
        { "陌上花", "陌上花杀一肖 兔 开猫对", "兔" },
        { "一枝独秀", "一枝独秀杀一肖 龙 开猫对", "龙" },
        { "青玉", "青玉杀一合>>>10合 开00对", "10合" },
        { "输送机", "输送机杀一肖 鸡 开猫对", "鸡" },
        { "最亮月空", "最亮月空杀一合 11合 开00对", "11合" },
        { "电竞达人", "电竞达人新澳杀一段区【3段】开00准", "3段" },
        { "天之涯", "天之涯杀一合 12合 开00", "12合" },
        { "末日降临", "末日降临新澳杀段【4段】开00准", "4段" },
        { "凌志", "凌志新澳杀一尾 2尾 开00准", "2尾" },
        { "清风荷花", "清风荷花杀一肖 鸡 开猫对", "鸡" },
        { "雨后星星", "雨后星星绝杀合→→05合 开00准", "05合" },
        { "齐天大圣", "齐天大圣新澳杀一头 4头 开00对", "4头" },
        { "追踪使者", "追踪使者杀一肖 羊 开猫对", "羊" },
        { "阿尔法", "天机阁论坛●阿尔法㊣㊣㊣杀一肖】蛇 开00对", "蛇" },
        { "岁月漫长", "岁月漫长新澳杀半波【绿双】特00对", "绿双" },
        { "不决问风", "不决问风杀一合 05合 开00对", "05合" },
        { "白梦", "白梦杀一肖 兔 开猫对", "兔" },
        { "可乐仔", "可乐仔杀一肖 蛇 开猫对", "蛇" },
        { "紫燕儿杀一肖", "紫燕儿新澳杀【牛】√\n杀一尾【6尾】√", "牛" },
        { "紫燕儿尾", "紫燕儿新澳杀【牛】√\n杀一尾【6尾】√", "6尾" },
        { "沁园春", "沁园春杀二肖【鼠猴】开牛✅", "鼠猴" },
        { "杰少杀一肖", "原创杰少 新澳杀①肖【鼠】开猫准", "鼠" },
        { "杰少杀一尾", "原创杰少 新澳加杀尾【0尾】开00准", "0尾" },
        { "杰少禁一尾", "原创杰少 新澳禁一尾【5尾】开00对", "5尾" },
        { "杰少九肖", "原创杰少 新澳彩九肖中特 开猫准\n九肖【龙猴羊蛇虎兔鸡马狗】\n六肖【龙猴羊蛇虎兔】", "龙猴羊蛇虎兔鸡马狗" }
    };

    [Fact]
    public void AlphaIsNineZodiacsAndEveryConfirmedRuleIsPresent()
    {
        Assert.Equal("生肖", Assert.Single(Rules, rule => rule.Id == "阿尔法").Type);
        Assert.All(Rows.Select(row => (string)row[0]).Distinct(), id => Assert.Single(Rules, rule => rule.Id == id));
    }

    [Theory]
    [MemberData(nameof(Rows))]
    public void ReadsOnlyTheSoftwareSelectedDynamicIssue(string id, string row, string expected)
    {
        OcrRule rule = Assert.Single(Rules, rule => rule.Id == id);
        foreach (int issue in new[] { 7, 245, 1001 })
        {
            string[] lines = [$"{issue}期 {row}", $"{issue + 1}期 {row.Replace(expected.Replace(" ", ""), "")}"];
            Assert.Equal(expected, RuleEngine.ExtractFinalValue(lines, issue, rule));
            Assert.Null(RuleEngine.ExtractFinalValue(lines, issue - 1 <= 0 ? issue + 2 : issue - 1, rule));
        }
    }

    [Fact]
    public void RestoresTheCurrentDoomsdayRowFromItsAnchoredConsecutiveHistory()
    {
        OcrRule rule = Assert.Single(Rules, rule => rule.Id == "末日降临");
        string[] lines =
        [
            "【221期】【★☆☆☆☆☆☆☆☆☆】", "【231期】【★☆☆☆☆☆★☆☆★】", "【241期】【☆★☆☆☆】",
            "43期◆天机阁论坛末日降临新澳杀段◆【1段】开21准",
            "44期◆天机阁论坛末日降临新澳杀段◆【5段】开46准",
            "45期◆天机阁论坛末日降临新澳杀段◆【4段】开18准",
            "45期◆天机阁论坛末日降临新澳杀段◆【1段】开00准"
        ];

        Assert.Equal("1段", RuleEngine.ExtractFinalValue(lines, 246, rule));
        Assert.Null(RuleEngine.ExtractFinalValue(lines, 247, rule));
    }

    [Fact]
    public void BlueReadsFollowingTableAndColorReadsPrecedingTable()
    {
        string values = string.Join(' ', Enumerable.Range(1, 36).Select(value => value.ToString("00")));
        Assert.Equal(values, RuleEngine.ExtractFinalValue(
            ["244期:特码开在【发00准】身上", values, "245期:特码开在【发00准】身上"], 244,
            Assert.Single(Rules, rule => rule.Id == "蓝色")));
        Assert.Equal(values, RuleEngine.ExtractFinalValue(
            ["244期:开奖结果:00-00-00-00-00-00特00准", values, "245期:开奖结果:88-88-88-88-88-88特88准"], 245,
            Assert.Single(Rules, rule => rule.Id == "彩图")));
    }

    [Theory]
    [InlineData("沁园春", "245沁园春杀二肖【鼠鼠】开牛")]
    [InlineData("齐天大圣", "245期齐天大圣新澳杀一头 5头 开00")]
    [InlineData("依然公主", "245期依然公主杀一合 14合 开00")]
    [InlineData("杰少九肖", "245期原创杰少 九肖【龙猴羊蛇虎兔鸡马】 六肖【龙猴羊蛇虎兔】")]
    public void RejectsDuplicateIncompleteOrOutOfRangeValues(string id, string text)
    {
        Assert.Null(RuleEngine.ExtractFinalValue([text], 245, Assert.Single(Rules, rule => rule.Id == id)));
    }
}
