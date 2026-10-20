using OcrLineTool;

namespace OcrLineTool.Tests;

public sealed class RuleCatalogTests
{
    [Fact]
    public void GroupNameBatchUsesOneSnapshotAndReloadsForTheNextRefresh()
    {
        string root = Path.Combine(Path.GetTempPath(), "group-refresh-" + Guid.NewGuid().ToString("N"));
        string config = ResultFilePaths.ConfigurationDirectory(root);
        Directory.CreateDirectory(config);
        string rules = Path.Combine(config, "sample.json");
        File.WriteAllText(rules, "{\"group\":\"目标 群\",\"rules\":[]}");
        string[] folders = [@"C:\images\267期-目标群", @"C:\images\9.24-目标群", @"C:\images\目标群-备份"];
        try
        {
            var snapshot = RuleCatalog.GroupNamesForFolders(root, folders);
            Assert.Equal("目标 群", snapshot[folders[0]]);
            Assert.Equal("目标 群", snapshot[folders[1]]);
            Assert.Equal("目标群-备份", snapshot[folders[2]]);
            File.WriteAllText(rules, "{\"group\":\"目标群\",\"rules\":[]}");
            Assert.Equal("目标 群", snapshot[folders[0]]);
            Assert.Equal("目标群", RuleCatalog.GroupNamesForFolders(root, folders)[folders[0]]);
            Assert.Equal(ResultFilePaths.ForGroup(root, folders[0], 267),
                ResultFilePaths.ForGroupName(root, "目标群", 267));
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData("云OCR缓存_目标群.json")]
    [InlineData("界面设置.json")]
    public void NonRuleDocumentsCannotSupplyAGroupName(string fileName)
    {
        string root = Path.Combine(Path.GetTempPath(), "rule-poll-" + Guid.NewGuid().ToString("N"));
        string config = ResultFilePaths.ConfigurationDirectory(root);
        Directory.CreateDirectory(config);
        try
        {
            File.WriteAllText(Path.Combine(config, fileName), "{\"group\":\"目标群\",\"entries\":[]}");
            Assert.Equal(Path.Combine(config, "267期-目标群.json"),
                RuleCatalog.PathForFolder(root, @"C:\images\267期-目标群"));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void LoadsAllConfiguredItemsWithUniqueOutputIds()
    {
        IReadOnlyList<OcrRule> rules = RuleCatalog.Load(
            Path.Combine(ResultFilePaths.ConfigurationDirectory(AppContext.BaseDirectory), "嫣然心水.json"));

        Assert.Equal(103, rules.Count);
        Assert.Equal(rules.Count, rules.Select(rule => rule.Id).Distinct(StringComparer.Ordinal).Count());
        Assert.Contains(rules, rule => rule.Id == "钦差大臣公式一" && rule.Section == "公式一");
        Assert.Contains(rules, rule => rule.Id == "君军两尾" && rule.Type == "尾数组合");
        Assert.Contains(rules, rule => rule.Id == "恩平公式" && rule.RequiredKeyword == "杀二肖" && rule.Section == "杀二肖");
        Assert.Contains(rules, rule => rule.Id == "恩平九肖" && rule.Type == "九肖"
            && rule.Keyword == "恩平公式九肖" && rule.Folder == "恩平");
        Assert.Contains(rules, rule => rule.Id == "小灰灰一肖" && rule.Type == "单生肖");
        Assert.DoesNotContain(rules, rule => rule.Id == "小灰灰两肖");
        Assert.DoesNotContain(rules, rule => rule.Id == "小灰灰两尾");
        Assert.Contains(rules, rule => rule.Id == "小骚货" && rule.Type == "九肖" && rule.RequiredKeyword == "快乐的骚货");
        Assert.Contains(rules, rule => rule.Id == "小黄人五行" && rule.Type == "五行");
        Assert.Contains(rules, rule => rule.Id == "紫燕儿杀一肖" && rule.Type == "生肖");
        Assert.DoesNotContain(rules, rule => rule.Id == "紫燕儿（生肖）");
        Assert.DoesNotContain(rules, rule => rule.Id == "缘来如此（平特一肖）");
        Assert.Contains(rules, rule => rule.Id == "傻丫头" && rule.Type == "生肖");
        Assert.Contains(rules, rule => rule.Id == "青苹果" && rule.Type == "号码:6");
        Assert.Contains(rules, rule => rule.Id == "青苹果杀一肖" && rule.Type == "生肖"
            && rule.Keyword == "青苹果" && rule.Folder == "青苹果" && rule.RequiredKeyword == "青苹果");
        Assert.Contains(rules, rule => rule.Id == "蓝色" && rule.Type == "号码:36" && rule.Folder == "36码"
            && rule.RequiredKeyword == "特码开在");
        Assert.Contains(rules, rule => rule.Id == "彩图" && rule.Type == "号码:36" && rule.Folder == "36码"
            && rule.RequiredKeyword == "开奖结果");
        Assert.Contains(rules, rule => rule.Id == "傻丫头二肖" && rule.Type == "生肖组合");
        Assert.Contains(rules, rule => rule.Id == "白少华半头" && rule.Type == "半头" && rule.Folder == "白少华");
        Assert.Contains(rules, rule => rule.Id == "白少华五码" && rule.Type == "号码:5" && rule.Folder == "白少华");
        Assert.Contains(rules, rule => rule.Id == "永卟弃杀头" && rule.Type == "缺头");
        Assert.Contains(rules, rule => rule.Id == "恩平杀头" && rule.Type == "缺头"
            && rule.RequiredKeyword == "恩平澳彩四头" && rule.Folder == "恩平");
        Assert.Contains(rules, rule => rule.Id == "独傲洒脱杀肖肖" && rule.Type == "生肖"
            && rule.Folder == "独傲洒脱" && rule.RequiredKeyword == "一特肖");
        Assert.Contains(rules, rule => rule.Id == "独傲洒脱杀二肖" && rule.Type == "生肖组合"
            && rule.Folder == "独傲洒脱" && rule.RequiredKeyword == "双规");
        Assert.Contains(rules, rule => rule.Id == "阿莲杀肖肖" && rule.Type == "生肖"
            && rule.Folder == "阿莲" && rule.Section == "9月份"
            && rule.RequiredKeyword == "9月份");
        Assert.Contains(rules, rule => rule.Id == "阿莲杀尾尾" && rule.Type == "尾"
            && rule.Folder == "阿莲" && rule.Section == "9月份"
            && rule.RequiredKeyword == "9月份");
        Assert.DoesNotContain(rules, rule => rule.Id == "大中华杀肖肖");
        Assert.Contains(rules, rule => rule.Id == "借花献佛" && rule.Type == "统计生肖"
            && rule.Folder == "六扇门" && rule.RequiredKeyword == "借花献佛");
        Assert.Contains(rules, rule => rule.Id == "品鉴" && rule.Type == "统计生肖"
            && rule.Folder == "品鉴" && string.IsNullOrWhiteSpace(rule.RequiredKeyword));
        Assert.Contains(rules, rule => rule.Id == "小慧慧" && rule.Type == "统计生肖"
            && rule.Folder == "小慧慧" && rule.RequiredKeyword == "小慧慧新澳杀肖团队");
        Assert.Contains(rules, rule => rule.Id == "灰灰团" && rule.Type == "统计生肖"
            && rule.Folder == "灰灰团" && string.IsNullOrWhiteSpace(rule.RequiredKeyword));
        Assert.Contains(rules, rule => rule.Id == "火狼女两肖" && rule.Type == "生肖组合"
            && rule.Folder == "火狼女" && rule.Section == "禁" && rule.RequiredKeyword == "禁");
        Assert.Contains(rules, rule => rule.Id == "火狼女两尾" && rule.Type == "尾数组合"
            && rule.Folder == "火狼女" && rule.Section == "二尾" && rule.RequiredKeyword == "二尾");
        Assert.DoesNotContain(rules, rule => rule.Id == "冷酷女王");
        string[] newNames =
        [
            "缘来如此杀一肖", "钦差大臣公式一", "钦差大臣公式二", "君军两肖",
            "小黄人两肖", "辣椒炒肉头", "小黄人头", "紫燕儿尾", "缘来如此尾",
            "君军两尾", "辣椒炒肉尾", "小黄人两尾", "君军合", "小黄人五行",
            "柳叶刀", "长安之星", "雁塔题名半波", "雁塔题名杀头", "雁塔题名杀合",
            "雁塔题名杀肖肖",
            "华林肖", "华林尾", "华林半波", "傻丫头二肖", "小雨婷",
            "简单爱", "月来月好", "欧阳肖", "欧阳半波", "永卟弃杀头", "陈思思",
            "潮汕陈龙杀三码", "玉亚半波", "Alice两码", "恩平杀头", "恩平杀一尾",
            "白少华半头", "白少华五码"
        ];
        Assert.All(newNames, name => Assert.Contains(rules, rule => rule.Id == name));
        string[] oldNames =
        [
            "缘来如此（杀一肖）", "钦差大臣（公式一）", "钦差大臣（公式二）", "君军（生肖）",
            "小黄人（两肖）", "辣椒炒肉（头）", "小黄人（一头）", "紫燕儿（尾）",
            "缘来如此（尾）", "君军（尾）", "辣椒炒肉（尾）", "小黄人（两尾）",
            "君军（合）", "小黄人（五行）"
        ];
        Assert.All(oldNames, name => Assert.DoesNotContain(rules, rule => rule.Id == name));
    }

    [Fact]
    public void UsesSelectedFolderNameForRuleFile()
    {
        string path = RuleCatalog.PathForFolder(@"C:\工具", @"C:\图片\嫣然心水");

        Assert.Equal(@"C:\工具\配置文件\嫣然心水.json", path);
    }

    [Fact]
    public void LoadsTheDragonflyGroupWithStrictSubfolderRules()
    {
        IReadOnlyList<OcrRule> rules = RuleCatalog.Load(
            Path.Combine(ResultFilePaths.ConfigurationDirectory(AppContext.BaseDirectory), "蜻蜓一套.json"));

        Assert.Equal(9, rules.Count);
        Assert.Equal(
            new[] { "各种杀", "各种杀", "各种杀", "公式杀料", "公式杀料", "一套组合拳", "一套组合拳", "一套组合拳", "一套组合拳" },
            rules.Select(rule => rule.Folder));
        Assert.DoesNotContain(rules, rule => rule.Id == "绿格子双杀");
        Assert.Contains(rules, rule => rule.Id == "红蜻蜓" && rule.Type == "九肖");
        Assert.Contains(rules, rule => rule.Id == "红蜻蜓杀一肖" && rule.Type == "生肖"
            && rule.Section == "绝杀一肖" && rule.RequiredKeyword == "红蜻蜓绝杀一肖");
        Assert.Contains(rules, rule => rule.Id == "神奇宇宙" && rule.Type == "色单双");
        Assert.Contains(rules, rule => rule.Id == "墨羽" && rule.Type == "生肖");
    }

    [Fact]
    public void FormulaRulesUseTheirLockedFolderWhenTheImageHasNoTitle()
    {
        IReadOnlyList<OcrRule> rules = RuleCatalog.Load(
            Path.Combine(ResultFilePaths.ConfigurationDirectory(AppContext.BaseDirectory), "蜻蜓一套.json"));
        string image = Path.Combine("C:\\结果", "蜻蜓一套", "公式杀料", "sample.jpg");
        IReadOnlyList<OcrRule> actual = RuleEngine.FindMatches(
            image,
            ["244期（平2-2-D2+正3）=杀15尾", "244期（平2*4+特-D4+平3+正E1-2）=杀狗猴√"],
            rules);

        Assert.Equal("狗猴", RuleEngine.ExtractFinalValue(
            ["244期（平2*4+特-D4+平3+正E1-2）=杀狗猴√"], 244,
            rules.Single(rule => rule.Id == "公式杀两肖肖")));
        Assert.Equal("狗猴", RuleEngine.ExtractFinalValue(
            ["244期（平2-2-D2+正3）=杀15尾", "244期（平2*4+特-D4+平3+正E1-2）=杀狗猴√"], 244,
            rules.Single(rule => rule.Id == "公式杀两肖肖")));
        Assert.Equal("1尾+5尾", RuleEngine.ExtractFinalValue(
            ["244期（平2-2-D2+正3）=杀15尾"], 244,
            rules.Single(rule => rule.Id == "公式杀两尾尾")));
        Assert.Contains(actual, rule => rule.Id == "公式杀两尾尾");
        Assert.Contains(actual, rule => rule.Id == "公式杀两肖肖");
    }

    [Fact]
    public void BlackKillRowUsesItsLockedFolderWhenTheHeaderIsMissed()
    {
        IReadOnlyList<OcrRule> rules = RuleCatalog.Load(
            Path.Combine(ResultFilePaths.ConfigurationDirectory(AppContext.BaseDirectory), "蜻蜓一套.json"));
        string image = Path.Combine("C:\\结果", "蜻蜓一套", "各种杀", "sample.jpg");
        IReadOnlyList<OcrRule> actual = RuleEngine.FindMatches(
            image,
            ["244期土03合开：？00准"],
            rules);

        Assert.Contains(actual, rule => rule.Id == "黑字杀行");
        Assert.Contains(actual, rule => rule.Id == "黑字杀合");
    }

    [Fact]
    public void ResolvesDatedFoldersToTheMatchingGroupRuleFile()
    {
        string appDirectory = AppContext.BaseDirectory;

        Assert.Equal(
            Path.Combine(ResultFilePaths.ConfigurationDirectory(appDirectory), "新澳高级会员.json"),
            RuleCatalog.PathForFolder(appDirectory, @"C:\图片\8.31-新澳高级会员"));
        Assert.Equal(
            Path.Combine(ResultFilePaths.ConfigurationDirectory(appDirectory), "新澳六合彩资料.json"),
            RuleCatalog.PathForFolder(appDirectory, @"C:\图片\9.1-新澳六合彩资料"));
        Assert.Equal(
            Path.Combine(ResultFilePaths.ConfigurationDirectory(appDirectory), "嫣然心水.json"),
            RuleCatalog.PathForFolder(appDirectory, @"C:\图片\242期-嫣然心水"));
    }

    [Theory]
    [InlineData(@"C:\图片\新澳六合彩资料", "新澳六合彩资料")]
    [InlineData(@"C:\图片\8.31-新澳六合彩资料", "新澳六合彩资料")]
    [InlineData(@"C:\图片\第242期-嫣然心水", "嫣然心水")]
    [InlineData(@"C:\图片\新澳高级会员_243期", "新澳高级会员")]
    [InlineData(@"C:\图片\第12345期-新澳高级会员", "新澳高级会员")]
    [InlineData(@"C:\图片\2026年8月31日-新澳六合彩资料", "新澳六合彩资料")]
    [InlineData(@"C:\图片\8_31-新澳高级会员", "新澳高级会员")]
    [InlineData(@"C:\图片\242期-肖-新增", "肖新增")]
    public void MatchesAGroupOnlyAfterRemovingAnIssueOrDateWrapper(string selectedFolder, string expectedGroup)
    {
        Assert.True(RuleCatalog.IsGroupFolder(selectedFolder, expectedGroup));
    }

    [Theory]
    [InlineData(@"C:\图片\8.31-新澳六合彩资料", "新澳六合彩资料")]
    [InlineData(@"C:\图片\9.1-新澳高级会员", "新澳高级会员")]
    [InlineData(@"C:\图片\第12345期-嫣然心水", "嫣然心水")]
    public void ResolvesEveryConfiguredGroupThroughTheSharedDynamicResolver(
        string selectedFolder, string expectedGroup)
    {
        string[] configuredGroups = ["新澳六合彩资料", "新澳高级会员", "嫣然心水"];

        Assert.Equal(expectedGroup, RuleCatalog.MatchGroupFolder(selectedFolder, configuredGroups));
        Assert.Equal(expectedGroup, RuleCatalog.GroupNameForFolder(AppContext.BaseDirectory, selectedFolder));
    }

    [Theory]
    [InlineData(@"C:\图片\新澳六合彩资料备份", "新澳六合彩资料")]
    [InlineData(@"C:\图片\新澳六合彩资料-临时", "新澳六合彩资料")]
    [InlineData(@"C:\图片\8.31-新澳六合彩资料-备份", "新澳六合彩资料")]
    [InlineData(@"C:\图片\242期-嫣然心水-临时", "嫣然心水")]
    [InlineData(@"C:\图片\新澳六合彩资料-", "新澳六合彩资料")]
    [InlineData(@"C:\图片\新澳六合彩资料!", "新澳六合彩资料")]
    [InlineData(@"C:\图片\新澳六合彩资!料", "新澳六合彩资料")]
    public void DoesNotTreatAGroupNameWithAnArbitrarySuffixAsTheGroup(string selectedFolder, string expectedGroup)
    {
        Assert.False(RuleCatalog.IsGroupFolder(selectedFolder, expectedGroup));
    }

    [Fact]
    public void DoesNotSelectDistributionJsonAsAnOcrRuleCatalog()
    {
        string appDirectory = AppContext.BaseDirectory;

        string path = RuleCatalog.PathForFolder(appDirectory, @"C:\图片\242期-头");

        Assert.Equal(
            Path.Combine(ResultFilePaths.ConfigurationDirectory(appDirectory), "242期-头.json"),
            path);
        Assert.DoesNotContain("分发规则", path, StringComparison.Ordinal);
    }

    [Fact]
    public void DoesNotResolveAnArbitrarySuffixToAnOcrRuleFile()
    {
        string appDirectory = AppContext.BaseDirectory;

        string path = RuleCatalog.PathForFolder(
            appDirectory,
            @"C:\图片\8.31-新澳六合彩资料-备份");

        Assert.Equal(
            Path.Combine(
                ResultFilePaths.ConfigurationDirectory(appDirectory),
                "8.31-新澳六合彩资料-备份.json"),
            path);
    }

    [Fact]
    public void LoadsNewMacauRulesWithUniqueOutputNames()
    {
        IReadOnlyList<OcrRule> rules = RuleCatalog.Load(
            Path.Combine(ResultFilePaths.ConfigurationDirectory(AppContext.BaseDirectory), "新澳六合彩资料.json"));

        Assert.Equal(108, rules.Count);
        Assert.Equal(rules.Count, rules.Select(rule => rule.Id).Distinct(StringComparer.Ordinal).Count());
        // v1.5.189 新增、现已撤回的 15 条资料：Id 一旦回流就报错。
        string[] removed =
        [
            "买什么杀一肖", "买什么杀十码", "买什么杀半波", "买什么杀头", "买什么公式杀行", "买什么公式杀尾", "买什么公式杀肖",
            "唐朝杀行", "唐朝杀头", "唐朝杀尾", "唐朝杀肖", "黑客杀两肖", "老码头杀七码", "锦囊杀尾", "天宫杀头"
        ];
        Assert.DoesNotContain(rules, rule => removed.Contains(rule.Id));
        Assert.Contains(rules, rule => rule.Id == "白小姐四季" && rule.Type == "四季");
        Assert.Contains(rules, rule => rule.Id == "慈善四季"
            && rule.Keyword == "慈善网『四季生肖』付费版"
            && rule.Type == "四季");
        Assert.Contains(rules, rule => rule.Id == "白小姐来钱" && rule.Type == "九肖" && rule.AllowOpeningRow);
        Assert.Contains(rules, rule => rule.Id == "官方两肖" && rule.Type == "生肖组合" && rule.AllowNearbyValue);
        Assert.Contains(rules, rule => rule.Id == "老墨两肖" && rule.Type == "生肖组合" && rule.AllowNearbyValue);
        Assert.Contains(rules, rule => rule.Id == "帅铁两肖" && rule.Type == "缺两肖");
        Assert.Contains(rules, rule => rule.Id == "心水两两肖" && rule.Type == "缺两肖");
        Assert.Contains(rules, rule => rule.Id == "聚彩堂一肖" && rule.Type == "生肖"
            && rule.Keyword == "禁一肖一尾聚彩堂");
        Assert.Contains(rules, rule => rule.Id == "聚彩堂一尾" && rule.Type == "尾"
            && rule.Keyword == "禁一肖一尾聚彩堂");
        Assert.Contains(rules, rule => rule.Id == "姨妈肖杀" && rule.Type == "生肖"
            && rule.Keyword == "姨妈封杀一肖一尾");
        Assert.Contains(rules, rule => rule.Id == "姨妈尾杀" && rule.Type == "尾"
            && rule.Keyword == "姨妈封杀一肖一尾");
        Assert.Contains(rules, rule => rule.Id == "大赢家九肖" && rule.Type == "九肖"
            && rule.Keyword == "狂赢九肖");
        Assert.Contains(rules, rule => rule.Id == "亮剑九肖" && rule.Type == "九肖"
            && rule.Keyword == "亮剑团队" && rule.IgnoreIssue && rule.StrictIssueBlock);
        Assert.Contains(rules, rule => rule.Id == "雷锋" && rule.Type == "号码:4");
        Assert.Contains(rules, rule => rule.Id == "祖师公肖" && rule.Type == "生肖");
        Assert.Contains(rules, rule => rule.Id == "祖师公尾" && rule.Type == "尾");
        Assert.Contains(rules, rule => rule.Id == "大赢家杀尾" && rule.Type == "缺尾");
        Assert.Contains(rules, rule => rule.Id == "天机阁五行" && rule.Type == "五行");
        Assert.Contains(rules, rule => rule.Id == "大赢家五行" && rule.Type == "五行");
        Assert.Contains(rules, rule =>
            rule.Id == "龙王" &&
            rule.Keyword == "龙王庙36个中特码付费版" &&
            rule.Type == "号码:36");
        Assert.Contains(rules, rule =>
            rule.Id == "龙王杀两肖" &&
            rule.Keyword == "龙王庙绝杀两肖付费版" &&
            rule.Type == "生肖组合");
        Assert.Contains(rules, rule => rule.Id == "藏宝杀一肖" && rule.Type == "生肖" && rule.Section == "特肖杀");
        Assert.DoesNotContain(rules, rule => rule.Id == "龙王36码");
        Assert.Contains(rules, rule => rule.Id == "小马哥" && rule.Keyword == "小马哥庄家杀12码");
        Assert.Contains(rules, rule => rule.Id == "金钱网" && rule.Keyword == "金钱网必杀12个特码");
        Assert.Contains(rules, rule => rule.Id == "大赢家" && rule.Keyword == "大赢家稳杀10码付费版");
        Assert.Contains(rules, rule =>
            rule.Id == "大懒趴" &&
            rule.Keyword == "大懒趴要杀的八码" &&
            rule.Type == "号码:8");
        Assert.Contains(rules, rule =>
            rule.Id == "铁甲小宝" &&
            rule.Keyword == "铁甲小宝四头出特" &&
            rule.Type == "缺头");
        Assert.Contains(rules, rule =>
            rule.Id == "伯公绝杀" &&
            rule.Keyword == "伯公绝杀特码" &&
            rule.Type == "号码:4");
        Assert.Contains(rules, rule =>
            rule.Id == "绿杀" &&
            rule.Keyword == "绿杀两肖" &&
            rule.Type == "生肖组合");
        Assert.Contains(rules, rule =>
            rule.Id == "摇钱树蓝杀" &&
            rule.Keyword == "摇钱树蓝杀双肖" &&
            rule.Type == "生肖组合");
        Assert.Contains(rules, rule =>
            rule.Id == "通天资料双尾" &&
            rule.Keyword == "通天资料必杀双尾特" &&
            rule.Type == "尾数组合");
        Assert.Contains(rules, rule =>
            rule.Id == "心水杀段" &&
            rule.Keyword == "心水站期期绝杀一段" &&
            rule.Type == "段");
        foreach (string province in new[] { "广东", "福建", "广西", "贵州", "海南", "江西" })
            Assert.Contains(rules, rule =>
                rule.Id == province + "两肖" &&
                rule.Keyword == province + "两肖必杀" &&
                rule.Type == "生肖组合");
        Assert.Contains(rules, rule =>
            rule.Id == "湖南两肖" &&
            rule.Keyword == "湖南报杀两肖" &&
            rule.Type == "生肖组合");
        foreach (string city in new[] { "上海", "深圳", "云南", "四川" })
            Assert.Contains(rules, rule =>
                rule.Id == city + "两肖" &&
                rule.Keyword == city + "必杀两肖" &&
                rule.Type == "生肖组合");
        Assert.Contains(rules, rule =>
            rule.Id == "特头杀" &&
            rule.Keyword == "特头杀一头期期中" &&
            rule.Type == "头");
        Assert.Contains(rules, rule =>
            rule.Id == "特头必中" &&
            rule.Keyword == "特头必中四头准全年" &&
            rule.Type == "缺头");
        Assert.Contains(rules, rule => rule.Id == "水哥肖" && rule.Keyword == "水哥杀一肖" && rule.Type == "生肖");
        Assert.Contains(rules, rule => rule.Id == "包公肖肖" && rule.Keyword == "包公图" && rule.Type == "生肖");
        Assert.Contains(rules, rule => rule.Id == "九宫格肖肖" && rule.Keyword == "九宫寻肖" && rule.Type == "生肖");
        Assert.Contains(rules, rule => rule.Id == "佛祖肖肖" && rule.Keyword == "佛祖禁肖图" && rule.Type == "生肖");
        Assert.Contains(rules, rule => rule.Id == "禁止肖肖" && rule.Keyword == "禁肖图" && rule.RequiredKeyword is null && rule.Type == "生肖");
        Assert.Contains(rules, rule => rule.Id == "三怪肖肖" && rule.Keyword == "三怪禁肖图" && rule.Type == "生肖");
        Assert.Contains(rules, rule => rule.Id == "王者肖肖" && rule.Keyword == "王者九点禁一肖" && rule.Type == "生肖");
        Assert.Contains(rules, rule => rule.Id == "小精肖肖" && rule.Keyword == "小精一一禁肖" && rule.Type == "生肖");
        Assert.Contains(rules, rule => rule.Id == "时点半" && rule.Keyword == "十点半集团大围" && rule.Type == "号码:36");
        Assert.Contains(rules, rule => rule.Id == "红禁肖" && rule.Keyword == "澳门禁肖图" && rule.RequiredKeyword is null && rule.Type == "生肖");
        Assert.Contains(rules, rule => rule.Id == "关公又来了肖" && rule.Keyword == "关公杀一肖又来了" && rule.Type == "生肖");
        Assert.Equal(
        [
            "小马哥", "张小艺", "雷锋", "狗庄", "藏宝十二码", "藏宝头", "藏宝杀一肖", "黄大仙", "宝典杀", "宝典尾", "宝典", "刘伯温二尾", "刘伯温",
            "庄家", "天线宝杀", "六叔公头", "通天", "帅铁", "帅铁尾", "帅铁头", "杀料", "杀料五码", "心水",
            "心水两肖", "白小姐杀两肖", "内幕", "强哥", "锁妖", "赛马会", "聚彩", "慈善", "金钱网", "彩虹",
            "天机阁", "天机阁五行", "妈祖两尾", "祖师公肖", "祖师公尾", "龙王杀", "龙王杀两肖", "龙王", "红人馆", "老人杀", "老人味",
            "姨妈", "姨妈杀头", "聚宝两肖", "大赢家", "大赢家杀头", "大赢家五行", "大赢家杀尾", "大懒趴", "铁甲小宝", "伯公绝杀", "绿杀", "摇钱树蓝杀", "通天资料双尾", "心水杀段",
            "广东两肖", "福建两肖", "广西两肖", "贵州两肖", "海南两肖", "江西两肖", "湖南两肖",
            "上海两肖", "深圳两肖", "云南两肖", "四川两肖", "特头杀", "特头必中"
            , "水哥肖", "包公肖肖", "九宫格肖肖", "佛祖肖肖", "禁止肖肖", "三怪肖肖", "王者肖肖", "小精肖肖", "时点半", "红禁肖", "关公又来了肖",
            "官方两肖", "老墨两肖", "图库禁两肖", "帅铁两肖", "心水两两肖", "金钱两肖", "王不王两肖",
            "曾道人小杀肖", "心水杀肖肖肖", "聚彩堂一肖", "聚彩堂一尾", "姨妈肖杀", "姨妈尾杀",
            "彩虹半波", "超级赢家半波波", "王不王一头", "神算子避头", "财神一头",
            "近期开奖员", "毛老二", "通天九九肖", "大赢家九肖", "亮剑九肖",
            "白小姐四季", "白小姐来钱", "慈善四季"
        ], rules.Select(rule => rule.Id));
    }

    [Fact]
    public void NewMacauKeywordsMatchLocalOcrTitles()
    {
        IReadOnlyList<OcrRule> rules = RuleCatalog.Load(
            Path.Combine(ResultFilePaths.ConfigurationDirectory(AppContext.BaseDirectory), "新澳六合彩资料.json"));
        string[] expected =
        [
            "水哥肖", "包公肖肖", "九宫格肖肖", "佛祖肖肖", "禁止肖肖", "三怪肖肖",
            "王者肖肖", "小精肖肖", "时点半", "红禁肖", "关公又来了肖"
        ];

        foreach (string id in expected)
        {
            OcrRule rule = Assert.Single(rules, item => item.Id == id);
            Assert.Contains(rule, rules);
            Assert.Contains(
                rule.Id,
                RuleEngine.FindMatches(
                    new[] { rule.Keyword, rule.RequiredKeyword ?? string.Empty },
                    [rule]).Select(item => item.Id));
        }
    }

    [Fact]
    public void LoadsNewMacauPremiumRulesWithUniqueOutputNames()
    {
        IReadOnlyList<OcrRule> rules = RuleCatalog.Load(
            Path.Combine(ResultFilePaths.ConfigurationDirectory(AppContext.BaseDirectory), "新澳高级会员.json"));

        Assert.Equal(19, rules.Count);
        Assert.Equal(rules.Count, rules.Select(rule => rule.Id).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(
        [
            "会员琴棋", "大赢家琴棋", "会员特供杀十码", "表弟", "翩翩公子尾", "翩翩公子肖", "祥瑞阁",
            "翩翩公子半波", "翩翩公子五行", "翩翩公子杀十码", "翩翩公子头", "祥瑞阁二肖", "藏宝九肖", "会员暴打",
            "六合日报三六", "六合绿杀两肖", "六合红杀两肖", "六合蓝杀两肖", "六合杀八码"
        ], rules.Select(rule => rule.Id));
        Assert.Contains(rules, rule =>
            rule.Id == "会员特供杀十码" && rule.Keyword == "会员特供绝杀10码" && rule.Type == "号码:10");
        Assert.Contains(rules, rule =>
            rule.Id == "表弟" && rule.Keyword == "表弟主36码" && rule.Type == "号码:36");
        Assert.Contains(rules, rule =>
            rule.Id == "翩翩公子尾" && rule.Keyword == "公子送尾数" && rule.Type == "缺尾");
        Assert.Contains(rules, rule =>
            rule.Id == "翩翩公子五行" && rule.Keyword == "公子4行" && rule.Type == "五行");
        Assert.Contains(rules, rule =>
            rule.Id == "祥瑞阁二肖" && rule.Keyword == "祥瑞阁" && rule.RequiredKeyword == "杀2肖" && rule.Type == "生肖组合");
        Assert.Contains(rules, rule =>
            rule.Id == "藏宝九肖" && rule.Keyword == "藏宝库" && rule.RequiredKeyword == "藏宝库" && rule.Type == "方位");
        // 六合日报系列：keyword 取各自副标题（颜色两肖只用两字，避开四字以上的一字容错），
        // required_keyword 统一「六合日报」作群身份前缀，五张卡不得互相串读。
        Assert.Contains(rules, rule =>
            rule.Id == "六合日报三六" && rule.Keyword == "平安36码" && rule.Type == "号码:36"
            && rule.RequiredKeyword == "六合日报");
        Assert.Contains(rules, rule =>
            rule.Id == "六合杀八码" && rule.Keyword == "绝杀八码" && rule.Type == "号码:8"
            && rule.RequiredKeyword == "六合日报");
        foreach ((string id, string keyword) in new[]
                 { ("六合绿杀两肖", "绿杀"), ("六合红杀两肖", "红杀"), ("六合蓝杀两肖", "蓝杀") })
        {
            Assert.Contains(rules, rule =>
                rule.Id == id && rule.Keyword == keyword && rule.Type == "生肖组合"
                && rule.RequiredKeyword == "六合日报");
        }
    }

    [Theory]
    [InlineData("黄大仙新澳.json", "黄大仙新澳", 24)]
    [InlineData("新澳高手.json", "新澳高手", 25)]
    public void LoadsTheNewGroupRules(string fileName, string groupName, int expectedCount)
    {
        string configuration = ResultFilePaths.ConfigurationDirectory(AppContext.BaseDirectory);
        IReadOnlyList<OcrRule> rules = RuleCatalog.Load(Path.Combine(configuration, fileName));

        Assert.Equal(expectedCount, rules.Count);
        Assert.Equal(expectedCount, rules.Select(rule => rule.Id).Distinct(StringComparer.Ordinal).Count());
        Assert.EndsWith(
            fileName,
            RuleCatalog.PathForFolder(AppContext.BaseDirectory, $@"C:\图片\8.31-{groupName}"),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LoadsCorrectedNewMacauExpertRules()
    {
        string configuration = ResultFilePaths.ConfigurationDirectory(AppContext.BaseDirectory);
        IReadOnlyList<OcrRule> rules = RuleCatalog.Load(Path.Combine(configuration, "新澳高手.json"));

        Assert.Contains(rules, rule =>
            rule.Id == "男人牛" &&
            rule.Keyword == "男人味稳杀二肖" &&
            rule.Section == "男人味稳杀二肖" &&
            rule.RequiredKeyword == "男人味稳杀二肖" &&
            rule.Folder == "老男女味");
        Assert.Contains(rules, rule =>
            rule.Id == "跑狗" &&
            rule.Type == "九肖" &&
            rule.AllowNearbyValue &&
            rule.Folder == "跑狗图");
        Assert.Contains(rules, rule =>
            rule.Id == "高山流水" &&
            rule.Type == "九肖" &&
            rule.RequiredKeyword == "精选⑨肖" &&
            rule.AllowNearbyValue &&
            rule.Folder == "高山流水");
        Assert.Contains(rules, rule =>
            rule.Id == "斩杀两肖" &&
            rule.Folder == "斩杀系列" &&
            rule.StrictIssueBlock &&
            rule.MatchByRowStructure &&
            rule.AllowValueWithoutKeyword);
    }

    [Fact]
    public void LoadsFixedPrinceTailRuleWithoutKeywordFallback()
    {
        string configuration = ResultFilePaths.ConfigurationDirectory(AppContext.BaseDirectory);
        IReadOnlyList<OcrRule> rules = RuleCatalog.Load(Path.Combine(configuration, "新澳高级会员.json"));
        OcrRule rule = Assert.Single(rules, item => item.Id == "翩翩公子尾");

        Assert.True(rule.AllowValueWithoutKeyword);
        Assert.Equal(
            "8尾",
            RuleEngine.ExtractValue(
                ["244期", "0 1 2 3 4 5 6 7 9 准！", "开??"],
                244,
                rule));
    }

    [Fact]
    public void LoadsFixedNineGridNearbyValueRule()
    {
        string configuration = ResultFilePaths.ConfigurationDirectory(AppContext.BaseDirectory);
        IReadOnlyList<OcrRule> rules = RuleCatalog.Load(Path.Combine(configuration, "新澳六合彩资料.json"));
        OcrRule rule = Assert.Single(rules, item => item.Id == "九宫格肖肖");

        Assert.True(rule.AllowNearbyValue);
        Assert.Equal(
            "羊",
            RuleEngine.ExtractValue(
                ["九宫寻肖", "羊", "第244期"],
            244,
            rule));
    }

    [Fact]
    public void MalformedRuleFileFallsBackToFileNameWhenResolvingGroup()
    {
        string appDirectory = Path.Combine(Path.GetTempPath(), "rule-catalog-malformed-group-" + Guid.NewGuid().ToString("N"));
        string configuration = ResultFilePaths.ConfigurationDirectory(appDirectory);
        string path = Path.Combine(configuration, "目标群.json");
        Directory.CreateDirectory(configuration);
        File.WriteAllText(path, "{");

        try
        {
            Assert.Equal(path, RuleCatalog.PathForFolder(appDirectory, @"C:\图片\目标群"));
        }
        finally
        {
            Directory.Delete(appDirectory, recursive: true);
        }
    }

    [Fact]
    public void RejectsMalformedRuleCatalogJson()
    {
        string root = Path.Combine(Path.GetTempPath(), "rule-catalog-invalid-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(root, "invalid.json");
        Directory.CreateDirectory(root);
        File.WriteAllText(path, "{");

        try
        {
            OcrException exception = Assert.Throws<OcrException>(() => RuleCatalog.Load(path));
            Assert.Contains("格式无效", exception.Message);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void RejectsCatalogWithoutRules()
    {
        string root = Path.Combine(Path.GetTempPath(), "rule-catalog-no-rules-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(root, "missing-rules.json");
        Directory.CreateDirectory(root);
        File.WriteAllText(path, "{}");

        try
        {
            OcrException exception = Assert.Throws<OcrException>(() => RuleCatalog.Load(path));
            Assert.Contains("缺少 rules", exception.Message);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void RejectsDuplicateOutputNames()
    {
        string root = Path.Combine(Path.GetTempPath(), "rule-catalog-duplicate-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(root, "duplicate.json");
        Directory.CreateDirectory(root);
        File.WriteAllText(path, """
            {"rules":[
              {"keyword":"甲","type":"生肖","label":"重复"},
              {"keyword":"乙","type":"尾","label":"重复"}
            ]}
            """);

        try
        {
            OcrException exception = Assert.Throws<OcrException>(() => RuleCatalog.Load(path));
            Assert.Contains("重复的输出名称", exception.Message);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("keyword")]
    [InlineData("type")]
    public void RejectsRuleMissingRequiredField(string missingField)
    {
        string root = Path.Combine(Path.GetTempPath(), "rule-catalog-required-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(root, "missing-required.json");
        Directory.CreateDirectory(root);
        string json = missingField == "keyword"
            ? "{\"rules\":[{\"type\":\"生肖\"}]}"
            : "{\"rules\":[{\"keyword\":\"甲\"}]}";
        File.WriteAllText(path, json);

        try
        {
            Assert.Throws<KeyNotFoundException>(() => RuleCatalog.Load(path));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void AllowsRuleWithOnlyRequiredFields()
    {
        string root = Path.Combine(Path.GetTempPath(), "rule-catalog-optional-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(root, "required-only.json");
        Directory.CreateDirectory(root);
        File.WriteAllText(path, "{\"rules\":[{\"keyword\":\"甲\",\"type\":\"生肖\"}]} ");

        try
        {
            OcrRule rule = Assert.Single(RuleCatalog.Load(path));

            Assert.Equal("甲", rule.Id);
            Assert.Null(rule.Label);
            Assert.Null(rule.Section);
            Assert.Null(rule.RequiredKeyword);
            Assert.Null(rule.Folder);
            Assert.False(rule.IgnoreIssue);
            Assert.False(rule.AllowNearbyValue);
            Assert.False(rule.AllowValueWithoutKeyword);
            Assert.False(rule.StrictIssueBlock);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ExcludesTemplateCatalogsFromRuleResolution()
    {
        string appDirectory = Path.Combine(Path.GetTempPath(), "rule-catalog-templates-" + Guid.NewGuid().ToString("N"));
        string configuration = ResultFilePaths.ConfigurationDirectory(appDirectory);
        string templatePath = Path.Combine(configuration, "目标群.templates.json");
        Directory.CreateDirectory(configuration);
        File.WriteAllText(templatePath, "{\"group\":\"目标群\",\"rules\":[]}");

        try
        {
            string resolvedPath = RuleCatalog.PathForFolder(appDirectory, @"C:\图片\目标群");

            Assert.Equal(Path.Combine(configuration, "目标群.json"), resolvedPath);
            Assert.NotEqual(templatePath, resolvedPath);
        }
        finally
        {
            Directory.Delete(appDirectory, recursive: true);
        }
    }

    // 六合日报系列：颜色两肖关键词只差一个字，必须靠 required_keyword（六合日报）
    // 与两字精确关键词区分，五张卡不得互相串读，别的群的同类卡面也不得命中。
    [Fact]
    public void SixDayCardsUseExactColorKeywordsAndTheSharedGroupPrefix()
    {
        IReadOnlyList<OcrRule> rules = RuleCatalog.Load(
            Path.Combine(ResultFilePaths.ConfigurationDirectory(AppContext.BaseDirectory), "新澳高级会员.json"));

        string[][] cases =
        [
            ["六合日报 绿杀两肖", "292期", "绿杀:兔 龙", "开??"],
            ["六合日报 红杀两肖", "292期", "红杀:蛇 猴", "开??"],
            ["六合日报 蓝杀两肖", "292期", "蓝杀:龙 鼠", "开??"]
        ];
        string[] expectedIds = ["六合绿杀两肖", "六合红杀两肖", "六合蓝杀两肖"];
        string[] expectedValues = ["兔龙", "蛇猴", "龙鼠"];
        for (int index = 0; index < cases.Length; index++)
        {
            // 每张卡只命中自己的规则，不能三条一起命中。
            Assert.Equal([expectedIds[index]], RuleEngine.FindMatches(cases[index], rules).Select(rule => rule.Id));
            Assert.Equal(expectedValues[index], RuleEngine.ExtractValue(cases[index], 292, rules.Single(rule => rule.Id == expectedIds[index])));

            // 没有「六合日报」前缀的同类卡面属于别的群，必须一条都不命中。
            string[] foreign = [.. cases[index][1..]];
            Assert.DoesNotContain(
                RuleEngine.FindMatches(foreign, rules),
                rule => rule.Id.StartsWith("六合", StringComparison.Ordinal));
        }
    }

    // 六合日报两张号码卡的号码行被期号格和开奖栏从中间切开，属于已复核的分段版式；
    // 只有这两条在分段白名单里，其他号码规则仍不接受同样版式。
    [Fact]
    public void SixDayNumberCardsMergeRowsSplitByTheIssueCell()
    {
        IReadOnlyList<OcrRule> rules = RuleCatalog.Load(
            Path.Combine(ResultFilePaths.ConfigurationDirectory(AppContext.BaseDirectory), "新澳高级会员.json"));

        string[] thirtySix =
        [
            "六合日报平安36码", "包准：01 03 04 05 06 07", "08 13 14 15 17 18 20 21",
            "292期", "22 23 24 25 26 27 28 29", "开??", "30 31 32 33 34 35 36 37", "40 41 42 43 47 48"
        ];
        Assert.Equal(
            "01 03 04 05 06 07 08 13 14 15 17 18 20 21 22 23 24 25 26 27 28 29 " +
            "30 31 32 33 34 35 36 37 40 41 42 43 47 48",
            RuleEngine.ExtractValue(thirtySix, 292, rules.Single(rule => rule.Id == "六合日报三六")));

        string[] eight =
        ["六合日报", "绝杀八码", "× 20 21 26 27 33 38 41", "292期", "开??", "49"];
        Assert.Equal(
            "20 21 26 27 33 38 41 49",
            RuleEngine.ExtractValue(eight, 292, rules.Single(rule => rule.Id == "六合杀八码")));

        // 同一版式给别的号码规则时必须仍然取不到值（白名单不外扩）。
        var foreign = new OcrRule("小马哥", "号码:10", "小马哥", StrictIssueBlock: true);
        Assert.Null(RuleEngine.ExtractValue(
            ["292期", "包准：01 02 03 04 05 06 07 08 09 10", "开??"], 292, foreign));
    }

    // 黑色六合彩、嫣然电报：各自独立的规则与模板目录，必须一一对应；
    // 两群的数据行印在期号格上方，除「天宫杀头」外目录默认非严格。
    [Theory]
    [InlineData("黑色六合彩", 10)]
    [InlineData("嫣然电报", 7)]
    public void LoadsTheTwoNewTemplateGroupsWithMatchingTemplates(string group, int expectedCount)
    {
        string configuration = ResultFilePaths.ConfigurationDirectory(AppContext.BaseDirectory);
        IReadOnlyList<OcrRule> rules = RuleCatalog.Load(Path.Combine(configuration, group + ".json"));
        VisualTemplateSet catalog = VisualTemplateMatcher.Load(Path.Combine(configuration, group + ".templates.json"));

        Assert.Equal(expectedCount, rules.Count);
        Assert.Equal(group, catalog.Folder);
        Assert.Equal(rules.Count, catalog.Templates.Count);
        Assert.Equal(
            rules.Select(rule => rule.Id).Order(StringComparer.Ordinal),
            catalog.Templates.Select(template => template.Id).Order(StringComparer.Ordinal));
        Assert.All(catalog.Templates, template =>
        {
            Assert.NotNull(template.FingerprintTopWidthRatio);
            Assert.NotNull(template.FingerprintBottomWidthRatio);
            Assert.True(template.FingerprintBottomWidthRatio > template.FingerprintTopWidthRatio);
        });
        // 模板只在当前所选群的目录里加载，两个新群必须各自被认作独立群。
        Assert.True(VisualTemplateMatcher.Supports($@"C:\结果\9.19-{group}"));
    }

    // 嫣然电报 292 期真实版式：期号在下、数据在上/下的四种排列都必须正确出值，
    // 且同一张卡上的上一期数据不能被当成本期值（跨期借值是硬错误）。
    [Fact]
    public void YanranTelegramReadsEachIssueWithoutBorrowingTheNeighbour()
    {
        IReadOnlyList<OcrRule> rules = RuleCatalog.Load(
            Path.Combine(ResultFilePaths.ConfigurationDirectory(AppContext.BaseDirectory), "嫣然电报.json"));
        OcrRule Rule(string id) => rules.Single(rule => rule.Id == id);

        // 公式杀头：两期数据都在期号下方。
        string[] heads = ["公式绝杀头", "292期", "2头", "开??", "291期", "1头", "虎17错"];
        Assert.Equal("2头", RuleEngine.ExtractFinalValue(heads, 292, Rule("公式杀头")));
        Assert.Equal("1头", RuleEngine.ExtractFinalValue(heads, 291, Rule("公式杀头")));

        // 公式半波：数据行带「1肖」前缀。
        string[] half = ["公式杀半波", "292期", "1肖绿波双", "开??", "291期", "1肖绿波双", "虎17中"];
        Assert.Equal("绿双", RuleEngine.ExtractFinalValue(half, 292, Rule("公式半波")));
        Assert.Equal("绿双", RuleEngine.ExtractFinalValue(half, 291, Rule("公式半波")));

        // 公式杀一肖：同一生肖重复三遍，取唯一那个；两期各取各的，不并成冲突。
        string[] zodiac = ["公式绝杀肖", "292期", "金牌绝杀一肖：牛牛牛", "开??", "291期", "金牌绝杀一肖：猪猪猪", "虎17中"];
        Assert.Equal("牛", RuleEngine.ExtractFinalValue(zodiac, 292, Rule("公式杀一肖")));
        Assert.Equal("猪", RuleEngine.ExtractFinalValue(zodiac, 291, Rule("公式杀一肖")));

        // 十码必杀：号码被期号格和「开??」切断，标签里的 10 不算号码，
        // 「直接不要」之后属于下一期，不能并进来。
        string[] ten =
        [
            "⑩码必杀", "今期10码：0813", "18 24 25 29", "292期", "开??",
            "32 41 42 48", "直接不要", "今期10码：02 16 18 19 20 21", "901期", "中17中"
        ];
        Assert.Equal(
            "08 13 18 24 25 29 32 41 42 48",
            RuleEngine.ExtractFinalValue(ten, 292, Rule("嫣然电报十码必杀")));

        // 数量不足时保持缺失，绝不靠下一期补齐。
        string[] shortTen =
            ["⑩码必杀", "今期10码：0216", "18 19 20 21", "291期", "开??", "30 31 47", "直接不要"];
        Assert.Null(RuleEngine.ExtractFinalValue(shortTen, 291, Rule("嫣然电报十码必杀")));
    }

    // 黑色六合彩「黑客杀尾」：九个尾数缺一个，OCR 可能整行读出也可能拆成单字符。
    // 两种版式都要得出缺的那个尾数；不足九个一律缺失，不猜。
    [Fact]
    public void BlackMacauTailCardReadsSplitAndWholeRows()
    {
        IReadOnlyList<OcrRule> rules = RuleCatalog.Load(
            Path.Combine(ResultFilePaths.ConfigurationDirectory(AppContext.BaseDirectory), "黑色六合彩.json"));
        OcrRule tail = rules.Single(rule => rule.Id == "黑客杀尾");

        string[] whole = ["黑客数据", "数据分析", "必中九尾", "292期", "数据分析 0 1 2 3 5 6 7 8 9 尾", "开??"];
        Assert.Equal("4尾", RuleEngine.ExtractFinalValue(whole, 292, tail));

        string[] split =
            ["黑客数据", "数据分析", "必中九尾", "292期", "数据分析", "0", "1", "2", "3", "5", "6", "7", "8", "9尾", "开??"];
        Assert.Equal("4尾", RuleEngine.ExtractFinalValue(split, 292, tail));

        // 只有八个尾数时无法确定缺哪个，必须保持缺失。
        Assert.Null(RuleEngine.ExtractFinalValue(
            ["黑客数据", "292期", "数据分析 0 2 3 5 6 7 8 9 尾", "开??"], 292, tail));
    }

    // 黑色六合彩「黑客杀两肖」：数据行「数据杀特肖：鼠 兔」在期号下方，取两个不同生肖。
    [Fact]
    public void BlackMacauKillTwoZodiacsReadsTheLabelledRow()
    {
        IReadOnlyList<OcrRule> rules = RuleCatalog.Load(
            Path.Combine(ResultFilePaths.ConfigurationDirectory(AppContext.BaseDirectory), "黑色六合彩.json"));
        OcrRule pair = rules.Single(rule => rule.Id == "黑客杀两肖");

        Assert.Equal("鼠兔", RuleEngine.ExtractFinalValue(
            ["黑客数据", "数据分析", "绝杀两肖", "292期", "数据杀特肖：鼠 兔", "开??"], 292, pair));
        Assert.Equal("猴虎", RuleEngine.ExtractFinalValue(
            ["黑客数据", "291期", "数据杀特肖：猴 虎", "开??"], 291, pair));
        // 只有一个生肖或三个生肖都不是合法的两肖。
        Assert.Null(RuleEngine.ExtractFinalValue(
            ["292期", "数据杀特肖：鼠", "开??"], 292, pair));
        Assert.Null(RuleEngine.ExtractFinalValue(
            ["292期", "数据杀特肖：鼠 兔 龙", "开??"], 292, pair));
    }

    // 嫣然电报「公式杀行」：同群的「公式绝杀肖」卡标题也含「绝杀」，
    // 通用单五行分支会从别处凑出五行字，两张卡各给一个值被判冲突。
    // 只认带「绝杀一行」字段标签的那一行。
    [Fact]
    public void YanranTelegramRowCardRequiresItsOwnFieldLabel()
    {
        IReadOnlyList<OcrRule> rules = RuleCatalog.Load(
            Path.Combine(ResultFilePaths.ConfigurationDirectory(AppContext.BaseDirectory), "嫣然电报.json"));
        OcrRule row = rules.Single(rule => rule.Id == "公式杀行");

        string[] real = ["公式绝杀行", "293期", "绝杀一行：水水水", "开??", "292期", "绝杀一行：金 金 金", "羊36中"];
        Assert.Equal("水", RuleEngine.ExtractFinalValue(real, 293, row));
        Assert.Equal("金", RuleEngine.ExtractFinalValue(real, 292, row));

        // 同群的杀肖卡没有「绝杀一行」标签，必须取不到值，不能凑出五行字。
        string[] zodiacCard =
            ["公式绝杀肖", "293期", "金牌绝杀一肖：羊 羊 羊", "开??", "292期", "金牌绝杀一肖：牛 牛 牛", "羊36中"];
        Assert.Null(RuleEngine.ExtractFinalValue(zodiacCard, 293, row));
    }

    // 缺五行：卡面印四个五行字（允许重复），输出没出现的那个并带「行」后缀。
    // 印出的字不足以唯一反推时保持缺失，绝不猜。
    [Fact]
    public void MissingElementCardInvertsTheFourPrintedCharacters()
    {
        IReadOnlyList<OcrRule> rules = RuleCatalog.Load(
            Path.Combine(ResultFilePaths.ConfigurationDirectory(AppContext.BaseDirectory), "黑色六合彩.json"));
        OcrRule red = rules.Single(rule => rule.Id == "红牛四行");
        OcrRule black = rules.Single(rule => rule.Id == "黑客四行");

        Assert.Equal("木行", RuleEngine.ExtractFinalValue(
            ["红牛论坛 【中特四行】", "293期", "四行中特 金 水 火 土 开??"], 293, red));
        Assert.Equal("水行", RuleEngine.ExtractFinalValue(
            ["黑客数据", "GPT解4行", "293期", "五行必中【金 木 火 土】 开??"], 293, black));

        // 三个字、四个字全同：都无法唯一反推。
        Assert.Null(RuleEngine.ExtractFinalValue(
            ["293期", "四行中特 金 水 火 开??"], 293, red));
        Assert.Null(RuleEngine.ExtractFinalValue(
            ["293期", "四行中特 金 金 金 金 开??"], 293, red));
    }

    // 缺半波：六种半波印出五种，输出缺的那一种；印出的不是五种时保持缺失。
    [Fact]
    public void MissingHalfWaveCardInvertsTheFivePrintedWaves()
    {
        IReadOnlyList<OcrRule> rules = RuleCatalog.Load(
            Path.Combine(ResultFilePaths.ConfigurationDirectory(AppContext.BaseDirectory), "黑色六合彩.json"));
        OcrRule wave = rules.Single(rule => rule.Id == "黑客半波");

        Assert.Equal("蓝单", RuleEngine.ExtractFinalValue(
            ["黑客数据", "GPT半波预测", "293期",
             "数据半波【红波单 红波双 蓝波双 绿波单 绿波双】 开??"], 293, wave));

        // 只印四种：缺两种，无法唯一反推。
        Assert.Null(RuleEngine.ExtractFinalValue(
            ["293期", "数据半波【红波单 红波双 蓝波双 绿波单】 开??"], 293, wave));
    }

    // 号码卡的开奖格一旦开出具体号码，那之后的数字属于开奖结果，不能被当成杀号。
    // 只有未开奖的「开??」占位才允许越过继续收集跨行的号码段。
    [Fact]
    public void NumberCardsNeverBorrowTheDrawnResultAsAKillNumber()
    {
        IReadOnlyList<OcrRule> rules = RuleCatalog.Load(
            Path.Combine(ResultFilePaths.ConfigurationDirectory(AppContext.BaseDirectory), "黑色六合彩.json"));
        OcrRule ten = rules.Single(rule => rule.Id == "锦囊杀十码");

        // 只识别到九个号码且开奖格已开出 36：不能把 36 当成第十个杀号。
        Assert.Null(RuleEngine.ExtractFinalValue(
            ["293期", "锦囊杀10码：(05 08 10 22 25 26 28 39 43) 开36中"], 293, ten));

        // 未开奖的占位允许越过，跨行收齐十个。
        Assert.Equal("05 08 10 22 25 26 28 39 43 45", RuleEngine.ExtractFinalValue(
            ["锦胜", "锦囊杀10码：(05 08 10 22 25 26", "293期", "开??", "28 39 43 45)"], 293, ten));
    }
}

