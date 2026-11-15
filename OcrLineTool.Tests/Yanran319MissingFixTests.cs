using OcrLineTool;

namespace OcrLineTool.Tests;

// 2026-11-15（319 期）两张真实卡片的回归：
// 1. 欧阳半波：整张多列表里右下角「318期禁红双 / 319期禁蓝单」与上面的禁肖行
//    被本地 OCR 交错排开，按「欧阳」区段回溯会被其它期号行截断，值因此丢掉。
// 2. 东南仔：【独行客】卡的作者名竖排在右侧，OCR 拆成「东 / 南 / 用仔」，
//    规则改按卡面标题「独行客」认卡；值行「319区杀-猴++兔」只取第一个生肖。
public sealed class Yanran319MissingFixTests
{
    private static IReadOnlyList<OcrRule> Rules() => RuleCatalog.Load(Path.Combine(
        ResultFilePaths.ConfigurationDirectory(AppContext.BaseDirectory), "嫣然心水.json"));

    [Fact]
    public void OuyangHalfWaveReadsTheBottomRightColorRow()
    {
        IReadOnlyList<OcrRule> rules = Rules();
        OcrRule rule = Assert.Single(rules, item => item.Id == "欧阳半波");
        string[] lines =
        [
            "RA", "不忘初心", "方得始终", "新澳门六合彩", "63-265×15", "291期禁猪", "266期禁虎",
            "292期禁虎", "欧阳", "267期禁猴", "293期禁羊", "268期禁鼠", "294期", "月禁猴",
            "269期禁羊", "295期", "禁猴", "270期禁鸡", "296期", "月禁兔", "271期禁羊", "297期",
            "禁虎", "272期", "月禁鼠", "298期", "禁狗", "273期禁牛", "299期", "月禁兔", "274期",
            "月禁龙", "316期禁牛", "300期", "禁猴", "275期", "301期禁马", "禁牛", "318期",
            "276期", "302期", "禁狗", "277期", "禁蛇", "319期禁马", "303期", "禁马", "278期",
            "月禁虎", "禁猪", "279期禁牛", "304期", "虎兔羊羊", "318期禁红双", "305期", "280期禁猴",
            "禁", "319期禁蓝单", "281期禁马", "306期", "禁"
        ];
        string path = @"C:\结果\11.15-嫣然心水\乖乖团队\card.jpg";

        Assert.Equal(["欧阳半波"], RuleEngine.FindMatches(path, lines, [rule], rules).Select(item => item.Id));
        Assert.Equal("蓝单", RuleEngine.ExtractValue(lines, 319, rule));
    }

    [Fact]
    public void DongnanZaiIsIdentifiedByItsPrintedTitleAndKeepsTheFirstZodiac()
    {
        IReadOnlyList<OcrRule> rules = Rules();
        OcrRule rule = Assert.Single(rules, item => item.Id == "东南仔");
        string[] lines =
        [
            "【独行客】", "NEW", "新澳", "禁", "1", "霄",
            "310区杀-牛++猴", "311区杀-兔++猪", "东", "312区杀-鸡++猪", "313区杀-虎++龙",
            "314区杀-马++狗", "315区杀-龙++鼠", "316区杀-猴++牛", "317区杀-牛++羊", "南",
            "318区杀-羊++鼠", "用仔", "319区杀-猴++兔", "慎参、连错了", "收装农装袋"
        ];
        string path = @"C:\结果\11.15-嫣然心水\东南仔\card.jpg";
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            [@"C:\结果\11.15-嫣然心水\东南仔"] = 2
        };

        Assert.Equal(["东南仔"], RuleEngine.FindMatches(path, lines, [rule], rules, counts).Select(item => item.Id));
        Assert.Equal("猴", RuleEngine.ExtractValue(lines, 319, rule));
    }
}
