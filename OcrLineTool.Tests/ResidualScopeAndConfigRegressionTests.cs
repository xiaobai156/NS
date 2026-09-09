using System.Text.Json;
using OcrLineTool;
using Xunit;

namespace OcrLineTool.Tests;

[Trait("Category", "ReauditAccuracy")]
public sealed class ResidualScopeAndConfigRegressionTests
{
    private static OcrRule Rule(string group, string id) => RuleCatalog.Load(Path.Combine(
        ResultFilePaths.ConfigurationDirectory(AppContext.BaseDirectory), group + ".json"))
        .Single(rule => rule.Id == id);

    [Fact]
    public void SharedAuthorRowCannotBorrowAValueFromTheNextAuthorCell()
    {
        OcrRule rule = Rule("嫣然心水", "南国挽心");
        Assert.Null(RuleEngine.ExtractFinalValue(
            ["251期 南国挽心 待更新 陌上花 鸡"], 251, rule));
    }

    [Fact]
    public void SharedAuthorRowStillReadsItsOwnValueBeforeTheNextAuthorCell()
    {
        OcrRule rule = Rule("嫣然心水", "南国挽心");
        Assert.Equal("鸡", RuleEngine.ExtractFinalValue(
            ["251期 南国挽心 鸡 陌上花 待更新"], 251, rule));
    }

    [Fact]
    public void SharedSectionRowCannotBorrowAValueFromTheNextSection()
    {
        OcrRule rule = Rule("嫣然心水", "钦差大臣公式一");
        Assert.Null(RuleEngine.ExtractFinalValue(
            ["251期 钦差大臣 公式一 待更新 公式二 狗"], 251, rule));
    }

    [Fact]
    public void CatalogCarriesSiblingBoundariesForSharedSheets()
    {
        OcrRule rule = Rule("嫣然心水", "南国挽心");
        Assert.NotNull(rule.SiblingBoundaries);
        Assert.Contains("陌上花", rule.SiblingBoundaries!);
    }

    [Theory]
    [InlineData("strictIssueBlock", "\"true\"", true)]
    [InlineData("strictIssueBlock", "null", false)]
    [InlineData("ignoreIssue", "1", false)]
    [InlineData("allowNearbyValue", "\"false\"", false)]
    [InlineData("allowValueWithoutKeyword", "{}", false)]
    [InlineData("singleValuePerIssue", "[]", false)]
    public void BooleanOptionsRejectNonBooleanJson(
        string property, string rawValue, bool atRoot)
    {
        string root = Path.Combine(Path.GetTempPath(), "ns-config-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, "test.json");
        try
        {
            object rule = new Dictionary<string, object?>
            {
                ["keyword"] = "测试",
                ["type"] = "生肖"
            };
            using JsonDocument raw = JsonDocument.Parse(rawValue);
            var ruleMap = (Dictionary<string, object?>)rule;
            if (!atRoot)
                ruleMap[property] = raw.RootElement.Clone();

            var document = new Dictionary<string, object?>
            {
                ["version"] = 1,
                ["group"] = "测试",
                ["rules"] = new[] { ruleMap }
            };
            if (atRoot)
                document[property] = raw.RootElement.Clone();

            File.WriteAllText(path, JsonSerializer.Serialize(document));
            OcrException error = Assert.Throws<OcrException>(() => RuleCatalog.Load(path));
            Assert.Contains(property, error.Message, StringComparison.Ordinal);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }
}
