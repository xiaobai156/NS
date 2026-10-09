using System.Drawing;
using System.Drawing.Imaging;
using OcrLineTool;

namespace OcrLineTool.Tests;

public sealed class ZodiacIconMatcherTests
{
    private const string Source =
        @"C:\Users\Administrator\Desktop\每天工具\飞机抓图\结果\10.9-新澳六合彩资料\全部图片\20261009_173121_02b811f3_651057.jpg";
    private const string HistoricalSource =
        @"C:\Users\Administrator\AppData\Local\Temp\codex-clipboard-eecae8d4-9cef-4025-ab05-9dda0168ec85.jpg";

    [Fact]
    public void DragonKingTwoZodiacRuleUsesTheFixedIconCard()
    {
        OcrRule rule = RuleCatalog.Load(Path.Combine(
            ResultFilePaths.ConfigurationDirectory(AppContext.BaseDirectory), "新澳六合彩资料.json"))
            .Single(item => item.Id == "龙王杀两肖");
        Assert.Equal("生肖组合", rule.Type);

        // This is a real-image regression on the user's fixed card. A clean
        // checkout without the external source image still checks the rule/config.
        if (!File.Exists(Source))
            return;

        string[] rows = Enumerable.Range(269, 14)
            .Reverse()
            .Select(issue => $"{issue}期")
            .ToArray();
        OcrEvidence evidence = OcrEvidence.FromLines(Source, rows);
        Assert.Equal("兔羊", RuleEngine.ExtractFinalValue(evidence, 282, rule));
        Assert.Equal("蛇猪", RuleEngine.ExtractFinalValue(evidence, 281, rule));
        Assert.Equal("龙蛇", RuleEngine.ExtractFinalValue(evidence, 279, rule));
        Assert.Equal("虎鸡", RuleEngine.ExtractFinalValue(evidence, 270, rule));
    }

    [Fact]
    public void TemplateCropCoordinatesStillUseTheCardIssueOrder()
    {
        if (!File.Exists(Source))
            return;

        OcrRule rule = RuleCatalog.Load(Path.Combine(
            ResultFilePaths.ConfigurationDirectory(AppContext.BaseDirectory), "新澳六合彩资料.json"))
            .Single(item => item.Id == "龙王杀两肖");
        string cropPath = Path.Combine(Path.GetTempPath(), $"zodiac-template-{Guid.NewGuid():N}.png");
        try
        {
            using (var source = new Bitmap(Source))
            using (Bitmap crop = source.Clone(new Rectangle(0, 90, source.Width, 232), PixelFormat.Format24bppRgb))
                crop.Save(cropPath, ImageFormat.Png);

            OcrEvidenceIdentity identity = OcrEvidenceIdentity.Capture(Source, cropPath, "template-crop");
            OcrEvidence evidence = new(
                Source,
                cropPath,
                identity.SourceHash,
                identity.InputHash,
                identity.ViewId,
                [
                    new("282期", new OcrBox(10, 112, 70, 30), 0.99, "template-crop", "main"),
                    new("281期", new OcrBox(10, 180, 70, 30), 0.99, "template-crop", "main")
                ]);

            Assert.Equal("兔羊", RuleEngine.ExtractFinalValue(evidence, 282, rule));
        }
        finally
        {
            try { File.Delete(cropPath); } catch (IOException) { }
        }
    }

    [Fact]
    public void WhiteRabbitDoesNotShiftRowsOrFallBackToAdjacentIssue()
    {
        if (!File.Exists(HistoricalSource))
            return;

        OcrRule rule = RuleCatalog.Load(Path.Combine(
            ResultFilePaths.ConfigurationDirectory(AppContext.BaseDirectory), "新澳六合彩资料.json"))
            .Single(item => item.Id == "龙王杀两肖");
        string[] rows = Enumerable.Range(187, 14)
            .Reverse()
            .Select(issue => $"{issue}期")
            .ToArray();
        OcrEvidence evidence = OcrEvidence.FromLines(HistoricalSource, rows);

        Assert.Equal("兔蛇", RuleEngine.ExtractFinalValue(evidence, 190, rule));
        Assert.Equal("牛虎", RuleEngine.ExtractFinalValue(evidence, 191, rule));
    }
}
