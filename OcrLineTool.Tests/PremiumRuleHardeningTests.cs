using OcrLineTool;

namespace OcrLineTool.Tests;

public sealed class PremiumRuleHardeningTests
{
    private static OcrRule Rule(string id) => RuleCatalog.Load(Path.Combine(
        ResultFilePaths.ConfigurationDirectory(AppContext.BaseDirectory), "新澳高级会员.json"))
        .Single(rule => rule.Id == id);

    public static IEnumerable<object[]> Samples()
    {
        yield return ["会员特供杀十码", "绝杀:05 07 10 16 26 33 35 37 42 49", "05 07 10 16 26 33 35 37 42 49"];
        yield return ["表弟", "36码【02 03 04 05 06 07 08 09 11\n12 13 14 15 19 21 22 24 25 26 27\n28 30 32 33 34 35 36 37 40 41 42\n43 45 46 47 49】", "02 03 04 05 06 07 08 09 11 12 13 14 15 19 21 22 24 25 26 27 28 30 32 33 34 35 36 37 40 41 42 43 45 46 47 49"];
        yield return ["祥瑞阁二肖", "祥瑞阁杀：蛇 鼠 准！", "蛇鼠"];
        yield return ["翩翩公子头", "公子禁止1头：四头 准！", "4头"];
        yield return ["翩翩公子杀十码", "公子神算杀10码：04 05 06\n09 21 24 25 27 40 46 准！", "04 05 06 09 21 24 25 27 40 46"];
        yield return ["翩翩公子半波", "公子秒杀半波：藍波雙 准！", "蓝双"];
        yield return ["翩翩公子五行", "公子4行：金 木 火 土 准！", "金木火土"];
        yield return ["翩翩公子尾", "公子送尾数：0 1 2 3 4 5 6\n7 9 准！", "8尾"];
        yield return ["翩翩公子肖", "公子杀一肖：雞 雞 雞 准！", "鸡"];
        yield return ["祥瑞阁", "01 02 03 04 05 06 07 08 10 11 12 13 14 15 16 17 19\n22 23 25 26 28 30 31 33 34 35 36 37 39 41 42 43 44\n45 47", "01 02 03 04 05 06 07 08 10 11 12 13 14 15 16 17 19 22 23 25 26 28 30 31 33 34 35 36 37 39 41 42 43 44 45 47"];
        yield return ["会员暴打", "会员九肖:狗龍雞馬虎羊鼠猴豬", "狗龙鸡马虎羊鼠猴猪"];
    }

    [Theory]
    [MemberData(nameof(Samples))]
    public void AllConfiguredCardsUseTheSelectedIssueAndOnlyItsWholeBlock(string id, string payload, string expected)
    {
        OcrRule rule = Rule(id);
        foreach (int issue in new[] { 7, 318, 1001 })
        {
            string[] lines = [$"{issue}期开奖", "22 23 14 02 13 38 18", "鸡 猴 蛇 马 牛",
                rule.Keyword, $"{issue + 1}期", "其他内容 开??", $"{issue}期",
                ..payload.Split('\n'), "牛18中", $"{issue - 1}期", "其他内容 开??"];
            Assert.Equal(expected, RuleEngine.ExtractFinalValue(lines, issue, rule));
            Assert.Equal(expected, RuleEngine.ExtractValue(lines, issue, rule));
            Assert.Null(RuleEngine.ExtractFinalValue(lines, issue + 2, rule));
        }
    }

    [Theory]
    [InlineData("翩翩公子头", "公子禁止1头：准！")]
    [InlineData("翩翩公子头", "公子禁止1头：四头 二头 准！")]
    [InlineData("翩翩公子头", "公子禁止1头：五头 准！")]
    [InlineData("翩翩公子肖", "公子杀一肖：兔 鼠 兔 准！")]
    [InlineData("祥瑞阁二肖", "祥瑞阁杀：蛇 鼠 牛 准！")]
    [InlineData("祥瑞阁二肖", "祥瑞阁杀：蛇 蛇 准！")]
    [InlineData("翩翩公子半波", "公子秒杀半波：蓝波双 红波单 准！")]
    [InlineData("翩翩公子五行", "公子4行：金 木 水 准！")]
    [InlineData("翩翩公子五行", "公子4行：金 木 水 火 土 准！")]
    [InlineData("翩翩公子五行", "公子4行：金 木 水 土 土 准！")]
    [InlineData("翩翩公子尾", "公子送尾数：0 1 2 3 4 5 6 7 准！")]
    [InlineData("翩翩公子尾", "公子送尾数：0 1 2 3 4 5 6 7 9 9 准！")]
    [InlineData("翩翩公子尾", "公子送尾数：0 1 2 3 4 5 6 7 8 9 准！")]
    public void RejectsIncompleteOrConflictingTypedValues(string id, string payload)
    {
        Assert.Null(RuleEngine.ExtractFinalValue(["318期", payload, "牛18中", "317期", "其他内容"], 318, Rule(id)));
    }

    [Theory]
    [InlineData("会员特供杀十码", "绝杀:")]
    [InlineData("翩翩公子杀十码", "公子神算杀10码：")]
    public void NumbersCannotBeRepairedWithOpeningDuplicateInvalidOrExcessNumbers(string id, string prefix)
    {
        string nine = "01 02 03 04 05 06 07 08 09";
        foreach (string payload in new[] { nine, nine + " 09", nine + " 00", nine + " 50", nine + " 10 11",
            nine + " 10 10", nine + " 10 00", nine + " 10 50", nine + " 10 X" })
        {
            Assert.Null(RuleEngine.ExtractFinalValue(
                ["318期", prefix + payload, "牛18中", "317期", prefix + "01 02 03 04 05 06 07 08 09 10"], 318, Rule(id)));
        }
    }

    [Theory]
    [InlineData("表弟")]
    [InlineData("祥瑞阁")]
    public void ThirtySixNumbersNeverComeFromThePreviousBlockOrOpening(string id)
    {
        string complete = string.Join(' ', Enumerable.Range(1, 36).Select(n => n.ToString("00")));
        string incomplete = string.Join(' ', Enumerable.Range(1, 35).Select(n => n.ToString("00")));
        OcrRule rule = Rule(id);
        Assert.Null(RuleEngine.ExtractFinalValue([rule.Keyword, "319期", complete, "开??", "318期", incomplete, "鸡36中"], 318, rule));
        Assert.Null(RuleEngine.ExtractFinalValue([rule.Keyword, "318期", incomplete, "317期", "36"], 318, rule));
        Assert.Null(RuleEngine.ExtractFinalValue([rule.Keyword, "318期", complete + " 37"], 318, rule));
        Assert.Null(RuleEngine.ExtractFinalValue([rule.Keyword, "318期", complete + " 36"], 318, rule));
    }

    [Fact]
    public void MissingTailCannotBorrowTheNinthDigitAcrossAnOpeningOrNextIssue()
    {
        OcrRule rule = Rule("翩翩公子尾");
        Assert.Null(RuleEngine.ExtractFinalValue(["318期 公子送尾数：0 1 2 3 4 5 6 7", "狗09中", "317期 公子送尾数：9"], 318, rule));
    }

    [Fact]
    public void FormatsOnlyTheMissingElementAndUsesDifferentSelectedRows()
    {
        OcrRule rule = Rule("翩翩公子五行");
        string[] lines = ["319期 公子4行：金 木 火 土 准！", "318期 公子4行：金 木 水 土 准！"];
        foreach ((int issue, string expected) in new[] { (319, "水"), (318, "火") })
        {
            string? value = RuleEngine.ExtractFinalValue(lines, issue, rule);
            Assert.NotNull(value);
            Assert.Equal(expected + "行 " + rule.Id, Assert.Single(RuleEngine.FormatOutput([rule], new Dictionary<string, string> { [rule.Id] = value })));
        }
    }

    [Fact]
    public void WholeBlockIsValidatedBeforeReturningAPartialSuccess()
    {
        Assert.Null(RuleEngine.ExtractFinalValue(["318期 公子杀一肖：兔", "鼠 兔 准！"], 318, Rule("翩翩公子肖")));
        Assert.Null(RuleEngine.ExtractFinalValue(["318期 绝杀:01 02 03 04 05 06 07 08 09 10", "11 准！"], 318, Rule("会员特供杀十码")));
        Assert.Null(RuleEngine.ExtractFinalValue(["318期 公子4行：金木水土", "火 准！"], 318, Rule("翩翩公子五行")));
    }

    [Fact]
    public void ConflictingCopiesOfTheSameIssueAreNotChosenArbitrarily()
    {
        Assert.Null(RuleEngine.ExtractFinalValue(["318期 公子禁止1头：零头 准！", "318期 公子禁止1头：四头 准！"], 318, Rule("翩翩公子头")));
    }

    [Fact]
    public void StrictModeComesFromCatalogDefaultOrAnExplicitRuleOverride()
    {
        string directory = ResultFilePaths.ConfigurationDirectory(AppContext.BaseDirectory);
        foreach (string path in Directory.EnumerateFiles(directory, "*.json")
            .Where(path => !path.EndsWith(".templates.json") && !path.EndsWith("分发规则.json")))
        {
            bool hardenedCatalog = Path.GetFileName(path) is "新澳高级会员.json" or "新澳六合彩资料.json" or "黄大仙新澳.json" or "蜻蜓一套骁腾.json";
            foreach (OcrRule rule in RuleCatalog.Load(path))
            {
                bool explicitYanranStrict = Path.GetFileName(path) == "嫣然心水.json" && rule.Id == "小骚货";
                bool explicitZhanShaStrict = Path.GetFileName(path) == "新澳高手.json"
                    && rule.Id is "斩杀半波" or "斩杀一行" or "斩杀两尾" or "斩杀两肖" or "斩杀一头" or "天空杀";
                Assert.Equal(hardenedCatalog || explicitYanranStrict || explicitZhanShaStrict, rule.StrictIssueBlock);
            }
        }
    }

    [Theory]
    [InlineData("头", "公子禁止1头", "翩翩公子头", "公子禁止1头：零头 准！", "0头")]
    [InlineData("生肖", "公子杀一肖", "翩翩公子肖", "公子杀一肖：龍 龍 龍 准！", "龙")]
    public void SplitAndBarePeriodRowsWorkWithoutChangingTheLegacyDefault(string type, string keyword, string id, string payload, string expected)
    {
        OcrRule rule = Rule(id);
        Assert.Equal(expected, RuleEngine.ExtractFinalValue(["319期 其他内容 开??", "318", payload, "317期 其他内容"], 318, rule));
        Assert.Equal(expected, RuleEngine.ExtractFinalValue([$"999996期 {payload} 999995期 其他内容"], 999996, rule));
        Assert.False(new OcrRule(keyword, type, id).StrictIssueBlock);
    }

    [Fact]
    public void CompactDigitsAreAcceptedOnlyWhenCompleteAndUnambiguous()
    {
        Assert.Equal("8尾", RuleEngine.ExtractFinalValue(["318期", "公子送尾数：0123456", "79准！"], 318, Rule("翩翩公子尾")));
        Assert.Equal("01 02 03 04 05 06 07 08 09 10", RuleEngine.ExtractFinalValue(["318期绝杀:0102030405 0607080910 开??"], 318, Rule("会员特供杀十码")));
        Assert.Null(RuleEngine.ExtractFinalValue(["318期绝杀:01020304050 0607080910 开??"], 318, Rule("会员特供杀十码")));
    }

    [Theory]
    [InlineData("公子送尾数：0 1 2 3 4 6 7\n8 9 准！ 开??", "5尾")]
    [InlineData("公子送尾数：0123467\n89准！ 牛30中", "5尾")]
    public void PrinceTailReadsTheTwoLineFixedCardBeforeOpeningResult(string text, string expected)
    {
        Assert.Equal(expected, RuleEngine.ExtractFinalValue(["251期 " + text], 251, Rule("翩翩公子尾")));
    }

    [Fact]
    public void NonAsciiOcrDigitsFailSafelyRatherThanThrowing()
    {
        Assert.Null(RuleEngine.ExtractFinalValue(["３１８期 绝杀:01 02 03 04 05 06 07 08 09 10"], 318, Rule("会员特供杀十码")));
        Assert.Null(RuleEngine.ExtractFinalValue(["318期 绝杀:０１ 02 03 04 05 06 07 08 09 10"], 318, Rule("会员特供杀十码")));
    }

    [Theory]
    [InlineData(251)]
    [InlineData(318)]
    public void PianpianTenNumbersNeverBorrowOpeningBannerNumbers(int issue)
    {
        string[] lines = [
            "鸡10 牛06 龙39 47 37 17 刷14",
            $"{issue - 1}期开",
            "翩翩公子妙算杀10码",
            $"{issue}期 公子神算杀10码：03 06 07",
            "11 16 20 21 26 39 47 准！ 开??",
            $"{issue - 1}期 公子神算杀10码：03 04 08",
            "24 29 33 34 44 48 49 准！蛇14中"
        ];
        OcrRule rule = Rule("翩翩公子杀十码");
        Assert.Equal("03 06 07 11 16 20 21 26 39 47", RuleEngine.ExtractFinalValue(lines, issue, rule));
        Assert.Equal("03 04 08 24 29 33 34 44 48 49", RuleEngine.ExtractFinalValue(lines, issue - 1, rule));
        lines[4] = "11 16 20 准！ 开??";
        Assert.Null(RuleEngine.ExtractFinalValue(lines, issue, rule));
        lines[4] = "11 16 20 21 26 39 47 48 准！ 开??";
        Assert.Null(RuleEngine.ExtractFinalValue(lines, issue, rule));
    }

    [Fact]
    public void SharedFinalResultEntryRejectsBadValuesAndAcceptsAValidatedRetry()
    {
        OcrRule[] rules = [Rule("翩翩公子头")];
        var values = new Dictionary<string, string>();
        var apply = typeof(MainForm).GetMethod("AddExtractedValues", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        apply.Invoke(null, [new[] { "318期 公子禁止1头：准！", "317期 公子禁止1头：四头 准！" }, rules, 318, values]);
        Assert.Empty(values);
        apply.Invoke(null, [new[] { "318期 公子禁止1头：零头 准！" }, rules, 318, values]);
        Assert.Equal("0头", values[rules[0].Id]);
        Assert.Equal("0头 翩翩公子头", Assert.Single(RuleEngine.FormatOutput(rules, values)));
    }
}
