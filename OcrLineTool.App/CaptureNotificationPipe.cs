using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace OcrLineTool;

// 抓图程序抓完一个群后，通过这条命名管道主动喊一声；识别程序只处理被点到名的群，
// 不去监听、也不去定时扫图片目录。
internal sealed class CaptureNotification
{
    public string Action { get; set; } = string.Empty;
    public string Group { get; set; } = string.Empty;
    public string Date { get; set; } = string.Empty;
    public string NotifiedAt { get; set; } = string.Empty;
}

internal static class CaptureNotificationProtocol
{
    // 这里只写管道名：.NET 自己会补上 \\.\pipe\ 前缀；
    // 抓图程序用的是完整路径 \\.\pipe\OcrLineTool.CaptureCompleted.v1（见 CAPTURE_NOTIFY_PIPE）。
    internal const string PipeName = "OcrLineTool.CaptureCompleted.v1";
    internal const string CompletedAction = "capture-completed";

    internal static string? ReadGroup(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return null;

        CaptureNotification? message;
        try
        {
            message = JsonSerializer.Deserialize<CaptureNotification>(
                line, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException)
        {
            return null;
        }

        if (message is null ||
            !string.Equals(message.Action, CompletedAction, StringComparison.OrdinalIgnoreCase))
            return null;

        string group = message.Group?.Trim() ?? string.Empty;
        return group.Length == 0 ? null : group;
    }
}

// 后台守着一个管道实例，收到一条就回调一次群名；抓图程序没连上来时它就静静等着，不占 CPU。
internal sealed class CaptureNotificationListener : IDisposable
{
    private readonly Action<string> onGroupCaptured;
    private readonly string pipeName;
    private readonly CancellationTokenSource stop = new();
    private Task? listening;

    internal CaptureNotificationListener(Action<string> onGroupCaptured)
        : this(onGroupCaptured, CaptureNotificationProtocol.PipeName)
    {
    }

    // 测试用：换一条独立的管道名，免得和真实窗体在监听的那条抢连接。
    internal CaptureNotificationListener(Action<string> onGroupCaptured, string pipeName)
    {
        this.onGroupCaptured = onGroupCaptured;
        this.pipeName = pipeName;
    }

    // 同时守几个实例：一个实例收完一条要销毁重建，中间有极短的空档，
    // 抓图程序若正好这时连上来会拿到“找不到管道”。多守几个就不容易撞上。
    private const int ListenerCount = 4;

    internal void Start()
    {
        if (listening is not null)
            return;
        var tasks = new Task[ListenerCount];
        for (int index = 0; index < tasks.Length; index++)
            tasks[index] = Task.Run(ListenAsync);
        listening = Task.WhenAll(tasks);
    }

    private async Task ListenAsync()
    {
        while (!stop.IsCancellationRequested)
        {
            try
            {
                await using var server = new NamedPipeServerStream(
                    pipeName,
                    PipeDirection.In,
                    NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);
                await server.WaitForConnectionAsync(stop.Token).ConfigureAwait(false);
                using var reader = new StreamReader(server, new UTF8Encoding(false));
                string? line = await reader.ReadLineAsync(stop.Token).ConfigureAwait(false);
                string? group = CaptureNotificationProtocol.ReadGroup(line);
                if (group is null)
                    continue;
                try
                {
                    onGroupCaptured(group);
                }
                catch (Exception exception) when (exception is InvalidOperationException or ObjectDisposedException)
                {
                    // 主窗体正在销毁：这条通知丢掉即可，按约定不做启动补跑。
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // 管道偶尔被复位不算错误：歇一下再开一个新实例继续等。
                try
                {
                    await Task.Delay(200, stop.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
    }

    public void Dispose()
    {
        stop.Cancel();
        // 监听循环靠取消令牌自己退出；等任务真正结束再释放令牌，避免它还在用时被回收。
        Task? running = listening;
        listening = null;
        if (running is null)
        {
            stop.Dispose();
            return;
        }
        _ = running.ContinueWith(
            _ => stop.Dispose(), CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }
}
