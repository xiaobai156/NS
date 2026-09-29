using OcrLineTool;
using Xunit;

namespace OcrLineTool.Tests;

[Trait("Category", "ReauditAccuracy")]
public sealed class ResidualLocalPrimaryEvidenceRegressionTests
{
    private static OcrRule Rule(string group, string id) => RuleCatalog.Load(Path.Combine(
        ResultFilePaths.ConfigurationDirectory(AppContext.BaseDirectory), group + ".json"))
        .Single(rule => rule.Id == id);

    [Fact]
    public void ApplyingOneEvidenceWithTwoCompleteValuesMarksConflict()
    {
        string root = Path.Combine(Path.GetTempPath(), "ns-local-primary-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string image = Path.Combine(root, "source.png");
        File.WriteAllBytes(image, [1, 2, 3, 4]);
        try
        {
            OcrRule rule = Rule("嫣然心水", "南国挽心");
            OcrEvidence evidence = OcrEvidence.FromLines(image,
            [
                "251期 南国挽心 鸡",
                "251期 南国挽心 狗"
            ], "local-primary-test");
            var values = new ResultValues(StringComparer.Ordinal);
            var ledger = new ResultEvidenceLedger();

            MainForm.AddExtractedEvidenceValues(evidence, [rule], 251, values, ledger);

            Assert.True(ResultValues.IsConflict(values, rule.Id));
            Assert.False(values.ContainsKey(rule.Id));
            Assert.Equal("conflict", ledger.Records[rule.Id].Status);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    [Fact]
    public void MisalignedRowsInThreeColumnCardKeepTheIssueRowsOwnNumber()
    {
        string root = Path.Combine(Path.GetTempPath(), "ns-local-primary-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string image = Path.Combine(root, "source.png");
        File.WriteAllBytes(image, [7, 7, 7, 7]);
        try
        {
            OcrRule rule = Rule("嫣然心水", "Alice两码");
            // 真实 88313 卡（186–272 期排成三竖列）在 272 期一带的实测框：
            // 右列比左/中列低约 30px，左中列「213期/244期」那一行正好夹在右列
            // 「271期」「272期」两行之间。分区若拿“运行平均中心”归行，平均线会被
            // 这一行拖到 1118 附近，于是 271 期的 34.39 粘到 272 期锚点前面，
            // 复合读得出 34 39 与几何行读的 02 25 相冲 → 判同一期结果冲突。
            OcrLineEvidence[] raw =
            [
                new("Alice", new OcrBox(420, 238, 60, 40), 0.99, "paddle/original", "main"),
                new("211期", new OcrBox(0, 1036, 73, 37), 0.99, "paddle/original", "main"),
                new("禁", new OcrBox(70, 1039, 30, 32), 0.99, "paddle/original", "main"),
                new("01.40开01", new OcrBox(94, 1036, 122, 35), 0.99, "paddle/original", "main"),
                new("242期", new OcrBox(233, 1032, 81, 37), 0.99, "paddle/original", "main"),
                new("禁", new OcrBox(310, 1035, 30, 32), 0.99, "paddle/original", "main"),
                new("01.45开09", new OcrBox(335, 1033, 125, 35), 0.99, "paddle/original", "main"),
                new("271期", new OcrBox(486, 1081, 69, 34), 0.99, "paddle/original", "main"),
                new("禁", new OcrBox(552, 1085, 28, 28), 0.99, "paddle/original", "main"),
                new("34.39开35", new OcrBox(573, 1082, 117, 32), 0.99, "paddle/original", "main"),
                new("213期", new OcrBox(0, 1100, 72, 37), 0.99, "paddle/original", "main"),
                new("禁", new OcrBox(68, 1104, 27, 30), 0.99, "paddle/original", "main"),
                new("09.16开35", new OcrBox(90, 1099, 131, 37), 0.99, "paddle/original", "main"),
                new("244期", new OcrBox(233, 1097, 81, 37), 0.99, "paddle/original", "main"),
                new("禁", new OcrBox(312, 1102, 28, 29), 0.99, "paddle/original", "main"),
                new("17.46开46", new OcrBox(337, 1100, 122, 32), 0.99, "paddle/original", "main"),
                new("272期", new OcrBox(486, 1111, 73, 33), 0.99, "paddle/original", "main"),
                new("禁", new OcrBox(554, 1114, 29, 27), 0.99, "paddle/original", "main"),
                new("02.25开", new OcrBox(574, 1112, 93, 32), 0.99, "paddle/original", "main")
            ];
            byte[] bytes = File.ReadAllBytes(image);
            OcrEvidence evidence = OcrEvidence.FromCapturedBytes(
                image, bytes, "paddle", OcrEvidenceLayout.Partition(raw)) with { TokenItems = raw };

            RuleExtractionResult result = RuleEngine.ExtractFinalResult(evidence, 272, rule);

            Assert.Equal(RuleExtractionStatus.Success, result.Status);
            Assert.Equal("02 25", result.Value);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    [Fact]
    public void SameRunCloudEvidenceCannotBeReusedAfterSourceReplacement()
    {
        string root = Path.Combine(Path.GetTempPath(), "ns-local-primary-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string image = Path.Combine(root, "source.png");
        File.WriteAllBytes(image, [4, 3, 2, 1]);
        try
        {
            byte[] bytes = File.ReadAllBytes(image);
            OcrEvidence evidence = OcrEvidence.FromCapturedBytes(image, bytes, "local-primary/cloud",
            [
                new("251期 南国挽心 鸡", new OcrBox(10, 10, 180, 20), 0.98, "cloud", "main")
            ]);

            Assert.True(MainForm.IsEvidenceCurrent(evidence));
            File.WriteAllBytes(image, [9, 9, 9, 9]);
            Assert.False(MainForm.IsEvidenceCurrent(evidence));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }
}
