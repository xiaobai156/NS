using System.IO.Pipes;

namespace OcrLineTool.Tests;

// 抓图程序抓完一个群会通过命名管道喊一声；这里验证识别程序这一端的接线。
public sealed class CaptureNotificationTests
{
    [Fact]
    public async Task ListenerHandsTheGroupNameOverWhenTheCaptureProgramCalls()
    {
        // 用独立管道名：真实窗体也在监听默认那条，测试抢同一条会互相抢连接。
        string pipeName = "OcrLineTool.Test." + Guid.NewGuid().ToString("N");
        var received = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var listener = new CaptureNotificationListener(group => received.TrySetResult(group), pipeName);
        listener.Start();

        await using (var client = new NamedPipeClientStream(
            ".", pipeName, PipeDirection.Out, PipeOptions.Asynchronous))
        {
            await client.ConnectAsync(5000);
            await using var writer = new StreamWriter(client) { AutoFlush = true };
            await writer.WriteLineAsync("{\"action\":\"capture-completed\",\"group\":\"新澳高手\"}");
        }

        Assert.Equal("新澳高手", await received.Task.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void LineWithoutTheCompletedActionIsIgnored()
    {
        Assert.Null(CaptureNotificationProtocol.ReadGroup("{\"action\":\"capture-started\",\"group\":\"新澳高手\"}"));
    }
}
