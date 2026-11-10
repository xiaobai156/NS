using OcrLineTool;
using Xunit;

namespace OcrLineTool.Tests;

// Regression coverage for the 嫣然心水 issue 253 local-medium lines that
// previously produced "未识别到当期目标数据". Each sample is the real medium
// OCR evidence captured from the group folder.
public sealed class Yanran253MissingRulesTests
{
    private static OcrRule[] Rules() => RuleCatalog.Load(
        Path.Combine(ResultFilePaths.ConfigurationDirectory(AppContext.BaseDirectory), "嫣然心水.json"))
        .ToArray();

    private static OcrRule Rule(string id) => Assert.Single(Rules(), rule => rule.Id == id);

    [Fact]
    public void ExtractsShayatouDoubleZodiacFromItsVerticalList()
    {
        OcrRule rule = Rule("傻丫头二肖");
        string[] lines =
        [
            "5新澳彩5", "傻丫头原创杀二肖",
            "239期杀狗猪", "240期杀兔虎", "241期杀龙兔", "242期杀虎猪", "243期杀兔猪",
            "244期杀狗蛇", "245期杀猴狗", "246期杀鼠鸡", "247期杀牛猪", "248期杀虎猪",
            "虎龙马狗狗鸡牛牛兔猪猴蛇牛鸡傻丫头",
            "249期杀虎猪", "250期杀猴牛", "251期杀猴猪", "252期杀牛龙", "253期杀龙牛",
            "六合无绝对，不包准。", "跟弃自由，看帖勿喷！", "福"
        ];

        Assert.Equal("龙牛", RuleEngine.ExtractFinalValue(lines, 253, rule));
    }

    [Fact]
    public void ExtractsJunjunSectionsFromTheSameCard()
    {
        string[] lines =
        [
            "君军新澳杀料", "9月份", "杀一肖",
            "244期杀狗+兔开46鸡", "245期杀兔+龙开18牛", "246期杀马+鼠开30牛", "247期杀鸡+鼠开40兔",
            "248期杀鼠+狗开20猪", "249期杀蛇+虎开23猴", "250期杀虎+蛇X开14蛇", "251期杀牛+猴开30牛",
            "252期杀猴+鼠开22鸡", "253期杀马+龙开",
            "杀一尾5",
            "244期杀0+3尾开46", "245期杀3+6尾开18", "246期杀4+5尾开30", "247期杀0+5尾开40",
            "248期杀9+7尾开20", "248期杀1+3X尾开23", "250期杀8+4X尾开14", "251期杀8+2尾开30",
            "252期杀0+2X尾开22", "253期杀9+7尾开",
            "杀一合",
            "244期杀06合开46", "245期杀04合开18", "246期杀11合开30", "247期杀01合开40",
            "248期杀04合开20", "249期杀09合开23", "250期杀11合开14", "251期杀08合开30",
            "252期杀09合开22", "253期杀10合开",
            "爱心奉献对错咬老曾"
        ];

        Assert.Equal("马龙", RuleEngine.ExtractFinalValue(lines, 253, Rule("君军两肖")));
        Assert.Equal("9尾+7尾", RuleEngine.ExtractFinalValue(lines, 253, Rule("君军两尾")));
        Assert.Equal("10合", RuleEngine.ExtractFinalValue(lines, 253, Rule("君军合")));
    }

    [Fact]
    public void ExtractsEnpingFormulaWhenTheValueRowOmitsTheAuthor()
    {
        OcrRule rule = Rule("恩平公式");
        string[] lines =
        [
            "主题:253期：恩平公式杀二肖",
            "作者:恩平公式",
            "人气:3786065/回帖:298",
            "2014/11/10 14:02:10",
            "恩平公式杀二肖，",
            "253期杀，猪牛",
            "2026-09-10 12:15:22编辑本帖",
            "最后修改:33分钟前[日志]"
        ];

        Assert.Equal("猪牛", RuleEngine.ExtractFinalValue(lines, 253, rule));
    }

    [Fact]
    public void ExtractsFirewolfTwoTailsBelowItsHeading()
    {
        OcrRule rule = Rule("火狼女两尾");
        string[] lines =
        [
            "234期禁<牛虎>开龙39", "235期禁<牛鼠>开猪32", "236期禁<猪蛇>开猴11", "237期禁<猪猴>开羊12",
            "238期禁<鼠猴>开虎17", "239期禁<猴羊>开虎05", "240期禁<猴羊>开龙27", "241期禁<羊虎>开马49",
            "242期禁<虎龙>开狗09", "243期禁<漏写>开狗21", "244期禁<兔狗>开鸡46", "245期禁<龙狗>开牛18",
            "246期禁<虎狗>开牛30", "247期禁<牛狗>开兔40", "248期禁<虎鸡>开猪20", "249期禁<牛猴>开猴23",
            "250期禁<羊鸡>开蛇14", "251期禁<猪狗>开牛30", "252期禁<猴羊>开鸡22", "253期禁<蛇龙>开猫50",
            "=狼女新澳杀二尾=火狼女", "记录：",
            "250期杀<7-6尾>开14", "251期杀<3-9尾>开30", "252期杀<8-9尾>开22", "253期杀<4-9尾>开50",
            "=火狼女新澳=", "杀二肖+杀二尾=2合1:", "记录："
        ];

        Assert.Equal("4尾+9尾", RuleEngine.ExtractFinalValue(lines, 253, rule));
    }

    [Fact]
    public void SeparatesDuaoSatuoBlocksAcrossHistoricalYears()
    {
        OcrRule single = Rule("独傲洒脱杀肖肖");
        OcrRule pair = Rule("独傲洒脱杀二肖");
        string[] lines =
        [
            "澳彩杀半色波", "2026", "001期...250期(250期错51期）",
            "251期:杀蓝单", "252期:杀蓝单", "253期:杀红单!?",
            "澳彩杀一特肖", "2026", "001期...248期(248期错23期）",
            "249期:杀牛", "独傲洒脱", "250期:杀龙中", "251期:杀猴中", "252期:杀兔中", "253期:杀兔！?",
            "澳彩杀2特肖（双规）", "2026", "001期...245期(245期错44期）",
            "246期:杀虎+猪", "247期:杀马+虎", "248期:杀猪+羊", "249期:杀牛+鼠", "250期:杀龙+牛",
            "251期:杀猴+龙", "252期:杀兔+龙", "253期:杀兔！?+蛇！?",
            "(另杀龙!?+虎!?"
        ];

        Assert.Equal("兔", RuleEngine.ExtractFinalValue(lines, 253, single));
        Assert.Equal("兔蛇", RuleEngine.ExtractFinalValue(lines, 253, pair));
    }

    [Fact]
    public void DuaoSatuoScopeWalkSkipsWatermarkCorruptedYearRows()
    {
        OcrRule single = Rule("独傲洒脱杀肖肖");
        OcrRule pair = Rule("独傲洒脱杀二肖");
        string[] lines =
        [
            "澳彩杀3尾",
            "2023185期.365期【180期错49期】",
            "2024",
            "001期..366期(366期错101期)",
            "2025",
            "001期.365期(365期错100期)",
            "2026",
            "001期..249期(249期错72期)",
            "250期：杀901尾",
            "251期：杀678尾",
            "252期：杀345尾",
            "253期：杀123尾",
            "254期：杀789尾！？",
            "澳彩杀半色波",
            "2025",
            "001期.365期(365期错54期)",
            "2026",
            "001期.250期(250期错51期)",
            "251期：杀蓝单",
            "252期：杀蓝单",
            "253期：杀红单",
            "254期：杀绿双!?",
            "澳彩杀一特肖",
            "2023",
            "185期..365期【180期错16期】",
            "20241",
            "独",
            "001期..366期(366期错36期)",
            "2025",
            "001期.365期(355期错36期)",
            "2026傲",
            "001期..248期(248期错23期)",
            "249期：杀牛",
            "250期：杀龙",
            "251期：杀猴洒",
            "252期：杀兔",
            "253期：杀免X",
            "254期：杀鼠",
            "脱",
            "澳彩杀2特肖（双规）",
            "246期：杀虎+猪",
            "254期：杀鼠！？+狗！？",
            "独傲洒脱"
        ];

        Assert.Equal("鼠", RuleEngine.ExtractFinalValue(lines, 254, single));
        Assert.Equal("鼠狗", RuleEngine.ExtractFinalValue(lines, 254, pair));
    }

    [Fact]
    public void ExtractsSuimuemanchangColorParityFromItsFullCard()
    {
        OcrRule rule = Rule("岁月漫长");
        string[] lines =
        [
            "228期岁月漫长新澳杀丰波", "【红单】", "特23错",
            "229期岁月漫长新澳杀半波", "【蓝单】", "特38对",
            "230期岁月漫长新澳杀半波", "【红双】", "特16对",
            "231期岁月漫长新澳杀半波", "【红双】", "特20对",
            "232期岁月漫长新澳杀半波", "【红双】", "特42对",
            "233期岁月漫长新澳杀半波", "【红双】", "特07对",
            "234期岁月漫长新澳杀半波", "【蓝双】", "特39对",
            "235期岁月漫长新澳杀半波", "【蓝双】", "特32对",
            "236期岁月漫长新澳杀半波", "【蓝单】", "特11对",
            "237期岁月漫长新澳杀半波", "【红双】", "特12 错",
            "238期岁月漫长新澳杀半波", "【红单】", "特17对",
            "239期岁月漫长新澳杀半波", "【绿双】", "特05对",
            "240期岁月漫长新澳杀半波", "【绿单】", "特27错",
            "241期岁月漫长新澳杀半波", "【绿单】", "特49 错",
            "242期岁月漫长新澳杀半波", "【蓝单】", "特09 错",
            "243期岁月漫长新澳杀半波", "【蓝双】", "特21对",
            "244期岁月漫长新澳杀半波", "【绿双】", "特46 对",
            "245期岁月漫长新澳杀半波", "【绿双】", "特18对",
            "246期岁月漫长新澳杀半波", "【蓝单】", "特30对",
            "247期岁月漫长新澳杀半波", "【红单】", "特40对",
            "248期岁月漫长新澳杀半波", "【蓝单】", "特20对",
            "249期岁月漫长新澳杀半波", "【蓝单】", "特23对",
            "250期岁月漫长新澳杀半波", "【蓝双】", "特14错",
            "251期岁月漫长新澳杀半波", "【绿单】", "特30对",
            "252期岁月漫长新澳杀半波", "【绿单】", "特22对",
            "253期岁月漫长新澳杀半波", "【蓝单】", "特16对",
            "254期岁月漫长新澳杀半波", "【红单】", "特00对"
        ];

        Assert.Equal("红单", RuleEngine.ExtractFinalValue(lines, 254, rule));
    }

    [Fact]
    public void ExtractsBaimengSingleZodiacFromItsHistoryList()
    {
        OcrRule rule = Rule("白梦");
        string[] lines =
        [
            "101期：", "【☆☆☆☆☆☆☆☆☆☆】", "111期：", "【☆☆☆☆☆☆☆☆☆☆】",
            "121期：", "【☆☆☆☆☆☆☆☆☆☆】", "131期：", "【☆☆☆☆★☆☆☆☆☆】",
            "141期：", "【☆☆☆★☆☆☆☆☆☆】", "151期：", "【☆☆☆☆☆☆☆☆☆☆】",
            "161期：", "【☆☆☆☆☆☆☆☆☆☆】", "171期：", "【☆☆☆☆☆☆★☆☆☆】",
            "181期：", "【☆★☆☆☆☆☆☆☆☆】", "191期：", "【☆☆☆☆☆☆☆☆☆☆】",
            "201期：", "【☆★★☆☆☆☆★☆☆】", "211期：", "【☆☆☆☆☆☆☆☆☆☆】",
            "221期：", "【☆☆☆☆☆☆☆☆☆☆】", "231期：【☆☆☆★☆☆☆☆☆☆】",
            "241期：【☆☆☆☆☆☆☆☆☆☆】", "251期：【☆☆☆】",
            "235期：新澳门【天机阁论坛●白梦正正正正杀一肖】虎开猪对",
            "236期：新澳门【天机阁论坛●白梦正正正正杀一肖】羊 开猴对",
            "237期：新澳门【天机阁论坛●白梦正正正正杀一肖】狗 开羊对",
            "238期：新澳门【天机阁论坛●白梦正正正正杀一肖】鸡开虎对",
            "239期：新澳门【天机阁论坛●白梦正正正正杀一肖】鸡开虎对",
            "240期：新澳门【天机阁论坛●白梦正正正正杀一肖】虎开龙对",
            "241期：新澳门【天机阁论坛●白梦正正正正杀一肖】鼠 开马对",
            "242期：新澳门【天机阁论坛●白梦正正正正杀一肖】马开狗对",
            "243期：新澳门【天机阁论坛●白梦正正正正杀一肖】羊开狗对",
            "244期：新澳门【天机阁论坛●白梦正正正正杀一肖】虎开鸡对",
            "245期：新澳门【天机阁论坛●白梦正正正正杀一肖】兔 开牛对",
            "246期：新澳门【天机阁论坛●白梦正正正正杀一肖】龙开牛对",
            "247期：新澳门【天机阁论坛●白梦正正正正杀一肖】狗 开兔对",
            "248期：新澳门【天机阁论坛●白梦正正正正杀一肖】鸡开猪对",
            "249期：新澳门【天机阁论坛●白梦正正正正杀一肖】兔开猴对",
            "250期：新澳门【天机阁论坛●白梦正正正正杀一肖】狗开蛇对",
            "251期：新澳门【天机阁论坛●白梦正正正正杀一肖】狗开牛对",
            "252期：新澳门【天机阁论坛●白梦正正杀一肖】马开鸡对",
            "253期：新澳门【天机阁论坛●白梦正正正正杀一肖】马开兔对",
            "254期：新澳门【天机阁论坛●白梦正正正正杀一肖】蛇"
        ];

        Assert.Equal("蛇", RuleEngine.ExtractFinalValue(lines, 254, rule));
    }

    [Fact]
    public void ExtractsZiyanerSingleZodiacFromItsVerticalList()
    {
        OcrRule rule = Rule("紫燕儿杀一肖");
        string[] lines =
        [
            "紫燕儿",
            "228期:新奥杀【龙】√", "229期:新奥杀【虎】√", "230期:新奥杀【鸡】√", "231期:新奥杀【牛】√",
            "232期:新奥杀【鼠】√", "233期:新奥杀【鸡】√", "235期:新奥杀【鸡】√", "236期:新奥杀【猴】X",
            "237期:新奥杀【牛】√", "238期:新奥杀【马】√", "239期:新奥杀【兔】√", "240期:新奥杀【蛇】√",
            "241期:新奥杀【鸡】√", "242期:新奥杀【猪】√", "243期:新奥杀【鸡】√", "244期:新奥杀【龙】√",
            "245期:新奥杀【牛】X", "246期：新奥杀【猪】√", "247期：新奥杀【虎】√", "248期:新奥杀【羊】√",
            "249期:新奥杀【鼠】√", "250期:新奥杀【牛】√", "251期:新奥杀【龙】√", "252期:新奥杀【龙】√",
            "253期:新奥杀【蛇】",
            "227期：杀一尾【1尾】√", "251期:杀一尾【0尾】X", "252期:杀一尾【3尾】√", "253期:杀一尾【2尾】"
        ];

        Assert.Equal("蛇", RuleEngine.ExtractFinalValue(lines, 253, rule));
    }

    [Fact]
    public void ExtractsAlianZodiacAndTailFromTheNineMonthBlock()
    {
        OcrRule zodiac = Rule("阿莲杀肖肖");
        OcrRule tail = Rule("阿莲杀尾尾");
        string[] lines =
        [
            "啊莲", "9月份杀料",
            "244期禁狗开46[666]", "245期禁羊开18[666]", "246期禁龙开30[666]", "247期禁猪开40[666]",
            "248期禁虎开20[666]", "249期禁虎开23[666]", "250期禁猪开14[666]", "251期禁鸡开30[666]",
            "252期禁牛开22[666]", "253期禁鼠开？？",
            "244期禁6尾开46[裂开]", "245期禁8尾开18[裂开]", "246期禁9尾开30[666]", "247期禁1尾开40[666]",
            "248期禁1尾开20[666]", "249期禁8尾开23[666]", "250期禁8尾开14[666]", "251期禁9尾开30[666]",
            "252期禁3尾开22[666]", "253期禁7尾开？?"
        ];

        Assert.Equal("鼠", RuleEngine.ExtractFinalValue(lines, 253, zodiac));
        Assert.Equal("7尾", RuleEngine.ExtractFinalValue(lines, 253, tail));
    }

    [Fact]
    public void IgnoresEmptyTopFrequencyBuckets()
    {
        string[] grayGroupLines =
        [
            "wilie985团队", "新澳09月份(杀错排名推后)",
            "第253期统计(16人):",
            "〖0次】虎兔", "〖1次】羊猴", "〖2次】牛狗猪", "龙马鸡", "〖4次】鼠蛇",
            "【5次】", "〖6次】", "〖7次】",
            "09月份历史战绩(开出次数):", "212 4 0 2 5 42"
        ];
        OcrRule grayGroup = Rule("灰灰团");
        // The wrapped 龙马鸡 belongs to the 2-count bucket: the true sum is 22,
        // not the printed 16. Bare text cannot independently resolve that discrepancy.
        Assert.Null(RuleEngine.ExtractFinalValue(grayGroupLines, 253, grayGroup));
        Assert.Equal("鼠蛇", RuleEngine.ExtractFinalValue(grayGroupLines
            .Select(s => s.Replace("统计(16人)", "统计(22人)")), 253, grayGroup));

        string[] xiaohuiLines =
        [
            "小惠慧新奥杀肖团队",
            "第253期:",
            "【0次】祝", "【1次】祝牛虎兔羊", "【2次】祝龙蛇马鸡", "【3次】", "禁鼠猴猪",
            "【4次】禁狗", "【5次】禁", "【6次】禁",
            "统计结果(共25人)", "按道理主少次,杀多次"
        ];
        OcrRule xiaohui = Rule("小慧慧");
        Assert.Equal("狗", RuleEngine.ExtractFinalValue(xiaohuiLines, 253, xiaohui));
    }

    [Fact]
    public void FindsXiaohuiWithLookalikeCharactersAndFolderIdentity()
    {
        OcrRule xiaohui = Rule("小慧慧");
        string image = @"C:\结果\9.10-嫣然心水\小慧慧\sample.jpg";
        string[] lines =
        [
            "小惠慧新奥杀肖团队",
            "第253期:",
            "【4次】禁狗"
        ];

        Assert.Single(RuleEngine.FindMatches(image, lines, [xiaohui]));
    }

    [Fact]
    public void ExtractsChanganzhixingWhenTheBannerWrapsBeforeThePayload()
    {
        OcrRule rule = Rule("长安之星");
        string[] lines =
        [
            "第252期:新澳门万佛★双波", "【绿波红波】22中啦",
            "第253期：新澳门万佛★双波", "二【红波绿波】00中啦", "OK",
            "新澳彩251期：☆天狼星☆新", "澳彩4头【3401】◆【特30】错", "→鸿运高手榜无名",
            "新澳彩253期：☆天狼星☆新", "澳彩4头【2034】【特00】错", "→鸿运高手榜无名",
            "251期★【天生我财正实战七尾", "正】0123689尾【特开30】中",
            "正252期正【长安之星***100新澳", "门六合彩100***杀码】【16,41】开",
            "22..对鸿运论坛可查",
            "正253期正【长安之星***100新澳", "门六合彩100***杀码】【47,19】开",
            "00..对鸿运论坛可查"
        ];

        Assert.Equal("47 19", RuleEngine.ExtractFinalValue(lines, 253, rule));
    }

    [Fact]
    public void ExtractsYantatimuWhenRowIndexMergesIntoThePeriodColumn()
    {
        OcrRule halfWave = Rule("雁塔题名半波");
        OcrRule tailNumber = Rule("雁塔题名杀合");
        OcrRule head = Rule("雁塔题名杀头");
        string[] halfWaveLines =
        [
            "1雁塔题名新澳门版", "2250错32250错42",
            "3251杀蓝双杀3头", "4252杀蓝单杀0头", "5253杀绿单杀0头", "6"
        ];
        string[] combineLines =
        [
            "1雁塔题名新澳门版", "2250错13250错15",
            "3251杀5合杀猴", "4252杀3合杀鼠", "5253杀4合杀龙", "6", "7"
        ];

        Assert.Equal("绿单", RuleEngine.ExtractFinalValue(halfWaveLines, 253, halfWave));
        Assert.Equal("04合", RuleEngine.ExtractFinalValue(combineLines, 253, tailNumber));
        Assert.Equal("0头", RuleEngine.ExtractFinalValue(halfWaveLines, 253, head));
    }

    // 314 期真实回归素材（嫣然心水本地 medium 行，1 倍整图、3 倍统计块放大、6 倍数字放大三遍读数一致）：
    // 三张卡的总量都和逐行次数之和对不上 1——品鉴印 43 而 12 行相加 44，小慧慧印 26 而 Σ=25，
    // 灰灰团印 24 而 Σ=23。行、次数、生肖都没有漏读或跨行错配，差 1 出在卡面自己，
    // 所以允许 |Σ − 卡面| ≤ 1 且最高次数行唯一时取最高次数的生肖（这里三张都是鸡）。
    // 断言的是鸡而不是第一个出现的猴/兔/牛虎……：放行后仍必须走最高次数分支。
    [Fact]
    public void SummarySheetsRequireIndependentEvidenceWhenPrintedTotalDisagrees()
    {
        string[] pinjian =
        [
            "314期您的统计结果", "【生肖统计】(总次数:43次)",
            "[01次】猴×××××", "[02次]鼠牛兔马", "[04次】虎蛇羊",
            "[05次】猪狗×××××", "[06次】龙×××××", "[07次】鸡×××××"
        ];
        string[] xiaohuihui =
        [
            "第314期：",
            "【0次】祝兔", "【1次】", "祝牛蛇马羊猴", "【2次】祝鼠虎", "【3次】", "禁狗猪",
            "【4次】禁龙", "【5次】", "【6次】禁鸡",
            "统计结果（共26人）"
        ];
        string[] huihuituan =
        [
            "第314期统计(24人):",
            "【0次】兔", "【1次】牛虎马羊狗猪", "【2次】龙猴", "【3次】",
            "【4次】蛇鼠", "【5次】鸡", "【6次】", "【7次】"
        ];

        Assert.Null(RuleEngine.ExtractFinalValue(pinjian, 314, Rule("品鉴")));
        Assert.Null(RuleEngine.ExtractFinalValue(xiaohuihui, 314, Rule("小慧慧")));
        Assert.Null(RuleEngine.ExtractFinalValue(huihuituan, 314, Rule("灰灰团")));

        // Raw text never resolves a printed-total discrepancy, whatever its size.
        string[] offByTwo =
        [
            "314期您的统计结果", "【生肖统计】(总次数:46次)",
            "[01次】猴×××××", "[02次]鼠牛兔马", "[04次】虎蛇羊",
            "[05次】猪狗×××××", "[06次】龙×××××", "[07次】鸡×××××"
        ];
        Assert.Null(RuleEngine.ExtractFinalValue(offByTwo, 314, Rule("品鉴")));
        Assert.Equal("已找到314期文字，但次数合计与卡面总量不符（卡面46，合计44，6行）；需独立完整统计复核",
            RuleEngine.DescribeExtractionFailure(offByTwo, 314, Rule("品鉴")));
    }

    // Ties themselves are valid. A mismatched total requires independent evidence.
    [Fact]
    public void SummarySheetsRejectOffByOneWhenTheTopCountIsSharedAndStillJoinTiesThatAddUp()
    {
        OcrRule pinjian = Rule("品鉴");
        string[] tiedOffByOne =
        [
            "314期您的统计结果", "【生肖统计】(总次数:7次)", "[3次]鼠", "[3次]牛"
        ];
        string[] tiedExact =
        [
            "314期您的统计结果", "【生肖统计】(总次数:6次)", "[3次]鼠", "[3次]牛"
        ];

        Assert.Null(RuleEngine.ExtractFinalValue(tiedOffByOne, 314, pinjian));
        Assert.Equal("已找到314期文字，但次数合计与卡面总量不符（卡面7，合计6，2行）；需独立完整统计复核",
            RuleEngine.DescribeExtractionFailure(tiedOffByOne, 314, pinjian));
        Assert.Equal("鼠牛", RuleEngine.ExtractFinalValue(tiedExact, 314, pinjian));
    }

    // A higher missing bucket before a later populated bucket remains evidence of an incomplete read.
    [Fact]
    public void SummarySheetsStillRejectAGapAboveTheTopCountInsideTheTolerance()
    {
        OcrRule pinjian = Rule("品鉴");
        string[] lines =
        [
            "314期您的统计结果", "【生肖统计】(总次数:6次)",
            "[1次]兔", "[3次]", "[2次]龙", "[1次]马", "[1次]羊"
        ];

        Assert.Null(RuleEngine.ExtractFinalValue(lines, 314, pinjian));
        Assert.Equal("已找到314期文字，但最高次数之上存在空档（第3次无生肖）",
            RuleEngine.DescribeExtractionFailure(lines, 314, pinjian));
    }

    // 非总量分支也要各自可区分：没有次数行、次数行没生肖、区段词缺失、最高档之上有空档。
    [Fact]
    public void SummarySheetsReportWhichStepFailedInsteadOfOneGenericSentence()
    {
        OcrRule pinjian = Rule("品鉴");
        var sectioned = new OcrRule("品鉴", "统计生肖", "品鉴", Section: "生肖统计");

        string noRows = RuleEngine.DescribeExtractionFailure(
            ["314期您的统计结果", "本期暂无表格"], 314, pinjian);
        string noZodiac = RuleEngine.DescribeExtractionFailure(
            ["314期您的统计结果", "【0次】", "【1次】", "【2次】"], 314, pinjian);
        string noSection = RuleEngine.DescribeExtractionFailure(
            ["314期您的统计结果", "【1次】猴"], 314, sectioned);
        string totalMismatch = RuleEngine.DescribeExtractionFailure(
            ["314期您的统计结果", "【生肖统计】(总次数:43次)", "[01次】猴", "[02次]鼠牛"], 314, pinjian);
        string gap = RuleEngine.DescribeExtractionFailure(
            ["314期您的统计结果", "【1次】兔", "【3次】", "【2次】龙"], 314, pinjian);

        Assert.Equal("已找到314期文字，但统计块内没有可解析的次数行", noRows);
        Assert.Equal("已找到314期文字，但统计行里没有生肖", noZodiac);
        Assert.Equal("已找到314期文字，但最高次数之上存在空档（第3次无生肖）", gap);
        Assert.Equal("已找到314期文字，但次数合计与卡面总量不符（卡面43，合计5，2行）；需独立完整统计复核", totalMismatch);
        Assert.StartsWith("已找到314期文字，但未定位到本期统计块", noSection);
        Assert.Contains("生肖统计", noSection);
        Assert.Equal(5, new[] { noRows, noZodiac, noSection, totalMismatch, gap }
            .Distinct(StringComparer.Ordinal).Count());
        // 非总量分支同样不能再吐出旧的那一句笼统文案。
        Assert.DoesNotContain("未提取到最高次数生肖", noRows + noZodiac + noSection + gap);
    }
}
