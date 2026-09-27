namespace OcrLineTool;

/// <summary>
/// 复抓阶段的进度与剩余时间状态机：阶段之间独立重置，过期阶段的报告凭令牌丢弃，
/// 限流等待不计入均速。纯状态（不含计时器/控件），便于单测。
/// </summary>
internal sealed class RecognitionStageTracker
{
    private int stageToken;

    internal string StageName { get; private set; } = string.Empty;
    internal int Completed { get; private set; }
    internal int Total { get; private set; }
    internal int StageCompleted { get; private set; }
    internal int StageTotal { get; private set; }
    internal TimeSpan ExcludedWait { get; private set; }

    /// <summary>阶段内是否正处在与识别速度无关的等待中（限流暂停、等待手动继续）。</summary>
    internal bool IsWaiting { get; private set; }

    /// <summary>本阶段是否具备估算剩余时间的样本（加载模型、全缓存命中、等待中都没有）。</summary>
    internal bool Estimable => !IsWaiting && StageTotal > 0 && StageCompleted > 0;

    /// <summary>累计不计入均速的等待（限流、等待手动继续）。</summary>
    internal void AddExcludedWait(TimeSpan wait)
    {
        if (wait > TimeSpan.Zero)
            ExcludedWait += wait;
    }

    /// <summary>进入与识别速度无关的等待：期间不给出倒计时。</summary>
    internal void BeginWait() => IsWaiting = true;

    /// <summary>等待结束。</summary>
    internal void EndWait() => IsWaiting = false;

    /// <summary>开始一个新阶段；令牌自增，上一阶段的后续报告一律被丢弃。</summary>
    internal int Begin(string stage)
    {
        StageName = stage;
        Completed = 0;
        Total = 0;
        StageCompleted = 0;
        StageTotal = 0;
        ExcludedWait = TimeSpan.Zero;
        IsWaiting = false;
        return ++stageToken;
    }

    /// <summary>结束当前阶段；令牌自增，使该阶段残留的回调失效。</summary>
    internal void End()
    {
        StageName = string.Empty;
        Completed = 0;
        Total = 0;
        StageCompleted = 0;
        StageTotal = 0;
        ExcludedWait = TimeSpan.Zero;
        IsWaiting = false;
        stageToken++;
    }

    /// <summary>
    /// 记录一次阶段进度。返回 false 表示该报告属于已过期阶段，调用方不得用它更新界面。
    /// <paramref name="completed"/> / <paramref name="total"/> 是整批进度，<paramref name="stageCompleted"/> /
    /// <paramref name="stageTotal"/> 是本阶段用于估算剩余的单位数；<paramref name="stageTotal"/> 为 0 表示无法估算。
    /// 已完成数一律不超过各自总数。等待时间由 <see cref="AddExcludedWait"/> 单独累计，不随报告清零。
    /// </summary>
    internal bool Report(int token, string stage, int completed, int total, int stageCompleted, int stageTotal)
    {
        if (token != stageToken)
            return false;
        StageName = stage;
        Total = Math.Max(0, total);
        Completed = Math.Clamp(completed, 0, Total);
        StageTotal = Math.Max(0, stageTotal);
        StageCompleted = Math.Clamp(stageCompleted, 0, StageTotal);
        return true;
    }

    /// <summary>当前阶段预计剩余；样本不足或阶段无法估算时返回 null（界面显示“无法估算”）。</summary>
    internal TimeSpan? EstimateRemaining(TimeSpan stageElapsed)
    {
        if (!Estimable)
            return null;
        double workSeconds = Math.Max(0, (stageElapsed - ExcludedWait).TotalSeconds);
        int remaining = Math.Max(0, StageTotal - StageCompleted);
        return TimeSpan.FromSeconds(workSeconds / StageCompleted * remaining);
    }
}
