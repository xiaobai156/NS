using OcrLineTool;

namespace OcrLineTool.Tests;

[Trait("Category", "P0Accuracy")]
public sealed class P0DataAccuracyRegressionTests
{
    private static OcrRule Rule(string group, string id) => RuleCatalog.Load(
        Path.Combine(ResultFilePaths.ConfigurationDirectory(AppContext.BaseDirectory), group + ".json"))
        .Single(rule => rule.Id == id);

    public static IEnumerable<object[]> UnsafeCases()
    {
        yield return ["R01", "新澳高级会员", "会员特供杀十码", 251,
            new[] { "会员特供绝杀10码", "页眉广告 01 02", "251期 绝杀:03 04 05 06 07 08 09 10 准!" }];
        yield return ["R02", "新澳高级会员", "会员特供杀十码", 251,
            new[] { "会员特供绝杀10码", "页眉广告 01 02", "250期 绝杀:03 04 05 06 07 08 09 10 11 12", "251期 绝杀:13 14 15 16 17 18 19 20" }];
        yield return ["R04", "嫣然心水", "青苹果", 251,
            new[] { "251期 青苹果 杀六码 01 02 03 04 05 开猴06中" }];
        yield return ["R05", "嫣然心水", "青苹果", 251,
            new[] { "251期 青苹果 杀六码 01 02 03 04 05", "旁栏参考：06" }];
        yield return ["R06", "嫣然心水", "南国挽心", 251,
            new[] { "南国挽心", "250期 杀狗", "251期 陌上花 杀鸡" }];
        yield return ["R07", "嫣然心水", "钦差大臣公式一", 251,
            new[] { "钦差大臣", "公式一", "251期 待更新", "公式二", "251期 鸡" }];
        yield return ["R08", "嫣然心水", "南国挽心", 251,
            new[] { "统计表", "251期", "南国挽心 待更新 陌上花 禁 鸡" }];
        yield return ["R09", "新澳高手", "高山流水", 251,
            new[] { "高山流水", "250期 精选⑨肖 马蛇龙兔虎牛鼠猪狗", "251期 待更新" }];
        yield return ["R10", "新澳高手", "亚太一头", 251,
            new[] { "251期 亚太地区四头：013" }];
        yield return ["R11", "新澳高手", "亚太尾", 251,
            new[] { "251期 亚太地区八尾：0134567" }];
    }

    [Theory]
    [MemberData(nameof(UnsafeCases))]
    public void UnsafeP0CasesStayMissing(string id, string group, string ruleId, int issue, string[] lines)
    {
        Assert.Null(RuleEngine.ExtractFinalValue(lines, issue, Rule(group, ruleId)));
    }

    [Fact]
    public void BlueReadsOnlyTheSelectedIssuesFollowingTable()
    {
        string expected = string.Join(' ', Enumerable.Range(14, 36).Select(n => n.ToString("00")));
        string previous = string.Join(' ', Enumerable.Range(1, 36).Select(n => n.ToString("00")));
        Assert.Equal(expected, RuleEngine.ExtractFinalValue(
            ["蓝色", "250期 特码开在", previous, "251期 特码开在", expected],
            251, Rule("嫣然心水", "蓝色")));
    }

    [Fact]
    public void ColorStillReadsTheImmediatelyPrecedingSelectedIssueTable()
    {
        string expected = string.Join(' ', Enumerable.Range(1, 36).Select(n => n.ToString("00")));
        Assert.Equal(expected, RuleEngine.ExtractFinalValue(
            ["250期:开奖结果:00-00-00-00-00-00特00准", expected,
             "251期:开奖结果:88-88-88-88-88-88特88准"],
            251, Rule("嫣然心水", "彩图")));
    }

    [Fact]
    public void CompleteCurrentIssueNumberRowStillSucceeds()
    {
        Assert.Equal("01 02 03 04 05 06 07 08 09 10", RuleEngine.ExtractFinalValue(
            ["会员特供绝杀10码", "251期 绝杀:01 02 03 04 05 06 07 08 09 10"],
            251, Rule("新澳高级会员", "会员特供杀十码")));
    }
}
