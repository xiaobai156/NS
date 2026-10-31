using System.Diagnostics;

namespace OcrLineTool;

/// <summary>
/// 云账号轮换：本地主识别、云主识别、手动复抓三套流程都要「遇到无权限/无额度就换下一个，
/// 最多三个」。抽出来之后边界条件只有一处，不会出现某条流程忘了判越界。
/// </summary>
internal sealed class CloudAccountRotation
{
    private readonly IReadOnlyList<OcrCredential> accounts;
    private int index;

    public CloudAccountRotation(IReadOnlyList<OcrCredential> accounts)
    {
        if (accounts.Count == 0)
            throw new ArgumentException("账号轮换至少要有一个账号。", nameof(accounts));
        this.accounts = accounts;
    }

    public OcrCredential Current => accounts[index];

    public int Count => accounts.Count;

    public int Position => index;

    /// <summary>后面还有没有账号可换。false 表示这一轮已经用尽。</summary>
    public bool HasNext => index + 1 < accounts.Count;

    /// <summary>换到下一个账号；已经是最后一个时返回 false，位置不变。</summary>
    public bool TryAdvance()
    {
        if (!HasNext)
            return false;
        index++;
        return true;
    }

    /// <summary>按顺序遍历全部账号（首次请求用得到，不需要手动维护下标）。</summary>
    public IEnumerable<OcrCredential> All
    {
        get
        {
            for (int position = 0; position < accounts.Count; position++)
                yield return accounts[position];
        }
    }
}

/// <summary>
/// 按免费额度限速：第一次请求不等，之后每次请求前补足最小间隔。
/// 原来三个流程各自用一对「Stopwatch + bool started」记录，容易漏掉 Restart 或判反条件。
/// </summary>
internal sealed class CloudRequestPacer
{
    private readonly Stopwatch stopwatch = new();

    public TimeSpan MinimumInterval { get; set; }

    public bool Started { get; private set; }

    /// <summary>距离下一次可以请求还差多久；没请求过或已经够了就是零。</summary>
    public TimeSpan Remaining
    {
        get
        {
            if (!Started)
                return TimeSpan.Zero;
            TimeSpan remaining = MinimumInterval - stopwatch.Elapsed;
            return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
        }
    }

    /// <summary>记一次请求已经发出，并从这里开始重新计时。</summary>
    public void MarkRequested()
    {
        stopwatch.Restart();
        Started = true;
    }

    /// <summary>
    /// 等到下一次可以请求。等待方式由调用方决定：界面流程用 Task.Delay 并显示倒计时，
    /// 手动复抓用不计入阶段耗时的等待。三套流程原来各写一遍「取 Remaining、判正、Delay」。
    /// </summary>
    public async Task WaitAsync(Func<TimeSpan, Task> delay, Action<TimeSpan>? announce = null)
    {
        TimeSpan remaining = Remaining;
        if (remaining <= TimeSpan.Zero)
            return;
        announce?.Invoke(remaining);
        await delay(remaining);
    }
}

/// <summary>
/// 限流自动重试：云主识别、云备用识别、手动复抓各写过一遍「没到上限就退避重试、
/// 到了就交给调用方处理，恢复之后计数归零」。抽成一份，上限和退避曲线只有一处。
/// </summary>
internal sealed class CloudRateLimitRetries
{
    private int attempts;

    public int Count => attempts;

    /// <summary>还有机会时给出这一轮的退避时长并计一次数；用尽时返回 false 且不改状态。</summary>
    public bool TryBeginRetry(out TimeSpan delay)
    {
        if (attempts >= CloudOcrPolicy.MaxAutomaticRetries)
        {
            delay = TimeSpan.Zero;
            return false;
        }

        attempts++;
        delay = CloudOcrPolicy.RetryDelay(attempts);
        return true;
    }

    /// <summary>等过一次人工恢复之后重新给满重试机会。</summary>
    public void Reset() => attempts = 0;
}
