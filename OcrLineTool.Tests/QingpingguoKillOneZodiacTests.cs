using OcrLineTool;
using System.Text;
using System.Text.Json;

namespace OcrLineTool.Tests;

// 青苹果杀一肖：与“青苹果（杀六码）”同处 青苹果 文件夹，卡面为 {期号}新澳杀①肖《X》开…
// 文本取自真实图片（10.17-嫣然心水\青苹果）的本地 OCR，见
// artifacts/qingpingguo-20261017/ocr-gpu-small.json。真实识别会把《》丢掉或读成“<”，
// 所以判定只能依赖“开”之前的那个生肖，不能依赖书名号。
public sealed class QingpingguoKillOneZodiacTests
{
    private const string PathInFolder = @"C:\图片\10.17-嫣然心水\青苹果\card.jpg";

    private static readonly string[] RealOcrLines =
    [
        "新澳六合彩", "爱", "原创青苹果", "原创青苹果",
        "{289期}新澳杀①肖猪开猫50对",
        "{288期}新澳杀①肖<猪开马25对",
        "{287期}新澳杀①肖<马开猪08对",
        "{286期}新澳杀①肖鸡开羊24对",
        "{285期}新澳杀①肖<虎开鼠19对",
        "{284期}新澳杀①肖猴开猪32对",
        "{283期}新澳杀①肖鸡开牛18对",
        "{282期}新澳杀①肖<猴开猴23错",
        "{281期}新澳杀①肖<蛇开鸡10对",
        "{280期}新澳杀①肖<牛开龙15对",
        "{279期}新澳杀①肖猴开鸡10对",
        "{278期}新澳杀①肖<虎开蛇02对",
        "{277期}新澳杀①肖<虎开猴11对",
        "{276期}新澳杀①肖<鼠开蛇14对"
    ];

    private static IReadOnlyList<OcrRule> Rules => RuleCatalog.Load(Path.Combine(
        ResultFilePaths.ConfigurationDirectory(AppContext.BaseDirectory), "嫣然心水.json"));

    private static OcrRule Rule => Rules.Single(rule => rule.Id == "青苹果杀一肖");

    private static string DistributionConfigPath => Path.Combine(
        ResultFilePaths.ConfigurationDirectory(AppContext.BaseDirectory), "肖新增分发规则.json");

    [Theory]
    [InlineData(289, "猪")]
    [InlineData(288, "猪")]
    [InlineData(287, "马")]
    [InlineData(286, "鸡")]
    [InlineData(285, "虎")]
    [InlineData(284, "猴")]
    [InlineData(283, "鸡")]
    [InlineData(282, "猴")]
    [InlineData(281, "蛇")]
    [InlineData(280, "牛")]
    [InlineData(279, "猴")]
    [InlineData(278, "虎")]
    [InlineData(277, "虎")]
    [InlineData(276, "鼠")]
    public void RealCardYieldsTheZodiacBeforeTheOpening(int issue, string expected) =>
        Assert.Equal(expected, RuleEngine.ExtractFinalValue(RealOcrLines, issue, Rule));

    [Fact]
    public void IssueWithoutItsOwnRowStaysMissing()
    {
        Assert.Null(RuleEngine.ExtractFinalValue(RealOcrLines, 290, Rule));
    }

    [Fact]
    public void RealCardIsSelectedAsCandidate()
    {
        Assert.Contains(RuleEngine.FindMatches(PathInFolder, RealOcrLines, [Rule], Rules), item => item.Id == Rule.Id);
    }

    // 同文件夹的“杀六码”卡（只有号码，没有《X》行）。版式按现有规则推算，
    // 只用于验证“绝不可能从兄弟卡取值”，不作为该卡的真实版式依据。
    private static readonly string[] SiblingKillSixLines =
    [
        "新澳六合彩", "原创青苹果",
        "{289期}杀六码 01 02 03 04 05 06",
        "{288期}杀六码 07 08 09 10 11 12"
    ];

    [Fact]
    public void SiblingCardCanNeverProvideTheValue()
    {
        // 兄弟卡（同一作者、同一文件夹）没有“新澳杀…《X》”行，取不出值。
        // 身份关键词同为“青苹果”，所以候选层允许入选；正确性由“取不出值”保证。
        Assert.Null(RuleEngine.ExtractFinalValue(SiblingKillSixLines, 289, Rule));
    }

    [Fact]
    public void PlannerPicksTheCardThatHoldsTheTargetValue()
    {
        const string card = @"C:\图片\10.17-嫣然心水\青苹果\青苹果杀一肖.jpg";
        const string sibling = @"C:\图片\10.17-嫣然心水\青苹果\青苹果杀六码.jpg";
        var local = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            [card] = RealOcrLines,
            [sibling] = SiblingKillSixLines
        };

        IReadOnlyList<LocalCandidatePlan> plans = LocalCandidatePlanner.Build([card, sibling], local, Rules, 289, Rules);

        LocalCandidatePlan plan = Assert.Single(plans, item => item.Path == card);
        Assert.True(plan.IsPrimary);
        Assert.Contains(Rule.Id, plan.Rules.Select(rule => rule.Id));
    }

    [Fact]
    public void ForeignAuthorCardIsNotSelected()
    {
        // 别家作者卡（紫燕儿）行形状相同，但不是这张：不在 青苹果 文件夹、也没有“青苹果”。
        // 注意取值本身只看行形状，身份把关在候选层，所以这里只断言“不入选”。
        string[] foreignCard = ["紫燕儿", "{289期}新澳杀<猪开猫50对"];
        Assert.DoesNotContain(RuleEngine.FindMatches(PathInFolder, foreignCard, [Rule], Rules), item => item.Id == Rule.Id);
        Assert.False(RuleEngine.MatchesRuleFolder(@"C:\图片\10.17-嫣然心水\紫燕儿\card.jpg", Rule));
    }

    [Fact]
    public void KillSixRuleDoesNotReadThisCard()
    {
        OcrRule killSix = Rules.Single(rule => rule.Id == "青苹果");
        Assert.Null(RuleEngine.ExtractFinalValue(RealOcrLines, 289, killSix));
    }

    [Fact]
    public async Task ValueIsDistributedToZodiacAdditions()
    {
        string temp = Directory.CreateTempSubdirectory("ocr-qingpingguo-").FullName;
        try
        {
            string group = Directory.CreateDirectory(Path.Combine(temp, "10.17-嫣然心水")).FullName;
            string target = Directory.CreateDirectory(Path.Combine(temp, "out")).FullName;
            string targetFile = Path.Combine(target, "289期-肖-新增.txt");
            await File.WriteAllTextAsync(targetFile, "生肖次数排行榜" + Environment.NewLine, new UTF8Encoding(false));

            var values = new ResultValues(StringComparer.Ordinal);
            ResultValues.AddTo(values, Rule.Id, RuleEngine.ExtractFinalValue(RealOcrLines, 289, Rule)!);
            string[] lines = RuleEngine.FormatOutput([Rule], values);
            Assert.Contains("猪 青苹果杀一肖", lines);

            IReadOnlySet<string> written = await ResultDistributor.DistributeAsync(
                group, 289, lines, target, DistributionConfigPath);

            Assert.Contains("猪 青苹果杀一肖", written);
            Assert.Contains("猪 青苹果杀一肖", await File.ReadAllTextAsync(targetFile));
        }
        finally
        {
            Directory.Delete(temp, recursive: true);
        }
    }

    [Fact]
    public void RuleAndDistributionAreConfigured()
    {
        Assert.Equal("生肖", Rule.Type);
        Assert.Equal("青苹果", Rule.Folder);
        Assert.Equal("青苹果", Rule.RequiredKeyword);

        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(DistributionConfigPath));
        JsonElement source = Assert.Single(
            document.RootElement.GetProperty("sources").EnumerateArray(),
            item => item.GetProperty("sourceGroup").GetString() == "嫣然心水");
        Assert.Contains("青苹果杀一肖",
            source.GetProperty("labels").EnumerateArray().Select(item => item.GetString()));
    }
}
