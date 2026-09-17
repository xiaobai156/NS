using OcrLineTool;

namespace OcrLineTool.Tests;

public sealed class EnpingYantaRegressionTests
{
    private static IReadOnlyList<OcrRule> Rules() => RuleCatalog.Load(Path.Combine(
        ResultFilePaths.ConfigurationDirectory(AppContext.BaseDirectory), "嫣然心水.json"));

    private static OcrRule Rule(string id) => Rules().Single(rule => rule.Id == id);

    [Fact]
    public void EnpingTailReadsTheTargetIssueLineAndIgnoresTheForumFooter()
    {
        OcrRule rule = Rule("恩平杀一尾");
        string[] lines =
        [
            "作者:恩平公式", "人气:6573662/回帖:473", "2015/8/17 10:14:08",
            "恩平杀一尾，252期止28期错1期",
            "252期杀，4尾22", "253期杀，9尾16", "254期杀，8尾02", "255期杀，6尾44",
            "256期杀，7尾01", "257期杀，0尾07", "258期杀，5尾46", "259期杀，8尾22",
            "260期杀，9尾", "→→→", "2026-09-17 09:00:00编辑本帖", "最后修改:6分钟前[日志]"
        ];

        Assert.Equal("9尾", RuleEngine.ExtractFinalResult(lines, 260, rule).Value);
        Assert.Equal("5尾", RuleEngine.ExtractFinalResult(lines, 258, rule).Value);
        Assert.Equal("7尾", RuleEngine.ExtractFinalResult(lines, 256, rule).Value);
    }

    [Fact]
    public void EnpingTailStillReportsARealSameIssueConflict()
    {
        OcrRule rule = Rule("恩平杀一尾");

        RuleExtractionResult result = RuleEngine.ExtractFinalResult(
            ["260期杀，9尾", "260期杀，5尾"], 260, rule);

        Assert.Equal(RuleExtractionStatus.Conflict, result.Status);
    }

    [Fact]
    public void YantaZodiacReadsTheTargetIssueRowOfTheTwoColumnCard()
    {
        OcrRule rule = Rule("雁塔题名杀肖肖");
        string[] lines =
        [
            "雁塔题名新澳门版", "1", "250错13", "250错15", "2", "251杀5合", "杀猴",
            "3", "252杀3合", "杀鼠", "4", "253杀4合", "杀龙", "5", "254杀7合", "杀狗",
            "6", "255杀2合", "杀猴", "7", "256杀8合", "杀虎", "8", "257杀1合", "杀羊",
            "9", "258杀7合", "杀牛", "10", "259杀10合", "杀龙", "11", "260杀4合", "杀龙",
            "12", "13"
        ];

        Assert.Equal("龙", RuleEngine.ExtractFinalResult(lines, 260, rule).Value);
        Assert.Equal("龙", RuleEngine.ExtractFinalResult(lines, 259, rule).Value);
        Assert.Equal("牛", RuleEngine.ExtractFinalResult(lines, 258, rule).Value);
    }
}
