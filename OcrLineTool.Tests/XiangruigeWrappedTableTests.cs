using OcrLineTool;
using Xunit;

namespace OcrLineTool.Tests;

// 祥瑞阁 254 期：号码被期号/开奖栏切成上下两段，且证据被分成多个区域。
public sealed class XiangruigeWrappedTableTests
{
    private const string Expected =
        "01 02 03 04 05 07 09 10 12 15 17 19 20 21 22 24 26 28 29 32 33 35 36 37 "
        + "38 39 40 41 42 43 44 45 46 47 48 49";

    private static OcrRule Rule() => RuleCatalog.Load(Path.Combine(
        ResultFilePaths.ConfigurationDirectory(AppContext.BaseDirectory), "新澳高级会员.json"))
        .Single(item => item.Id == "祥瑞阁");

    private static string[] Lines() =>
    [
        "祥瑞阁",
        "祥瑞阁主大包围36码",
        "01 02 03 04 05 07 09 10 12 15 17 19 20 21 22 24 26",
        "254期",
        "28 29 32 33 35 36 37 38 39 40 41 42 43 44 45 46 47",
        "开??",
        "4849",
        "02 03 04 05 06 07 08 11 13 15 16 17 18 19 20 21 22",
        "253期",
        "25 27 28 29 30 31 32 33 34 35 36 37 38 40 41 42 43",
        "兔16中",
        "4447"
    ];

    [Fact]
    public void FlatReadingExtractsTheThirtySixNumbers()
    {
        Assert.Equal(Expected, RuleEngine.ExtractFinalValue(Lines(), 254, Rule()));
    }

    [Fact]
    public void SplitRegionsWithoutPositionedReadingStillExtract()
    {
        string[] lines = Lines();
        var items = new List<OcrLineEvidence>();
        for (int index = 0; index < lines.Length; index++)
        {
            string region = index <= 2 ? "left" : "right";
            items.Add(new OcrLineEvidence(lines[index], null, 0.99, "test", region));
        }
        var evidence = new OcrEvidence(
            "source.png", "input.png", "source-hash", "input-hash", "test", items.ToArray());

        Assert.Equal(Expected, RuleEngine.ExtractFinalResult(evidence, 254, Rule()).Value);
    }
}
