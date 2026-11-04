using OcrLineTool;

namespace OcrLineTool.Tests;

// 嫣然心水 308 期三条缺失的回归：
//  - 小雨婷：同一张卡上还有同期的四肖栏，单肖取值不能被「龙虎鼠鸡」带偏；
//  - 烟草味：卡面名字是竖排水印，OCR 读成「烟草」+「味」，只能靠目录认卡；
//  - 东哥半头：卡面标题实际写的是「绝杀半头单双(杀五码)」。
public sealed class Yanran308MissingFixTests
{
    private static IReadOnlyList<OcrRule> Rules => RuleCatalog.Load(Path.Combine(
        ResultFilePaths.ConfigurationDirectory(AppContext.BaseDirectory), "嫣然心水.json"));

    private static OcrRule Rule(string id) => Rules.Single(rule => rule.Id == id);

    private static string ImagePath(string folder, string fileName) =>
        Path.Combine(@"C:\Users\Administrator\Desktop\每天工具\飞机抓图\结果\11.4-嫣然心水", folder, fileName);

    private static IReadOnlyList<OcrRule> Match(string folder, string fileName, string[] lines) =>
        RuleEngine.FindMatches(ImagePath(folder, fileName), lines, Rules, Rules);

    [Fact]
    public void SingleZodiacRowWinsOverTheFourZodiacSectionOnTheSameCard()
    {
        string[] lines =
        [
            "❤小雨婷",
            "305期禁【羊】开蛇",
            "306期禁【兔】开龙",
            "307期禁【猴】开牛",
            "308期禁【龙】开猫",
            "禁四肖",
            "306期禁",
            "【马兔蛇猴】",
            "开15",
            "307期禁",
            "【猴羊牛蛇】",
            "开42",
            "308期禁",
            "【龙虎鼠鸡】",
            "开猫"
        ];

        OcrRule rule = Rule("小雨婷");
        Assert.Contains(Match("小雨婷", "20261104_123022_0f1e4b12_7935.jpg", lines),
            item => item.Id == rule.Id);
        Assert.Equal("龙", RuleEngine.ExtractFinalValue(lines, 308, rule));
    }

    [Fact]
    public void FourZodiacOnlyCardStillReportsNoSingleValue()
    {
        // 只剩四肖栏时必须判缺失，不能拿「龙」冒充单肖。
        string[] lines = ["小雨婷", "308期禁", "【龙虎鼠鸡】", "开猫"];
        Assert.Null(RuleEngine.ExtractFinalValue(lines, 308, Rule("小雨婷")));
    }

    [Fact]
    public void TobaccoCardIsIdentifiedByItsFolderBecauseTheNameIsAVerticalWatermark()
    {
        string[] lines =
        [
            "305期", "禁", "一肖猴",
            "306期", "一肖鼠",
            "307期", "月禁一肖虎",
            "308期禁一肖虎"
        ];

        OcrRule rule = Rule("烟草味");
        Assert.Contains(Match("烟草味", "20261104_170815_0f1e4b12_8034.jpg", lines),
            item => item.Id == rule.Id);
        Assert.Equal("虎", RuleEngine.ExtractFinalValue(lines, 308, rule));
    }

    [Fact]
    public void TobaccoFolderNoLongerClaimsAnotherMaterialsCard()
    {
        string[] lines = ["❤沁园春❤", "308❤沁园春❤杀【猴龙】开"];
        Assert.DoesNotContain(Match("烟草味", "20261104_123022_0f1e4b12_7934.jpg", lines),
            item => item.Id == "烟草味");
    }

    [Fact]
    public void HalfHeadCardIsIdentifiedByTheTitleItActuallyPrints()
    {
        string[] lines =
        [
            "307期杀1头双开:",
            "42【[耶][發]】",
            "308期杀3头单开:",
            "【?】",
            "【绝杀半头单双(杀五码)六助",
            "同步】"
        ];

        OcrRule rule = Rule("东哥半头");
        Assert.Contains(Match("东哥", "20261104_165209_0f1e4b12_8031.jpg", lines),
            item => item.Id == rule.Id);
        Assert.Equal("3头单", RuleEngine.ExtractFinalValue(lines, 308, rule));
    }
}
