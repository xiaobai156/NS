using OcrLineTool;
using Xunit;

namespace OcrLineTool.Tests;

// 新澳六合彩资料 254 期：宝典杀“精杀12码”第一格与号码同行、其余竖排。
public sealed class Macau254WrappedNumberTests
{
    [Fact]
    public void BaodianKillReadsTheLabelRowNumberAndTheVerticalRest()
    {
        OcrRule rule = RuleCatalog.Load(Path.Combine(
            ResultFilePaths.ConfigurationDirectory(AppContext.BaseDirectory), "新澳六合彩资料.json"))
            .Single(item => item.Id == "宝典杀");
        string[] lines =
        [
            "宝典", "【精杀12码】",
            "精杀12码：03", "04", "05", "10", "11", "14",
            "254期", "开??",
            "39 40", "41", "42", "46", "49",
            "精杀12码：02", "11", "12", "25", "28", "29",
            "253期", "兔16中",
            "31", "36", "37", "39", "42", "44"
        ];

        Assert.Equal(
            "03 04 05 10 11 14 39 40 41 42 46 49",
            RuleEngine.ExtractFinalValue(lines, 254, rule));
    }
}
