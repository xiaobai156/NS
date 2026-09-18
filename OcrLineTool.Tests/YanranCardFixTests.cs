using OcrLineTool;

namespace OcrLineTool.Tests;

/// <summary>
/// 258 期真实失败的四张卡（嫣然心水）：雁塔题名两列卡（值行在期号行上方）、
/// 恩平杀一尾（同一行并了两期）、简单爱（右下小块读散）、爱晚亭五码（无标题表
/// 按文件夹+行形状认卡）。
/// </summary>
public sealed class YanranCardFixTests
{
    private static IReadOnlyList<OcrRule> Rules => RuleCatalog.Load(Path.Combine(
        ResultFilePaths.ConfigurationDirectory(AppContext.BaseDirectory), "嫣然心水.json"));

    private static OcrRule Rule(string id) => Rules.Single(rule => rule.Id == id);

    [Fact]
    public void EnpingTailTakesTheTargetIssueSegment()
    {
        OcrRule rule = Rule("恩平杀一尾");
        string[] split =
        [
            "主题:258期，杀一尾",
            "恩平杀一尾，252期止28期错1期",
            "252期杀，4尾22",
            "257期杀，0尾07",
            "258期杀，5尾"
        ];
        string[] merged = ["257期杀，0尾07 258期杀，5尾"];

        Assert.Equal("5尾", RuleEngine.ExtractFinalValue(split, 258, rule));
        Assert.Equal("5尾", RuleEngine.ExtractFinalValue(merged, 258, rule));
        Assert.Equal("0尾", RuleEngine.ExtractFinalValue(split, 257, rule));
    }

    [Fact]
    public void YantaHeadReadsTheValueLineAboveTheIssueLine()
    {
        OcrRule rule = Rule("雁塔题名杀头");
        string[] lines =
        [
            "雁塔题名新澳门版", "10", "杀2头", "258杀红双", "9", "杀0头", "257杀红双",
            "8", "杀3头", "256杀蓝双"
        ];

        Assert.Equal("2头", RuleEngine.ExtractFinalValue(lines, 258, rule));
        Assert.Equal("0头", RuleEngine.ExtractFinalValue(lines, 257, rule));
    }

    [Fact]
    public void YantaHalfWaveAndHeReadTheirOwnCells()
    {
        OcrRule half = Rule("雁塔题名半波");
        OcrRule he = Rule("雁塔题名杀合");
        string[] halfLines =
        [
            "雁塔题名新澳门版", "10", "杀2头", "258杀红双", "9", "杀0头", "257杀红双"
        ];
        string[] heLines =
        [
            "雁塔题名新澳门版", "10", "杀牛", "258杀7合", "9", "杀兔", "257杀1合"
        ];

        Assert.Equal("红双", RuleEngine.ExtractFinalValue(halfLines, 258, half));
        Assert.Equal("07合", RuleEngine.ExtractFinalValue(heLines, 258, he));
    }

    // 261 期真实失败卡（爱晚亭子文件夹 20260918_204252_84058.jpg）：无标题的
    // 每期一行统计表（"NNN期：X开YY"），按子文件夹+行形状认卡，261 期取值"蛇"。
    // 卡上没有任何资料名，且顶部有 8 行区间统计（"001-030期错3"）不允许破坏行形状判定。
    [Fact]
    public void AiwantingKillZodiacReadsTheTargetRowWithoutAnyTitleOnTheCard()
    {
        OcrRule rule = Rule("爱晚亭杀肖");
        string[] lines =
        [
            "001-030期错3", "031-060期错3", "061-090期错2", "091-120期错2",
            "121-150期错3", "151-180期错5", "181-210期错3", "211-240期错4",
            "241期：猴开马49", "242期：鸡开狗09", "243期：马开狗21", "244期：00开鸡46",
            "245期：龙开牛18", "246期：鼠开牛30", "247期：马开兔40", "248期：龙开猪20",
            "249期：马开猴23", "250期：狗开蛇14", "251期：龙开牛30", "252期：牛开鸡22",
            "253期：鼠开兔16", "254期：马开蛇02", "255期：狗开猪44", "256期：羊开马01",
            "257期：鼠开鼠07", "258期：猪开鸡46", "259期:鸡开鸡22×", "260期：猴开猪20",
            "261期：蛇开00"
        ];

        Assert.Equal("蛇", RuleEngine.ExtractFinalValue(lines, 261, rule));
        Assert.Contains(rule, RuleEngine.FindMatches(
            @"C:\图片\9.18-嫣然心水\爱晚亭\20260918_204252_84058.jpg", lines, [rule], [rule]));
    }

    [Fact]
    public void AiwantingKillZodiacDoesNotTakeTheNumberTablesOfTheSameFolder()
    {
        OcrRule rule = Rule("爱晚亭杀肖");
        string[] shareTable =
        [
            "261期新澳门：", "01,02,03,05,06,07,08,10,11,14,15,17,18,19,20,22,23,25,26,27,37,",
            "39,41,42,43,44,46,49,（共28码）", "爱晚亭分享，连错4，建议反买"
        ];
        string[] universalCatTable =
        [
            "【爱晚亭万能猫新澳门数据②】", "167期起1.1.4.1", "●爱晚亭③●您的计算结果：",
            "【统计总】2026261期:", "【0次】：43,49,(共2码)", "【1次】：02,08,12",
            "【5次】：29,31,32,36,(共4码)", "【新澳门彩票@万能猫】"
        ];

        Assert.DoesNotContain(rule, RuleEngine.FindMatches(
            @"C:\图片\9.18-嫣然心水\爱晚亭\20260918_204253_84060.jpg", shareTable, [rule], [rule]));
        Assert.DoesNotContain(rule, RuleEngine.FindMatches(
            @"C:\图片\9.18-嫣然心水\爱晚亭\20260918_204254_84062.jpg", universalCatTable, [rule], [rule]));
        // 子文件夹不对时同样不能顶替。
        Assert.DoesNotContain(rule, RuleEngine.FindMatches(
            @"C:\图片\9.18-嫣然心水\小灰灰\20260918_204252_84058.jpg",
            ["261期：蛇开00", "241期：猴开马49", "242期：鸡开狗09", "243期：马开狗21",
             "244期：00开鸡46", "245期：龙开牛18"], [rule], [rule]));
    }

    // 261 期真实卡（雁塔题名子文件夹 20260918_183918_83966.jpg）：左列"261杀蓝单"
    // 不带"期"字，右列"杀0头"被 OCR 读成下一行。期号识别与同行取值都必须兼容。
    [Fact]
    public void YantaHeadReadsTheValueCellWhenOcrReadsTheTwoColumnsAsTwoLines()
    {
        OcrRule rule = Rule("雁塔题名杀头");
        string[] lines =
        [
            "雁塔题名新澳门版", "1", "260错33", "260错44", "2", "261杀蓝单", "杀0头",
            "3", "4", "5", "6"
        ];

        Assert.Equal("0头", RuleEngine.ExtractFinalValue(lines, 261, rule));
        Assert.Equal("蓝单", RuleEngine.ExtractFinalValue(lines, 261, Rule("雁塔题名半波")));
    }

    [Fact]
    public void YantaHeadStaysMissingWhenTheTargetRowHasNoValueCell()
    {
        OcrRule rule = Rule("雁塔题名杀头");
        string[] lines =
        [
            "雁塔题名新澳门版", "260错44", "260错33", "261杀蓝单",
            "1杀蓝单", "杀0头", "260错44", "260错33"
        ];

        Assert.Null(RuleEngine.ExtractFinalValue(lines, 261, rule));
    }

    [Fact]
    public void YantaZodiacReadsItsOwnCell()
    {
        OcrRule rule = Rule("雁塔题名杀肖肖");
        string[] lines =
        [
            "雁塔题名新澳门版", "1", "250错13", "250错15", "2", "251杀5合", "杀猴",
            "3", "252杀3合", "杀鼠", "4", "253杀4合", "杀龙", "5", "254杀7合", "杀狗",
            "6", "255杀2合", "杀猴", "256杀8合", "杀虎", "8", "257杀1合", "杀羊",
            "9", "258杀7合", "杀牛", "10", "259杀10合", "杀龙", "11", "12"
        ];

        Assert.Equal("龙", RuleEngine.ExtractFinalValue(lines, 259, rule));
        Assert.Equal("牛", RuleEngine.ExtractFinalValue(lines, 258, rule));
        Assert.Equal("虎", RuleEngine.ExtractFinalValue(lines, 256, rule));
    }

    [Fact]
    public void YantaZodiacKeepsStrictCellBoundaries()
    {
        OcrRule rule = Rule("雁塔题名杀肖肖");
        string[] twoZodiacs =
        [
            "雁塔题名新澳门版", "259杀10合", "杀龙牛", "258杀7合", "杀牛"
        ];
        string[] missingRightCell =
        [
            "雁塔题名新澳门版", "258杀7合", "杀牛", "259杀10合", "11", "12"
        ];

        Assert.Null(RuleEngine.ExtractFinalValue(twoZodiacs, 259, rule));
        Assert.Null(RuleEngine.ExtractFinalValue(missingRightCell, 259, rule));
    }

    [Fact]
    public void AiWantingFiveNumbersMatchByFolderAndRowShape()
    {
        OcrRule rule = Rule("爱晚亭");
        string[] lines =
        [
            "242期：32.33.24.23.15.开狗09",
            "243期：30.31.36.20.16.开狗21",
            "244期:00开鸡46",
            "245期：30.31.05.06.03.开牛18",
            "246期：29.30.01.37.44.开牛30",
            "247期：28.29.47.46.37.开兔40",
            "248期：30.29.33.31.24.开猪20",
            "249期：30.33.29.34.35.开猴23",
            "250期：31.32.08.07.48.开蛇14",
            "251期：33.34.32.31.11.开牛30",
            "252期：35.34.02.03.49.开鸡22",
            "253期：34.35.10.11.02.开兔16",
            "254期：32.33.28.26.25.开蛇02",
            "255期：31.29.22.14.13.开猪44",
            "256期：33.32.08.07.06.开马01",
            "257期：29.30.07.04.06.开鼠07",
            "258期: 34.35.12.03.01.开00"
        ];
        string imagePath = Path.Combine(@"C:\图片\9.15-嫣然心水\爱晚亭", "20260915_185227_82840.jpg");

        Assert.Contains(rule.Id, RuleEngine.FindMatches(imagePath, lines, Rules, Rules).Select(item => item.Id));
        Assert.Equal("34 35 12 03 01", RuleEngine.ExtractFinalValue(lines, 258, rule));
    }

    [Fact]
    public void AiWantingStatisticsCardYieldsNoFiveNumbers()
    {
        OcrRule rule = Rule("爱晚亭");
        string[] statistics =
        [
            "●爱晚亭③●您的计算结果：",
            "【统计总】2026258期:",
            "【0次】：02,04,08,12,15,16,19,20,22,",
            "【1次】：03,06,10,11,13,14,17,18,23,25,",
            "【2次】：05,07,09,21,37,41,42，（共7码）",
            "【4次】：01，（共1码）×",
            "【共10行 总计40码】",
            "【新澳门彩票@万能猫】"
        ];

        Assert.Null(RuleEngine.ExtractFinalValue(statistics, 258, rule));
    }

    [Fact]
    public void AiWantingWatermarkStatisticsCardIsNeverACandidate()
    {
        OcrRule rule = Rule("爱晚亭");
        string[] statistics =
        [
            "●爱晚亭③●您的计算结果：",
            "【统计总】2026258期:",
            "【0次】：02,04,08,12,15,16,19,20,22,",
            "【1次】：03,06,10,11,13,14,17,18,23,25,",
            "【2次】：05,07,09,21,37,41,42，（共7码）",
            "【共10行 总计40码】"
        ];
        string inFolder = Path.Combine(@"C:\图片\9.15-嫣然心水\爱晚亭", "20260915_185228_82842.jpg");

        Assert.DoesNotContain(rule.Id, RuleEngine.FindMatches(inFolder, statistics, [rule], Rules).Select(item => item.Id));
    }

    [Fact]
    public void AiWantingOutsideItsFolderIsNeverACandidate()
    {
        OcrRule rule = Rule("爱晚亭");
        string[] rows =
        [
            "245期：30.31.05.06.03.开牛18",
            "246期：29.30.01.37.44.开牛30",
            "247期：28.29.47.46.37.开兔40",
            "248期：30.29.33.31.24.开猪20",
            "249期：30.33.29.34.35.开猴23",
            "258期: 34.35.12.03.01.开00"
        ];
        string otherFolder = Path.Combine(@"C:\图片\9.15-嫣然心水\乖乖团队", "20260915_185227_82840.jpg");

        Assert.DoesNotContain(rule.Id, RuleEngine.FindMatches(otherFolder, rows, [rule], Rules).Select(item => item.Id));
    }

    [Fact]
    public void AiWantingRowFormatIsMandatoryForTheValue()
    {
        OcrRule rule = Rule("爱晚亭");

        // 目标期行只有 4 个号 / 有重复 / 越界 → 缺失。
        Assert.Null(RuleEngine.ExtractFinalValue(["258期: 34.35.12.03.开00"], 258, rule));
        Assert.Null(RuleEngine.ExtractFinalValue(["258期: 34.35.12.03.34.开00"], 258, rule));
        Assert.Null(RuleEngine.ExtractFinalValue(["258期: 34.35.12.03.50.开00"], 258, rule));
        Assert.Null(RuleEngine.ExtractFinalValue(["257期: 29.30.07.04.06.开鼠07"], 258, rule));

        // 认卡：整表不足 5 行整行 → 不当候选；目标行残缺但其它行正常 → 候选成立但取值缺失。
        string[] tooFewRows =
        [
            "255期：31.29.22.14.开猪44",
            "256期：33.32.08.07.开马01",
            "257期：29.30.07.04.开鼠07",
            "258期: 34.35.12.03.开00"
        ];
        string inFolder = Path.Combine(@"C:\图片\9.15-嫣然心水\爱晚亭", "card.jpg");
        Assert.DoesNotContain(rule.Id, RuleEngine.FindMatches(inFolder, tooFewRows, [rule], Rules).Select(item => item.Id));

        string[] mixedRows =
        [
            "253期：34.35.10.11.02.开兔16",
            "254期：32.33.28.26.25.开蛇02",
            "255期：31.29.22.14.13.开猪44",
            "256期：33.32.08.07.06.开马01",
            "257期：29.30.07.04.06.开鼠07",
            "258期: 34.35.12.03.开00"
        ];
        Assert.Contains(rule.Id, RuleEngine.FindMatches(inFolder, mixedRows, [rule], Rules).Select(item => item.Id));
        Assert.Null(RuleEngine.ExtractFinalValue(mixedRows, 258, rule));
    }

    [Fact]
    public void AiWantingAcceptsCommaAndSpaceSeparators()
    {
        OcrRule rule = Rule("爱晚亭");

        Assert.Equal("34 35 12 03 01", RuleEngine.ExtractFinalValue(["258期：34,35,12,03,01,开00"], 258, rule));
        Assert.Equal("34 35 12 03 01", RuleEngine.ExtractFinalValue(["258期 34 35 12 03 01"], 258, rule));
    }

    [Fact]
    public void JianDanAiStripNeedsTargetIssueAndOneZodiac()
    {
        Assert.Equal("牛", RuleEngine.ExtractRightBlockZodiacFromStrip(["258期禁牛"], 258));
        Assert.Equal("羊", RuleEngine.ExtractRightBlockZodiacFromStrip(["258期 禁 羊"], 258));
        Assert.Null(RuleEngine.ExtractRightBlockZodiacFromStrip(["257期禁羊"], 258));
        Assert.Null(RuleEngine.ExtractRightBlockZodiacFromStrip(["258期禁牛羊"], 258));
        Assert.Null(RuleEngine.ExtractRightBlockZodiacFromStrip(["257期禁羊 258期禁牛"], 258));
        Assert.Equal("牛", RuleEngine.ExtractRightBlockZodiacFromStrip(["258期258期禁牛"], 258));
        Assert.Null(RuleEngine.ExtractRightBlockZodiacFromStrip(["258期"], 258));
    }

    [Fact]
    public void YantaHeadReadsThroughPartitionedEvidence()
    {
        OcrRule rule = Rule("雁塔题名杀头");
        // 运行时证据：Items 是分区后的列块，TokenItems 才是逐格原始项。
        var tokens = new List<OcrLineEvidence>
        {
            new("雁塔题名新澳门版", new OcrBox(20, 20, 300, 40), 0.99, "paddle/original", "main"),
            new("10", new OcrBox(20, 100, 40, 30), 0.99, "paddle/original", "main"),
            new("杀2头", new OcrBox(20, 140, 90, 30), 0.99, "paddle/original", "main"),
            new("258杀红双", new OcrBox(20, 180, 160, 30), 0.99, "paddle/original", "main"),
            new("9", new OcrBox(20, 220, 40, 30), 0.99, "paddle/original", "main"),
            new("杀0头", new OcrBox(20, 260, 90, 30), 0.99, "paddle/original", "main"),
            new("257杀红双", new OcrBox(20, 300, 160, 30), 0.99, "paddle/original", "main")
        };
        var partitioned = tokens
            .Select(item => item with { RegionId = "column-" + item.Box!.Y })
            .ToList();
        var evidence = new OcrEvidence(
            "crop.png", "crop.png", "H", "H", "local-primary/medium", partitioned, tokens);

        RuleExtractionResult result = RuleEngine.ExtractFinalResult(evidence, 258, rule);

        Assert.Equal(RuleExtractionStatus.Success, result.Status);
        Assert.Equal("2头", result.Value);
    }

    [Fact]
    public void EnpingTailIgnoresFooterDigits()
    {
        OcrRule rule = Rule("恩平杀一尾");
        string[] lines =
        [
            "主题:258期，杀一尾",
            "作者:恩平公式",
            "恩平杀一尾，252期止28期错1期",
            "257期杀，0尾07",
            "258期杀，5尾",
            "→→→",
            "2026-09-15 08:29:30编辑本贴",
            "最后修改:1分钟前[日志]",
            "签名:人生最好的境界就是：健康的活着，合理的忙着"
        ];

        Assert.Equal("5尾", RuleEngine.ExtractFinalValue(lines, 258, rule));
    }

    [Fact]
    public void AiWantingExtractsThroughEvidence()
    {
        OcrRule rule = Rule("爱晚亭");
        string[] rows =
        [
            "242期：32.33.24.23.15.开狗09",
            "243期：30.31.36.20.16.开狗21",
            "245期：30.31.05.06.03.开牛18",
            "246期：29.30.01.37.44.开牛30",
            "247期：28.29.47.46.37.开兔40",
            "248期：30.29.33.31.24.开猪20",
            "258期: 34.35.12.03.01.开00"
        ];
        List<OcrLineEvidence> tokens = rows
            .Select((row, index) => new OcrLineEvidence(
                row, new OcrBox(20, 100 + index * 40, 500, 30), 0.99, "paddle/original", "main"))
            .ToList();
        var evidence = new OcrEvidence(
            "crop.png", "crop.png", "H", "H", "local-primary/medium", tokens, tokens);

        RuleExtractionResult result = RuleEngine.ExtractFinalResult(evidence, 258, rule);

        Assert.Equal(RuleExtractionStatus.Success, result.Status);
        Assert.Equal("34 35 12 03 01", result.Value);
    }

        [Fact]
    public void HeValueSurvivesAnOcrLineBreakBetweenNumberAndMark()
    {
        OcrRule zuiLiang = Rule("最亮月空");
        OcrRule yuHou = Rule("雨后星星");
        string[] zuiLiangLines =
        [
            "258期：新澳门【天机阁论坛●最亮月空正正杀一合】04",
            "合开46对",
            "259期：新澳门【天机阁论坛●最亮月空企杀一合】07",
            "合开00对"
        ];
        string[] yuHouLines =
        [
            "天机阁雨后星星正绝杀合→→01",
            "合开23准",
            "259期：天机阁雨后星星正绝杀合→→02",
            "合开00准"
        ];

        Assert.Equal("04合", RuleEngine.ExtractFinalValue(zuiLiangLines, 258, zuiLiang));
        Assert.Equal("07合", RuleEngine.ExtractFinalValue(zuiLiangLines, 259, zuiLiang));
        Assert.Equal("02合", RuleEngine.ExtractFinalValue(yuHouLines, 259, yuHou));
    }

    [Fact]
    public void HeValueAcceptsMergedAndAdjacentFormsButKeepsItsLimits()
    {
        OcrRule rule = Rule("最亮月空");

        Assert.Equal("07合", RuleEngine.ExtractFinalValue(["259期：最亮月空杀一合】07合开00对"], 259, rule));
        Assert.Equal("07合", RuleEngine.ExtractFinalValue(["259期：最亮月空杀一合】07 合开00对"], 259, rule));
        Assert.Null(RuleEngine.ExtractFinalValue(["259期：最亮月空杀一合】14合开00对"], 259, rule));
        Assert.Null(RuleEngine.ExtractFinalValue(["259期：最亮月空杀一合】07合 08合开00对"], 259, rule));
    }

    [Fact]
    public void RightBlockRectUsesTheTargetIssueCell()    {
        var items = new List<OcrLineEvidence>
        {
            new("254期", new OcrBox(20, 900, 60, 30), 0.9, "paddle/original", "main"),
            new("255期", new OcrBox(420, 600, 60, 30), 0.9, "paddle/original", "main"),
            new("256期", new OcrBox(420, 640, 60, 30), 0.9, "paddle/original", "main"),
            new("257期", new OcrBox(420, 680, 60, 30), 0.9, "paddle/original", "main"),
            new("258期", new OcrBox(420, 720, 60, 30), 0.9, "paddle/original", "main")
        };

        (int X, int Y, int Width, int Height)? rect = SummaryRowRecovery.ComputeRightBlockRect(items, 258);

        Assert.NotNull(rect);
        Assert.Equal(416, rect!.Value.X);
        Assert.Equal(718, rect.Value.Y);
        Assert.Equal(0, rect.Value.Width);
        Assert.Equal(34, rect.Value.Height);

        items.Add(new OcrLineEvidence("258期", new OcrBox(20, 760, 60, 30), 0.9, "paddle/original", "main"));
        Assert.Null(SummaryRowRecovery.ComputeRightBlockRect(items, 258));
    }
}
