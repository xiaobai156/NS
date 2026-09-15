using OcrLineTool;

namespace OcrLineTool.Tests;

public sealed class NewCardsExtractionTests
{
    private static IReadOnlyList<OcrRule> Rules(string file) => RuleCatalog.Load(Path.Combine(
        ResultFilePaths.ConfigurationDirectory(AppContext.BaseDirectory), file));
    private static OcrRule Rule(string id) => Rules("新澳六合彩资料.json").Single(rule => rule.Id == id);

    [Fact]
    public void JiuwangyeDerivesTheMissingHeadFromTheFourHeadRow()
    {
        OcrRule rule = Rules("嫣然心水.json").Single(item => item.Id == "九王爷");
        string[] lines =
        [
            "【256期新澳门区天机阁九王爷中特四头】★0头,2头,3头,4头,",
            "【257期新澳门区天机阁九王爷中特四头】0头,1头,3头,4头,"
        ];

        Assert.Equal("2头", RuleEngine.ExtractFinalValue(lines, 257, rule));
        Assert.Equal("1头", RuleEngine.ExtractFinalValue(lines, 256, rule));
    }

    [Fact]
    public void JiuwangyeStaysMissingWhenTheFourHeadsAreNotClean()
    {
        OcrRule rule = Rules("嫣然心水.json").Single(item => item.Id == "九王爷");

        Assert.Null(RuleEngine.ExtractFinalValue(
            ["【257期新澳门区天机阁九王爷中特四头】0头,1头,3头,"], 257, rule));
        Assert.Null(RuleEngine.ExtractFinalValue(
            ["【257期新澳门区天机阁九王爷中特四头】0头,1头,1头,3头,"], 257, rule));
        Assert.Null(RuleEngine.ExtractFinalValue(
            ["【257期新澳门区天机阁九王爷中特四头】0头,1头,2头,3头,4头,"], 257, rule));
        Assert.Null(RuleEngine.ExtractFinalValue(
            ["【256期新澳门区天机阁九王爷中特四头】0头,2头,3头,4头,"], 257, rule));
    }

    [Fact]
    public void OfficialTwoZodiacsUsesThePairAboveTheIssue()
    {
        string[] lines =
        [
            "六", "官方殺肖", "合官", "牛蛇", "語", "第254期", "特碼大包圍",
            "26 31 27 33 32 46", "1224 13 11 38 42", "06 45 21 48 10 01",
            "上期开奖结果：34 04 38 03 31 27 T05"
        ];
        Assert.Equal("牛蛇", RuleEngine.ExtractFinalValue(lines, 254, Rule("官方两肖")));
    }

    [Fact]
    public void LaomoPairIgnoresThePoemZodiacs()
    {
        string[] lines =
        [
            "六合彩来了", "告诉老墨", "上期开奖结果34 04 38 03 31 27 T05", "祝君中獎長跟必賺",
            "兔", "豬", "第254期", "四五書頭點玄機", "043", "32", "雞狗本是同根生"
        ];
        Assert.Equal("兔猪", RuleEngine.ExtractFinalValue(lines, 254, Rule("老墨两肖")));
    }

    [Fact]
    public void TukuTwoZodiacsReadsTheRowBelowTheIssue()
    {
        string[] lines =
        [
            "图库禁肖图", "第254期", "鼠", "豬", "上期开奖结果：34 04 38 03 31 27 T05"
        ];
        Assert.Equal("鼠猪", RuleEngine.ExtractFinalValue(lines, 254, Rule("图库禁两肖")));
    }

    [Fact]
    public void ShuaiteTenZodiacsDeductTheTwoMissing()
    {
        string[] lines =
        [
            "新澳", "05 35 15 28 13 49刷 16", "虎猴龍兔馬馬新兔", "253期开", "帥铁精选十肖",
            "254期", "精选:牛雞虎羊蛇鼠豬馬龍兔", "开??",
            "253期", "精选:馬狗牛雞兔豬猴羊虎鼠", "兔16中"
        ];
        Assert.Equal("狗猴", RuleEngine.ExtractFinalValue(lines, 254, Rule("帅铁两肖")));
    }

    [Fact]
    public void XinshuiTenZodiacsDeductTheTwoMissing()
    {
        string[] lines =
        [
            "新澳", "05 35 15 28 13 49刷 16", "虎猴龍兔馬馬新兔", "253期开", "心水站【无错十肖〗期期中",
            "254期", "猴雞牛鼠豬蛇馬虎狗龍", "开??中",
            "253期", "蛇虎羊雞猴馬牛龍狗鼠", "兔16错"
        ];
        Assert.Equal("兔羊", RuleEngine.ExtractFinalValue(lines, 254, Rule("心水两两肖")));
    }

    [Fact]
    public void JinqianPairReadsTonightKillRow()
    {
        string[] lines =
        [
            "新澳", "05 35 15 28 13 49刷 16", "虎猴龍兔馬篤新兔", "253期开", "金钱网【买特杀两肖】",
            "254期", "今晚杀【豬羊】", "开??",
            "253期", "今晚杀【雞龍】", "兔16中"
        ];
        Assert.Equal("猪羊", RuleEngine.ExtractFinalValue(lines, 254, Rule("金钱两肖")));
    }

    [Fact]
    public void WangzhongwangPairReadsKillRow()
    {
        string[] lines =
        [
            "新澳", "05 35 15 28 13 49刷 16", "虎猴龍兔馬馬新兔", "253期开", "王中王【禁开双肖】经典版",
            "254期", "杀特肖：馬鼠", "开??",
            "253期", "杀特肖：卜 馬ł", "兔16中"
        ];
        Assert.Equal("马鼠", RuleEngine.ExtractFinalValue(lines, 254, Rule("王不王两肖")));
    }

    [Fact]
    public void ZengdaorenSingleZodiac()
    {
        string[] lines =
        [
            "曾道人禁肖", "第254期", "恭喜發財", "恭喜發財", "禁", "蛇",
            "上期开奖结果：34 04 38 03 31 27 T05"
        ];
        Assert.Equal("蛇", RuleEngine.ExtractFinalValue(lines, 254, Rule("曾道人小杀肖")));
    }

    [Fact]
    public void XinshuiSingleZodiac()
    {
        string[] lines =
        [
            "新澳", "05 35 15 28 13 49", "刷16", "虎猴龍兔馬馬新兔", "253期开",
            "心水站『杀特肖』全年无错", "254期", "今晚生肖不要买:鼠", "开??中",
            "253期", "今晚生肖不要买:虎", "兔16中"
        ];
        Assert.Equal("鼠", RuleEngine.ExtractFinalValue(lines, 254, Rule("心水杀肖肖肖")));
    }

    [Fact]
    public void JucaitangZodiacAndTailFromTheSameCard()
    {
        string[] lines =
        [
            "新澳", "05 35 15 28 13 49刷 16", "虎猴龍兔馬馬新兔", "253期开",
            "选特码【禁一肖一尾】聚彩堂", "254期", "禁:【馬】", "【8】尾", "开??",
            "253期", "禁：【鼠】", "【6】尾", "兔16错"
        ];
        Assert.Equal("马", RuleEngine.ExtractFinalValue(lines, 254, Rule("聚彩堂一肖")));
        Assert.Equal("8尾", RuleEngine.ExtractFinalValue(lines, 254, Rule("聚彩堂一尾")));
    }

    [Fact]
    public void YimaZodiacAndTailFromTheSameCard()
    {
        string[] lines =
        [
            "新澳", "05 35 15 28 13 49斤", "刷16", "253期开", "月经",
            "姨妈封杀→【一肖一尾】", "254期", "杀:【牛】", "【7】尾", "开??",
            "253期", "杀：【虎】", "【6】尾", "兔16错"
        ];
        Assert.Equal("牛", RuleEngine.ExtractFinalValue(lines, 254, Rule("姨妈肖杀")));
        Assert.Equal("7尾", RuleEngine.ExtractFinalValue(lines, 254, Rule("姨妈尾杀")));
    }

    [Fact]
    public void CaihongHalfWave()
    {
        string[] lines =
        [
            "新澳", "05 35 15 28 13 49", "刷", "16", "253期开", "虎猴龍兔馬馬", "新", "兔",
            "彩虹网【精杀半波】正版", "254期", "今期特码杀：蓝波单！", "开??",
            "253期", "今期特码杀：", "红波双", "兔16中"
        ];
        Assert.Equal("蓝单", RuleEngine.ExtractFinalValue(lines, 254, Rule("彩虹半波")));
    }

    [Fact]
    public void ChaoyingjiaHalfWave()
    {
        string[] lines =
        [
            "新澳", "05 35 15 28 13 49", "刷", "16", "虎猴龍兔馬馬", "253期开", "新", "兔",
            "赢", "大赢家", "绝杀半波", "付费版", "254期", "赢家必杀【红波单】", "开??",
            "253期", "赢家必杀【", "蓝波单", "J", "兔16中"
        ];
        Assert.Equal("红单", RuleEngine.ExtractFinalValue(lines, 254, Rule("超级赢家半波波")));
    }

    [Fact]
    public void HeadCardsReadTheKillRow()
    {
        string[] wang =
        [
            "新澳", "05 35 15 28 13 49刷 16", "虎猴龍兔馬馬新兔", "253期开",
            "王中王【绝杀一头】经典版", "254期", "特码绝杀:三头√", "开??",
            "253期", "特码绝杀:四头√", "兔16中"
        ];
        Assert.Equal("3头", RuleEngine.ExtractFinalValue(wang, 254, Rule("王不王一头")));

        string[] shen =
        [
            "新澳", "05 35 15 28 13 49刷 16", "虎猴龍兔馬馬新兔", "253期开",
            "神算子【避开①头】绝杀版", "254期", "特码避开:三头√", "开??",
            "253期", "特码避开:三头√", "兔16中"
        ];
        Assert.Equal("3头", RuleEngine.ExtractFinalValue(shen, 254, Rule("神算子避头")));

        string[] cai =
        [
            "新澳", "05 35 15 28 13 49", "刷16", "虎猴龍兔馬马新兔", "253期开",
            "财神网【绝杀一头】精华版", "254期", "财神禁：【3头】", "开??",
            "253期", "财神禁：【4头】", "兔16中"
        ];
        Assert.Equal("3头", RuleEngine.ExtractFinalValue(cai, 254, Rule("财神一头")));
    }

    [Fact]
    public void NineZodiacTablesReadTheTargetRow()
    {
        string[] recent =
        [
            "新澳", "05 35 15 28 13 49刷 16", "253期开虎", "猴", "龍兔", "馬馬新兔",
            "近期开奖员【九肖中特】", "254期", "羊兔虎雞蛇馬龍牛鼠", "开??",
            "253期", "羊豬虎兔鼠龍狗雞猴", "兔16中"
        ];
        Assert.Equal("羊兔虎鸡蛇马龙牛鼠", RuleEngine.ExtractFinalValue(recent, 254, Rule("近期开奖员")));

        string[] maolao =
        [
            "新澳", "05 35 15 28 13 49刷16", "253期开虎猴龍兔馬馬新兔",
            "李嘉诚网友【老毛头】九肖中特", "254期", "龍鼠羊虎狗兔豬猴牛", "开??",
            "253期", "猴豬雞馬羊龍蛇狗兔", "兔16中"
        ];
        Assert.Equal("龙鼠羊虎狗兔猪猴牛", RuleEngine.ExtractFinalValue(maolao, 254, Rule("毛老二")));

        string[] tongtian =
        [
            "新澳", "05 35 15 28 13 49刷16", "253期开虎猴龍兔馬馬新兔",
            "通天资料--九肖中特", "254期", "【虎兔雞蛇猴牛狗豬鼠】", "开??",
            "253期", "【兔猴鼠牛馬豬蛇狗雞】", "兔16中"
        ];
        Assert.Equal("虎兔鸡蛇猴牛狗猪鼠", RuleEngine.ExtractFinalValue(tongtian, 254, Rule("通天九九肖")));

        string[] dayingjia =
        [
            "新澳", "053515281349", "刷", "16", "253期开", "虎猴龍兔馬馬新", "兔", "赢",
            "大赢家", "【狂赢九肖】", "付费版", "254期", "狂赢九肖:狗羊虎猴蛇龍鼠牛兔", "开??",
            "253期", "狂赢九肖:龍蛇鼠猴狗豬雞馬虎", "兔16错"
        ];
        Assert.Equal("狗羊虎猴蛇龙鼠牛兔", RuleEngine.ExtractFinalValue(dayingjia, 254, Rule("大赢家九肖")));
    }

    [Fact]
    public void ZhanShaSeriesReadsTheTargetIssueRow()
    {
        IReadOnlyList<OcrRule> rules = Rules("新澳高手.json");
        OcrRule Rule(string id) => rules.Single(rule => rule.Id == id);

        string[] halfWave =
        [
            "219期(红双)√", "220期(红单)√", "221期(蓝单)√", "222期(绿单)√",
            "251期(红双)×", "252期(红单)√", "253期(蓝单)√", "254期(绿单)√"
        ];
        Assert.Equal("绿单", RuleEngine.ExtractFinalValue(halfWave, 254, Rule("斩杀半波")));

        string[] oneLine =
        [
            "斩杀一行", "227期(金)√", "228期(土)√", "252期(金)√", "253期(金)√", "254期(水)√"
        ];
        Assert.Equal("水", RuleEngine.ExtractFinalValue(oneLine, 254, Rule("斩杀一行")));

        string[] twoTails =
        [
            "斩杀两尾", "224期(8.4尾)√", "225期(8.5尾)√",
            "252期(2.7尾)×", "253期(0.1尾)√", "254期(5.6尾)√"
        ];
        Assert.Equal("5尾+6尾", RuleEngine.ExtractFinalValue(twoTails, 254, Rule("斩杀两尾")));

        string[] twoZodiacs =
        [
            "斩杀两肖", "220期(鸡牛)√", "221期(蛇虎)√",
            "252期(鸡牛)×", "253期(蛇虎)√", "254期(羊猪)√"
        ];
        Assert.Equal("羊猪", RuleEngine.ExtractFinalValue(twoZodiacs, 254, Rule("斩杀两肖")));

        string[] oneHead =
        [
            "斩杀一头", "218期(0头)√", "219期(3头)√",
            "252期(1头)√", "253期(0头)√", "254期(0头)√"
        ];
        Assert.Equal("0头", RuleEngine.ExtractFinalValue(oneHead, 254, Rule("斩杀一头")));
    }

    [Fact]
    public void ZhanShaTitleLessStripsMatchByFolderAndRowShape()
    {
        IReadOnlyList<OcrRule> rules = Rules("新澳高手.json");
        string Path(string file) => @"C:\图片\9.10-新澳高手\斩杀系列\" + file;

        string[] halfWave =
        [
            "248期(绿双)√", "249期(绿单)√", "250期(红单)√",
            "251期(红双)×", "252期(红单)√", "253期(蓝单)√"
        ];
        string[] oneLine =
        [
            "248期(水)√", "249期(±)√", "250期(金)√",
            "251期(土)√", "252期(金)√", "253期(金)√"
        ];
        string[] twoTails =
        [
            "248期(4.9尾)√", "249期(1.5尾)√", "250期(3.6尾)√",
            "251期(2.8尾)√", "252期(2.7尾)×", "253期(0.1尾)√"
        ];
        string[] twoZodiacs =
        [
            "248期(龙马)√", "249期(鼠龙)√", "250期(猪猴)√",
            "251期(鼠牛)×", "252期(鸡牛)×", "253期(蛇虎)√"
        ];
        string[] oneHead =
        [
            "248期(3头)√", "249期(2头)×", "250期(0头)√",
            "251期(3头)×", "252期(1头)√", "253期(0头)√"
        ];

        Assert.Equal("斩杀半波", Assert.Single(RuleEngine.FindMatches(Path("a.jpg"), halfWave, rules, rules)).Id);
        Assert.Equal("斩杀一行", Assert.Single(RuleEngine.FindMatches(Path("b.jpg"), oneLine, rules, rules)).Id);
        Assert.Equal("斩杀两尾", Assert.Single(RuleEngine.FindMatches(Path("c.jpg"), twoTails, rules, rules)).Id);
        Assert.Equal("斩杀两肖", Assert.Single(RuleEngine.FindMatches(Path("d.jpg"), twoZodiacs, rules, rules)).Id);
        Assert.Equal("斩杀一头", Assert.Single(RuleEngine.FindMatches(Path("e.jpg"), oneHead, rules, rules)).Id);

        Assert.Equal("蓝单", RuleEngine.ExtractFinalValue(halfWave, 253, rules.Single(rule => rule.Id == "斩杀半波")));
        Assert.Equal("金", RuleEngine.ExtractFinalValue(oneLine, 253, rules.Single(rule => rule.Id == "斩杀一行")));
        Assert.Equal("0尾+1尾", RuleEngine.ExtractFinalValue(twoTails, 253, rules.Single(rule => rule.Id == "斩杀两尾")));
        Assert.Equal("蛇虎", RuleEngine.ExtractFinalValue(twoZodiacs, 253, rules.Single(rule => rule.Id == "斩杀两肖")));
        Assert.Equal("0头", RuleEngine.ExtractFinalValue(oneHead, 253, rules.Single(rule => rule.Id == "斩杀一头")));

        string[] threeHeads =
        [
            "255期：三头必中【0.2.3】开？00中", "254期:三头必中【0.2.4】开鸡22中",
            "253期：三头必中【0.2.1】开兔16中", "252期：三头必中【0.2.4】开鸡22中",
            "251期：三头必中【0.3.4】开牛30中", "250期：三头必中【1.2.3】开蛇14中"
        ];
        Assert.Empty(RuleEngine.FindMatches(Path("f.jpg"), threeHeads, rules, rules));
    }

    [Fact]
    public void NvrenweiKillsTwoZodiacs()
    {
        string[] lines =
        [
            "澳门女人味", "AOMENNVRENWEI", "2026年254期",
            "244", "【女人味杀2肖】", "虎猴", "开鸡46准",
            "253", "【女人味杀2肖】", "马羊", "开兔16准",
            "254", "【女人味杀2肖】", "马兔", "开？00准",
            "249【女人味⑥肖】", "羊猪狗鼠蛇虎", "开猴23错",
            "254【女人味⑥肖】", "猪鸡羊狗龙蛇", "开？00准",
            "254【女人味③肖】", "猪鸡羊",
            "254【女人味①肖】猪",
            "无私奉献！敬请参考，错误勿怪"
        ];
        Assert.Equal("马兔", RuleEngine.ExtractFinalValue(lines, 254,
            Rules("新澳高手.json").Single(rule => rule.Id == "女人味")));
    }

    [Fact]
    public void NvrenweiDoesNotCrossMatchMaleOrElderCards()
    {
        IReadOnlyList<OcrRule> rules = Rules("新澳高手.json");
        OcrRule women = rules.Single(rule => rule.Id == "女人味");
        OcrRule men = rules.Single(rule => rule.Id == "男人牛");

        string[] womenCard =
        [
            "澳门女人味", "AOMENNVRENWEI", "2026年255期",
            "245【女人味杀2肖】", "虎猴", "开牛18准",
            "246【女人味杀2肖】", "猪龙", "开牛30准",
            "247【女人味杀2肖】", "羊蛇", "开兔40准",
            "248【女人味杀2肖】", "羊蛇", "开猪20准",
            "249【女人味杀2肖", "龙牛", "开猴23准",
            "250【女人味杀2肖", "鼠牛", "开蛇14准",
            "251【女人味杀2肖", "牛鸡", "开牛30错",
            "252【女人味杀2肖", "鸡狗", "开鸡22错",
            "253【女人味杀2肖】", "马羊", "开兔16准",
            "254【女人味杀2肖", "马兔", "开蛇02准",
            "255【女人味杀2肖】", "虎狗", "开？00准",
            "250【女人味⑥肖】", "马猪兔猴鸡龙", "开蛇14错",
            "251【女人味⑥肖】", "兔狗鼠马龙羊", "开牛30错",
            "252【女人味⑥肖】", "牛虎蛇兔马鼠", "开鸡22错",
            "253【女人味⑥肖】龙猪猴蛇狗鸡", "开兔16错",
            "254【女人味⑥肖】猪鸡羊狗龙蛇", "开蛇02准",
            "255【女人味⑥肖】马鼠兔鸡牛蛇", "开？00准",
            "255【女人味③肖】马鼠兔", "255【女人味①肖】马",
            "无私奉献！敬请参考，错误勿怪"
        ];
        string[] menCard =
        [
            "澳门男人味", "R", "【原创】→男人味：",
            "244期【男人味稳杀二肖】→兔龙开鸡46准",
            "245期【男人味稳杀二肖】→龙蛇开牛18准",
            "246期【男人味稳杀二肖】→蛇马开牛30准",
            "247期【男人味稳杀二肖】→马羊开兔40准",
            "248期【男人味稳杀二肖】→羊猴开猪20准",
            "250期【男人味稳杀二肖】→鸡狗", "开蛇14准",
            "251期【男人味稳杀二肖】→狗猪", "开牛30准",
            "252期【男人味稳杀二肖】→猪鼠开鸡22准",
            "253期【男人味稳杀二肖】→鼠牛开兔16准",
            "254期【男人味稳杀二肖】→牛虎开蛇02准",
            "255期【男人味稳杀二肖】→虎兔开？准",
            "253期【男人味六肖】→鼠兔虎龙蛇马开兔16准",
            "255期【男人味六肖】→虎蛇龙马羊猴开？准",
            "255期【男人味三肖】→虎蛇龙"
        ];
        string[] elderCard =
        [
            "澳门老人味", "LAORENWEI", "原创者：钱多多",
            "252期:绝杀三肖→兔龙蛇←开鸡22√",
            "253期:绝杀三肖→羊猪鸡←开兔16√",
            "254期:绝杀三肖→狗猴马←开蛇02√",
            "255期:绝杀三肖→鼠羊虎←开？？√",
            "252期：六肖《猴鼠虎鸡猪马》开鸡22中",
            "253期：六肖《牛马猴龙蛇兔》开兔16中",
            "254期：六肖《龙猪鸡牛虎蛇》开蛇02中",
            "255期：六肖《鸡猴马狗牛蛇》开？？中",
            "无私奉献！个人心水，错误勿怪！"
        ];

        Assert.Equal("虎狗", RuleEngine.ExtractFinalValue(womenCard, 255, women));
        Assert.Contains(women, RuleEngine.FindMatches(
            @"C:\图片\9.12-新澳高手\老男女味\women.jpg", womenCard, rules, rules));
        Assert.DoesNotContain(women, RuleEngine.FindMatches(
            @"C:\图片\9.12-新澳高手\老男女味\men.jpg", menCard, rules, rules));
        // The elder card has no exact sibling identity, so the fuzzy candidate
        // may appear; it must never produce a value.
        Assert.Null(RuleEngine.ExtractFinalValue(elderCard, 255, women));
        Assert.Contains(men, RuleEngine.FindMatches(
            @"C:\图片\9.12-新澳高手\老男女味\men.jpg", menCard, rules, rules));
    }

    [Fact]
    public void AomenTianKongKillsTenNumbers()
    {
        string[] lines =
        [
            "★澳门天空★决杀十码",
            "255期", "决杀X十码", "【03 18 21 22 23 26 34 35 37 39】", "开??准",
            "253期", "决杀X十码", "【01 02 0306 08 18 29 39 43 45】", "开16准",
            "252期", "决杀X十码", "【03 06 11 15 19 26 37 38 43 44】", "开22准",
            "251期", "决杀X十码", "【03 08 10 11 22 23 32 36 42 48】", "开30准"
        ];

        IReadOnlyList<OcrRule> rules = Rules("新澳高手.json");
        OcrRule rule = rules.Single(item => item.Id == "天空杀");

        Assert.Equal("03 18 21 22 23 26 34 35 37 39", RuleEngine.ExtractFinalValue(lines, 255, rule));
        Assert.Equal("03 08 10 11 22 23 32 36 42 48", RuleEngine.ExtractFinalValue(lines, 251, rule));
        Assert.Contains(rule, RuleEngine.FindMatches(
            @"C:\图片\9.12-新澳高手\澳门天空杀\a.jpg", lines, rules, rules));
        Assert.DoesNotContain(rule, RuleEngine.FindMatches(
            @"C:\图片\9.12-新澳高手\其他文件夹\a.jpg", lines, rules, rules));
    }

    [Fact]
    public void NewRulesMatchTheirRealCardTextAsCandidates()
    {
        IReadOnlyList<OcrRule> gaoshou = Rules("新澳高手.json");
        OcrRule RuleFrom(IReadOnlyList<OcrRule> rules, string id) => rules.Single(rule => rule.Id == id);

        string[] halfWave =
        [
            "219期(红双)√", "220期(红单)√", "221期(蓝单)√", "222期(绿单)√",
            "252期(红单)√", "253期(蓝单)√", "254期(绿单)√"
        ];
        Assert.Contains(RuleFrom(gaoshou, "斩杀半波"),
            RuleEngine.FindMatches(@"C:\图片\9.11-新澳高手\斩杀系列\a.jpg", halfWave, gaoshou, gaoshou));

        string[] twoZodiacs = ["斩杀两肖", "252期(鸡牛)×", "253期(蛇虎)√", "254期(羊猪)√"];
        Assert.Contains(RuleFrom(gaoshou, "斩杀两肖"),
            RuleEngine.FindMatches(@"C:\图片\9.11-新澳高手\斩杀系列\b.jpg", twoZodiacs, gaoshou, gaoshou));
        Assert.DoesNotContain(RuleFrom(gaoshou, "斩杀半波"),
            RuleEngine.FindMatches(@"C:\图片\9.11-新澳高手\斩杀系列\b.jpg", twoZodiacs, gaoshou, gaoshou));

        string[] nvrenwei =
        [
            "澳门女人味", "AOMENNVRENWEI", "2026年254期",
            "254", "【女人味杀2肖】", "马兔", "开？00准",
            "254【女人味⑥肖】", "猪鸡羊狗龙蛇", "开？00准"
        ];
        Assert.Contains(RuleFrom(gaoshou, "女人味"),
            RuleEngine.FindMatches(@"C:\图片\9.11-新澳高手\老男女味\c.jpg", nvrenwei, gaoshou, gaoshou));

        IReadOnlyList<OcrRule> yanran = Rules("嫣然心水.json");
        string[] aiwanting =
        [
            "【9.30分开新澳门彩爱晚亭杀1", "肖】", "001-030期错3", "爱晚亭",
            "253期：", "鼠开兔16", "254期：", "马开00",
            "爱晚亭新澳门杀1肖从001期开始", "统计"
        ];
        Assert.Contains(RuleFrom(yanran, "爱晚亭杀肖"),
            RuleEngine.FindMatches(@"C:\图片\9.11-嫣然心水\爱晚亭\d.jpg", aiwanting, yanran, yanran));

        string[] heibulaji =
        [
            "254期九肖中特→鼠狗龙虎马猪鸡羊猴",
            "254期八肖中特→鼠狗龙虎马猪鸡羊",
            "253期九肖中特→虎羊兔鼠狗马龙蛇猪"
        ];
        Assert.Contains(RuleFrom(yanran, "黑不啦唧"),
            RuleEngine.FindMatches(@"C:\图片\9.11-嫣然心水\黑了吧唧\e.jpg", heibulaji, yanran, yanran));
    }

    [Fact]
    public void ChangAnZhiXingIgnoresItsBannerDecorationDigits()
    {
        OcrRule rule = Rules("嫣然心水.json").Single(item => item.Id == "长安之星");
        string[] lines =
        [
            "252期正【长安之星***700新澳门六合彩700***杀码】【16,41】开22..对鸿运论", "坛可查",
            "正253期【长安之星***700新澳门六合彩700***杀码】【47,19】开16..对鸿运论", "坛可查",
            "254期正【长安之星***100新澳门六合彩700***杀码】【03,47】开02..对鸿运", "论坛可查",
            "255期【长安之星***700新澳门六合彩700***杀码】【26,10】开44..对鸿运", "论坛可查",
            "256期【长安之星***700新澳门六合彩700***杀码】【45,36】开00..对鸿运", "论坛可查"
        ];

        Assert.Equal("45 36", RuleEngine.ExtractFinalValue(lines, 256, rule));

        string[] wrapped = ["正253期正【长安之星***100新澳", "门六合彩100***杀码】【47,19】开"];
        Assert.Equal("47 19", RuleEngine.ExtractFinalValue(wrapped, 253, rule));

        string[] missingAnswer = ["256期【长安之星***700新澳门六合彩700***杀码】开00..对"];
        Assert.Null(RuleEngine.ExtractFinalValue(missingAnswer, 256, rule));
    }

    [Fact]
    public void YanranPinJianAndHuiHuiTuanIdentifyByTheirWatermarks()
    {
        IReadOnlyList<OcrRule> rules = Rules("嫣然心水.json");
        OcrRule pinJian = rules.Single(rule => rule.Id == "品鉴");
        OcrRule huiHuiTuan = rules.Single(rule => rule.Id == "灰灰团");

        string[] pinJianCard =
        [
            "【❤品鉴新澳杀一肖❤】", "【建宇建宇月错2禁】鸡", "【哭包哭包月错1】", "马", "【码小弟弟月错1】", "虎×",
            "【烟雨沫沫月错1禁】", "牛兔羊猪狗蛇猪蛇兔兔兔兔牛兔兔狗", "【梁微微一月错0】", "品鉴",
            "2026256期二您的统计结果",
            "【生肖统计】(总次数:45次)", "【00次〗龙", "【01次】羊", "【02次】猴", "【03次】鼠马猪",
            "【04次》蛇鸡××××", "【05次】牛虎狗×××", "〖【10次》兔×××××", "【2026/09/1313:53:42】"
        ];

        Assert.Contains(pinJian, RuleEngine.FindMatches(@"C:\图片\9.13-嫣然心水\品鉴\a.jpg", pinJianCard, rules, rules));
        Assert.Equal("兔", RuleEngine.ExtractFinalValue(pinJianCard, 256, pinJian));

        string[] huiHuiTuanCard =
        [
            "wilie985团队", "新澳09月份(杀错排名推后)", "【傻丫头】月错0禁马", "【小灰灰正】月错1禁鼠",
            "灰灰团", "【野狼正正】月错1禁猪", "第255期统计(25人):",
            "〖0次〗兔狗", "【1次〗鼠羊猴鸡", "【2次〗猪", "【3次〗牛龙马", "〖【4次】虎蛇"
        ];

        Assert.Contains(huiHuiTuan, RuleEngine.FindMatches(@"C:\图片\9.13-嫣然心水\灰灰团\a.jpg", huiHuiTuanCard, rules, rules));
        Assert.Equal("虎蛇", RuleEngine.ExtractFinalValue(huiHuiTuanCard, 255, huiHuiTuan));
        // The sheet is still a 255-period block: a 256-period run must stay missing.
        Assert.Null(RuleEngine.ExtractFinalValue(huiHuiTuanCard, 256, huiHuiTuan));
    }

    [Fact]
    public void XiaohuihuiKillListMatchesByRowStructure()
    {
        IReadOnlyList<OcrRule> rules = Rules("嫣然心水.json");
        OcrRule rule = rules.Single(item => item.Id == "小灰灰一肖");

        string[] killCard =
        [
            "235刹兔开猪32√", "236刹猪开猴11√", "237刹鸡开羊12√", "238刹蛇开虎17√", "239刹兔开虎05√",
            "240刹鸡开龙27√", "241刹兔开马49√", "242刹蛇开狗09√", "243刹兔开狗21√", "244刹虎开鸡46√",
            "245刹猪开牛18√", "246刹牛开牛30x", "247刹猴开兔40√", "248杀龙开猪20√", "249杀狗开猴23√",
            "250杀牛开蛇14√", "251杀蛇开牛30√", "252杀虎开鸡22√", "253杀鸡开兔16√", "254杀牛开蛇02√",
            "255杀龙开猪44√", "256杀鼠开鸭88?"
        ];

        Assert.Contains(rule, RuleEngine.FindMatches(@"C:\图片\9.13-嫣然心水\小灰灰\a.jpg", killCard, rules, rules));
        Assert.Equal("鼠", RuleEngine.ExtractFinalValue(killCard, 256, rule));

        string[] pinTeCard =
        [
            "薪澳", "eeeecce", "小灰灰荣誉出品", "平特肖", "253《虎》开05", "254《马》开00",
            "255《猪》开44中特", "256《鸡》开22?", "平特尾", "254《0尾》开10中毒平", "255《1尾》开11", "256《2尾》开22?"
        ];
        Assert.DoesNotContain(rule, RuleEngine.FindMatches(@"C:\图片\9.13-嫣然心水\小灰灰\b.jpg", pinTeCard, rules, rules));
    }

    [Fact]
    public void ChaoshanChenlongReadsTheSplitForbiddenLabelRow()
    {
        IReadOnlyList<OcrRule> rules = Rules("嫣然心水.json");
        OcrRule rule = rules.Single(item => item.Id == "潮汕陈龙杀三码");
        string[] lines =
        [
            "潮汕陈龙", "245期禁01.13.25",
            "246期", "禁", "07.19.31",
            "247期", "禁", "06.18.30",
            "248期", "禁", "23.35.47",
            "249期", "禁", "12.24.48",
            "250期", "禁", "24.36.48",
            "251期", "禁", "09.19.31",
            "252期", "禁", "05.17.29",
            "253期", "禁", "10.22.34",
            "254期", "禁", "12.24.48",
            "255期", "禁", "10.34.46",
            "256期", "禁", "11.23.35",
            "257期", "月禁", "41.29.05",
            "澳门特别行政区"
        ];

        Assert.Equal("41 29 05", RuleEngine.ExtractFinalValue(lines, 257, rule));
        Assert.Equal("05 17 29", RuleEngine.ExtractFinalValue(lines, 252, rule));
    }

    [Fact]
    public void ChaoshanChenlongReadsTheRowMergedForbiddenLabel()
    {
        IReadOnlyList<OcrRule> rules = Rules("嫣然心水.json");
        OcrRule rule = rules.Single(item => item.Id == "潮汕陈龙杀三码");
        string[] lines =
        [
            "RA", "不忘初心", "方得始终", "新澳门六合彩", "潮汕陈龙", "13-244×14",
            "245期禁01.13.25", "246期禁07.19.31", "247期禁06.18.30", "248期禁23.35.47",
            "249期禁12.24.48", "250期禁24.36.48", "251期禁09.19.31", "252期禁05.17.29",
            "253期禁10.22.34", "254期禁12.24.48", "255期禁10.34.46", "256期禁11.23.35",
            "257期月禁41.29.05", "澳门特别行政区"
        ];

        Assert.Equal("41 29 05", RuleEngine.ExtractFinalValue(lines, 257, rule));

        var items = lines
            .Select((text, index) => new OcrLineEvidence(
                text, new OcrBox(20, 100 + index * 46, 300, 30), 0.99, "original", "main"))
            .ToList();
        var evidence = new OcrEvidence("probe.png", "probe.png", "h", "h", "original", items);
        RuleExtractionResult result = RuleEngine.ExtractFinalResult(evidence, 257, rule);
        Assert.Equal(RuleExtractionStatus.Success, result.Status);
        Assert.Equal("41 29 05", result.Value);
    }

    [Fact]
    public void TobaccoStripRecoversTheIssueRowZodiac()
    {
        Assert.Equal("羊", RuleEngine.ExtractIssueRowZodiacFromStrip(["257期禁一肖羊"], 257));
        Assert.Equal("羊", RuleEngine.ExtractIssueRowZodiacFromStrip(["257期禁一肖", "羊"], 257));
        Assert.Null(RuleEngine.ExtractIssueRowZodiacFromStrip(["257期禁一肖羊兔"], 257));
        Assert.Null(RuleEngine.ExtractIssueRowZodiacFromStrip(["253期禁一肖牛"], 257));
        Assert.Null(RuleEngine.ExtractIssueRowZodiacFromStrip(["257期禁一肖羊", "野马"], 257));
    }

    [Fact]
    public void HighMountainMergedTiersTakeTheFirstNineZodiacs()
    {
        IReadOnlyList<OcrRule> rules = Rules("新澳高手.json");
        OcrRule rule = rules.Single(item => item.Id == "高山流水");
        string[] merged =
        [
            "高山流水", "第257期",
            "257期：精选肖：狗猪马兔虎龙牛羊猴 257期：精选肖：狗猪马兔虎龙牛 257期：精选⑤肖：狗猪马兔虎"
        ];

        Assert.Equal("狗猪马兔虎龙牛羊猴", RuleEngine.ExtractFinalValue(merged, 257, rule));
        Assert.Null(RuleEngine.ExtractFinalValue(
            ["高山流水", "第257期", "257期：精选肖：狗猪马兔虎龙牛 257期：精选⑤肖：狗猪马兔虎"], 257, rule));
    }

    [Fact]
    public void LiangweiIdentifiesByItsDedicatedFolderWhenSmallOcrSplitsTheWatermark()
    {
        IReadOnlyList<OcrRule> rules = Rules("嫣然心水.json");
        OcrRule rule = rules.Single(item => item.Id == "梁微微");
        string[] split = ["244期杀兔开46", "梁", "微", "247期杀鼠开40", "255期杀猴开44", "256期杀狗开01", "257期杀狗开?"];
        Assert.Contains(rule, RuleEngine.FindMatches(@"C:\图片\9.14-嫣然心水\梁薇薇\a.jpg", split, rules, rules));
        Assert.Equal("狗", RuleEngine.ExtractFinalValue(split, 257, rule));

        string[] foreign =
        [
            "③烟雨沫沫(3((新澳)", "禁一肖", "烟雨沫沫", "255期牛开猪44", "256期票牛开马01", "257期禁蛇"
        ];
        Assert.DoesNotContain(rule, RuleEngine.FindMatches(@"C:\图片\9.14-嫣然心水\梁薇薇\b.jpg", foreign, rules, rules));
    }

    [Fact]
    public void HighMountainStreamMatchesTheRealCardWithoutTheCircledNine()
    {        IReadOnlyList<OcrRule> rules = Rules("新澳高手.json");
        OcrRule rule = rules.Single(item => item.Id == "高山流水");

        string[] card =
        [
            "高山流水", "爱X", "新澳门", "第256期",
            "256期：精选肖：兔马鸡猪狗虎牛蛇羊",
            "256期：精选肖：兔马鸡猪狗虎牛",
            "256期：精选⑤肖：兔马鸡猪狗",
            "256期:精选③肖：兔马鸡",
            "256期:精选①肖：兔",
            "256期:精选码:28.13.22.32.33.17.42.02.36"
        ];

        Assert.Contains(rule, RuleEngine.FindMatches(@"C:\图片\9.13-新澳高手\高山流水\a.jpg", card, rules, rules));
        Assert.Equal("兔马鸡猪狗虎牛蛇羊", RuleEngine.ExtractFinalValue(card, 256, rule));
    }

    [Fact]
    public void AiwantingKillZodiac()
    {
        string[] lines =
        [
            "【9.30分开新澳门彩爱晚亭杀1", "肖】", "001-030期错3", "031-060期错3",
            "121-150期错3", "爱晚亭", "211-240期错4",
            "241期:猴开马49", "242期：", "鸡开狗09", "243期：", "马开狗21",
            "253期：", "鼠开兔16", "254期：", "马开00",
            "爱晚亭新澳门杀1肖从001期开始", "统计"
        ];
        Assert.Equal("马", RuleEngine.ExtractFinalValue(lines, 254,
            Rules("嫣然心水.json").Single(rule => rule.Id == "爱晚亭杀肖")));
    }

    [Fact]
    public void HeibulajiNineZodiacs()
    {
        string[] lines =
        [
            "254期九肖中特→鼠狗龙虎马猪鸡羊猴",
            "254期八肖中特→鼠狗龙虎马猪鸡羊",
            "254期七肖中特→鼠狗龙虎马猪鸡",
            "254期六肖中特→鼠狗龙虎马猪",
            "254期五肖中特→鼠狗龙虎马",
            "254期四肖中特→鼠狗龙虎",
            "253期九肖中特→虎羊兔鼠狗马龙蛇猪",
            "253期八肖中特→虎羊兔鼠狗马龙蛇"
        ];
        Assert.Equal("鼠狗龙虎马猪鸡羊猴", RuleEngine.ExtractFinalValue(lines, 254,
            Rules("嫣然心水.json").Single(rule => rule.Id == "黑不啦唧")));
    }
}
