using OcrLineTool;

namespace OcrLineTool.Tests;

public sealed class MacauRuleHardeningTests
{
    private static IReadOnlyList<OcrRule> Rules() => RuleCatalog.Load(Path.Combine(
        ResultFilePaths.ConfigurationDirectory(AppContext.BaseDirectory), "新澳六合彩资料.json"));
    private static OcrRule Rule(string id) => Rules().Single(rule => rule.Id == id);
    public static IEnumerable<object[]> Samples() => SampleText.Split('\n', StringSplitOptions.RemoveEmptyEntries)
        .Select(line => line.Trim().Split('|')).Select(parts => new object[] { parts[0], parts[1].Replace('~', '\n'), parts[2] });
    private const string SampleText = """
        小马哥|庄家必杀:01 04 07 12 14 16 21 27~30 33 43 49|01 04 07 12 14 16 21 27 30 33 43 49
        张小艺|十不中:06 11 17 23 29 33 36 39 45 46|06 11 17 23 29 33 36 39 45 46
        雷锋|狗(21) 猴(23) 鼠(31) 猪(20)|21 23 31 20
        狗庄|03 08 14 22 37 38 39 43 44 45 46~49|03 08 14 22 37 38 39 43 44 45 46 49
        藏宝十二码|藏宝库杀一波12码:03 04 09 10 14 15 25 26 31 42 47 48|03 04 09 10 14 15 25 26 31 42 47 48
        藏宝头|藏宝库无错四头付费版~今晚买:0 2 3 4头|1头
        黄大仙|01 02 07 11 16 25 31 39 41 44|01 02 07 11 16 25 31 39 41 44
        宝典杀|精杀12码:02 06 12 17 20 23~33 35 37 44 45 48|02 06 12 17 20 23 33 35 37 44 45 48
        宝典尾|一尾绝杀→8 8 8 8 8|8尾
        宝典|包围36码03 04 06 07 08 09 10~12 13 14 15 16 19 20 22 24~25 26 28 29 30 31 32 33 34~35 36 37 38 39 41 43 45 46~47 48|03 04 06 07 08 09 10 12 13 14 15 16 19 20 22 24 25 26 28 29 30 31 32 33 34 35 36 37 38 39 41 43 45 46 47 48
        刘伯温二尾|特码封杀→[6 8 尾]|6尾+8尾
        刘伯温|杀:03 06 07 23 24 27 33 44|03 06 07 23 24 27 33 44
        庄家|吃码01 03 04 15 17 22 33 37 42 47|01 03 04 15 17 22 33 37 42 47
        天线宝杀|12码→01 03 06 10 13 14 15 24 26 30 33~43 ←精选杀|01 03 06 10 13 14 15 24 26 30 33 43
        六叔公头|🚫禁【1】头|1头
        通天|05 07 13 25 28 35 37 40|05 07 13 25 28 35 37 40
        帅铁|帅铁杀一肖【馬】|马
        帅铁尾|帅铁杀一尾【8】|8尾
        帅铁头|帅铁杀一头【3】|3头
        杀料|杀→02 05 09 12 22 30 42 43~44 46|02 05 09 12 22 30 42 43 44 46
        杀料五码|杀→羊 狗 鼠 蛇 虎 12 14 41 43 45|12 14 41 43 45
        心水|36计:01 02 04 05 07 10 11~12 13 14 15 16 17 18 19 20~21 23 24 25 26 27 28 29 30~34 35 36 38 39 42 43 44 46~48 49|01 02 04 05 07 10 11 12 13 14 15 16 17 18 19 20 21 23 24 25 26 27 28 29 30 34 35 36 38 39 42 43 44 46 48 49
        心水两肖|牛 狗 ✓|牛狗
        白小姐杀两肖|杀生肖:【兔 馬】|兔马
        内幕|02 03 04 05 06 07 08 11 12 13 14 15 20~21 22 23 24 25 26 27 28 29 30 32 34 37~38 40 42 43 44 45 46 47 48 49|02 03 04 05 06 07 08 11 12 13 14 15 20 21 22 23 24 25 26 27 28 29 30 32 34 37 38 40 42 43 44 45 46 47 48 49
        强哥|强哥三十六码:02 03 04 06 09 10 11 12~13 15 16 17 18 19 20 21 22 23 25 26~27 28 31 32 33 34 37 38 39 40 41 42~43 44 46 47|02 03 04 06 09 10 11 12 13 15 16 17 18 19 20 21 22 23 25 26 27 28 31 32 33 34 37 38 39 40 41 42 43 44 46 47
        锁妖|锁三十六码:02 04 05 06 07 08 09 10~11 12 14 16 17 19 20 23 26 28 29 30~31 32 33 34 35 36 37 38 39 40 41 43~45 46 47 48|02 04 05 06 07 08 09 10 11 12 14 16 17 19 20 23 26 28 29 30 31 32 33 34 35 36 37 38 39 40 41 43 45 46 47 48
        赛马会|35码 赛马会👉01 02 03 04~05 06 07 08 09 11 12 15~16 18 19 21 24 25 28 29~30 31 34 35 36 38 40 42~43 44 45 46 47 48 49|01 02 03 04 05 06 07 08 09 11 12 15 16 18 19 21 24 25 28 29 30 31 34 35 36 38 40 42 43 44 45 46 47 48 49
        聚彩|杀:01 07 08 12 19 32 39 43~46 48|01 07 08 12 19 32 39 43 46 48
        慈善|杀特码:19 21 29 30 31 36 40 48|19 21 29 30 31 36 40 48
        金钱网|杀特码:10 16 18 21 22 24 25 32 38~45 46 49|10 16 18 21 22 24 25 32 38 45 46 49
        彩虹|05 10 13 16 17 23 25 32 35 43|05 10 13 16 17 23 25 32 35 43
        天机阁|杀03 07 10 11 13 20 27 31 43 48|03 07 10 11 13 20 27 31 43 48
        天机阁五行|必中【金 水 火 土】码|金水火土
        妈祖两尾|精杀【0 9】尾🔪|0尾+9尾
        祖师公肖|①杀:虎 肖 ①杀:0 尾|虎
        祖师公尾|①杀:虎 肖 ①杀:0 尾|0尾
        龙王杀|杀:02 04 06 07 19 22 24 27~31 32|02 04 06 07 19 22 24 27 31 32
        龙王|01 02 03 04 06 07 08 09 10~11 12 13 14 15 19 20 22 23~24 26 27 28 29 30 31 33 36~37 38 39 40 42 44 45 47 49|01 02 03 04 06 07 08 09 10 11 12 13 14 15 19 20 22 23 24 26 27 28 29 30 31 33 36 37 38 39 40 42 44 45 47 49
        红人馆|01 02 03 05 06 07 08 09 10~11 14 15 16 17 18 19 21 22~23 24 26 27 29 30 31 32 34~35 36 37 41 42 44 45 46 47|01 02 03 05 06 07 08 09 10 11 14 15 16 17 18 19 21 22 23 24 26 27 29 30 31 32 34 35 36 37 41 42 44 45 46 47
        老人杀|不开:08 15 22 24 32 41|08 15 22 24 32 41
        老人味|01 02 04 05 07 08 09 10~11 12 13 14 16 17 18 20~21 25 26 27 28 29 30 34~35 36 37 38 39 40 41 44~45 46 47 48|01 02 04 05 07 08 09 10 11 12 13 14 16 17 18 20 21 25 26 27 28 29 30 34 35 36 37 38 39 40 41 44 45 46 47 48
        姨妈|杀08 11 14 15 18 19 23 30 34 40~45 49|08 11 14 15 18 19 23 30 34 40 45 49
        姨妈杀头|今晚你买:0头 必输!!|0头
        聚宝两肖|今期庄吃:【兔 牛】|兔牛
        大赢家|稳杀:02 05 10 13 28 29 43 45 46 49|02 05 10 13 28 29 43 45 46 49
        大赢家杀头|买 三头 会输很惨!|3头
        大赢家五行|大赢家四行:木 水 火 土|木水火土
        大赢家杀尾|狂赢九尾:0 1 3 4 5 6 7 8 9|2尾
        大懒趴|杀:04 05 12 13 19 28 31 34|04 05 12 13 19 28 31 34
        铁甲小宝|【四头出特→→→0 2 3 4】|1头
        伯公绝杀|杀四个特码---09 24 40 42|09 24 40 42
        绿杀|绿杀:羊 猪|羊猪
        摇钱树蓝杀|蓝杀:狗 雞|狗鸡
        通天资料双尾|【5 8】尾|5尾+8尾
        心水杀段|杀:3 段!! 不会开|3段
        广东两肖|广东报👉:【猴龙】✓|猴龙
        福建两肖|福建报👉:【龙马】✓|龙马
        广西两肖|广西报👉:【兔狗】✓|兔狗
        贵州两肖|贵州报👉:【鸡虎】✓|鸡虎
        海南两肖|海南报👉:【虎猴】✓|虎猴
        江西两肖|江西报👉:【猴狗】✓|猴狗
        湖南两肖|湖南报👉:【龙兔】✓|龙兔
        上海两肖|上海报👉:【蛇鸡】✓|蛇鸡
        深圳两肖|深圳报👉:【猪狗】✓|猪狗
        云南两肖|云南报👉:【兔猪】✓|兔猪
        四川两肖|四川报👉:【龙狗】✓|龙狗
        特头杀|特杀---0 头|0头
        特头必中|必中四头((0 1 2 4))头|3头
        """;

    public static IEnumerable<object[]> SplitIssueNumberCards()
    {
        yield return ["宝典", "包围36码01020405080910111213151718192223", "242628293031323334", "3536384041424344474849", "包围36码03040607080910121314151619202224", "01 02 04 05 08 09 10 11 12 13 15 17 18 19 22 23 24 26 28 29 30 31 32 33 34 35 36 38 40 41 42 43 44 47 48 49"];
        yield return ["心水", "36计01030406070809101213151617182225", "262728293031323335", "3738394041424546474849", "36计01020405071011121314151617181920", "01 03 04 06 07 08 09 10 12 13 15 16 17 18 22 25 26 27 28 29 30 31 32 33 35 37 38 39 40 41 42 45 46 47 48 49"];
        yield return ["内幕", "02030405060709101213141617", "18202223242627282933343536", "37383940414246474849", "02030405060709111213141520", "02 03 04 05 06 07 09 10 12 13 14 16 17 18 20 22 23 24 26 27 28 29 33 34 35 36 37 38 39 40 41 42 46 47 48 49"];
        yield return ["强哥", "强哥三十六码0102030405060708101215161718202123242527", "283031323334353638414344", "45474849", "强哥三十六码0203040609101112131516171819202122232526", "01 02 03 04 05 06 07 08 10 12 15 16 17 18 20 21 23 24 25 27 28 30 31 32 33 34 35 36 38 41 43 44 45 47 48 49"];
        yield return ["锁妖", "锁三十六码0203040708091012131415161718192122232426", "272829333435404142434445", "46474849", "锁三十六码0204050607080910111214161719202326282930", "02 03 04 07 08 09 10 12 13 14 15 16 17 18 19 21 22 23 24 26 27 28 29 33 34 35 40 41 42 43 44 45 46 47 48 49"];
        yield return ["赛马会", "35码赛马会010203040607081011141516", "1920212225272829", "303132333437383941434546474849", "35码赛马会010203040506070809111215", "01 02 03 04 06 07 08 10 11 14 15 16 19 20 21 22 25 27 28 29 30 31 32 33 34 37 38 39 41 43 45 46 47 48 49"];
        yield return ["龙王", "010305060708091011", "121315161719222324", "252628293031323435363840414345464749", "010203040607080910", "01 03 05 06 07 08 09 10 11 12 13 15 16 17 19 22 23 24 25 26 28 29 30 31 32 34 35 36 38 40 41 43 45 46 47 49"];
        yield return ["红人馆", "020304050607091011", "121415161721222324252627282931323638", "394142444546474849", "010203050607080910", "02 03 04 05 06 07 09 10 11 12 14 15 16 17 21 22 23 24 25 26 27 28 29 31 32 36 38 39 41 42 44 45 46 47 48 49"];
        yield return ["老人味", "01020508091213141517181921222324", "2527293031323536", "373940414243444546474849", "01020405070809101112131416171820", "01 02 05 08 09 12 13 14 15 17 18 19 21 22 23 24 25 27 29 30 31 32 35 36 37 39 40 41 42 43 44 45 46 47 48 49"];
        yield return ["表弟", "36码0203040506070809111213141519212224252627", "2830323334353637404142", "4345464749", "36码0102040506070809101214151617181920222324", "02 03 04 05 06 07 08 09 11 12 13 14 15 19 21 22 24 25 26 27 28 30 32 33 34 35 36 37 40 41 42 43 45 46 47 49"];
        yield return ["祥瑞阁", "祥瑞阁主大包围36码0102030405060708101112131415161719", "2223252628303133343536373941424344", "4547", "祥瑞阁主大包围36码0102030407080911121314161718192122", "01 02 03 04 05 06 07 08 10 11 12 13 14 15 16 17 19 22 23 25 26 28 30 31 33 34 35 36 37 39 41 42 43 44 45 47"];
    }

    [Theory]
    [MemberData(nameof(SplitIssueNumberCards))]
    public void CurrentNumberTableMayStraddleIssueAndOpeningWithoutBorrowingNextRow(
        string id, string leading, string middle, string trailing, string nextLeading, string expected)
    {
        var rule = id is "表弟" or "祥瑞阁"
            ? RuleCatalog.Load(Path.Combine(ResultFilePaths.ConfigurationDirectory(AppContext.BaseDirectory), "新澳高级会员.json")).Single(item => item.Id == id)
            : Rule(id);
        int count = int.Parse(rule.Type.AsSpan("号码:".Length));
        foreach (int issue in new[] { 7, 246, 1001 })
        {
            string[] card = [rule.Keyword, leading, $"{issue}期", middle, "开？？", trailing,
                nextLeading, $"{issue - 1}期", "010203040506070809101112131415161718192021222324252627282930313233343536", "牛18错"];
            Assert.Equal(expected, RuleEngine.ExtractFinalValue(card, issue, rule));
            string[] olderTarget = [rule.Keyword, leading, $"{issue + 1}期", middle, "开？？", trailing,
                leading, $"{issue}期", middle, "开？？", trailing];
            Assert.Equal(expected, RuleEngine.ExtractFinalValue(olderTarget, issue, rule));

            string brokenTrailing = trailing[..^2];
            string[] broken = [rule.Keyword, leading, $"{issue}期", middle, "开？？", brokenTrailing,
                nextLeading, $"{issue - 1}期", "010203040506070809101112131415161718192021222324252627282930313233343536", "牛18错"];
            Assert.Null(RuleEngine.ExtractFinalValue(broken, issue, rule));
            Assert.Equal(count, expected.Split(' ').Length);
        }
    }

    [Theory]
    [MemberData(nameof(Samples))]
    public void SelectedTableBlockIsCompleteDynamicAndIndependentOfHeading(string id, string body, string expected)
    {
        var rule = Rule(id);
        foreach (int issue in new[] { 7, 318, 1001 })
        {
            string[] lines = [$"{issue}期开", "09 14 03 17 33 40 46", "狗 蛇 龙 虎 鸡",
                rule.Keyword, $"{issue + 1}期 无数据 开??", $"{issue}期",
                ..body.Split('\n'), "雞46中", $"{issue - 1}期 无数据 开??"];
            Assert.Equal(expected, RuleEngine.ExtractValue(lines, issue, rule));
            Assert.Equal(expected, RuleEngine.ExtractFinalValue(lines, issue, rule));
            Assert.Equal(expected, RuleEngine.ExtractFinalValue([$"{issue}期 {body} 开??"], issue, rule));
            Assert.Null(RuleEngine.ExtractFinalValue(lines, issue + 2, rule));
            Assert.Null(RuleEngine.ExtractFinalValue([$"{issue}期 {body}", "X 开??"], issue, rule));
        }
    }

    public static IEnumerable<object[]> Portraits() =>
        new[] { "水哥肖", "包公肖肖", "九宫格肖肖", "佛祖肖肖", "禁止肖肖", "三怪肖肖",
            "王者肖肖", "小精肖肖", "红禁肖", "关公又来了肖" }.Select(id => new object[] { id });

    [Theory]
    [MemberData(nameof(Portraits))]
    public void PortraitRequiresOneIssueAndOneZodiacBeforeTheOldOpening(string id)
    {
        var rule = Rule(id);
        foreach (string zodiac in new[] { "兔", "鸡", "马" })
        {
            string[] lines = [rule.Keyword, zodiac, "第318期", "上期开奖结果:09 14 03 17 33 40 T46"];
            Assert.Equal(zodiac, RuleEngine.ExtractFinalValue(lines, 318, rule));
            Assert.Equal(zodiac, RuleEngine.ExtractValue(lines, 318, rule));
            Assert.Equal(zodiac, RuleEngine.ExtractFinalValue(["第318期", rule.Keyword, zodiac, "上期开奖结果:鸡46中"], 318, rule));
            Assert.Null(RuleEngine.ExtractFinalValue(lines, 319, rule));
            Assert.Null(RuleEngine.ExtractFinalValue([..lines[..3], "羊"], 318, rule));
            Assert.Null(RuleEngine.ExtractFinalValue(["317期", zodiac, "318期"], 318, rule));
        }
    }

    [Fact]
    public void AllConfiguredRulesHaveASampleAndEnableStrictValidation()
    {
        // 特殊版式卡由 NewCardsExtractionTests 用当天真实 OCR 文本单独覆盖：
        // 期号上方取值、十肖反推、一肖一尾同卡等版式不适用通用样本脚手架。
        string[] specialLayouts =
        [
            "官方两肖", "老墨两肖", "图库禁两肖", "帅铁两肖", "心水两两肖", "金钱两肖", "王不王两肖",
            "曾道人小杀肖", "心水杀肖肖肖", "聚彩堂一肖", "聚彩堂一尾", "姨妈肖杀", "姨妈尾杀",
            "彩虹半波", "超级赢家半波波", "王不王一头", "神算子避头", "财神一头",
            "近期开奖员", "毛老二", "通天九九肖", "大赢家九肖"
        ];
        string[] covered = Samples().Concat(Portraits()).Select(row => (string)row[0])
            .Append("时点半").Concat(specialLayouts).Order().ToArray();
        Assert.Equal(Rules().Select(rule => rule.Id).Order(), covered);
        Assert.All(Rules(), rule => Assert.True(rule.StrictIssueBlock));
    }

    [Fact]
    public void PictureIdentityDoesNotDependOnLastWeeksZodiac()
    {
        foreach (string id in new[] { "禁止肖肖", "红禁肖" })
        {
            var rule = Rule(id);
            Assert.Null(rule.RequiredKeyword);
            Assert.Contains(rule, RuleEngine.FindMatches([rule.Keyword, "第318期", "牛"], [rule]));
        }
        Assert.DoesNotContain(Rule("禁止肖肖"),
            RuleEngine.FindMatches(["佛祖禁肖图", "第318期", "龙"], Rules()));
    }

    [Fact]
    public void TimelessCardNeedsOwnTitleAndCompleteNumbersOnly()
    {
        var rule = Rule("时点半");
        string numbers = string.Join(' ', Enumerable.Range(1, 36).Select(n => n.ToString("00")));
        foreach (int issue in new[] { 7, 318, 1001 })
            Assert.Equal(numbers, RuleEngine.ExtractFinalValue(
                [$"{issue}期", rule.Keyword, "36码", numbers, "上期开奖结果:鸡46中"], issue, rule));
        // 期号不再要求，但必须出现本资料自己的标题。
        Assert.Equal(numbers, RuleEngine.ExtractFinalValue([rule.Keyword, "36码", numbers], 318, rule));
        Assert.Null(RuleEngine.ExtractFinalValue(["标题误读", "36码", numbers], 318, rule));
        Assert.Null(RuleEngine.ExtractFinalValue([numbers], 318, rule));
        Assert.Null(RuleEngine.ExtractFinalValue(["318期", rule.Keyword, "36码", numbers + " 36"], 318, rule));
        Assert.Null(RuleEngine.ExtractFinalValue(["318期", rule.Keyword, "36码", numbers.Replace("36", "35")], 318, rule));
    }

    public static IEnumerable<object[]> NumberSamples() => Samples()
        .Where(row => Rule((string)row[0]).Type.StartsWith("号码:"));

    [Theory]
    [MemberData(nameof(NumberSamples))]
    public void NumberCardsRejectExtraDuplicateOutOfRangeAndCrossPeriodRepairs(string id, string body, string expected)
    {
        var rule = Rule(id);
        string last = expected.Split(' ')[^1];
        int pos = body.LastIndexOf(last, StringComparison.Ordinal);
        Assert.True(pos >= 0);
        foreach (string replacement in new[] { "", "00", "50", expected.Split(' ')[0], last + " " + last, last + " 49" })
        {
            string broken = body[..pos] + replacement + body[(pos + last.Length)..];
            Assert.Null(RuleEngine.ExtractFinalValue(["318期", broken, "鸡46中", "317期", body], 318, rule));
        }
    }

    [Theory]
    [InlineData("宝典尾", "一尾绝杀→8 8 7 8 8")]
    [InlineData("帅铁", "帅铁杀一肖【马牛】")]
    [InlineData("绿杀", "绿杀:羊 羊")]
    [InlineData("绿杀", "绿杀:羊 猪 牛")]
    [InlineData("刘伯温二尾", "特码封杀→[6 6 尾]")]
    [InlineData("妈祖两尾", "精杀【0 9 9】尾")]
    [InlineData("特头必中", "必中四头((0 1 2 4 4))头")]
    [InlineData("铁甲小宝", "四头出特→0 1 2 5")]
    [InlineData("特头杀", "特杀---5头")]
    [InlineData("六叔公头", "禁【1 2】头")]
    [InlineData("帅铁尾", "帅铁杀一尾【18】")]
    [InlineData("天机阁五行", "必中【金 木 水 土 土】码")]
    [InlineData("大赢家五行", "大赢家四行:金 木 水")]
    [InlineData("大赢家杀尾", "狂赢九尾:0 1 3 4 5 6 7 8 9 9")]
    [InlineData("心水杀段", "杀:3 4段 不会开")]
    public void MalformedValuesAreMissingNotPartialSuccesses(string id, string body) =>
        Assert.Null(RuleEngine.ExtractFinalValue(["318期", body, "鸡46中"], 318, Rule(id)));

    [Theory]
    [InlineData("特头必中", "必中四头((0 1 2 4))头", "3头")]
    [InlineData("大赢家五行", "大赢家四行:木 水 火 土", "金")]
    [InlineData("天机阁五行", "必中【金 水 火 土】码", "木")]
    [InlineData("大赢家杀尾", "狂赢九尾:0 1 3 4 5 6 7 8 9", "2尾")]
    [InlineData("心水杀段", "杀:0 段!! 不会开", "0段")]
    [InlineData("大赢家杀头", "买 零头 会输很惨!", "0头")]
    public void FinalOutputUsesTheMissingItemOrExplicitSingleValue(string id, string body, string expected)
    {
        var rule = Rule(id);
        var value = RuleEngine.ExtractFinalValue(["318期", body], 318, rule);
        Assert.NotNull(value);
        string formatted = rule.Type is "五行" or "单五行" ? expected + "行" : expected;
        Assert.Equal(formatted + " " + id, Assert.Single(RuleEngine.FormatOutput([rule], new Dictionary<string, string> { [id] = value })));
    }

    [Fact]
    public void SharedAncestorCardDoesNotMixItsTwoValues()
    {
        var rules = new[] { Rule("祖师公肖"), Rule("祖师公尾") };
        var values = new Dictionary<string, string>();
        typeof(MainForm).GetMethod("AddExtractedValues", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .Invoke(null, [new[] { "318期 ①杀:虎肖 ①杀:0尾 鸡46中", "317期 ①杀:兔肖 ①杀:7尾 鸡46中" }, rules, 318, values]);
        Assert.Equal(["虎 祖师公肖", "0尾 祖师公尾"], RuleEngine.FormatOutput(rules, values));
    }

    [Fact]
    public void ConflictingCopiesOfSelectedIssueFailClosed()
    {
        var rule = Rule("帅铁头");
        Assert.Null(RuleEngine.ExtractFinalValue(["318期 帅铁杀一头【3】 开??", "318期 帅铁杀一头【1】 开??"], 318, rule));
        Assert.Null(RuleEngine.ExtractFinalValue(["318期 帅铁杀一头【3 4】 开??", "318期 帅铁杀一头【1】 开??"], 318, rule));
    }

    [Theory]
    [InlineData("帅铁头", "帥鐵殺一頭【三】", "3头")]
    [InlineData("祖师公尾", "①殺:龍 肖 ①殺:0 尾", "0尾")]
    [InlineData("宝典杀", "精殺12碼:02 06 12 17 20 23 33 35 37 44 45 48", "02 06 12 17 20 23 33 35 37 44 45 48")]
    public void TraditionalPayloadTextUsesTheSameStrictRules(string id, string body, string expected) =>
        Assert.Equal(expected, RuleEngine.ExtractFinalValue(["318期", body, "雞46中"], 318, Rule(id)));

    [Fact]
    public void TraditionalPortraitAndFooterCannotSupplyTheZodiac()
    {
        var rule = Rule("禁止肖肖");
        Assert.Contains(rule, RuleEngine.FindMatches(["第318期", "禁肖圖", "牛"], [rule]));
        Assert.Null(RuleEngine.ExtractFinalValue(["禁肖圖", "318期", "上期開獎結果:雞46中"], 318, rule));
    }

    [Fact]
    public void FollowingPeriodPrefixIsNotPartOfTheSelectedValue()
    {
        var rule = Rule("帅铁头");
        string[] lines = ["第319期 帅铁杀一头【1】", "第318期 帅铁杀一头【3】", "第317期 帅铁杀一头【4】"];
        Assert.Equal("3头", RuleEngine.ExtractFinalValue(lines, 318, rule));
        Assert.Equal("1头", RuleEngine.ExtractFinalValue(lines, 319, rule));
        Assert.Equal("4头", RuleEngine.ExtractFinalValue(lines, 317, rule));
    }

    [Fact]
    public void MixedZodiacNumberPairsDoNotRequireVisibleParentheses()
    {
        Assert.Equal("21 23 31 20", RuleEngine.ExtractFinalValue(
            ["318期 狗21 猴23 鼠31 豬20 開??"], 318, Rule("雷锋")));
    }

    [Fact]
    public async Task ValidatedSamplesFlowThroughRealDistributionConfigsOnlyToTheSelectedIssue()
    {
        const int issue = 318;
        var rules = Rules();
        var values = new Dictionary<string, string>();
        foreach (object[] sample in Samples())
        {
            var rule = Rule((string)sample[0]);
            string? value = RuleEngine.ExtractFinalValue(["318期", (string)sample[1], "鸡46中"], issue, rule);
            Assert.NotNull(value);
            values.Add(rule.Id, value);
        }
        foreach (object[] portrait in Portraits())
        {
            var rule = Rule((string)portrait[0]);
            values.Add(rule.Id, RuleEngine.ExtractFinalValue([rule.Keyword, "318期", "兔"], issue, rule)!);
        }
        var specialValues = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["官方两肖"] = "牛蛇",
            ["老墨两肖"] = "兔猪",
            ["图库禁两肖"] = "鼠猪",
            ["帅铁两肖"] = "狗猴",
            ["心水两两肖"] = "兔羊",
            ["金钱两肖"] = "猪羊",
            ["王不王两肖"] = "马鼠",
            ["曾道人小杀肖"] = "蛇",
            ["心水杀肖肖肖"] = "鼠",
            ["聚彩堂一肖"] = "马",
            ["聚彩堂一尾"] = "8尾",
            ["姨妈肖杀"] = "牛",
            ["姨妈尾杀"] = "7尾",
            ["彩虹半波"] = "蓝单",
            ["超级赢家半波波"] = "红单",
            ["王不王一头"] = "3头",
            ["神算子避头"] = "3头",
            ["财神一头"] = "3头",
            ["近期开奖员"] = "羊兔虎鸡蛇马龙牛鼠",
            ["毛老二"] = "龙鼠羊虎狗兔猪猴牛",
            ["通天九九肖"] = "虎兔鸡蛇猴牛狗猪鼠",
            ["大赢家九肖"] = "狗羊虎猴蛇龙鼠牛兔"
        };
        foreach ((string id, string value) in specialValues)
            values.Add(id, value);
        string numbers = string.Join(' ', Enumerable.Range(1, 36).Select(n => n.ToString("00")));
        values.Add("时点半", RuleEngine.ExtractFinalValue([$"{issue}期", "十点半集团大围", "36码", numbers], issue, Rule("时点半"))!);
        string[] output = RuleEngine.FormatOutput(rules, values);
        Assert.DoesNotContain(output, line => line.StartsWith("缺失"));
        string folder = Path.Combine(Path.GetTempPath(), "ocr-macau-distribution-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            string configuration = ResultFilePaths.ConfigurationDirectory(AppContext.BaseDirectory);
            foreach (string config in Directory.EnumerateFiles(configuration, "*分发规则.json"))
            {
                using var json = System.Text.Json.JsonDocument.Parse(File.ReadAllText(config));
                string file = json.RootElement.GetProperty("targetFile").GetString()!;
                Assert.Equal(file, Path.GetFileName(file));
                await File.WriteAllTextAsync(Path.Combine(folder, file.Replace("{issue}", issue.ToString())), "原有内容\n");
            }
            var result = await ResultDistributor.DistributeAllAsync(@"C:\图片\9.2-新澳六合彩资料", issue, output, folder, configuration);
            Assert.Empty(result.Errors);
            Assert.Equal(output.Order(), result.DistributedLines.Order());
            Assert.Contains("3头 特头必中", await File.ReadAllLinesAsync(Path.Combine(folder, "318期-头.txt")));
            Assert.Contains("金行 大赢家五行", await File.ReadAllLinesAsync(Path.Combine(folder, "318期-五行.txt")));
            Assert.Contains("2尾 大赢家杀尾", await File.ReadAllLinesAsync(Path.Combine(folder, "318期-尾.txt")));
            Assert.All(Directory.EnumerateFiles(folder), file => Assert.StartsWith("318期", Path.GetFileName(file)));
            Assert.All(Directory.EnumerateFiles(folder), file => Assert.StartsWith("原有内容", File.ReadAllText(file)));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
