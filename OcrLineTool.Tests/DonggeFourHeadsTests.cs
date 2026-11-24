using OcrLineTool;

namespace OcrLineTool.Tests;

// 2026-11-19（323 期）嫣然心水「东哥四头」：抓到的是一张没有标题的四头表，
// 每期一行「323期0124头开：」，规则原来要求卡面出现「至尊分享四头」，
// 这张截出来的纯表格没有标题，识别时被判成「未找到对应图片」。
public sealed class DonggeFourHeadsTests
{
    private static IReadOnlyList<OcrRule> Rules() => RuleCatalog.Load(Path.Combine(
        ResultFilePaths.ConfigurationDirectory(AppContext.BaseDirectory), "嫣然心水.json"));

    private static string[] HeaderlessTable() =>
    [
        "298期0123头开：08【[耶][發]】",
        "299期0124头开：23【[耶][發]】",
        "300期1234头开：38【[耶][發]】",
        "301期0123头开:17",
        "【[耶][發]】",
        "302期0124头开：45【[耶][發]】",
        "303期1234头开:17",
        "【[耶][發]】",
        "304期0234头开：21",
        "【[耶][發]】",
        "305期0234头开：26【[耶][發]】",
        "306期1234头开：15【[耶][發]】",
        "307期0234头开：42【[耶][發]】",
        "308期0134头开：46【[耶][發]】",
        "309期0134头开:13",
        "【[耶][發]】",
        "310期0124头开：46",
        "【[耶][發]】",
        "311期0124头开：34【[裂开][调",
        "谢]】",
        "312期0134头开：38【[耶][發]】",
        "313期1234头开:43",
        "【[耶][發]】",
        "314期1234头开:31",
        "【[耶][發]】",
        "315期0123头开:48",
        "【[裂开][凋",
        "谢]】",
        "316期0134头开：03【[耶][發]】",
        "317期0234头开：26【[耶][發]】",
        "318期0134头开：38【[耶][發]】",
        "319期0124头开：05【[耶][發]】",
        "320期0124头开：20【[耶][發]】",
        "321期0234头开:42",
        "【[耶][發]】",
        "322期1234头开:07【[裂开][凋",
        "谢]】",
        "323期0124头开：",
        "【?】",
        "1"
    ];

    [Fact]
    public void HeaderlessFourHeadTableIsIdentifiedByItsRowShape()
    {
        IReadOnlyList<OcrRule> rules = Rules();
        OcrRule rule = Assert.Single(rules, item => item.Id == "东哥四头");
        string[] lines = HeaderlessTable();
        string path = @"C:\结果\11.19-嫣然心水\东哥\20261119_185953_0f1e4b12_14258.jpg";

        Assert.Equal(["东哥四头"], RuleEngine.FindMatches(path, lines, [rule], rules).Select(item => item.Id));
        Assert.Equal("3头", RuleEngine.ExtractFinalValue(lines, 323, rule));
    }

    [Fact]
    public void AnotherMaterialInTheSameFolderIsNotMistakenForTheFourHeadTable()
    {
        IReadOnlyList<OcrRule> rules = Rules();
        OcrRule rule = Assert.Single(rules, item => item.Id == "东哥四头");
        string[] lines =
        [
            "【至尊分享杀四肖地摊料】",
            "322期杀四肖:龙蛇猴狗",
            "323期杀四肖:鼠龙马猪",
            "开：【?】"
        ];
        string path = @"C:\结果\11.19-嫣然心水\东哥\20261119_185953_0f1e4b12_14259.jpg";

        Assert.Empty(RuleEngine.FindMatches(path, lines, [rule], rules));
    }
}
