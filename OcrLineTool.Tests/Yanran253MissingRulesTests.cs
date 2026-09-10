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
            "第253期统计(25人):",
            "〖0次】虎兔", "〖1次】羊猴", "〖2次】牛狗猪", "龙马鸡", "〖4次】鼠蛇",
            "【5次】", "〖6次】", "〖7次】",
            "09月份历史战绩(开出次数):", "212 4 0 2 5 42"
        ];
        OcrRule grayGroup = Rule("灰灰团");
        Assert.Equal("鼠蛇", RuleEngine.ExtractFinalValue(grayGroupLines, 253, grayGroup));

        string[] xiaohuiLines =
        [
            "小惠慧新奥杀肖团队",
            "第253期:",
            "【0次】祝", "【1次】祝牛虎兔羊", "【2次】祝龙蛇马鸡", "【3次】", "禁鼠猴猪",
            "【4次】禁狗", "【5次】禁", "【6次】禁",
            "统计结果(共26人)", "按道理主少次,杀多次"
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
}
