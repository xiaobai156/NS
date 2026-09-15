using OcrLineTool;

namespace OcrLineTool.Tests;

/// <summary>
/// 宝典（特围36码固定海报）单缺格邻位补读：258 期真实证据里中模型漏读了
/// 第二行的 “21”，云视图同一行序列带着 21，补回后必须仍是 36 个唯一号码。
/// </summary>
public sealed class BaodianGapFillTests
{
    private static OcrRule Rule => RuleCatalog
        .Load(Path.Combine(ResultFilePaths.ConfigurationDirectory(AppContext.BaseDirectory), "新澳六合彩资料.json"))
        .Single(rule => rule.Id == "宝典");

    private static OcrLineEvidence Medium(string text, int x, int y, int width, int height) =>
        new(text, new OcrBox(x, y, width, height), 0.99, "local-primary/medium", "main");

    private static OcrLineEvidence Cloud(string text, int y) =>
        new(text, new OcrBox(20, y, 700, 31), 0.9, "retry/Tencent/tencent", "main");

    private static OcrEvidence MediumView(params OcrLineEvidence[] extra)
    {
        var items = new List<OcrLineEvidence>
        {
            Medium("宝典【特围36码】", 144, 28, 461, 74),
            Medium("包围36码03 05 07 0809 10 11", 135, 130, 491, 34),
            Medium("12", 133, 167, 54, 31),
            Medium("13", 176, 167, 51, 30),
            Medium("15", 239, 168, 59, 29),
            Medium("16", 282, 168, 54, 29),
            Medium("18", 349, 169, 42, 27),
            Medium("22", 453, 169, 43, 27),
            Medium("23 24", 508, 167, 97, 31),
            Medium("258期", 12, 196, 93, 39),
            Medium("25", 133, 202, 42, 29),
            Medium("27", 180, 201, 47, 31),
            Medium("28 29", 236, 199, 102, 35),
            Medium("30", 345, 201, 54, 30),
            Medium("32 33", 391, 200, 107, 32),
            Medium("34 35", 508, 200, 97, 32),
            Medium("准??", 678, 196, 76, 38),
            Medium("36", 132, 236, 42, 29),
            Medium("38", 185, 236, 41, 29),
            Medium("39 40", 237, 234, 101, 32),
            Medium("42", 347, 236, 44, 28),
            Medium("44 45", 398, 234, 101, 32),
            Medium("46 47", 508, 234, 97, 32),
            Medium("48", 131, 268, 45, 32),
            Medium("49", 184, 269, 40, 30)
        };
        items.AddRange(extra);
        // 与运行时一致：Items 是分区后的行列段，TokenItems 才是逐格原始项。
        IReadOnlyList<OcrLineEvidence> partitioned = OcrEvidenceLayout.Partition(items);
        return new OcrEvidence(
            "crop.png", "crop.png", "H1", "H1", "local-primary/medium", partitioned, items);
    }

    private static OcrEvidence CloudView(params string[] lines)
    {
        var items = lines
            .Select((text, index) => new OcrLineEvidence(
                text, new OcrBox(20, 130 + index * 37, 700, 31), 0.9, "retry/Tencent/tencent", "main"))
            .ToList();
        return new OcrEvidence("crop.png", "crop.png", "H1", "H1", "retry/Tencent/tencent", items);
    }

    private const string Expected =
        "03 05 07 08 09 10 11 12 13 15 16 18 21 22 23 24 25 27 28 29 30 32 33 34 35 "
        + "36 38 39 40 42 44 45 46 47 48 49";

    [Fact]
    public void FillsTheSingleMissingCellFromTheOtherView()
    {
        OcrEvidence medium = MediumView();
        OcrEvidence cloud = CloudView(
            "258期25 2725 27 28 29 30 32 3334 35准??",
            "12 1312 13 15 16 18 21 22 23 24",
            "36 3836 38 39 40 42 44 45 46 47");

        Assert.Equal(
            RuleExtractionStatus.Missing,
            RuleEngine.ExtractFinalResult(medium, 258, Rule).Status);

        (string Value, OcrEvidence Evidence)? filled =
            RuleEngine.TryFillSingleMissingNumberCell([medium, cloud], 258, Rule);

        Assert.NotNull(filled);
        Assert.Equal(Expected, filled!.Value.Value);
        Assert.Equal("retry/Tencent/tencent", filled.Value.Evidence.ViewId);
    }

    [Fact]
    public void WitnessWithoutGeometryStillFills()
    {
        OcrEvidence medium = MediumView();
        var cloud = new OcrEvidence(
            "crop.png", "crop.png", "H1", "H1", "retry/Tencent",
            [new OcrLineEvidence("12 1312 13 15 16 18 21 22 23 24", null, 0.9, "retry/Tencent")]);

        (string Value, OcrEvidence Evidence)? filled =
            RuleEngine.TryFillSingleMissingNumberCell([medium, cloud], 258, Rule);

        Assert.NotNull(filled);
        Assert.Equal(Expected, filled!.Value.Value);
    }

    [Fact]
    public void NeighbourContextMustMatch()
    {
        OcrEvidence medium = MediumView();
        OcrEvidence cloud = CloudView("12 13 15 18 21 22 23 24");

        Assert.Null(RuleEngine.TryFillSingleMissingNumberCell([medium, cloud], 258, Rule));
    }

    [Fact]
    public void TwoDifferentCandidatesStayMissing()
    {
        OcrEvidence medium = MediumView();
        OcrEvidence first = CloudView("12 1312 13 15 16 18 21 22 23 24");
        OcrEvidence second = CloudView("12 1312 13 15 16 18 20 22 23 24") with
        {
            ViewId = "retry/Baidu"
        };

        Assert.Null(RuleEngine.TryFillSingleMissingNumberCell([medium, first, second], 258, Rule));
    }

    [Fact]
    public void CandidateAlreadyInsideTheBlockIsRejected()
    {
        OcrEvidence medium = MediumView();
        OcrEvidence cloud = CloudView("12 1312 13 15 16 18 22 22 23 24");

        Assert.Null(RuleEngine.TryFillSingleMissingNumberCell([medium, cloud], 258, Rule));
    }

    [Fact]
    public void TwoMissingCellsStayMissing()
    {
        OcrEvidence medium = new OcrEvidence(
            "crop.png", "crop.png", "H1", "H1", "local-primary/medium",
            MediumView().Items
                .Where(item => item.Text is not ("18" or "42"))
                .ToArray());
        OcrEvidence cloud = CloudView("12 1312 13 15 16 18 21 22 23 24");

        Assert.Null(RuleEngine.TryFillSingleMissingNumberCell([medium, cloud], 258, Rule));
    }

    [Fact]
    public void OnlyTheReviewedRuleUsesTheFill()
    {
        OcrEvidence medium = MediumView();
        OcrEvidence cloud = CloudView("12 1312 13 15 16 18 21 22 23 24");
        var other = new OcrRule("宝典特围36码", "号码:36", Label: "金钱网", StrictIssueBlock: true);

        Assert.Null(RuleEngine.TryFillSingleMissingNumberCell([medium, cloud], 258, other));
    }

    [Fact]
    public void FillNeedsTwoDistinctViews()
    {
        OcrEvidence medium = MediumView();
        OcrEvidence sameView = CloudView("12 1312 13 15 16 18 21 22 23 24") with
        {
            ViewId = "local-primary/medium"
        };

        Assert.Null(RuleEngine.TryFillSingleMissingNumberCell([medium], 258, Rule));
        Assert.Null(RuleEngine.TryFillSingleMissingNumberCell([medium, sameView], 258, Rule));
    }

    [Fact]
    public void MainFormHelperAcceptsBoundViewShapedEvidence()
    {
        IReadOnlyList<OcrRule> rules = RuleCatalog.Load(Path.Combine(
            ResultFilePaths.ConfigurationDirectory(AppContext.BaseDirectory), "新澳六合彩资料.json"));
        OcrEvidence medium = MediumView() with { ViewId = "local-primary/medium/固定模板" };
        OcrEvidence cloud = CloudView(
            "258期25 2725 27 28 29 30 32 3334 35准??",
            "12 1312 13 15 16 18 21 22 23 24") with
        {
            ViewId = "local-primary/cloud/Tencent/固定模板"
        };
        var values = new ResultValues(StringComparer.Ordinal);
        var ledger = new ResultEvidenceLedger();

        MainForm.TryFillSingleMissingNumberCells(rules, 258, values, ledger, medium, cloud);

        Assert.True(values.TryGetValue("宝典", out string? value));
        Assert.Equal(Expected, value);
        Assert.Equal("success", ledger.Records["宝典"].Status);
    }

    [Fact]
    public void MainFormHelperSkipsWhenOnlyOneViewExists()
    {
        IReadOnlyList<OcrRule> rules = RuleCatalog.Load(Path.Combine(
            ResultFilePaths.ConfigurationDirectory(AppContext.BaseDirectory), "新澳六合彩资料.json"));
        OcrEvidence medium = MediumView() with { ViewId = "local-primary/medium/固定模板" };
        var values = new ResultValues(StringComparer.Ordinal);
        var ledger = new ResultEvidenceLedger();

        MainForm.TryFillSingleMissingNumberCells(rules, 258, values, ledger, medium, null);

        Assert.False(values.ContainsKey("宝典"));
    }
}
