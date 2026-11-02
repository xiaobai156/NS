namespace OcrLineTool.Tests;

// 收到“抓图完成”通知后到底该跑整群、只补缺失，还是干脆跳过。
public sealed class AutomaticRunDecisionTests
{
    [Fact]
    public void FirstRunOfTheDayAlwaysRunsTheWholeGroup() =>
        Assert.Equal(
            MainForm.AutomaticRunAction.FullRun,
            MainForm.DecideAutomaticRun(
                firstRunToday: true, hasGroupResult: true, previousSignature: "same", currentSignature: "same"));

    [Fact]
    public void LaterRunWithTheSameImagesIsSkipped() =>
        Assert.Equal(
            MainForm.AutomaticRunAction.SkipUnchanged,
            MainForm.DecideAutomaticRun(
                firstRunToday: false, hasGroupResult: true, previousSignature: "sig-1", currentSignature: "sig-1"));

    [Fact]
    public void LaterRunWithNewImagesOnlyRetriesTheMissingOnes() =>
        Assert.Equal(
            MainForm.AutomaticRunAction.MissingRetry,
            MainForm.DecideAutomaticRun(
                firstRunToday: false, hasGroupResult: true, previousSignature: "sig-1", currentSignature: "sig-2"));

    [Fact]
    public void LaterRunWithUnchangedImagesIsSkippedEvenWithoutAGroupResult() =>
        Assert.Equal(
            MainForm.AutomaticRunAction.SkipUnchanged,
            MainForm.DecideAutomaticRun(
                firstRunToday: false, hasGroupResult: false, previousSignature: "sig-1", currentSignature: "sig-1"));

    [Fact]
    public void LaterRunWithoutAGroupResultRunsTheWholeGroupWhenNewImagesArrive() =>
        Assert.Equal(
            MainForm.AutomaticRunAction.FullRun,
            MainForm.DecideAutomaticRun(
                firstRunToday: false, hasGroupResult: false, previousSignature: "sig-1", currentSignature: "sig-2"));

    [Fact]
    public void LaterRunWithoutAPreviousSignatureIsNotTreatedAsUnchanged() =>
        Assert.Equal(
            MainForm.AutomaticRunAction.MissingRetry,
            MainForm.DecideAutomaticRun(
                firstRunToday: false, hasGroupResult: true, previousSignature: string.Empty, currentSignature: "sig-1"));
}
