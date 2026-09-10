using System.Text;
using System.Text.Json;
using OcrLineTool;

namespace OcrLineTool.Tests;

public sealed class ResultDistributorTests
{
    [Fact]
    public void ProductionNewMacauPremiumConfigsContainTheExactRequestedNames()
    {
        var expected = new Dictionary<string, string[]>
        {
            ["杀数字分发规则.json"] = ["会员特供杀十码", "翩翩公子杀十码"],
            ["大围分发规则.json"] = ["表弟", "祥瑞阁"],
            ["尾分发规则.json"] = ["翩翩公子尾"],
            ["肖分发规则.json"] = ["翩翩公子肖"],
            ["半波分发规则.json"] = ["翩翩公子半波"],
            ["五行分发规则.json"] = ["翩翩公子五行"],
            ["头分发规则.json"] = ["翩翩公子头"],
            ["二肖分发规则.json"] = ["祥瑞阁二肖"]
        };

        foreach ((string configName, string[] labels) in expected)
            Assert.Equal(labels, ReadSourceLabels(configName, "新澳高级会员"));
    }

    [Fact]
    public void ProductionYanranConfigsContainTheExactRequestedNames()
    {
        var expected = new Dictionary<string, string[]>
        {
            ["肖分发规则.json"] =
            [
                "南国挽心", "陌上花", "一枝独秀", "输送机", "清风荷花", "追踪使者", "白梦", "可乐仔", "紫燕儿杀一肖",
                "小苹果", "烟雨沫沫", "紫蝴蝶", "老兵惜缘", "杨丽珠", "缘来如此杀一肖", "木桃", "玩不转", "大哥6688", "大酒窝", "杰少杀一肖"
            ],
            ["肖新增分发规则.json"] =
            [
                "傻丫头", "东南仔", "梁微微", "烟草味", "辣椒炒肉肖肖", "钦差大臣公式一", "钦差大臣公式二", "苏柒若", "爱晚亭",
                "小灰灰一肖", "阿尔法", "柳叶刀", "华林肖", "小雨婷", "简单爱", "月来月好", "欧阳肖", "陈思思",
                "独傲洒脱杀肖肖", "阿莲杀肖肖", "借花献佛"
            ],
            ["二肖分发规则.json"] = ["沁园春", "君军两肖", "恩平公式", "小黄人两肖", "傻丫头二肖", "独傲洒脱杀二肖", "火狼女两肖"],
            ["头分发规则.json"] = ["齐天大圣", "九王爷", "辣椒炒肉头", "小黄人头", "雁塔题名杀头", "永卟弃杀头", "恩平杀头"],
            ["半头分发规则.json"] = ["白少华半头"],
            ["尾分发规则.json"] = ["凌志", "紫燕儿尾", "大小姐", "缘来如此尾", "君军两尾", "辣椒炒肉尾", "小黄人两尾", "华林尾", "恩平杀一尾", "杰少杀一尾", "杰少禁一尾", "阿莲杀尾尾", "火狼女两尾"],
            ["生肖分发规则.json"] = ["杰少九肖", "小骚货"],
            ["合分发规则.json"] = ["依然公主", "游牧草民", "青玉", "最亮月空", "天之涯", "雨后星星", "不决问风", "君军合", "雁塔题名杀合"],
            ["半波分发规则.json"] = ["岁月漫长", "雁塔题名半波", "华林半波", "欧阳半波", "玉亚半波"],
            ["五行分发规则.json"] = ["小黄人五行"],
            ["段分发规则.json"] = ["电竞达人", "末日降临"],
            ["杀五码分发规则.json"] = ["长安之星", "潮汕陈龙杀三码", "Alice两码", "爱晚亭", "白少华五码"]
        };

        foreach ((string configName, string[] labels) in expected)
            Assert.Equal(labels, ReadSourceLabels(configName, "嫣然心水"));

        Assert.Equal(["跑狗", "高山流水"], ReadSourceLabels("生肖分发规则.json", "新澳高手"));
    }

    [Fact]
    public async Task EnforcesTheConfiguredNumberCountForEachName()
    {
        string folder = CreateTempFolder();
        try
        {
            string target = Path.Combine(folder, "242期-杀五码-成功.txt");
            string config = Path.Combine(folder, "杀五码分发规则.json");
            await File.WriteAllTextAsync(target, "原有内容\r\n");
            await File.WriteAllTextAsync(
                config,
                JsonSerializer.Serialize(new
                {
                    targetFile = "{issue}期-杀五码-成功.txt",
                    sources = new[]
                    {
                        new
                        {
                            sourceGroup = "嫣然心水",
                            labels = new[] { "长安之星" },
                            numberCounts = new Dictionary<string, int> { ["长安之星"] = 2 }
                        }
                    }
                }));

            await ResultDistributor.DistributeAsync(
                @"C:\图片\嫣然心水",
                242,
                ["20,45 长安之星", "20,45,49 长安之星"],
                folder,
                config);

            Assert.Equal(["原有内容", "20,45 长安之星"], await File.ReadAllLinesAsync(target));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public async Task ReportsLinesThatWereSuccessfullyDistributed()
    {
        string folder = CreateTempFolder();
        try
        {
            string target = Path.Combine(folder, "242期-杀五码-成功.txt");
            string config = Path.Combine(folder, "杀五码分发规则.json");
            await File.WriteAllTextAsync(target, "原有内容\r\n");
            await File.WriteAllTextAsync(
                config,
                JsonSerializer.Serialize(new
                {
                    targetFile = "{issue}期-杀五码-成功.txt",
                    sources = new[]
                    {
                        new
                        {
                            sourceGroup = "嫣然心水",
                            labels = new[] { "长安之星" },
                            numberCounts = new Dictionary<string, int> { ["长安之星"] = 2 }
                        }
                    }
                }));

            IReadOnlySet<string> distributed = await ResultDistributor.DistributeAsync(
                @"C:\图片\嫣然心水",
                242,
                ["20,45 长安之星", "20,45,49 长安之星"],
                folder,
                config);

            Assert.Equal(["20,45 长安之星"], distributed);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public async Task ContinuesOtherDistributionFilesAndReportsOnlySuccessfulLines()
    {
        string folder = CreateTempFolder();
        string configs = CreateTempFolder();
        try
        {
            await File.WriteAllTextAsync(Path.Combine(folder, "242期-肖.txt"), string.Empty);
            await File.WriteAllTextAsync(
                Path.Combine(configs, "肖分发规则.json"),
                JsonSerializer.Serialize(new
                {
                    targetFile = "{issue}期-肖.txt",
                    sources = new[] { new { sourceGroup = "嫣然心水", labels = new[] { "南国挽心" } } }
                }));
            await File.WriteAllTextAsync(
                Path.Combine(configs, "尾分发规则.json"),
                JsonSerializer.Serialize(new
                {
                    targetFile = "{issue}期-尾.txt",
                    sources = new[] { new { sourceGroup = "嫣然心水", labels = new[] { "凌志" } } }
                }));

            DistributionResult result = await ResultDistributor.DistributeAllAsync(
                @"C:\图片\嫣然心水",
                242,
                ["虎 南国挽心", "9尾 凌志", "羊 未配置"],
                folder,
                configs);

            Assert.Equal(["虎 南国挽心"], result.DistributedLines);
            Assert.Single(result.Errors);
            Assert.Contains("242期-尾.txt", result.Errors[0]);
            Assert.Equal(
                ["虎 南国挽心（已分流）", "9尾 凌志", "羊 未配置"],
                ResultDistributor.MarkDistributedLines(
                    ["虎 南国挽心", "9尾 凌志", "羊 未配置"],
                    result.DistributedLines));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
            Directory.Delete(configs, recursive: true);
        }
    }

    [Fact]
    public async Task ReplacesTheIssuePlaceholderInThePlacementMarker()
    {
        string folder = CreateTempFolder();
        try
        {
            string target = Path.Combine(folder, "242期-半波.txt");
            string config = Path.Combine(folder, "半波分发规则.json");
            await File.WriteAllLinesAsync(target, ["华林", "", "242期排行", "排名\t内容\t数量"]);
            await File.WriteAllTextAsync(
                config,
                JsonSerializer.Serialize(new
                {
                    targetFile = "{issue}期-半波.txt",
                    placement = new { mode = "beforeLine", marker = "{issue}期排行", blankLineBeforeMarker = true },
                    sources = new[] { new { sourceGroup = "嫣然心水", labels = new[] { "岁月漫长" } } }
                }));

            await ResultDistributor.DistributeAsync(
                @"C:\图片\嫣然心水",
                242,
                ["蓝单 岁月漫长"],
                folder,
                config);

            Assert.Equal(
                ["华林", "蓝单 岁月漫长", "", "242期排行", "排名\t内容\t数量"],
                await File.ReadAllLinesAsync(target));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public async Task ProductionPendingConfigsDistributeToTheirIndependentTargets()
    {
        string folder = CreateTempFolder();
        try
        {
            await File.WriteAllLinesAsync(
                Path.Combine(folder, "242期-肖.txt"),
                ["羽墨", "", "生肖次数排行榜", "马 33次", "", "前一期失败统计", "无"]);
            await File.WriteAllLinesAsync(Path.Combine(folder, "242期-大围-成功.txt"), ["原有大围"]);
            await File.WriteAllLinesAsync(Path.Combine(folder, "242期-杀数字-成功.txt"), ["原有杀数字"]);
            await File.WriteAllLinesAsync(Path.Combine(folder, "242期-五行.txt"), ["土行 男儿本色"]);
            await File.WriteAllLinesAsync(
                Path.Combine(folder, "242期-段.txt"),
                ["2段 花间辞令", "", "排行", "合计 200条"]);

            await ResultDistributor.DistributeAllAsync(
                @"C:\图片\新澳六合彩资料",
                242,
                [
                    "兔 帅铁",
                    "猪 祖师公肖",
                    "01,02,03,04,05,06,07,08,09,10,11,12,13,14,15,16,17,18,19,20,21,22,23,24,25,26,27,28,29,30,31,32,33,34,35,36 宝典",
                    "01,02,03,04,05,06,07,08,09,10,11,12,13,14,15,16,17,18,19,20,21,22,23,24,25,26,27,28,29,30,31,32,33,34,35,36 心水",
                    "01,02,03,04,05,06,07,08,09,10,11,12,13,14,15,16,17,18,19,20,21,22,23,24,25,26,27,28,29,30,31,32,33,34,35,36 内幕",
                    "01,02,03,04,05,06,07,08,09,10,11,12,13,14,15,16,17,18,19,20,21,22,23,24,25,26,27,28,29,30,31,32,33,34,35,36 强哥",
                    "01,02,03,04,05,06,07,08,09,10,11,12,13,14,15,16,17,18,19,20,21,22,23,24,25,26,27,28,29,30,31,32,33,34,35,36 锁妖",
                    "01,02,03,04,05,06,07,08,09,10,11,12,13,14,15,16,17,18,19,20,21,22,23,24,25,26,27,28,29,30,31,32,33,34,35 赛马会",
                    "01,02,03,04,05,06,07,08,09,10,11,12,13,14,15,16,17,18,19,20,21,22,23,24,25,26,27,28,29,30,31,32,33,34,35,36 龙王",
                    "01,02,03,04,05,06,07,08,09,10,11,12,13,14,15,16,17,18,19,20,21,22,23,24,25,26,27,28,29,30,31,32,33,34,35,36 红人馆",
                    "01,02,03,04,05,06,07,08,09,10,11,12,13,14,15,16,17,18,19,20,21,22,23,24,25,26,27,28,29,30,31,32,33,34,35,36 老人味",
                    "01,02,03,04,05,06,07,08,09,10,11,12,13,14,15,16,17,18,19,20,21,22,23,24,25,26,27,28,29,30,31,32,33,34,35,36 时点半",
                    "05,06,07,09,15,24,25,27,30,32,38,44 小马哥",
                    "木 天机阁五行",
                    "木 大赢家五行",
                    "0段 心水杀段",
                    "兔 帅铁备份",
                    "01,02 龙王36码",
                    "木 大赢家五行备份"
                ],
                folder,
                ProductionConfigDirectory);

            Assert.Equal(
                ["羽墨", "兔 帅铁", "猪 祖师公肖", "", "生肖次数排行榜", "马 33次", "", "前一期失败统计", "无"],
                await File.ReadAllLinesAsync(Path.Combine(folder, "242期-肖.txt")));
            Assert.Equal(
                [
                    "原有大围", "01,02,03,04,05,06,07,08,09,10,11,12,13,14,15,16,17,18,19,20,21,22,23,24,25,26,27,28,29,30,31,32,33,34,35,36 宝典", "01,02,03,04,05,06,07,08,09,10,11,12,13,14,15,16,17,18,19,20,21,22,23,24,25,26,27,28,29,30,31,32,33,34,35,36 心水", "01,02,03,04,05,06,07,08,09,10,11,12,13,14,15,16,17,18,19,20,21,22,23,24,25,26,27,28,29,30,31,32,33,34,35,36 内幕", "01,02,03,04,05,06,07,08,09,10,11,12,13,14,15,16,17,18,19,20,21,22,23,24,25,26,27,28,29,30,31,32,33,34,35,36 强哥", "01,02,03,04,05,06,07,08,09,10,11,12,13,14,15,16,17,18,19,20,21,22,23,24,25,26,27,28,29,30,31,32,33,34,35,36 锁妖",
                    "01,02,03,04,05,06,07,08,09,10,11,12,13,14,15,16,17,18,19,20,21,22,23,24,25,26,27,28,29,30,31,32,33,34,35 赛马会", "01,02,03,04,05,06,07,08,09,10,11,12,13,14,15,16,17,18,19,20,21,22,23,24,25,26,27,28,29,30,31,32,33,34,35,36 龙王", "01,02,03,04,05,06,07,08,09,10,11,12,13,14,15,16,17,18,19,20,21,22,23,24,25,26,27,28,29,30,31,32,33,34,35,36 红人馆", "01,02,03,04,05,06,07,08,09,10,11,12,13,14,15,16,17,18,19,20,21,22,23,24,25,26,27,28,29,30,31,32,33,34,35,36 老人味", "01,02,03,04,05,06,07,08,09,10,11,12,13,14,15,16,17,18,19,20,21,22,23,24,25,26,27,28,29,30,31,32,33,34,35,36 时点半"
                ],
                await File.ReadAllLinesAsync(Path.Combine(folder, "242期-大围-成功.txt")));
            Assert.Equal(
                ["原有杀数字", "05,06,07,09,15,24,25,27,30,32,38,44 小马哥"],
                await File.ReadAllLinesAsync(Path.Combine(folder, "242期-杀数字-成功.txt")));
            Assert.Equal(
                ["土行 男儿本色", "木 天机阁五行", "木 大赢家五行"],
                await File.ReadAllLinesAsync(Path.Combine(folder, "242期-五行.txt")));
            Assert.Equal(
                ["2段 花间辞令", "0段 心水杀段", "", "排行", "合计 200条"],
                await File.ReadAllLinesAsync(Path.Combine(folder, "242期-段.txt")));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public async Task ProductionTwoZodiacConfigUsesTheSixteenExactNames()
    {
        string folder = CreateTempFolder();
        try
        {
            await File.WriteAllTextAsync(Path.Combine(folder, "242期-杀数字-成功.txt"), string.Empty);
            await File.WriteAllTextAsync(Path.Combine(folder, "242期-杀五码-成功.txt"), string.Empty);
            await File.WriteAllLinesAsync(Path.Combine(folder, "242期-尾.txt"), ["华林", "", "尾数 次数"]);
            await File.WriteAllLinesAsync(Path.Combine(folder, "242期-头.txt"), ["极品大王", "", "内容\t次数\t排名"]);
            await File.WriteAllLinesAsync(
                Path.Combine(folder, "242期-二肖.txt"),
                ["男人牛", "", "生肖次数排行榜", "马 60次"]);
            string[] outputLines =
            [
                "虎牛 心水两肖",
                "龙鸡 白小姐杀两肖",
                "蛇虎 聚宝两肖",
                "牛鸡 摇钱树蓝杀",
                "猴鸡 绿杀",
                "猪蛇 广东两肖",
                "狗鸡 福建两肖",
                "虎狗 广西两肖",
                "龙鼠 贵州两肖",
                "猪牛 海南两肖",
                "羊猴 江西两肖",
                "羊蛇 湖南两肖",
                "鸡马 上海两肖",
                "龙马 深圳两肖",
                "蛇羊 云南两肖",
                "兔虎 四川两肖",
                "虎牛 心水两肖备份",
                "牛鸡 摇钱树蓝杀尾"
            ];

            await ResultDistributor.DistributeAllAsync(
                @"C:\图片\新澳六合彩资料",
                242,
                outputLines,
                folder,
                ProductionConfigDirectory);

            string[] actual = await File.ReadAllLinesAsync(Path.Combine(folder, "242期-二肖.txt"));
            Assert.Equal(["男人牛", .. outputLines.Take(16), "", "生肖次数排行榜", "马 60次"], actual);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public async Task ProductionHeadConfigUsesTheSevenExactNames()
    {
        string folder = CreateTempFolder();
        try
        {
            await File.WriteAllTextAsync(Path.Combine(folder, "242期-杀数字-成功.txt"), string.Empty);
            await File.WriteAllTextAsync(Path.Combine(folder, "242期-杀五码-成功.txt"), string.Empty);
            await File.WriteAllLinesAsync(
                Path.Combine(folder, "242期-尾.txt"),
                ["华林", "", "尾数 次数", "2尾  41次"]);
            await File.WriteAllLinesAsync(
                Path.Combine(folder, "242期-头.txt"),
                ["极品大王", "", "内容\t次数\t排名", "2头\t77\t1"]);
            await File.WriteAllLinesAsync(
                Path.Combine(folder, "242期-二肖.txt"),
                ["男人牛", "", "生肖次数排行榜", "马 60次"]);

            await ResultDistributor.DistributeAllAsync(
                @"C:\图片\新澳六合彩资料",
                242,
                [
                    "0头 六叔公头",
                    "0头 帅铁头",
                    "1头 姨妈杀头",
                    "3头 大赢家杀头",
                    "4头 铁甲小宝",
                    "3头 特头杀",
                    "0头 特头必中",
                    "0头 六叔公头备份",
                    "1头 大赢家"
                ],
                folder,
                ProductionConfigDirectory);

            Assert.Equal(
                [
                    "极品大王",
                    "0头 六叔公头",
                    "0头 帅铁头",
                    "1头 姨妈杀头",
                    "3头 大赢家杀头",
                    "4头 铁甲小宝",
                    "3头 特头杀",
                    "0头 特头必中",
                    "",
                    "内容\t次数\t排名",
                    "2头\t77\t1"
                ],
                await File.ReadAllLinesAsync(Path.Combine(folder, "242期-头.txt")));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public async Task ProductionTailConfigUsesTheSevenExactNames()
    {
        string folder = CreateTempFolder();
        try
        {
            await File.WriteAllTextAsync(Path.Combine(folder, "242期-杀数字-成功.txt"), string.Empty);
            await File.WriteAllTextAsync(Path.Combine(folder, "242期-杀五码-成功.txt"), string.Empty);
            await File.WriteAllLinesAsync(
                Path.Combine(folder, "242期-尾.txt"),
                ["华林", "", "尾数 次数", "2尾  41次"]);
            await File.WriteAllLinesAsync(
                Path.Combine(folder, "242期-头.txt"),
                ["极品大王", "", "内容\t次数\t排名", "2头\t77\t1"]);
            await File.WriteAllLinesAsync(
                Path.Combine(folder, "242期-二肖.txt"),
                ["男人牛", "", "生肖次数排行榜", "马 60次"]);
            await File.WriteAllLinesAsync(
                Path.Combine(folder, "242期-肖.txt"),
                ["羽墨", "", "生肖次数排行榜", "马 33次"]);

            await ResultDistributor.DistributeAllAsync(
                @"C:\图片\新澳六合彩资料",
                242,
                [
                    "3尾 宝典尾",
                    "4尾 帅铁尾",
                    "5尾 6尾 刘伯温二尾",
                    "4尾 5尾 妈祖两尾",
                    "2尾 祖师公尾",
                    "5尾 大赢家杀尾",
                    "2尾 3尾 通天资料双尾",
                    "猪 祖师公肖",
                    "5尾 大赢家杀尾备份"
                ],
                folder,
                ProductionConfigDirectory);

            Assert.Equal(
                [
                    "华林",
                    "3尾 宝典尾",
                    "4尾 帅铁尾",
                    "5尾 6尾 刘伯温二尾",
                    "4尾 5尾 妈祖两尾",
                    "2尾 祖师公尾",
                    "5尾 大赢家杀尾",
                    "2尾 3尾 通天资料双尾",
                    "",
                    "尾数 次数",
                    "2尾  41次"
                ],
                await File.ReadAllLinesAsync(Path.Combine(folder, "242期-尾.txt")));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public async Task InsertsTailDataBeforeTheRankingWithOneBlankLine()
    {
        string folder = CreateTempFolder();
        try
        {
            string target = Path.Combine(folder, "242期-尾.txt");
            string config = Path.Combine(folder, "尾分发规则.json");
            await File.WriteAllLinesAsync(
                target,
                ["7尾 刘半仙", "华林", "", "尾数 次数", "2尾  41次"],
                new UTF8Encoding(true));
            await File.WriteAllTextAsync(
                config,
                JsonSerializer.Serialize(new
                {
                    targetFile = "{issue}期-尾.txt",
                    placement = new { mode = "beforeLine", marker = "尾数 次数", blankLineBeforeMarker = true },
                    sources = new[]
                    {
                        new
                        {
                            sourceGroup = "新澳六合彩资料",
                            labels = new[]
                            {
                                "宝典尾", "帅铁尾", "刘伯温二尾", "妈祖两尾", "祖师公尾", "大赢家杀尾", "通天资料双尾"
                            }
                        }
                    }
                }));
            string[] outputLines =
            [
                "3尾 宝典尾",
                "4尾 帅铁尾",
                "5尾 6尾 刘伯温二尾",
                "4尾 5尾 妈祖两尾",
                "2尾 祖师公尾",
                "5尾 大赢家杀尾",
                "2尾 3尾 通天资料双尾"
            ];

            await ResultDistributor.DistributeAsync(@"C:\图片\新澳六合彩资料", 242, outputLines, folder, config);
            await ResultDistributor.DistributeAsync(@"C:\图片\新澳六合彩资料", 242, outputLines, folder, config);

            Assert.Equal(
                [
                    "7尾 刘半仙",
                    "华林",
                    "3尾 宝典尾",
                    "4尾 帅铁尾",
                    "5尾 6尾 刘伯温二尾",
                    "4尾 5尾 妈祖两尾",
                    "2尾 祖师公尾",
                    "5尾 大赢家杀尾",
                    "2尾 3尾 通天资料双尾",
                    "",
                    "尾数 次数",
                    "2尾  41次"
                ],
                await File.ReadAllLinesAsync(target));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public async Task ProductionKillFiveConfigUsesTheThreeExactNames()
    {
        string folder = CreateTempFolder();
        try
        {
            await File.WriteAllTextAsync(Path.Combine(folder, "242期-杀数字-成功.txt"), string.Empty);
            await File.WriteAllTextAsync(Path.Combine(folder, "242期-杀五码-成功.txt"), "原有内容\r\n");
            await File.WriteAllLinesAsync(
                Path.Combine(folder, "242期-尾.txt"),
                ["华林", "", "尾数 次数", "2尾  41次"]);
            await File.WriteAllLinesAsync(
                Path.Combine(folder, "242期-头.txt"),
                ["极品大王", "", "内容\t次数\t排名", "2头\t77\t1"]);
            await File.WriteAllLinesAsync(
                Path.Combine(folder, "242期-二肖.txt"),
                ["男人牛", "", "生肖次数排行榜", "马 60次"]);

            await ResultDistributor.DistributeAllAsync(
                @"C:\图片\新澳六合彩资料",
                242,
                [
                    "23,36,46,17 雷锋",
                    "06,25,31,33,41 杀料五码",
                    "08,11,27,30 伯公绝杀",
                    "23,36,46,17 雷锋资料",
                    "05,12,14,18,19,20,26,31,34,36 杀料",
                    "08,11,27,30 伯公绝杀尾"
                ],
                folder,
                ProductionConfigDirectory);

            Assert.Equal(
                ["原有内容", "23,36,46,17 雷锋", "06,25,31,33,41 杀料五码", "08,11,27,30 伯公绝杀"],
                await File.ReadAllLinesAsync(Path.Combine(folder, "242期-杀五码-成功.txt")));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public async Task DistributesEachCategoryFromItsOwnJsonFile()
    {
        string folder = CreateTempFolder();
        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(folder, "杀数字分发规则.json"),
                CreateConfig("{issue}期-杀数字-成功.txt", "张小艺"));
            await File.WriteAllTextAsync(
                Path.Combine(folder, "杀五码分发规则.json"),
                CreateConfig("{issue}期-杀五码-成功.txt", "雷锋", "杀料五码", "伯公绝杀"));
            await File.WriteAllTextAsync(Path.Combine(folder, "242期-杀数字-成功.txt"), "数字原有\r\n");
            await File.WriteAllTextAsync(Path.Combine(folder, "242期-杀五码-成功.txt"), "五码原有\r\n");

            await ResultDistributor.DistributeAllAsync(
                @"C:\图片\新澳六合彩资料",
                242,
                [
                    "05,09,11,14,15,22,27,28,37,47 张小艺",
                    "23,36,46,17 雷锋",
                    "06,25,31,33,41 杀料五码",
                    "08,11,27,30 伯公绝杀"
                ],
                folder,
                folder);

            Assert.Equal(
                ["数字原有", "05,09,11,14,15,22,27,28,37,47 张小艺"],
                await File.ReadAllLinesAsync(Path.Combine(folder, "242期-杀数字-成功.txt")));
            Assert.Equal(
                ["五码原有", "23,36,46,17 雷锋", "06,25,31,33,41 杀料五码", "08,11,27,30 伯公绝杀"],
                await File.ReadAllLinesAsync(Path.Combine(folder, "242期-杀五码-成功.txt")));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public async Task UsesTheExactLabelsConfiguredInJson()
    {
        string folder = CreateTempFolder();
        try
        {
            string target = Path.Combine(folder, "242期-杀数字-成功.txt");
            string config = Path.Combine(folder, "杀数字分发规则.json");
            await File.WriteAllTextAsync(target, "原有内容\r\n", new UTF8Encoding(false));
            await File.WriteAllTextAsync(
                config,
                JsonSerializer.Serialize(new
                {
                    targetFile = "{issue}期-杀数字-成功.txt",
                    sources = new[]
                    {
                        new
                        {
                            sourceGroup = "新澳六合彩资料",
                            labels = new[] { "自定义名称" }
                        }
                    }
                }));

            await ResultDistributor.DistributeAsync(
                @"C:\图片\新澳六合彩资料",
                242,
                ["01,02 自定义名称", "05,09,11,14,15,22,27,28,37,47 张小艺"],
                folder,
                config);

            Assert.Equal(["原有内容", "01,02 自定义名称"], await File.ReadAllLinesAsync(target));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public async Task AppendsOnlyConfiguredNewMacauKillNumbersToTheIssueFileBottom()
    {
        string folder = CreateTempFolder();
        try
        {
            string target = Path.Combine(folder, "242期-杀数字-成功.txt");
            await File.WriteAllLinesAsync(target, ["原有第一行", "原有最后一行"], new UTF8Encoding(false));

            await ResultDistributor.DistributeAsync(
                @"C:\图片\新澳六合彩资料",
                242,
                [
                    "05,09,11,14,15,22,27,28,37,47 张小艺",
                    "01,08,14,25,28,32,37,41 刘伯温",
                    "05,09,11,14,15,22,27,28,37,47 张小艺备份",
                    "04,10,13,19,29,31,38,42,44,49 战胜庄家",
                    "5尾 6尾 刘伯温二尾",
                    "06,25,31,33,41 杀料五码",
                    "缺失（未找到对应图片） 狗庄"
                ],
                folder);

            Assert.Equal(
                [
                    "原有第一行",
                    "原有最后一行",
                    "05,09,11,14,15,22,27,28,37,47 张小艺",
                    "01,08,14,25,28,32,37,41 刘伯温"
                ],
                await File.ReadAllLinesAsync(target));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public async Task DoesNotDuplicateLinesWhenTheSameIssueRunsAgain()
    {
        string folder = CreateTempFolder();
        try
        {
            string line = "05,09,11,14,15,22,27,28,37,47 张小艺";
            string target = Path.Combine(folder, "242期-杀数字-成功.txt");
            await File.WriteAllLinesAsync(target, ["原有内容"], new UTF8Encoding(false));

            await ResultDistributor.DistributeAsync(@"C:\图片\新澳六合彩资料", 242, [line], folder);
            await ResultDistributor.DistributeAsync(@"C:\图片\新澳六合彩资料", 242, [line], folder);

            Assert.Equal(["原有内容", line], await File.ReadAllLinesAsync(target));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Theory]
    [InlineData(@"C:\图片\8.31-新澳六合彩资料")]
    [InlineData(@"C:\图片\242期-新澳六合彩资料")]
    [InlineData(@"C:\图片\新澳六合彩资料_242期")]
    public async Task DistributesDatedGroupFoldersUsingTheirExactBaseGroup(string selectedDirectory)
    {
        string folder = CreateTempFolder();
        try
        {
            string target = Path.Combine(folder, "242期-杀数字-成功.txt");
            await File.WriteAllTextAsync(target, "原有内容\r\n");

            await ResultDistributor.DistributeAsync(
                selectedDirectory,
                242,
                ["05,09,11,14,15,22,27,28,37,47 张小艺"],
                folder,
                Path.Combine(ProductionConfigDirectory, "杀数字分发规则.json"));

            Assert.Equal(
                ["原有内容", "05,09,11,14,15,22,27,28,37,47 张小艺"],
                await File.ReadAllLinesAsync(target));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public async Task IgnoresOtherGroups()
    {
        string folder = CreateTempFolder();
        try
        {
            await ResultDistributor.DistributeAsync(
                @"C:\图片\嫣然心水",
                242,
                ["05,09,11,14,15,22,27,28,37,47 张小艺"],
                folder);

            Assert.Empty(Directory.GetFiles(folder));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Theory]
    [InlineData(@"C:\图片\新澳六合彩资料备份")]
    [InlineData(@"C:\图片\新澳资料")]
    [InlineData(@"C:\图片\新澳六合彩资料-临时")]
    [InlineData(@"C:\图片\8.31-新澳六合彩资料-备份")]
    public async Task RequiresTheExactGroupDirectoryName(string selectedDirectory)
    {
        string folder = CreateTempFolder();
        try
        {
            await ResultDistributor.DistributeAsync(
                selectedDirectory,
                242,
                ["05,09,11,14,15,22,27,28,37,47 张小艺"],
                folder);

            Assert.Empty(Directory.GetFiles(folder));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public async Task ReportsWhenTheDynamicIssueFileDoesNotExist()
    {
        string folder = CreateTempFolder();
        try
        {
            OcrException exception = await Assert.ThrowsAsync<OcrException>(() =>
                ResultDistributor.DistributeAsync(
                    @"C:\图片\新澳六合彩资料",
                    243,
                    ["05,09,11,14,15,22,27,28,37,47 张小艺"],
                    folder));

            Assert.Contains("243期-杀数字-成功.txt", exception.Message);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void NewGroupsHaveAllRequestedDistributionRoutes()
    {
        Assert.Contains("68小陈", ReadSourceLabels("段分发规则.json", "黄大仙新澳"));
        Assert.Equal(
            ["68赵高", "香奈风清扬", "战狼蔷薇"],
            ReadSourceLabels("头分发规则.json", "黄大仙新澳"));
        Assert.Equal(
            ["68宝爷", "香奈肖肖", "红人关公肖", "红人极点肖"],
            ReadSourceLabels("肖新增分发规则.json", "黄大仙新澳"));
        Assert.Equal(["68旺仔", "香奈尾", "红人关公尾"], ReadSourceLabels("尾分发规则.json", "黄大仙新澳"));
        Assert.Equal(
            ["68老大", "香奈微风细雨", "战狼天空"],
            ReadSourceLabels("生肖分发规则.json", "黄大仙新澳"));
        Assert.Equal(
            ["图库", "全网"],
            ReadSourceLabels("大围分发规则.json", "新澳高手"));
        Assert.Equal(
            ["有点帅", "高手两肖", "男人牛", "完美两肖", "黄杀", "亚太两肖", "战澳两肖"],
            ReadSourceLabels("二肖分发规则.json", "新澳高手"));
        Assert.Equal(["跑狗", "高山流水"], ReadSourceLabels("生肖分发规则.json", "新澳高手"));
        Assert.Single(ReadSourceLabels("大围分发规则.json", "新澳六合彩资料"), label => label == "时点半");
        Assert.Single(ReadSourceLabels("杀数字分发规则.json", "新澳六合彩资料"), label => label == "小马哥");
        Assert.Equal(["绿格子双杀", "公式杀两肖肖"], ReadSourceLabels("二肖分发规则.json", "蜻蜓一套骁腾"));
        Assert.Equal(["高手头头", "亚太一头", "战澳头"], ReadSourceLabels("头分发规则.json", "新澳高手"));
        Assert.Equal(["黑字杀头"], ReadSourceLabels("头分发规则.json", "蜻蜓一套骁腾"));
        Assert.Equal(["黑字杀行"], ReadSourceLabels("五行分发规则.json", "蜻蜓一套骁腾"));
        Assert.Equal(["黑字杀合"], ReadSourceLabels("合分发规则.json", "蜻蜓一套骁腾"));
        Assert.Equal(["公式杀两尾尾"], ReadSourceLabels("尾分发规则.json", "蜻蜓一套骁腾"));
        Assert.Equal(["红蜻蜓", "骁腾"], ReadSourceLabels("生肖分发规则.json", "蜻蜓一套骁腾"));
        Assert.Equal(["神奇宇宙"], ReadSourceLabels("半波分发规则.json", "蜻蜓一套骁腾"));
        Assert.Equal(["墨羽", "骁腾杀肖"], ReadSourceLabels("肖分发规则.json", "蜻蜓一套骁腾"));
    }

    [Fact]
    public async Task AppendsWhenLegacyPlacementMarkerIsMissing()
    {
        string folder = CreateTempFolder();
        try
        {
            string target = Path.Combine(folder, "243期-尾.txt");
            string config = Path.Combine(folder, "尾分发规则.json");
            await File.WriteAllTextAsync(target, "现有尾数据\n", new UTF8Encoding(false));
            await File.WriteAllTextAsync(config, JsonSerializer.Serialize(new
            {
                targetFile = "{issue}期-尾.txt",
                placement = new { mode = "beforeLine", marker = "尾数 次数" },
                sources = new[] { new { sourceGroup = "新澳六合彩资料", labels = new[] { "宝典尾" } } }
            }));

            await ResultDistributor.DistributeAsync(
                @"C:\图片\新澳六合彩资料", 243, ["8尾 宝典尾"], folder, config);

            Assert.Equal(["现有尾数据", "8尾 宝典尾"], await File.ReadAllLinesAsync(target));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    private static string CreateTempFolder()
    {
        string folder = Path.Combine(Path.GetTempPath(), $"kill-number-distributor-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        return folder;
    }

    private static string[] ReadSourceLabels(string configName, string sourceGroup)
    {
        using JsonDocument document = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(ProductionConfigDirectory, configName)));
        JsonElement source = document.RootElement.GetProperty("sources")
            .EnumerateArray()
            .Single(item => item.GetProperty("sourceGroup").GetString() == sourceGroup);
        return source.GetProperty("labels").EnumerateArray().Select(item => item.GetString()!).ToArray();
    }

    private static string CreateConfig(string targetFile, params string[] labels) =>
        JsonSerializer.Serialize(new
        {
            targetFile,
            sources = new[] { new { sourceGroup = "新澳六合彩资料", labels } }
        });

    private static string ProductionConfigDirectory =>
        ResultFilePaths.ConfigurationDirectory(AppContext.BaseDirectory);
}
