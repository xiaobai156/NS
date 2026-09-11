namespace OcrLineTool.Tests;

public sealed class CloudOcrPolicyTests
{
    [Theory]
    [InlineData(OcrProvider.Baidu, 1000)]
    [InlineData(OcrProvider.Tencent, 500)]
    public void UsesConservativeFreeTierIntervals(OcrProvider provider, int milliseconds)
    {
        Assert.Equal(TimeSpan.FromMilliseconds(milliseconds), CloudOcrPolicy.MinimumInterval(provider));
    }

    [Fact]
    public void RecognizesBaiduQpsLimitError()
    {
        var error = new OcrException("百度 OCR 错误", "18");

        Assert.True(CloudOcrPolicy.IsRateLimit(OcrProvider.Baidu, error));
        Assert.False(CloudOcrPolicy.IsRateLimit(OcrProvider.Tencent, error));
    }

    [Fact]
    public void UsesBoundedRetryDelays()
    {
        Assert.Equal(
            [TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(8)],
            Enumerable.Range(1, CloudOcrPolicy.MaxAutomaticRetries).Select(CloudOcrPolicy.RetryDelay));
    }

    [Theory]
    [InlineData(OcrProvider.Baidu, "6", true)]
    [InlineData(OcrProvider.Baidu, "17", true)]
    [InlineData(OcrProvider.Baidu, "19", true)]
    [InlineData(OcrProvider.Baidu, "110", true)]
    [InlineData(OcrProvider.Baidu, "18", false)]
    [InlineData(OcrProvider.Tencent, "AuthFailure.SignatureFailure", true)]
    [InlineData(OcrProvider.Tencent, "UnauthorizedOperation", true)]
    [InlineData(OcrProvider.Tencent, "RequestLimitExceeded", false)]
    public void ClassifiesCredentialProblemsThatMaySwitchAccounts(
        OcrProvider provider, string code, bool expected)
    {
        var error = new OcrException("云 OCR 错误", code);

        Assert.Equal(expected, CloudOcrPolicy.IsCredentialProblem(provider, error));
    }
}
