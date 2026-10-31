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
}
