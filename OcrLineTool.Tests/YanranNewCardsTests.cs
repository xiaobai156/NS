using OcrLineTool;

namespace OcrLineTool.Tests;

// 2026-11-19（323 期）嫣然心水新增两张卡：
// 冷酷女王上半段「新澳禁①肖」每期一个生肖、下半段「新澳禁②肖」每期两个生肖；
// 忘忧草每期「杀一肖」一个生肖，卡底部的「=提交…」只是备注，不参与取值。
// 两张卡标题区的 OCR 都认不准（Lofe冷酷e女王 / 惹憂中∮新奥），身份只能靠卡面内容。
public sealed class YanranNewCardsTests
{
    private static IReadOnlyList<OcrRule> Rules() => RuleCatalog.Load(Path.Combine(
        ResultFilePaths.ConfigurationDirectory(AppContext.BaseDirectory), "嫣然心水.json"));

    private static string[] CoolQueenCard() =>
    [
        "Lofe冷酷e女王",
        "新澳禁①肖",
        "304期：禁【牛】",
        "305期：禁【虎】",
        "306期：禁【鸡】",
        "307期：禁【猴】",
        "308期：禁【龙】",
        "309期：禁【牛】",
        "310期：禁【蛇】",
        "311期：禁【猴】",
        "312期：禁【龙】",
        "313期：禁【虎】",
        "314期：禁【鸡】",
        "315期：禁【牛】",
        "316期：禁【羊】",
        "317期：禁【马】",
        "318期：禁【马】",
        "319期：禁【鸡】",
        "320期：禁【蛇】",
        "321期：禁【猴】",
        "322期：【羊】",
        "323期：禁【猪】",
        "新澳禁②肖",
        "322期：禁【兔鸡】开鼠",
        "323期：禁【牛兔】开",
        "仅供参考对错勿怪"
    ];

    private static string[] WangYouCaoCard() =>
    [
        "惹憂中∮新奥",
        "301期杀一肖 马 开虎对",
        "302期杀一肖 猪 开狗对",
        "303期杀一肖 蛇 开虎对",
        "304期杀一肖 兔 开狗对",
        "305期杀一肖 鸡 开蛇对",
        "306期杀一肖 狗 开龙对",
        "307期杀一肖 鸡 开牛对",
        "308期杀一肖 鼠 开鸡对",
        "309期杀一肖 牛 开马对",
        "310期杀一肖 龙 开鸡对",
        "311期杀一肖 龙 开鸡对",
        "312期杀一肖 龙 开蛇对",
        "313期杀一肖 鼠 开鼠错",
        "314期杀一肖 龙 开鼠对",
        "315期杀一肖 马 开羊对",
        "316期杀一肖 马 开龙对",
        "317期杀一肖 鸡 开蛇对",
        "318期杀一肖 猴 开蛇对",
        "319期杀一肖 蛇 开虎对",
        "320期杀一肖 虎 开猪对",
        "321期杀一肖 虎 开虎错",
        "322期杀一肖 猪 开鼠对",
        "323期杀一肖 牛 开",
        "=提交牛+鼠猪兔"
    ];

    [Fact]
    public void CoolQueenCardKeepsItsOneZodiacAndTwoZodiacSectionsApart()
    {
        IReadOnlyList<OcrRule> rules = Rules();
        OcrRule one = Assert.Single(rules, item => item.Id == "冷酷女王杀一肖");
        OcrRule two = Assert.Single(rules, item => item.Id == "冷酷女王两肖");
        string[] lines = CoolQueenCard();
        string path = @"C:\结果\11.19-嫣然心水\冷酷女王\20261119_180023_0f1e4b12_14244.jpg";

        string[] matched = RuleEngine.FindMatches(path, lines, [one, two], rules)
            .Select(item => item.Id).ToArray();
        Assert.Contains("冷酷女王杀一肖", matched);
        Assert.Contains("冷酷女王两肖", matched);
        Assert.Equal("猪", RuleEngine.ExtractFinalValue(lines, 323, one));
        Assert.Equal("牛兔", RuleEngine.ExtractFinalValue(lines, 323, two));
    }

    [Fact]
    public void WangYouCaoCardTakesOnlyTheKillOneZodiacValue()
    {
        IReadOnlyList<OcrRule> rules = Rules();
        OcrRule rule = Assert.Single(rules, item => item.Id == "忘忧草杀一肖");
        string[] lines = WangYouCaoCard();
        string path = @"C:\结果\11.19-嫣然心水\忘忧草\20261119_180024_0f1e4b12_14245.jpg";

        Assert.Equal(["忘忧草杀一肖"], RuleEngine.FindMatches(path, lines, [rule], rules)
            .Select(item => item.Id));
        Assert.Equal("牛", RuleEngine.ExtractFinalValue(lines, 323, rule));
        Assert.DoesNotContain("鼠猪兔", RuleEngine.ExtractFinalValue(lines, 323, rule));
    }
}
