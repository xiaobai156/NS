using OcrLineTool;

namespace OcrLineTool.Tests;

public sealed class AuditSafetyRegressionTests
{
    private static readonly OcrRule Doomsday = new("末日降临", "段", RequiredKeyword: "末日降临", Folder: "天机阁杀料");
    private static readonly OcrRule Zodiac = new("南国挽心", "生肖", RequiredKeyword: "南国挽心", Folder: "天机阁杀料");
    private static readonly OcrRule Tail = new("公子送尾数", "缺尾", "翩翩公子尾", StrictIssueBlock: true);

    [Fact]
    public void PreviousSuffixCannotInventRequestedIssue() => Assert.Null(RuleEngine.ExtractFinalValue(
        ["249期 末日降临 1段", "50期 末日降临 2段", "50期 末日降临 3段"], 251, Doomsday));

    [Fact]
    public void EvenMatchingShortSuffixNeedsExplicitIssueEvidence() => Assert.Null(RuleEngine.ExtractFinalValue(
        ["249期 末日降临 1段", "51期 末日降临 3段"], 251, Doomsday));

    [Fact]
    public void EightTailsCannotInventZero() => Assert.Null(RuleEngine.ExtractFinalValue(
        ["251期", "公子送尾数：1 2 3 4 5 6 7 8 准！"], 251, Tail));

    [Fact]
    public void InlineIssuesCannotBorrowPreviousValue() => Assert.Null(RuleEngine.ExtractFinalValue(
        ["250期 南国挽心 鸡 251期 南国挽心 待更新"], 251, Zodiac));

    [Fact]
    public void ConflictingTargetRowsCannotChooseFirst() => Assert.Null(RuleEngine.ExtractFinalValue(
        ["251期 南国挽心 鸡", "251期 南国挽心 狗"], 251, Zodiac));

    [Fact]
    public void SummaryIsScopedToRequestedIssue() => Assert.Equal("狗", RuleEngine.ExtractFinalValue(
        ["统计表", "250期", "南国挽心 禁 鸡", "251期", "南国挽心 禁 狗"], 251, Zodiac));

    [Fact]
    public void ConflictingSummaryCopiesCannotChooseFirst() => Assert.Null(RuleEngine.ExtractFinalValue(
        ["统计表", "251期", "南国挽心 禁 鸡", "251期", "南国挽心 禁 狗"], 251, Zodiac));

    [Fact]
    public void ExplicitIssueRetainsCorrectValue() => Assert.Equal("3段", RuleEngine.ExtractFinalValue(
        ["250期 末日降临 2段", "251期 末日降临 3段"], 251, Doomsday));

    [Fact]
    public void CompleteDistinctTailSetStillWorks() => Assert.Equal("8尾", RuleEngine.ExtractFinalValue(
        ["251期", "公子送尾数：0 1 2 3 4 5 6 7 9 准！"], 251, Tail));

    [Fact]
    public void OtherIssueCannotProduceResult() => Assert.Null(RuleEngine.ExtractFinalValue(
        ["250期 南国挽心 鸡"], 251, Zodiac));

    [Fact]
    public void IdenticalTargetCopiesRemainValid() => Assert.Equal("狗", RuleEngine.ExtractFinalValue(
        ["251期 南国挽心 狗", "251期 南国挽心 狗"], 251, Zodiac));
}
