using OcrLineTool;

namespace OcrLineTool.Tests;

// 新澳六合彩资料 群 2026-09-26 新增的两条资料：白小姐四季（新类型「四季」）与白小姐来钱
// （九肖写在开奖行上，需要 allow_opening_row）。行文是当天本机 medium 对裁剪图的真实识别结果。
public sealed class BaixiaojieCardTests
{
    private static IReadOnlyList<OcrRule> Rules => RuleCatalog.Load(Path.Combine(
        ResultFilePaths.ConfigurationDirectory(AppContext.BaseDirectory), "新澳六合彩资料.json"));
    private static OcrRule Rule(string id) => Rules.Single(rule => rule.Id == id);

    private static readonly string[] SeasonsCard =
    [
        "新澳", "48", "34", "2119", "36", "刷", "40", "268期开", "羊", "雞猴狗鼠羊", "兔",
        "白小姐一（四季生肖）预测",
        "春肖：兔虎龙", "夏肖：马蛇羊", "秋肖：鸡猴狗", "冬肖：鼠猪牛",
        "269期", "四季:夏肖秋肖冬肖", "开??",
        "268期", "四季:夏肖秋肖冬肖", "兔40错",
        "267期", "四季:春肖夏肖秋肖", "兔40中",
        "266期", "四季:夏肖秋肖冬肖", "豬08中",
        "265期", "四季:春肖夏肖冬肖", "馬49中",
        "264期", "四季:春肖夏肖冬肖", "狗21错",
        "263期", "四季:夏肖秋肖冬肖", "狗09中",
        "262期", "四季:夏肖秋肖冬肖", "牛30中",
        "261期", "四季:春肖秋肖冬肖", "羊24错",
        "260期", "四季:春肖夏肖秋肖", "豬20错",
        "259期", "四季:春肖秋肖冬肖", "雞22中",
        "258期", "四季:春肖秋肖冬肖", "雞46中",
        "257期", "四季·春肖夏肖各肖", "鼠07中"
    ];

    private static readonly string[] MoneyCard =
    [
        "新澳", "48341121193640", "268期开", "羊雞猴狗鼠羊新兔",
        "白小姐九肖来钱",
        "269期", "开:狗豬牛龍虎羊鼠雞馬", "开??",
        "268期", "开:豬蛇虎鼠兔狗龍馬雞", "兔40中",
        "267期", "开:羊龍猴牛馬鼠雞狗蛇", "兔40错",
        "266期", "开:兔牛龍豬蛇鼠雞狗馬", "豬08中",
        "265期", "开:鼠虎蛇豬羊雞兔狗龍", "馬49错",
        "264期", "开:龍鼠牛羊馬兔豬雞狗", "狗21中",
        "263期", "开:豬虎羊狗雞兔龍蛇馬", "狗09中",
        "262期", "开:猴雞馬龍牛羊蛇狗鼠", "牛30中",
        "261期", "开:兔雞羊牛狗豬龍猴虎", "羊24中",
        "260期", "开:虎豬羊蛇牛馬猴雞鼠", "豬20中",
        "259期", "开:龍牛馬猴蛇鼠雞兔豬", "雞22中",
        "258期", "开:豬虎猴狗羊牛雞鼠龍", "雞46中",
        "257期", "开:猴鼠牛兔豬馬龍羊蛇", "鼠07中",
        "256期", "开:龍狗羊馬豬蛇牛虎兔", "馬01中",
        "255期", "开:猴蛇鼠虎豬牛龍狗雞", "豬44中"
    ];

    [Fact]
    public void SeasonsCardTurnsTheThreeSeasonsIntoTheNineZodiacsOfTheRequestedIssue()
    {
        // 卡面用三个季节表示九个生肖（四季:夏肖秋肖冬肖），落库时按属性表还原成生肖：
        // 夏=马蛇羊、秋=鸡猴狗、冬=鼠猪牛。
        OcrRule rule = Rule("白小姐四季");
        Assert.Equal("四季", rule.Type);
        Assert.Equal("马蛇羊鸡猴狗鼠猪牛", RuleEngine.ExtractFinalValue(SeasonsCard, 269, rule));
        Assert.Equal("马蛇羊鸡猴狗鼠猪牛", RuleEngine.ExtractFinalValue(SeasonsCard, 268, rule));
        Assert.Equal("兔虎龙马蛇羊鸡猴狗", RuleEngine.ExtractFinalValue(SeasonsCard, 267, rule));
        Assert.Equal("马蛇羊鸡猴狗鼠猪牛", RuleEngine.ExtractFinalValue(SeasonsCard, 266, rule));
        Assert.Null(RuleEngine.ExtractFinalValue(SeasonsCard, 999, rule));

        Assert.True(RuleEngine.IsFormattedOutputValueValid(rule, "马蛇羊鸡猴狗鼠猪牛"));
        Assert.False(RuleEngine.IsFormattedOutputValueValid(rule, "夏秋冬"));        // 季节字不再落库
        Assert.False(RuleEngine.IsFormattedOutputValueValid(rule, "马蛇羊鸡猴狗鼠猪"));   // 只有八个生肖
        Assert.False(RuleEngine.IsFormattedOutputValueValid(rule, "马蛇羊鸡猴狗鼠猪狗"));  // 重复
    }

    [Fact]
    public void SeasonsCardNeedsThreeDifferentSeasonsBeforeItExpands()
    {
        // 重复季节、少于三个季节都不换算，缺项不猜。
        OcrRule rule = Rule("白小姐四季");
        Assert.Null(RuleEngine.ExtractFinalValue(["269期", "四季:夏肖夏肖冬肖"], 269, rule));
        Assert.Null(RuleEngine.ExtractFinalValue(["269期", "四季:夏肖秋肖"], 269, rule));
    }

    [Fact]
    public void MoneyCardReadsTheNineZodiacsFromTheOpeningRow()
    {
        OcrRule rule = Rule("白小姐来钱");
        Assert.Equal("九肖", rule.Type);
        Assert.True(rule.AllowOpeningRow);
        Assert.Equal("狗猪牛龙虎羊鼠鸡马", RuleEngine.ExtractFinalValue(MoneyCard, 269, rule));
        Assert.Equal(
            "狗猪牛龙虎羊鼠鸡马",
            RuleEngine.ExtractFinalValue(["白小姐九肖来钱", "269期", "开:狗豬牛龍虎羊鼠雞馬", "开??"], 269, rule));
        Assert.Null(RuleEngine.ExtractFinalValue(MoneyCard, 999, rule));
    }

    [Fact]
    public void WithoutTheOpeningRowFlagTheNineZodiacRowIsStillSkipped()
    {
        // 开奖行的「开」前缀是既有保护（上期开奖横幅不是数据行），这里是它必须显式放行的证据。
        OcrRule withoutFlag = Rule("白小姐来钱") with { AllowOpeningRow = false };
        Assert.Null(RuleEngine.ExtractFinalValue(MoneyCard, 269, withoutFlag));
    }

    [Fact]
    public void TheTwoMaterialsHaveTheirOwnProductionTemplates()
    {
        var catalog = VisualTemplateMatcher.Load(Path.Combine(
            ResultFilePaths.ConfigurationDirectory(AppContext.BaseDirectory), "新澳六合彩资料.templates.json"));
        foreach (string label in new[] { "白小姐四季", "白小姐来钱" })
        {
            var template = Assert.Single(catalog.Templates, item => item.Id == label);
            Assert.Equal([label], template.RuleIds);
            Assert.Equal(256, template.Fingerprint.Length);
        }
    }

    [Fact]
    public void SeasonsValueIsFiledUnderTheNineZodiacCategory()
    {
        string[] formatted = GroupResultFormatter.Format(
            [Rule("白小姐四季"), Rule("白小姐来钱")],
            ["马蛇羊鸡猴狗鼠猪牛 白小姐四季", "狗猪牛龙虎羊鼠鸡马 白小姐来钱"]);
        Assert.Equal(
            ["【九肖】", "马蛇羊鸡猴狗鼠猪牛 白小姐四季", "狗猪牛龙虎羊鼠鸡马 白小姐来钱"],
            formatted);
    }
}
