namespace OcrLineTool.Tests;

/// <summary>
/// 新澳六合彩资料「太极九肖」：九个生肖各占一个圆盘，OCR 拆成九个独立框。
/// 这里用当天那张真实卡（20261123_113216_02b811f3_701307.jpg，750×375）的
/// 本地 OCR 行与坐标，钉住取值、期号归属和缺项判定。
/// </summary>
public sealed class TaijiNineZodiacsTests
{
    private static readonly OcrRule Rule = RuleCatalog.Load(Path.Combine(
        ResultFilePaths.ConfigurationDirectory(AppContext.BaseDirectory), "新澳六合彩资料.json"))
        .Single(rule => rule.Id == "太极九肖");

    private static OcrEvidence Card(int issue, bool dropOneZodiac = false)
    {
        var raw = new List<(string Text, int X, int Y, int W, int H)>
        {
            ("影政易", 66, 0, 372, 44),
            ("太极肖", 118, 0, 511, 101),
            ("MACA", 40, 76, 45, 18),
            ($"第{issue}期", 124, 96, 126, 40),
            ("上期开奖结果：09 16 22 36~05 47 T23", 321, 107, 382, 26),
            ("兔", 64, 149, 103, 104),
            ("鼠", 237, 151, 99, 103),
            ("龙", 428, 154, 78, 94),
            ("猪", 581, 151, 108, 105),
            ("牛", 7, 263, 100, 93),
            ("狗", 168, 262, 99, 96),
            ("猴", 319, 264, 107, 96),
            ("马", 488, 267, 91, 84),
            ("羊", 634, 262, 103, 96)
        };
        if (dropOneZodiac)
            raw.RemoveAll(item => item.Text == "马");
        OcrLineEvidence[] lines = raw
            .Select(item => new OcrLineEvidence(item.Text, new OcrBox(item.X, item.Y, item.W, item.H), 1.0))
            .ToArray();
        return OcrEvidence.FromCapturedBytes("card.jpg", [1, 2, 3], "original",
            OcrEvidenceLayout.Partition(lines));
    }

    [Fact]
    public void CardKeepsTheNinePrintedZodiacsInReadingOrder()
    {
        RuleExtractionResult result = RuleEngine.ExtractFinalResult(Card(327), 327, Rule);
        Assert.Equal(RuleExtractionStatus.Success, result.Status);
        Assert.Equal("兔鼠龙猪牛狗猴马羊", result.Value);
    }

    [Fact]
    public void CardWithoutTheRequestedIssueYieldsNothing()
    {
        Assert.Equal(RuleExtractionStatus.Missing, RuleEngine.ExtractFinalResult(Card(327), 328, Rule).Status);
    }

    [Fact]
    public void CardWithEightZodiacsStaysMissingInsteadOfBorrowingOne()
    {
        Assert.Equal(RuleExtractionStatus.Missing,
            RuleEngine.ExtractFinalResult(Card(327, dropOneZodiac: true), 327, Rule).Status);
    }

    [Fact]
    public void RuleIsBoundToItsOwnCardParser()
    {
        Assert.Equal("taiji_nine_zodiacs", Rule.Extractor);
        Assert.Equal("太极九肖", Rule.OutputLabel);
    }

    [Fact]
    public void DiscCropsTrackThePrintedRowsAndMayPadPastImageEdges()
    {
        Rectangle firstTopDisc = TaijiNineZodiacsRecovery.DiscBounds(750, 375, 0);
        Rectangle firstBottomDisc = TaijiNineZodiacsRecovery.DiscBounds(750, 375, 4);
        Rectangle lastBottomDisc = TaijiNineZodiacsRecovery.DiscBounds(750, 375, 8);

        Assert.Equal(new Rectangle(65, 159, 100, 100), firstTopDisc);
        Assert.Equal(new Rectangle(13, 268, 100, 100), firstBottomDisc);
        Assert.Equal(new Rectangle(648, 268, 100, 100), lastBottomDisc);
    }

    [Fact]
    public void RecoveryBindsNineDiscReadsToTheRequestedIssueFromTheSameSourceImage()
    {
        using var sourceImage = new Bitmap(16, 16);
        string sourcePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".png");
        sourceImage.Save(sourcePath);
        var cropPaths = new List<string>();
        try
        {
            OcrEvidenceIdentity identity = OcrEvidenceIdentity.Capture(sourcePath, sourcePath, "candidate");
            OcrEvidence sourceEvidence = OcrEvidence.FromLines(sourcePath,
                ["第331期", "第332期", "开奖栏"], "source");
            string[] zodiacs = ["鸡", "猪", "马", "鼠", "猴", "兔", "蛇", "狗", "牛"];
            var discEvidence = new List<OcrEvidence>();
            for (int index = 0; index < zodiacs.Length; index++)
            {
                string cropPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".png");
                cropPaths.Add(cropPath);
                using var cropImage = new Bitmap(16, 16);
                cropImage.Save(cropPath);
                OcrEvidenceIdentity cropIdentity = OcrEvidenceIdentity.Capture(
                    sourcePath, cropPath, $"disc-{index}");
                OcrEvidence rawCropEvidence = OcrEvidence.FromLines(cropPath, [zodiacs[index]], "crop");
                discEvidence.Add(rawCropEvidence.Bind(cropIdentity));
            }

            OcrEvidence? combined = TaijiNineZodiacsRecovery.CombineWithSourceIssueEvidence(
                sourceEvidence, discEvidence, identity, 332);

            Assert.NotNull(combined);
            RuleExtractionResult result = RuleEngine.ExtractFinalResult(combined!, 332, Rule);
            Assert.Equal(RuleExtractionStatus.Success, result.Status);
            Assert.Equal("鸡猪马鼠猴兔蛇狗牛", result.Value);
            Assert.Equal(RuleExtractionStatus.Missing,
                RuleEngine.ExtractFinalResult(combined!, 331, Rule).Status);
        }
        finally
        {
            File.Delete(sourcePath);
            foreach (string cropPath in cropPaths)
                File.Delete(cropPath);
        }
    }

    [Fact]
    public void RecoveryRejectsDiscReadsWithoutTheRequestedIssueOnTheSourceImage()
    {
        using var sourceImage = new Bitmap(16, 16);
        string sourcePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".png");
        sourceImage.Save(sourcePath);
        var cropPaths = new List<string>();
        try
        {
            OcrEvidenceIdentity identity = OcrEvidenceIdentity.Capture(sourcePath, sourcePath, "candidate");
            OcrEvidence sourceEvidence = OcrEvidence.FromLines(sourcePath, ["第331期"], "source");
            string[] zodiacs = ["鸡", "猪", "马", "鼠", "猴", "兔", "蛇", "狗", "牛"];
            var discEvidence = new List<OcrEvidence>();
            foreach (string zodiac in zodiacs)
            {
                string cropPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".png");
                cropPaths.Add(cropPath);
                using var cropImage = new Bitmap(16, 16);
                cropImage.Save(cropPath);
                OcrEvidenceIdentity cropIdentity = OcrEvidenceIdentity.Capture(
                    sourcePath, cropPath, "disc");
                OcrEvidence rawCropEvidence = OcrEvidence.FromLines(cropPath, [zodiac], "crop");
                discEvidence.Add(rawCropEvidence.Bind(cropIdentity));
            }

            Assert.Null(TaijiNineZodiacsRecovery.CombineWithSourceIssueEvidence(
                sourceEvidence, discEvidence, identity, 332));
        }
        finally
        {
            File.Delete(sourcePath);
            foreach (string cropPath in cropPaths)
                File.Delete(cropPath);
        }
    }

    [Fact]
    public void RecoveryRejectsDiscWithMultipleZodiacCharacters()
    {
        using var sourceImage = new Bitmap(16, 16);
        string sourcePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".png");
        sourceImage.Save(sourcePath);
        var cropPaths = new List<string>();
        try
        {
            OcrEvidenceIdentity identity = OcrEvidenceIdentity.Capture(sourcePath, sourcePath, "candidate");
            OcrEvidence sourceEvidence = OcrEvidence.FromLines(sourcePath, ["第332期"], "source");
            string[] zodiacs = ["鸡", "猪", "马", "鼠", "猴", "兔", "蛇", "狗", "牛"];
            var discEvidence = new List<OcrEvidence>();
            for (int index = 0; index < zodiacs.Length; index++)
            {
                string cropPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".png");
                cropPaths.Add(cropPath);
                using var cropImage = new Bitmap(16, 16);
                cropImage.Save(cropPath);
                OcrEvidenceIdentity cropIdentity = OcrEvidenceIdentity.Capture(
                    sourcePath, cropPath, $"disc-{index}");
                string text = index == 4 ? "猴牛" : zodiacs[index];
                OcrEvidence rawCropEvidence = OcrEvidence.FromLines(cropPath, [text], "crop");
                discEvidence.Add(rawCropEvidence.Bind(cropIdentity));
            }

            Assert.Null(TaijiNineZodiacsRecovery.CombineWithSourceIssueEvidence(
                sourceEvidence, discEvidence, identity, 332));
        }
        finally
        {
            File.Delete(sourcePath);
            foreach (string cropPath in cropPaths)
                File.Delete(cropPath);
        }
    }
}
