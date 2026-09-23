using OcrLineTool;
using System.Text.Json;

namespace OcrLineTool.Tests;

public sealed class YanranFormatStabilityTests
{
    private static IReadOnlyList<OcrRule> Rules => RuleCatalog.Load(Path.Combine(
        ResultFilePaths.ConfigurationDirectory(AppContext.BaseDirectory), "嫣然心水.json"));

    private static OcrRule Rule(string id) => Rules.Single(rule => rule.Id == id);

    [Fact]
    public void YantaKeepsConflictingPartitionEvidenceInsteadOfOverridingByVoteCount()
    {
        OcrRule rule = Rule("雁塔题名杀头");
        var physicalTokens = new OcrLineEvidence[]
        {
            new("雁塔题名新澳门版", new OcrBox(10, 10, 300, 30), 0.99, "view", "main"),
            new("265杀红双", new OcrBox(10, 100, 160, 30), 0.99, "view", "main"),
            new("杀2头", new OcrBox(10, 140, 80, 30), 0.99, "view", "main")
        };
        var noisyPartition = new OcrLineEvidence[]
        {
            new("雁塔题名新澳门版", new OcrBox(10, 10, 300, 30), 0.99, "view", "column-0"),
            new("265杀红双", new OcrBox(10, 100, 160, 30), 0.99, "view", "column-0"),
            new("杀2头", new OcrBox(10, 140, 80, 30), 0.99, "view", "column-0"),
            new("265杀红双", new OcrBox(10, 200, 160, 30), 0.99, "view", "column-0"),
            new("杀3头", new OcrBox(10, 240, 80, 30), 0.99, "view", "column-0")
        };
        var evidence = new OcrEvidence(
            "card.jpg", "card.jpg", "source", "source", "view", noisyPartition, physicalTokens);

        RuleExtractionResult result = RuleEngine.ExtractFinalResult(evidence, 265, rule);

        Assert.Equal(RuleExtractionStatus.Conflict, result.Status);
    }

    [Theory]
    [InlineData("20260922_121805_85442.jpg", "雁塔题名杀头", 265, "2头")]
    [InlineData("20260922_121805_85442.jpg", "雁塔题名半波", 265, "蓝双")]
    [InlineData("20260922_195308_85582.jpg", "欧阳半波", 265, "绿双")]
    [InlineData("20260922_153900_85501.jpg", "紫燕儿杀一肖", 265, "猪")]
    [InlineData("20260922_121805_85442.jpg", "雁塔题名杀头", 263, "3头")]
    [InlineData("20260922_121805_85442.jpg", "雁塔题名半波", 261, "蓝单")]
    [InlineData("20260922_195308_85582.jpg", "欧阳半波", 264, "红双")]
    [InlineData("20260922_153900_85501.jpg", "紫燕儿杀一肖", 262, "猴")]
    public void ConfirmedGeometryReadsItsOwnIssue(string file, string id, int issue, string expected)
    {
        string fixture = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "Fixtures", "yanran-confirmed-ocr.json"));
        using var document = JsonDocument.Parse(File.ReadAllText(fixture));
        var item = document.RootElement.EnumerateArray().Single(item => item.GetProperty("File").GetString() == file);
        var tokens = JsonSerializer.Deserialize<OcrLineEvidence[]>(item.GetProperty("Tokens").GetRawText())!;
        var evidence = new OcrEvidence(file, file, "source", "source", "paddle",
            OcrEvidenceLayout.Partition(tokens), tokens);
        Assert.Equal(RuleExtractionResult.Success(expected), RuleEngine.ExtractFinalResult(evidence, issue, Rule(id)));
    }

    [Fact]
    public void YantaKeepsARealPhysicalRowConflict()
    {
        OcrRule rule = Rule("雁塔题名杀头");
        string[] lines =
        [
            "雁塔题名新澳门版", "265杀红双", "杀2头", "265杀红双", "杀3头"
        ];

        Assert.Equal(RuleExtractionStatus.Conflict,
            RuleEngine.ExtractFinalResult(lines, 265, rule).Status);
    }

    [Fact]
    public void OuyangHalfWaveNormalizesSplitIssueDigitsWithoutBorrowingAnotherRow()
    {
        OcrRule rule = Rule("欧阳半波");
        string[] lines = ["欧阳", "2 6 5期禁 绿单", "264期禁 红双"];

        Assert.Equal("绿单", RuleEngine.ExtractFinalValue(lines, 265, rule));
    }

    [Fact]
    public void OuyangHalfWaveJoinsDigitTokensBeforeTheIssueMarker()
    {
        OcrRule rule = Rule("欧阳半波");
        string[] lines = ["欧阳", "2", "6", "5期禁", "绿单"];

        Assert.Equal("绿单", RuleEngine.ExtractFinalValue(lines, 265, rule));
    }

    [Fact]
    public void OuyangHalfWaveDoesNotBorrowAColorFromTheNextSection()
    {
        OcrRule rule = Rule("欧阳半波");
        string[] lines = ["欧阳", "265期", "其他资料 红双"];

        Assert.Null(RuleEngine.ExtractFinalValue(lines, 265, rule));
    }

    [Fact]
    public void OuyangHalfWaveKeepsASectionWhoseTitleWasSplitByOcr()
    {
        OcrRule rule = Rule("欧阳半波");
        string[] lines = ["欧", "阳", "265期禁 绿单"];

        Assert.Equal("绿单", RuleEngine.ExtractFinalValue(lines, 265, rule));
    }

    [Fact]
    public void OuyangHalfWaveRejectsTheForeignParitySizeCard()
    {
        OcrRule halfWave = Rule("欧阳半波");
        OcrRule zodiac = Rule("欧阳肖");
        string[] lines =
        [
            "团队", "新澳门六合彩", "潮汕陈龙",
            "262期单+大双", "263期单+大双", "264期双+小单", "265期单+小双"
        ];
        string image = @"C:\结果\9.22-嫣然心水\乖乖团队\sample.jpg";

        Assert.DoesNotContain(halfWave, RuleEngine.FindMatches(image, lines, Rules, Rules));
        Assert.Null(RuleEngine.ExtractFinalValue(lines, 265, halfWave));
        Assert.Null(RuleEngine.ExtractFinalValue(lines, 265, zodiac));
    }

    [Fact]
    public void OuyangHalfWaveDoesNotExtractFromTheSiblingZodiacCard()
    {
        OcrRule halfWave = Rule("欧阳半波");
        OcrRule zodiac = Rule("欧阳肖");
        string[] lines = ["欧阳", "264期禁猪", "265期禁马"];
        string image = @"C:\结果\9.22-嫣然心水\乖乖团队\sample.jpg";

        Assert.Null(RuleEngine.ExtractFinalValue(lines, 265, halfWave));
        Assert.Contains(zodiac, RuleEngine.FindMatches(image, lines, [halfWave, zodiac], [halfWave, zodiac]));
    }

    [Fact]
    public void OuyangHalfWaveRejectsSplitForeignParitySizeRows()
    {
        OcrRule rule = Rule("欧阳半波");
        string[] lines =
        [
            "262期", "单+大双", "263期", "单+大双", "264期", "双+小单", "265期", "单+小双"
        ];

        Assert.Null(RuleEngine.ExtractFinalValue(lines, 265, rule));
    }

    [Fact]
    public void ZiyanerReadsOnlyTheKillZodiacCellWhenTheFollowingNoteHasAnotherZodiac()
    {
        OcrRule rule = Rule("紫燕儿杀一肖");
        string[] lines = ["紫燕儿", "265期新澳杀【牛】", "附注：羊"];

        Assert.Equal("牛", RuleEngine.ExtractFinalValue(lines, 265, rule));
    }

    [Fact]
    public void ZiyanerAlsoAcceptsIssueDigitsSplitAcrossOcrTokens()
    {
        OcrRule rule = Rule("紫燕儿杀一肖");
        string[] lines = ["紫燕儿", "2", "6", "5期新澳杀【牛】"];

        Assert.Equal("牛", RuleEngine.ExtractFinalValue(lines, 265, rule));
    }

    [Fact]
    public void ZiyanerAcceptsASectionTitleThatAppearsAfterItsRowsAsAWatermark()
    {
        OcrRule rule = Rule("紫燕儿杀一肖");
        string[] lines =
        [
            "265期:新奥杀", "【猪】", "227期:杀一尾", "【1尾】√", "紫燕儿"
        ];

        Assert.Equal("猪", RuleEngine.ExtractFinalValue(lines, 265, rule));
    }

    [Fact]
    public void ZiyanerDoesNotBorrowATargetRowFromAnotherKillZodiacTable()
    {
        OcrRule rule = Rule("紫燕儿杀一肖");
        string[] lines = ["265期禁狗", "紫燕儿", "227期:杀一尾", "【1尾】√"];

        Assert.Null(RuleEngine.ExtractFinalValue(lines, 265, rule));
    }

    [Fact]
    public void ZiyanerDoesNotGuessWhenTwoTargetRowsDisagree()
    {
        OcrRule rule = Rule("紫燕儿杀一肖");
        string[] lines = ["紫燕儿", "265期新澳杀【牛】", "265期新澳杀【羊】"];

        Assert.Equal(RuleExtractionStatus.Conflict,
            RuleEngine.ExtractFinalResult(lines, 265, rule).Status);
    }

    [Theory]
    [InlineData("265期新澳杀【牛羊】")]
    [InlineData("265期新澳杀待更新")]
    public void ZiyanerDoesNotReplaceAnInvalidCellWithTheFollowingZodiac(string row)
    {
        Assert.Null(RuleEngine.ExtractFinalValue(
            ["紫燕儿", row, "【猪】"], 265, Rule("紫燕儿杀一肖")));
    }
}
