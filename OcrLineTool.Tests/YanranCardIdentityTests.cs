using OcrLineTool;

namespace OcrLineTool.Tests;

// 304 期嫣然心水六条失败的回归守卫：
// 四条卡上读不出作者名（候选阶段判「未找到对应图片」），靠 allow_folder_identity 认夹子；
// 缘来如此同一张卡印了两段（杀一肖 / 杀一尾），同夹规则互相成为 peer，
// 按 peer 切边界会把期号切掉导致取不到值，所以要 ignore_peer_keywords。
// 另外简单爱/欧阳肖那张两列并排的统计表要用 Small 模型才读得稳。
public sealed class YanranCardIdentityTests
{
    private static OcrRule[] Rules() => RuleCatalog.Load(
        Path.Combine(ResultFilePaths.ConfigurationDirectory(AppContext.BaseDirectory), "嫣然心水.json"))
        .ToArray();

    private static OcrRule Rule(string id) => Rules().Single(rule => rule.Id == id);

    // 缘来如此那张卡的真实 OCR 行结构（同一张图上半段杀一肖、下半段杀一尾）。
    private static readonly string[] YuanLaiRuCiCard =
    [
        "288期杀一月【猴】开25√",
        "293期杀一肖【鸡】开05√",
        "303期杀一肖【鸡】开17√",
        "304期杀一肖【鸡】开00√缘来如此",
        "《新澳门杀一尾》",
        "303期杀一尾【5】开17√",
        "304期杀一尾【5】开00√",
        "《新澳门平特一肖》",
        "304期平特一肖【鼠】送虎开00√",
    ];

    [Fact]
    public void TheYuanLaiRuCiCardTakesItsOwnCellNotTheNeighbourSection()
    {
        OcrRule rule = Rule("缘来如此杀一肖");
        // 目录加载时同夹的「杀一尾」会成为 peer；开着 ignore_peer_keywords 才能保住期号。
        Assert.NotEmpty(rule.PeerKeywords ?? []);
        Assert.True(rule.IgnorePeerKeywords);
        Assert.Equal("鸡", RuleEngine.ExtractFinalValue(YuanLaiRuCiCard, 304, rule));
        Assert.Equal("5尾", RuleEngine.ExtractFinalValue(YuanLaiRuCiCard, 304, Rule("缘来如此尾")));
        // 没有本期那一行就判缺失，绝不拿上一期顶上。
        Assert.Null(RuleEngine.ExtractFinalValue(YuanLaiRuCiCard, 305, rule));
    }

    [Fact]
    public void WithoutThePeerOptOutTheIssueNumberIsCutOffAndTheRowIsLost()
    {
        // 钉住修复前的表现：peer 切边界会把「304期」切走，期号行不再成立。
        OcrRule rule = Rule("缘来如此杀一肖") with { IgnorePeerKeywords = false };
        Assert.Null(RuleEngine.ExtractFinalValue(YuanLaiRuCiCard, 304, rule));
    }

    [Theory]
    [InlineData("东南仔")]
    [InlineData("送钱宝宝")]
    [InlineData("缘来如此杀一肖")]
    [InlineData("缘来如此尾")]
    public void TheUnreadableAuthorCardsIdentifyThemselvesByFolder(string id)
    {
        // 这几张卡上的作者名 OCR 读不出来（或干脆被裁掉），只能靠夹子认身份。
        Assert.True(Rule(id).AllowFolderIdentity);
    }

    [Theory]
    [InlineData("简单爱")]
    [InlineData("欧阳肖")]
    [InlineData("苏柒若")]
    [InlineData("月来月好")]
    [InlineData("陈思思")]
    public void TheTwoColumnSummaryCardsAskForTheSmallModel(string id)
    {
        // 乖乖团队这几张两列并排的统计表，Medium 会把值列读丢（实测 6451 张 Medium 取不到、
        // Small 取到），规则要求补读时换 Small 重读。
        Assert.Equal("small", Rule(id).ReadModel);
    }

    [Fact]
    public void TheOtherYanranRulesKeepThePrimaryModelAndThePeerBoundary()
    {
        foreach (OcrRule rule in Rules())
        {
            if (rule.Id is "简单爱" or "欧阳肖" or "苏柒若" or "月来月好" or "陈思思")
                continue;
            Assert.Null(rule.ReadModel);
            if (rule.Id is "缘来如此杀一肖" or "缘来如此尾")
                continue;
            Assert.False(rule.IgnorePeerKeywords);
        }
    }

    [Fact]
    public void AnUnknownReadModelFailsLoudlyInsteadOfFallingBack()
    {
        string folder = Path.Combine(Path.GetTempPath(), "read-model-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, "测试群.json");
        try
        {
            File.WriteAllText(path, """
                {
                  "version": 1,
                  "group": "测试群",
                  "rules": [
                    { "keyword": "甲", "type": "生肖", "read_model": "large" }
                  ]
                }
                """);
            OcrException exception = Assert.Throws<OcrException>(() => RuleCatalog.Load(path));
            Assert.Contains("read_model", exception.Message, StringComparison.Ordinal);
            Assert.Contains("large", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
