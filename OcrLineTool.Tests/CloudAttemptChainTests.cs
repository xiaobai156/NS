namespace OcrLineTool.Tests;

/// <summary>
/// 云识别的账号轮换与限速间隔原来在三个流程里各写一遍，这里钉住它们的行为，
/// 免得哪天某条流程忘了判越界或忘了重新计时。
/// </summary>
public sealed class CloudAttemptChainTests
{
    [Fact]
    public void RotationStopsAtTheLastAccountInsteadOfWrappingAround()
    {
        var rotation = new CloudAccountRotation([Account("a"), Account("b"), Account("c")]);

        Assert.Equal("a", rotation.Current.Id);
        Assert.True(rotation.TryAdvance());
        Assert.Equal("b", rotation.Current.Id);
        Assert.True(rotation.TryAdvance());
        Assert.Equal("c", rotation.Current.Id);
        Assert.False(rotation.TryAdvance());
        Assert.Equal("c", rotation.Current.Id);
        Assert.Equal(2, rotation.Position);
    }

    [Fact]
    public void SingleAccountRotationNeverAdvances()
    {
        var rotation = new CloudAccountRotation([Account("only")]);

        Assert.False(rotation.HasNext);
        Assert.False(rotation.TryAdvance());
        Assert.Equal("only", rotation.Current.Id);
    }

    [Fact]
    public void RotationWithoutAccountsIsRejected()
    {
        Assert.Throws<ArgumentException>(() => new CloudAccountRotation([]));
    }

    [Fact]
    public void RotationEnumeratesEveryAccountInOrder()
    {
        var rotation = new CloudAccountRotation([Account("a"), Account("b"), Account("c")]);
        rotation.TryAdvance();

        // 遍历不受当前位置影响，三条流程的首次请求都按完整顺序找可用账号。
        Assert.Equal(["a", "b", "c"], rotation.All.Select(item => item.Id));
    }

    [Fact]
    public void PacerDoesNotWaitForTheFirstRequest()
    {
        var pacer = new CloudRequestPacer { MinimumInterval = TimeSpan.FromSeconds(5) };

        Assert.False(pacer.Started);
        Assert.Equal(TimeSpan.Zero, pacer.Remaining);
    }

    [Fact]
    public void PacerWaitsTheRemainingPartOfTheInterval()
    {
        var pacer = new CloudRequestPacer { MinimumInterval = TimeSpan.FromSeconds(30) };
        pacer.MarkRequested();

        Assert.True(pacer.Started);
        TimeSpan remaining = pacer.Remaining;
        Assert.True(remaining > TimeSpan.Zero, remaining.ToString());
        Assert.True(remaining <= TimeSpan.FromSeconds(30), remaining.ToString());
    }

    [Fact]
    public void PacerStopsWaitingOnceTheIntervalHasPassed()
    {
        var pacer = new CloudRequestPacer { MinimumInterval = TimeSpan.FromMilliseconds(1) };
        pacer.MarkRequested();
        Thread.Sleep(30);

        Assert.Equal(TimeSpan.Zero, pacer.Remaining);
    }

    [Fact]
    public void PacerWithoutAnIntervalNeverWaits()
    {
        var pacer = new CloudRequestPacer();
        pacer.MarkRequested();

        Assert.Equal(TimeSpan.Zero, pacer.Remaining);
    }

    private static OcrCredential Account(string id) =>
        new(OcrProvider.Tencent, "slot-" + id, id, "secret-" + id);
}
