using OcrLineTool;
using Xunit;

namespace OcrLineTool.Tests;

[Trait("Category", "ReauditAccuracy")]
public sealed class ResidualAccuracyRegressionTests
{
    private static OcrRule Rule(string group, string id) => RuleCatalog.Load(Path.Combine(
        ResultFilePaths.ConfigurationDirectory(AppContext.BaseDirectory), group + ".json"))
        .Single(rule => rule.Id == id);

    [Fact]
    public void ReviewedSplitNeverBorrowsPreviousIssueNumbers()
    {
        string[] lines =
        [
            "杀料网绝杀10码",
            "页眉广告 01 02",
            "250期 绝杀:03 04 05 06 07 08 09 10 11 12",
            "251期 绝杀:13 14 15 16 17 18 19 20"
        ];
        Assert.Null(RuleEngine.ExtractFinalValue(lines, 251, Rule("新澳六合彩资料", "杀料")));
    }

    [Fact]
    public void IncompleteNumericBracketNeverBorrowsOutsideNumbers()
    {
        Assert.Null(RuleEngine.ExtractFinalValue(
            ["251期 青苹果 杀六码【01 02 03 04 05】其他栏目06"],
            251, Rule("嫣然心水", "青苹果")));
    }

    [Fact]
    public void TwoDifferentCompleteNumericBracketsAreAmbiguous()
    {
        Assert.Null(RuleEngine.ExtractFinalValue(
            ["251期 青苹果 杀六码【01 02 03 04 05 06】【07 08 09 10 11 12】"],
            251, Rule("嫣然心水", "青苹果")));
    }

    [Fact]
    public void OpaqueEvidenceCannotHideAnExtraZodiacLine()
    {
        using var temp = new TempFiles("嫣然心水");
        string image = temp.File("sample.png", [1, 2, 3, 4]);
        OcrEvidence evidence = OcrEvidence.FromLines(image,
        [
            "251期 小灰灰绝杀一肖 鼠",
            "牛"
        ], "opaque");
        Assert.Null(RuleEngine.ExtractFinalValue(
            evidence, 251, Rule("嫣然心水", "小灰灰一肖")));
    }

    [Fact]
    public void StandaloneFourDigitNextIssueStopsNumberContinuation()
    {
        Assert.Null(RuleEngine.ExtractFinalValue(
            ["1001期 青苹果 杀六码 01 03 04 05", "1002"],
            1001, Rule("嫣然心水", "青苹果")));
    }

    [Theory]
    [InlineData("251期 亚太地区四头：00123")]
    [InlineData("251期 亚太地区四头：013 其他栏目2")]
    public void FourHeadComplementRequiresExactlyFourRawFieldDigits(string line)
    {
        Assert.Null(RuleEngine.ExtractFinalValue(
            [line], 251, Rule("新澳高手", "亚太一头")));
    }

    [Fact]
    public void UnnumberedIgnoreIssueCardIsTrustedByIdentity()
    {
        string numbers = string.Join(' ', Enumerable.Range(1, 36).Select(n => n.ToString("00")));
        Assert.Equal(numbers, RuleEngine.ExtractFinalValue(
            [$"十点半集团大围36码 {numbers}"], 251,
            Rule("新澳六合彩资料", "时点半")));
    }

    [Fact]
    public void MixedIssueIgnoreIssueCardUsesTheSoftwareIssue()
    {
        string numbers = string.Join(' ', Enumerable.Range(1, 36).Select(n => n.ToString("00")));
        Assert.Equal(numbers, RuleEngine.ExtractFinalValue(
            [$"251期 提示", $"250期 十点半集团大围36码 {numbers}"], 251,
            Rule("新澳六合彩资料", "时点半")));
    }

    [Fact]
    public void StaggeredPhysicalColumnsCannotCompleteOneNumberField()
    {
        using var temp = new TempFiles("嫣然心水");
        string image = temp.File("sample.png", [9, 8, 7]);
        string hash = LocalOcrIdentity.Image(image);
        var evidence = new OcrEvidence(
            image, image, hash, hash, "test",
            OcrEvidenceLayout.Partition(
            [
                new("251期 青苹果 杀六码 01 02 03 04 05", new OcrBox(0, 0, 400, 20), 0.99, "test", "main"),
                new("06", new OcrBox(900, 30, 20, 20), 0.99, "test", "main")
            ]));
        Assert.Equal(2, evidence.Items.Select(item => item.RegionId).Distinct().Count());
        Assert.Null(RuleEngine.ExtractFinalValue(evidence, 251, Rule("嫣然心水", "青苹果")));
    }

    [Fact]
    public void DeclaredRegionsAreNeverCollapsedBackToMain()
    {
        using var temp = new TempFiles("嫣然心水");
        string image = temp.File("sample.png", [5, 4, 3]);
        IReadOnlyList<OcrLineEvidence> items = OcrEvidenceLayout.Partition(
        [
            new("251期 南国挽心 鸡", new OcrBox(0, 0, 200, 20), 0.9, "test", "author-a"),
            new("251期 南国挽心 狗", new OcrBox(0, 30, 200, 20), 0.9, "test", "author-b")
        ]);
        Assert.Equal(["author-a", "author-b"], items.Select(item => item.RegionId).Distinct().Order().ToArray());
    }

    private sealed class TempFiles : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "ns-residual-" + Guid.NewGuid().ToString("N"));
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
