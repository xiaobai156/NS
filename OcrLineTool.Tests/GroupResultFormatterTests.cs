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
}
