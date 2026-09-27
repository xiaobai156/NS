using OcrLineTool;

namespace OcrLineTool.Tests;

public sealed class RecognitionStageTrackerTests
{
    [Fact]
    public void StaleStageCallbacksAreIgnored()
    {
        var tracker = new RecognitionStageTracker();
        int first = tracker.Begin("第一阶段");
        int second = tracker.Begin("第二阶段");

        Assert.False(tracker.Report(first, "第一阶段", 5, 5, 5, 5));
        Assert.Equal("第二阶段", tracker.StageName);
        Assert.Equal(0, tracker.Completed);
        Assert.True(tracker.Report(second, "第二阶段", 2, 4, 2, 4));
        Assert.Equal(2, tracker.Completed);
        Assert.Equal(4, tracker.Total);
    }

    [Fact]
    public void EndingTheStageInvalidatesItsCallbacks()
    {
        var tracker = new RecognitionStageTracker();
        int token = tracker.Begin("复抓：云兜底");
        tracker.End();

        Assert.False(tracker.Report(token, "复抓：云兜底", 1, 2, 1, 2));
        Assert.Equal(string.Empty, tracker.StageName);
        Assert.Equal(0, tracker.Total);
    }

    [Fact]
    public void ExpiredTokensAreRejectedWithoutTouchingTheCurrentStage()
    {
        var tracker = new RecognitionStageTracker();
        int first = tracker.Begin("第一阶段");
        int second = tracker.Begin("第二阶段");
        Assert.True(tracker.IsCurrent(second));
        Assert.False(tracker.IsCurrent(first));

        tracker.Report(second, "第二阶段", 2, 4, 2, 4);
        tracker.AddExcludedWait(TimeSpan.FromSeconds(3));
        tracker.BeginWait();

        // 无副作用的有效性判断：过期令牌被拒时当前阶段的名称/进度/等待累计都不动。
        Assert.False(tracker.IsCurrent(first));
        Assert.Equal("第二阶段", tracker.StageName);
        Assert.Equal(2, tracker.Completed);
        Assert.Equal(4, tracker.Total);
        Assert.Equal(TimeSpan.FromSeconds(3), tracker.ExcludedWait);
        Assert.True(tracker.IsWaiting);

        tracker.End();
        Assert.False(tracker.IsCurrent(second));
        Assert.False(tracker.IsCurrent(first));
    }

    [Fact]
    public void CompletedCountsNeverExceedTheirTotals()
    {
        var tracker = new RecognitionStageTracker();
        int token = tracker.Begin("阶段");

        Assert.True(tracker.Report(token, "阶段", 5, 3, 9, 2));
        Assert.Equal(3, tracker.Completed);
        Assert.Equal(3, tracker.Total);
        Assert.Equal(2, tracker.StageCompleted);
        Assert.Equal(2, tracker.StageTotal);
    }

    [Fact]
    public void RemainingTimeUsesOnlyTheCurrentStageAndExcludesPacingWaits()
    {
        var tracker = new RecognitionStageTracker();
        int token = tracker.Begin("云兜底");
        tracker.Report(token, "云兜底", 1, 4, 1, 4);
        tracker.AddExcludedWait(TimeSpan.FromSeconds(10));

        // 30 秒里有 10 秒是限流等待：实际均速 (30-10)/1 = 20 秒/张，还剩 3 张。
        Assert.Equal(TimeSpan.FromSeconds(60), tracker.EstimateRemaining(TimeSpan.FromSeconds(30)));
    }

    [Fact]
    public void WaitsAccumulateAndSurviveLaterReports()
    {
        var tracker = new RecognitionStageTracker();
        int token = tracker.Begin("云兜底");
        tracker.AddExcludedWait(TimeSpan.FromSeconds(4));
        tracker.Report(token, "云兜底", 1, 2, 1, 2);
        tracker.AddExcludedWait(TimeSpan.FromSeconds(6));

        // 后续报告不得清掉已累计的等待：10 秒等待全部从均速里扣掉。
        Assert.Equal(TimeSpan.FromSeconds(10), tracker.ExcludedWait);
        Assert.Equal(TimeSpan.FromSeconds(10), tracker.EstimateRemaining(TimeSpan.FromSeconds(20)));
    }

    [Fact]
    public void WaitingSuppressesTheCountdownUntilItEnds()
    {
        var tracker = new RecognitionStageTracker();
        int token = tracker.Begin("云兜底");
        tracker.Report(token, "云兜底", 1, 4, 1, 4);
        Assert.True(tracker.Estimable);

        tracker.BeginWait();
        Assert.True(tracker.IsWaiting);
        Assert.False(tracker.Estimable);
        Assert.Null(tracker.EstimateRemaining(TimeSpan.FromSeconds(30)));

        tracker.EndWait();
        Assert.False(tracker.IsWaiting);
        Assert.True(tracker.Estimable);
        Assert.Equal(TimeSpan.FromSeconds(90), tracker.EstimateRemaining(TimeSpan.FromSeconds(30)));
    }

    [Fact]
    public void StartingOrEndingAStageClearsTheWaitingFlag()
    {
        var tracker = new RecognitionStageTracker();
        tracker.BeginWait();
        tracker.Begin("新阶段");
        Assert.False(tracker.IsWaiting);

        tracker.BeginWait();
        tracker.End();
        Assert.False(tracker.IsWaiting);
    }

    [Fact]
    public void StagesWithoutSamplesDoNotPromiseATime()
    {
        var tracker = new RecognitionStageTracker();
        int token = tracker.Begin("加载模型");
        tracker.Report(token, "加载模型", 0, 0, 0, 0);

        Assert.False(tracker.Estimable);
        Assert.Null(tracker.EstimateRemaining(TimeSpan.FromSeconds(5)));

        tracker.Report(token, "识别", 0, 4, 0, 4);
        Assert.False(tracker.Estimable);
        Assert.Null(tracker.EstimateRemaining(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void SwitchingStageWithinOneTokenResetsTheSample()
    {
        var tracker = new RecognitionStageTracker();
        int token = tracker.Begin("复抓本机 OCR：准备候选");
        tracker.Report(token, "复抓本机 OCR：提取候选·识别", 1, 4, 1, 4);

        Assert.Equal("复抓本机 OCR：提取候选·识别", tracker.StageName);
        Assert.Equal(1, tracker.StageCompleted);
        Assert.Equal(4, tracker.StageTotal);
        Assert.Equal(TimeSpan.Zero, tracker.ExcludedWait);
    }
}
