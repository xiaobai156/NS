using OcrLineTool;

namespace OcrLineTool.Tests;

[Trait("Category", "P1Extraction")]
public sealed class P1ExtractionAccuracyRegressionTests
{
    private static OcrRule Rule(string group, string id) => RuleCatalog.Load(
        Path.Combine(ResultFilePaths.ConfigurationDirectory(AppContext.BaseDirectory), group + ".json"))
        .Single(rule => rule.Id == id);

    public static IEnumerable<object[]> UnsafeCases()
    {
        yield return ["R12", "嫣然心水", "小骚货", 251,
            new[] { "快乐的骚货 九肖", "251期 九肖待更新", "六肖 马蛇龙兔虎牛", "三肖 鼠猪狗" }];
        yield return ["R13", "嫣然心水", "小骚货", 251,
            new[] { "快乐的骚货 九肖", "251期 九肖 马蛇龙兔虎牛鼠猪狗", "鸡" }];
        yield return ["R14", "嫣然心水", "小灰灰一肖", 251,
            new[] { "251期 小灰灰绝杀一肖 鼠", "牛" }];
        yield return ["R15", "嫣然心水", "南国挽心", 251,
            new[] { "251期 南国挽心 鸡 狗" }];
        yield return ["R16", "嫣然心水", "沁园春", 251,
            new[] { "251期 沁园春 杀二肖 鼠牛虎" }];
        yield return ["R17", "嫣然心水", "沁园春", 251,
            new[] { "251期 沁园春 杀二肖 鼠鼠牛" }];
        yield return ["R18", "嫣然心水", "青苹果", 251,
            new[] { "251期 青苹果 杀六码 01 02 03 04 05 06", "251期 青苹果 杀六码 07 08 09 10 11 12" }];
        yield return ["R19", "嫣然心水", "杰少九肖", 251,
            new[] { "原创杰少", "251期 新澳九肖 马蛇龙兔虎牛鼠猪狗", "251期 新澳九肖 马蛇龙兔虎牛鼠猪鸡" }];
        yield return ["R20", "新澳六合彩资料", "时点半", 251,
            new[] { "250期 十点半集团大围36码 01 02 03 04 05 06 07 08 09 10 11 12 13 14 15 16 17 18 19 20 21 22 23 24 25 26 27 28 29 30 31 32 33 34 35 36" }];
        yield return ["R21", "嫣然心水", "南国挽心", 251,
            new[] { "南国挽心编号251 鼠" }];
        yield return ["R22", "嫣然心水", "南国挽心", 1001,
            new[] { "1001期 南国挽心 待更新", "1002", "鸡" }];
        yield return ["R23", "新澳六合彩资料", "包公肖肖", 251,
            new[] { "包公图", "251期", "￥" }];
        yield return ["R24", "新澳高级会员", "翩翩公子肖", 251,
            new[] { "251期 公子杀一肖：鸡? 准!" }];
        yield return ["R25", "蜻蜓一套骁腾", "绿格子双杀", 251,
            new[] { "251期 绝杀2肖【鸡狗】【鼠牛】" }];
        yield return ["R27", "嫣然心水", "借花献佛", 251,
            new[] { "借花献佛", "251期", "9次", "8次 鸡" }];
    }

    [Theory]
    [MemberData(nameof(UnsafeCases))]
    public void UnsafeExtractionCasesStayMissing(string id, string group, string ruleId, int issue, string[] lines)
    {
        Assert.Null(RuleEngine.ExtractFinalValue(lines, issue, Rule(group, ruleId)));
    }

    [Fact]
    public void PerRuleStrictFlagIsLoaded()
    {
        Assert.True(Rule("嫣然心水", "小骚货").StrictIssueBlock);
    }

    [Fact]
    public void FormulaUsesTheFinalKillMarker()
    {
        Assert.Equal("鸡牛", RuleEngine.ExtractFinalValue(
            ["251期 (1+1)=杀【狗猴】；最终=杀【鸡牛】"], 251,
            Rule("蜻蜓一套骁腾", "公式杀两肖肖")));
    }

    [Fact]
    public void ExactTwoZodiacsStillSucceed()
    {
        Assert.Equal("鼠牛", RuleEngine.ExtractFinalValue(
            ["251期 沁园春 杀二肖 鼠牛"], 251, Rule("嫣然心水", "沁园春")));
    }

    [Fact]
    public void ExplicitFourDigitIssueStillSucceeds()
    {
        Assert.Equal("鸡", RuleEngine.ExtractFinalValue(
            ["1001期 南国挽心 鸡"], 1001, Rule("嫣然心水", "南国挽心")));
    }

    [Fact]
    public void UnnumberedIgnoreIssueCardStaysUnverifiedWithoutTrustedPublicationEvidence()
    {
        string numbers = string.Join(' ', Enumerable.Range(1, 36).Select(n => n.ToString("00")));
        Assert.Null(RuleEngine.ExtractFinalValue(
            [$"十点半集团大围36码 {numbers}"], 251, Rule("新澳六合彩资料", "时点半")));
    }

    [Fact]
    public void CompleteHighestFrequencyRowStillSucceeds()
    {
        Assert.Equal("鸡", RuleEngine.ExtractFinalValue(
            ["借花献佛", "251期", "9次 鸡", "8次 牛"], 251,
            Rule("嫣然心水", "借花献佛")));
    }
}
