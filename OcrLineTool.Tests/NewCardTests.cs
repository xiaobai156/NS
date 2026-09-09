using OcrLineTool;

namespace OcrLineTool.Tests;

public class NewCardTests
{
    private static OcrRule Rule(string group, string id) => RuleCatalog.Load(Path.Combine(
        ResultFilePaths.ConfigurationDirectory(AppContext.BaseDirectory), group + ".json")).Single(r => r.Id == id);

    [Fact]
    public void TreasureKeepsStrictCountAndIgnoresColourAndOpeningBanner()
    {
        var rule = Rule("新澳六合彩资料", "藏宝十二码");
        Assert.True(rule.StrictIssueBlock);
        foreach (int issue in new[] { 251, 318, 1001 })
        {
            string[] lines = ["10 06 39 47 37 17 14", $"{issue - 1}期开", "藏宝库【杀一波12码】付费版",
                $"{issue}期", "杀：蓝", "03 04 09 10", "14 15 25 26", "31 42 47 48", "开??",
                $"{issue - 1}期", "杀：红", "01 08 12 13 18 19 24 30 35 40 45 46", "红中"];
            Assert.Equal("03 04 09 10 14 15 25 26 31 42 47 48", RuleEngine.ExtractFinalValue(lines, issue, rule));
            foreach (string bad in new[] { "31 42 47", "31 42 47 50", "31 42 47 03", "31 42 47 48 49" })
                Assert.Null(RuleEngine.ExtractFinalValue(lines.Select(l => l.Replace("31 42 47 48", bad)), issue, rule));
        }
    }
}
