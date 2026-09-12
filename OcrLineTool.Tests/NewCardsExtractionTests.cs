using OcrLineTool;

namespace OcrLineTool.Tests;

public sealed class NewCardsExtractionTests
{
    private static IReadOnlyList<OcrRule> Rules(string file) => RuleCatalog.Load(Path.Combine(
        ResultFilePaths.ConfigurationDirectory(AppContext.BaseDirectory), file));
    private static OcrRule Rule(string id) => Rules("新澳六合彩资料.json").Single(rule => rule.Id == id);

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
        Assert.DoesNotContain(women, RuleEngine.FindMatches(
            @"C:\图片\9.12-新澳高手\老男女味\elder.jpg", elderCard, rules, rules));
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
