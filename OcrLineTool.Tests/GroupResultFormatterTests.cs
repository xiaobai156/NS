using OcrLineTool;

namespace OcrLineTool.Tests;

public sealed class GroupResultFormatterTests
{
    [Fact]
    public void GroupsLinesByBusinessTypeAndPreservesValuesMissingReasonsAndDistributionMarks()
    {
        OcrRule[] rules =
        [
            new("合资料", "合", "依然公主"),
            new("肖资料", "生肖", "南国挽心"),
            new("号码资料", "号码:10", "会员特供杀十码"),
            new("大围资料", "号码:36", "表弟"),
            new("尾资料", "缺尾", "翩翩公子尾")
        ];
        string[] lines =
        [
            "03合 依然公主（已分流）",
            "虎 南国挽心",
            "02,04,05,13,20,28,30,34,38,41 会员特供杀十码（已分流）",
            "01,03,04,05,06,09,10,11,13,14,16,17,18,20,22,23,24,25,26,27,28,29,30,31,32,33,34,35,37,41,42,43,45,46,47,49 表弟",
            "缺失（未识别到当期目标数据） 翩翩公子尾"
        ];

        Assert.Equal(
        [
            "【尾】",
            "缺失（未识别到当期目标数据） 翩翩公子尾",
            "",
            "【一肖】",
            "虎 南国挽心",
            "",
            "【5个以上数字】",
            "02,04,05,13,20,28,30,34,38,41 会员特供杀十码（已分流）",
            "",
            "【30个以上数字】",
            "01,03,04,05,06,09,10,11,13,14,16,17,18,20,22,23,24,25,26,27,28,29,30,31,32,33,34,35,37,41,42,43,45,46,47,49 表弟",
            "",
            "【合】",
            "03合 依然公主（已分流）"
        ],
        GroupResultFormatter.Format(rules, lines));
    }

    [Fact]
    public void ReformattingAnOrganizedFileDoesNotDuplicateHeadersOrBlankLines()
    {
        OcrRule[] rules = [new("肖资料", "生肖", "南国挽心")];
        string[] lines = ["【肖】", "虎 南国挽心", "", "【其他】", "无法识别的旧行"];

        Assert.Equal(
            ["【一肖】", "虎 南国挽心", "", "【其他】", "无法识别的旧行"],
            GroupResultFormatter.Format(rules, lines));
    }

    [Fact]
    public void KeepsNineZodiacResultsOutOfOneAndTwoZodiacSections()
    {
        OcrRule[] rules = [new("解九肖", "九肖", "跑狗")];

        Assert.Equal(
            ["【九肖】", "羊猪鸡马蛇兔猴牛狗 跑狗"],
            GroupResultFormatter.Format(rules, ["羊猪鸡马蛇兔猴牛狗 跑狗"]));
    }

    [Fact]
    public void GroupsStatisticZodiacResultsWithOneZodiacResults()
    {
        OcrRule[] rules = [new("品鉴", "统计生肖")];

        Assert.Equal(
            ["【一肖】", "狗猴 品鉴"],
            GroupResultFormatter.Format(rules, ["狗猴 品鉴"]));
    }

    // 你手工改过的行（值不再是"缺失"）算已经有了结论：复抓跳过、重写时原样保留。
    [Fact]
    public void ConcludedValueLinesArePickedUpAndReappliedOverComputedValues()
    {
        OcrRule[] rules =
        [
            new("雁塔题名", "头", "雁塔题名杀头"),
            new("恩平", "头", "恩平杀头"),
            new("齐天大圣", "头", "齐天大圣")
        ];
        string[] existing =
        [
            "【头】",
            "3头 雁塔题名杀头",
            "4头 齐天大圣（已分流）",
            "缺失（未找到对应图片） 恩平杀头"
        ];

        Dictionary<string, string> concluded = GroupResultFormatter.ReadConcludedValueLines(existing, rules);

        Assert.Equal(2, concluded.Count);
        Assert.Equal("3头 雁塔题名杀头", concluded["雁塔题名杀头"]);
        Assert.Equal("4头 齐天大圣（已分流）", concluded["齐天大圣"]);
        Assert.DoesNotContain("恩平杀头", concluded.Keys);

        // 复抓这一轮算出来的值不能覆盖你手工写的行。
        string[] computed = ["缺失（未找到对应图片） 雁塔题名杀头", "4头 齐天大圣", "2头 恩平杀头"];
        Assert.Equal(
            ["3头 雁塔题名杀头", "4头 齐天大圣（已分流）", "2头 恩平杀头"],
            GroupResultFormatter.ReapplyConcludedValueLines(computed, rules, concluded));
    }

    [Fact]
    public void RetryScopeKeepsOnlyRulesWithoutValuesAndWithoutUserConclusions()
    {
        OcrRule[] rules =
        [
            new("雁塔题名", "头", "雁塔题名杀头"),
            new("恩平", "头", "恩平杀头"),
            new("小骚货", "九肖", "小骚货")
        ];
        Dictionary<string, string> concluded = new(StringComparer.Ordinal)
        {
            ["雁塔题名杀头"] = "3头 雁塔题名杀头"
        };

        OcrRule[] scope = GroupResultFormatter.MissingRetryRules(rules, ["小骚货"], concluded);

        Assert.Equal(["恩平杀头"], scope.Select(rule => rule.Id));
    }
}
