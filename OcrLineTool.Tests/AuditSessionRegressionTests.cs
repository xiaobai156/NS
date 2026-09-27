using System.Diagnostics;
using System.Reflection;
using OcrLineTool;

namespace OcrLineTool.Tests;

public sealed class AuditSessionRegressionTests
{
    [Fact]
    public void MidnightRefreshCannotChangeTheActiveIssueOrRetryState()
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        using var form = new MainForm();
        var type = typeof(MainForm);
        var issue = (NumericUpDown)type.GetField("issueInput", flags)!.GetValue(form)!;
        issue.Value = 251;
        type.GetField("lastIssue", flags)!.SetValue(form, 251);
        type.GetField("issueDate", flags)!.SetValue(form, CredentialSchedule.TodayInBeijing().AddDays(-1));
        type.GetMethod("SetBusy", flags)!.Invoke(form, [true]);
        try
        {
            type.GetMethod("RefreshCredentialLabel", flags)!.Invoke(form, null);
            Assert.Equal(251m, issue.Value);
            Assert.Equal(251, type.GetField("lastIssue", flags)!.GetValue(form));
        }
        finally { type.GetMethod("SetBusy", flags)!.Invoke(form, [false]); }
    }

    [Fact]
    public void DisposingAnActiveFormTwiceIsSafe()
    {
        var form = new MainForm();
        typeof(MainForm).GetMethod("SetBusy", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(form, [true]);
        form.Dispose();
        form.Dispose();
        Assert.True(form.IsDisposed);
    }

    [Fact]
    public async Task CancelingTheProcessRunnerEndsTheOwnedWorker()
    {
        var pidReady = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var info = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (string argument in new[] { "-NoProfile", "-NonInteractive", "-Command", "[Console]::WriteLine($PID); Start-Sleep -Seconds 60" })
            info.ArgumentList.Add(argument);
        using var cancellation = new CancellationTokenSource();
        int started = 0;
        Task<ProcessResult> task = new SystemProcessRunner().RunAsync(info,
            line => { if (int.TryParse(line.Trim(), out int pid)) pidReady.TrySetResult(pid); }, cancellation.Token,
            () => Interlocked.Increment(ref started));
        try
        {
            int pid = await pidReady.Task.WaitAsync(TimeSpan.FromSeconds(20));
            // 进程已经真的起来了：取消之后这一次启动仍然必须记在账上（第3项口径：启动事件不因取消回滚）。
            Assert.Equal(1, Volatile.Read(ref started));
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await task.WaitAsync(TimeSpan.FromSeconds(20)));
            bool exited;
            try { using Process process = Process.GetProcessById(pid); exited = process.HasExited; }
            catch (ArgumentException) { exited = true; }
            Assert.True(exited, "Cancel must terminate the owned worker, not just cancel its wait.");
        }
        finally
        {
            cancellation.Cancel();
            try { await task.WaitAsync(TimeSpan.FromSeconds(20)); } catch (OperationCanceledException) { }
        }
    }
}
