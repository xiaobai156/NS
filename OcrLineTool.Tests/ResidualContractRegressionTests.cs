using OcrLineTool;

namespace OcrLineTool.Tests;

[Trait("Category", "ReauditAccuracy")]
public sealed class ResidualContractRegressionTests
{
    private static OcrRule Rule(string group, string id) => RuleCatalog.Load(Path.Combine(
        ResultFilePaths.ConfigurationDirectory(AppContext.BaseDirectory), group + ".json"))
        .Single(rule => rule.Id == id);

    [Fact]
    public void SameLinePeerAuthorCannotSupplyWaitingAuthorsValue()
    {
        OcrRule rule = Rule("嫣然心水", "南国挽心");
        Assert.Null(RuleEngine.ExtractFinalValue(
            ["251期 南国挽心 待更新 陌上花 鸡"], 251, rule));
    }

    [Fact]
    public void SameLineAuthorKeepsOwnValueBeforeNextPeer()
    {
        OcrRule rule = Rule("嫣然心水", "南国挽心");
        Assert.Equal("鸡", RuleEngine.ExtractFinalValue(
            ["251期 南国挽心 鸡 陌上花 狗"], 251, rule));
    }

    [Fact]
    public void SummaryAuthorCellStopsAtTheNextAuthor()
    {
        OcrRule rule = Rule("嫣然心水", "南国挽心");
        Assert.Null(RuleEngine.ExtractFinalValue(
            ["统计表", "251期", "南国挽心 待更新 陌上花 禁 鸡"], 251, rule));
        Assert.Equal("鸡", RuleEngine.ExtractFinalValue(
            ["统计表", "251期", "南国挽心 禁 鸡 陌上花 禁 狗"], 251, rule));
    }

    [Theory]
    [InlineData("strictIssueBlock", "\"true\"")]
    [InlineData("ignoreIssue", "1")]
    [InlineData("allowNearbyValue", "null")]
    public void InvalidBooleanConfigurationIsRejected(string property, string value)
    {
        string root = Path.Combine(Path.GetTempPath(), "ns-rule-bool-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, "invalid.json");
        File.WriteAllText(path,
            $$"""
            {
              "group": "测试",
              "strictIssueBlock": true,
              "rules": [
                { "keyword": "测试", "type": "生肖", "{{property}}": {{value}} }
              ]
            }
            """);
        try
        {
            Assert.Throws<OcrException>(() => RuleCatalog.Load(path));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void InternalSameSourceConflictIsExplicitAndSticky()
    {
        using var temp = new TempFiles("嫣然心水");
        string image = temp.File("conflict.png", [1, 4, 9, 16]);
        string hash = LocalOcrIdentity.Image(image);
        OcrRule rule = Rule("嫣然心水", "青苹果");
        var evidence = new OcrEvidence(image, image, hash, hash, "test",
        [
            new("251期 青苹果 杀六码 01 02 03 04 05 06", new OcrBox(0, 0, 420, 20), 0.99, "test", "main"),
            new("251期 青苹果 杀六码 07 08 09 10 11 12", new OcrBox(0, 30, 420, 20), 0.99, "test", "main")
        ]);

        RuleExtractionResult result = RuleEngine.ExtractFinalResult(evidence, 251, rule);
        Assert.Equal(RuleExtractionStatus.Conflict, result.Status);
        Assert.Null(result.Value);

        var values = new ResultValues(StringComparer.Ordinal);
        var ledger = new ResultEvidenceLedger();
        MainForm.AddExtractedEvidenceValues(evidence, [rule], 251, values, ledger);
        Assert.True(ResultValues.IsConflict(values, rule.Id));
        Assert.Equal("conflict", ledger.Records[rule.Id].Status);

        OcrEvidence later = new(image, image, hash, hash, "later",
        [
            new("251期 青苹果 杀六码 01 02 03 04 05 06", new OcrBox(0, 0, 420, 20), 0.99, "later", "main")
        ]);
        MainForm.AddExtractedEvidenceValues(later, [rule], 251, values, ledger);
        Assert.True(ResultValues.IsConflict(values, rule.Id));
        Assert.False(values.ContainsKey(rule.Id));
    }

    private sealed class TempFiles : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "ns-contract-" + Guid.NewGuid().ToString("N"));
        public string GroupDirectory { get; }

        public TempFiles(string group)
        {
            Directory.CreateDirectory(Root);
            GroupDirectory = Path.Combine(Root, group);
            Directory.CreateDirectory(GroupDirectory);
        }

        public string File(string name, byte[] bytes)
        {
            string path = Path.Combine(GroupDirectory, name);
            System.IO.File.WriteAllBytes(path, bytes);
            return path;
        }

        public void Dispose()
        {
            try { Directory.Delete(Root, recursive: true); } catch { }
        }
    }
}
