using OcrLineTool;
using Xunit;

namespace OcrLineTool.Tests;

// 用户提供的其余卡面（今天目录里没有图），用真实版式锁定命中与取值。
public sealed class YanranPendingCardsTests
{
    private static OcrRule[] Rules() => RuleCatalog.Load(
        Path.Combine(ResultFilePaths.ConfigurationDirectory(AppContext.BaseDirectory), "嫣然心水.json"))
        .ToArray();

    private static OcrRule Rule(string id) => Assert.Single(Rules(), rule => rule.Id == id);

    [Fact]
    public void TobaccaoFlavorReadsItsOwnForbiddenZodiac()
    {
        OcrRule rule = Rule("烟草味");
        string image = @"C:\结果\9.10-嫣然心水\烟草味\sample.jpg";
        string[] lines = ["❤淡淡的烟草味杀肖❤", "248期禁一肖牛"];

        Assert.Single(RuleEngine.FindMatches(image, lines, [rule], Rules()));
        Assert.Equal("牛", RuleEngine.ExtractFinalValue(lines, 248, rule));
    }

    [Fact]
    public void ColaBoyAndRainStarMatchTheirTianjigeRows()
    {
        OcrRule cola = Rule("可乐仔");
        OcrRule rain = Rule("雨后星星");
        string folder = @"C:\结果\9.10-嫣然心水\天机阁杀料";
        string[] colaLines = ["252期：新澳门【天机阁论坛●可乐仔㊣㊣㊣杀一肖】猪 开蛇 对"];
        string[] rainLines = ["252期：天机阁雨后星星㊣绝杀合→→05合 开22 准"];

        Assert.Single(RuleEngine.FindMatches(Path.Combine(folder, "a.jpg"), colaLines, [cola], Rules()));
        Assert.Single(RuleEngine.FindMatches(Path.Combine(folder, "b.jpg"), rainLines, [rain], Rules()));
        Assert.Equal("猪", RuleEngine.ExtractFinalValue(colaLines, 252, cola));
        Assert.Equal("05合", RuleEngine.ExtractFinalValue(rainLines, 252, rain));
    }

    [Fact]
    public void ChaoshanChenlongReadsThreeNumbers()
    {
        OcrRule rule = Rule("潮汕陈龙杀三码");
        string[] lines = ["新澳门六合彩", "潮汕陈龙", "251期禁 09.19.31✓", "252期禁 05.17.29"];

        Assert.Equal("05 17 29", RuleEngine.ExtractFinalValue(lines, 252, rule));
    }

    [Fact]
    public void AliceReadsTwoNumbersBeforeTheOpeningColumn()
    {
        OcrRule rule = Rule("Alice两码");
        string[] lines = ["Alice", "251期 禁 10.37 开30✓", "252期 禁 06.45 开"];

        Assert.Equal("06 45", RuleEngine.ExtractFinalValue(lines, 252, rule));
    }

    [Fact]
    public void YongbuqiInfersTheMissingHead()
    {
        OcrRule rule = Rule("永卟弃杀头");
        string[] lines = ["永卟弃", "251期中 1234头✓", "252期中 0134头"];

        Assert.Equal("2头", RuleEngine.ExtractFinalValue(lines, 252, rule));
    }

    [Fact]
    public void OuyangHalfWaveReadsItsSingleValueRow()
    {
        OcrRule rule = Rule("欧阳半波");
        string[] lines = ["欧阳", "251期禁 绿双✓", "252期禁 绿单"];

        Assert.Equal("绿单", RuleEngine.ExtractFinalValue(lines, 252, rule));
    }

    [Fact]
    public void QinchaiFormulasUseTheirOwnSections()
    {
        OcrRule first = Rule("钦差大臣公式一");
        OcrRule second = Rule("钦差大臣公式二");
        string[] lines =
        [
            "钦差大臣", "澳门杀一肖(公式一)",
            "251期杀《牛》 开:30x", "252期杀《鸡》 开:00√",
            "仅供参考！对错勿怪！钦差大臣 澳门杀一肖(公式二)",
            "251期杀《猴》 开:牛√", "252期杀《牛》 开:驴√"
        ];

        Assert.Equal("鸡", RuleEngine.ExtractFinalValue(lines, 252, first));
        Assert.Equal("牛", RuleEngine.ExtractFinalValue(lines, 252, second));
    }
}
