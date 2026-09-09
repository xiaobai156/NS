from pathlib import Path

rule_tests = Path('OcrLineTool.Tests/RuleEngineTests.cs')
text = rule_tests.read_text(encoding='utf-8')
old = '''    [Fact]
    public void TreatsTheCurrencyGlyphAsSheepOnlyForTheBaogongPoster()
    {
        var baogong = new OcrRule(
            "包公图", "生肖", "包公肖肖", AllowNearbyValue: true, StrictIssueBlock: true);
        var other = new OcrRule(
            "禁肖图", "生肖", "禁止肖肖", AllowNearbyValue: true, StrictIssueBlock: true);
        string[] lines = ["包公图", "第247期", "杀一肖一码", "￥", "24"];

        Assert.Equal("羊", RuleEngine.ExtractFinalValue(lines, 247, baogong));
        Assert.Null(RuleEngine.ExtractFinalValue(lines, 247, other));
    }
'''
new = '''    [Fact]
    public void CurrencyGlyphIsNotHardCodedIntoAZodiacGuess()
    {
        var baogong = new OcrRule(
            "包公图", "生肖", "包公肖肖", AllowNearbyValue: true, StrictIssueBlock: true);
        var other = new OcrRule(
            "禁肖图", "生肖", "禁止肖肖", AllowNearbyValue: true, StrictIssueBlock: true);
        string[] lines = ["包公图", "第247期", "杀一肖一码", "￥", "24"];

        Assert.Null(RuleEngine.ExtractFinalValue(lines, 247, baogong));
        Assert.Null(RuleEngine.ExtractFinalValue(lines, 247, other));
    }
'''
if text.count(old) != 1:
    raise RuntimeError('currency-glyph legacy assertion anchor changed')
rule_tests.write_text(text.replace(old, new, 1), encoding='utf-8')

premium = Path('OcrLineTool.Tests/PremiumRuleHardeningTests.cs')
text = premium.read_text(encoding='utf-8')
old = '''    [Fact]
    public void StrictModeIsEnabledOnlyInTheHardenedCatalogs()
    {
        string directory = ResultFilePaths.ConfigurationDirectory(AppContext.BaseDirectory);
        foreach (string path in Directory.EnumerateFiles(directory, "*.json")
            .Where(path => !path.EndsWith(".templates.json") && !path.EndsWith("分发规则.json")))
        {
            Assert.All(RuleCatalog.Load(path), rule => Assert.Equal(
                Path.GetFileName(path) is "新澳高级会员.json" or "新澳六合彩资料.json" or "黄大仙新澳.json" or "蜻蜓一套骁腾.json",
                rule.StrictIssueBlock));
        }
    }
'''
new = '''    [Fact]
    public void StrictModeComesFromCatalogDefaultOrAnExplicitRuleOverride()
    {
        string directory = ResultFilePaths.ConfigurationDirectory(AppContext.BaseDirectory);
        foreach (string path in Directory.EnumerateFiles(directory, "*.json")
            .Where(path => !path.EndsWith(".templates.json") && !path.EndsWith("分发规则.json")))
        {
            bool hardenedCatalog = Path.GetFileName(path) is "新澳高级会员.json" or "新澳六合彩资料.json" or "黄大仙新澳.json" or "蜻蜓一套骁腾.json";
            foreach (OcrRule rule in RuleCatalog.Load(path))
            {
                bool explicitYanranStrict = Path.GetFileName(path) == "嫣然心水.json" && rule.Id == "小骚货";
                Assert.Equal(hardenedCatalog || explicitYanranStrict, rule.StrictIssueBlock);
            }
        }
    }
'''
if text.count(old) != 1:
    raise RuntimeError('strict-mode legacy assertion anchor changed')
premium.write_text(text.replace(old, new, 1), encoding='utf-8')

print('Updated two legacy assertions to the reviewed no-guess contract')
