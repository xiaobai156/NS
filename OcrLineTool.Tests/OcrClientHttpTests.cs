using System.Net;
using System.Text;

namespace OcrLineTool.Tests;

public sealed class OcrClientHttpTests
{
    [Fact]
    public async Task TencentRecognizeAsyncUsesInjectedHttpClient()
    {
        string imagePath = CreateTestImage();
        var handler = new FakeHttpMessageHandler((_, _) => Task.FromResult(JsonResponse(
            """{"Response":{"TextDetections":[{"DetectedText":"240期：腾讯测试"}]}}""")));

        try
        {
            using var httpClient = new HttpClient(handler);
            var client = new TencentOcrClient(CreateTencentCredential(), httpClient);

            Assert.Equal(["240期：腾讯测试"], await client.RecognizeAsync(imagePath));
            RequestSnapshot request = Assert.Single(handler.Requests);
            Assert.Equal("ocr.tencentcloudapi.com", request.Uri!.Host);
            Assert.Contains("Authorization", request.Headers.Keys);
            Assert.Contains("X-TC-Action", request.Headers.Keys);
        }
        finally
        {
            File.Delete(imagePath);
        }
    }

    [Fact]
    public async Task TencentRecognizeEvidenceRejectsImageReplacementDuringRequest()
    {
        string imagePath = CreateTestImage();
        var pending = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new FakeHttpMessageHandler(async (_, cancellationToken) =>
        {
            await pending.Task.WaitAsync(cancellationToken);
            return JsonResponse("""{"Response":{"TextDetections":[{"DetectedText":"240期：腾讯测试"}]}}""");
        });

        try
        {
            using var httpClient = new HttpClient(handler);
            var client = new TencentOcrClient(CreateTencentCredential(), httpClient);
            Task<OcrEvidence> recognition = client.RecognizeEvidenceAsync(imagePath);
            await WaitForRequestsAsync(handler, 1);
            File.WriteAllBytes(imagePath, [9, 9, 9, 9]);
            pending.TrySetResult(true);

            OcrException exception = await Assert.ThrowsAsync<OcrException>(() => recognition);
            Assert.Equal("OCR_IMAGE_CHANGED", exception.Code);
        }
        finally
        {
            pending.TrySetResult(true);
            File.Delete(imagePath);
        }
    }

    [Fact]
    public async Task TencentRateLimitErrorKeepsProviderCode()
    {
        string imagePath = CreateTestImage();
        var handler = new FakeHttpMessageHandler((_, _) => Task.FromResult(JsonResponse(
            """{"Response":{"Error":{"Code":"RequestLimitExceeded.RateLimitExceeded","Message":"slow down"}}}""")));

        try
        {
            using var httpClient = new HttpClient(handler);
            var client = new TencentOcrClient(CreateTencentCredential(), httpClient);

            OcrException exception = await Assert.ThrowsAsync<OcrException>(() => client.RecognizeAsync(imagePath));
            Assert.Equal("RequestLimitExceeded.RateLimitExceeded", exception.Code);
        }
        finally
        {
            File.Delete(imagePath);
        }
    }

    [Fact]
    public async Task TencentCancellationDoesNotBecomeConnectionFailure()
    {
        string imagePath = CreateTestImage();
        var canceled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new FakeHttpMessageHandler(async (_, cancellationToken) =>
        {
            try { await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); }
            finally { canceled.TrySetResult(true); }
            throw new InvalidOperationException();
        });

        try
        {
            using var httpClient = new HttpClient(handler);
            var client = new TencentOcrClient(CreateTencentCredential(), httpClient);
            using var cts = new CancellationTokenSource();
            Task<IReadOnlyList<string>> pending = client.RecognizeAsync(imagePath, cts.Token);
            cts.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
                await pending.WaitAsync(TimeSpan.FromSeconds(5)));
            await canceled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            File.Delete(imagePath);
        }
    }

    [Fact]
    public async Task BaiduRecognizeAsyncGetsTokenAndUsesInjectedHttpClient()
    {
        string imagePath = CreateTestImage();
        var handler = new FakeHttpMessageHandler((request, _) =>
            request.RequestUri!.AbsolutePath == "/oauth/2.0/token"
                ? Task.FromResult(JsonResponse("""{"access_token":"TEST_TOKEN","expires_in":3600}"""))
                : Task.FromResult(JsonResponse("""{"words_result":[{"words":"240期：百度测试"}]}""")));

        try
        {
            using var httpClient = new HttpClient(handler);
            var client = new BaiduOcrClient(CreateBaiduCredential(), httpClient);

            Assert.Equal(["240期：百度测试"], await client.RecognizeAsync(imagePath));

            Assert.Equal(2, handler.Requests.Count);
            RequestSnapshot tokenRequest = handler.Requests[0];
            RequestSnapshot ocrRequest = handler.Requests[1];
            Assert.Equal("/oauth/2.0/token", tokenRequest.Uri!.AbsolutePath);
            Assert.Contains("client_id=TEST_BAIDU_ID", tokenRequest.Body);
            Assert.Contains("client_secret=TEST_BAIDU_SECRET", tokenRequest.Body);
            Assert.Equal("/rest/2.0/ocr/v1/accurate", ocrRequest.Uri!.AbsolutePath);
            Assert.Contains("access_token=TEST_TOKEN", ocrRequest.Uri.Query);
            Assert.Contains("language_type=CHN_ENG", ocrRequest.Body);
            Assert.Contains("probability=true", ocrRequest.Body);
        }
        finally
        {
            File.Delete(imagePath);
        }
    }

    [Fact]
    public async Task BaiduRecognizeAsyncReusesAnUnexpiredToken()
    {
        string imagePath = CreateTestImage();
        var handler = new FakeHttpMessageHandler((request, _) =>
            request.RequestUri!.AbsolutePath == "/oauth/2.0/token"
                ? Task.FromResult(JsonResponse("""{"access_token":"TEST_TOKEN","expires_in":3600}"""))
                : Task.FromResult(JsonResponse("""{"words_result":[{"words":"缓存测试"}]}""")));

        try
        {
            using var httpClient = new HttpClient(handler);
            var client = new BaiduOcrClient(CreateBaiduCredential(), httpClient);

            await client.RecognizeAsync(imagePath);
            await client.RecognizeAsync(imagePath);

            Assert.Equal(1, handler.Requests.Count(request =>
                request.Uri!.AbsolutePath == "/oauth/2.0/token"));
            Assert.Equal(2, handler.Requests.Count(request =>
                request.Uri!.AbsolutePath == "/rest/2.0/ocr/v1/accurate"));
        }
        finally
        {
            File.Delete(imagePath);
        }
    }

    [Fact]
    public async Task BaiduTokenHttpFailurePreservesCurrentAuthErrorBehavior()
    {
        string imagePath = CreateTestImage();
        var handler = new FakeHttpMessageHandler((_, _) => Task.FromResult(
            JsonResponse("""{"error_description":"token rejected"}""", HttpStatusCode.InternalServerError)));

        try
        {
            using var httpClient = new HttpClient(handler);
            var client = new BaiduOcrClient(CreateBaiduCredential(), httpClient);

            OcrException exception = await Assert.ThrowsAsync<OcrException>(
                () => client.RecognizeAsync(imagePath));

            Assert.Equal("百度鉴权失败：token rejected", exception.Message);
            Assert.Single(handler.Requests);
        }
        finally
        {
            File.Delete(imagePath);
        }
    }

    private static async Task WaitForRequestsAsync(FakeHttpMessageHandler handler, int count)
    {
        for (int attempt = 0; attempt < 100 && handler.Requests.Count < count; attempt++)
            await Task.Delay(10);
        Assert.True(handler.Requests.Count >= count);
    }

    private static OcrCredential CreateTencentCredential() =>
        new(OcrProvider.Tencent, "测试腾讯", "TEST_ID", "TEST_SECRET");

    private static OcrCredential CreateBaiduCredential() =>
        new(OcrProvider.Baidu, "测试百度", "TEST_BAIDU_ID", "TEST_BAIDU_SECRET");

    private static string CreateTestImage()
    {
        string path = Path.Combine(Path.GetTempPath(), $"ocr-client-{Guid.NewGuid():N}.png");
        File.WriteAllBytes(path, [1, 2, 3, 4]);
        return path;
    }

    private static HttpResponseMessage JsonResponse(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private sealed class FakeHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> callback) : HttpMessageHandler
    {
        internal List<RequestSnapshot> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            string body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new(
                request.RequestUri,
                body,
                request.Headers.ToDictionary(pair => pair.Key, pair => string.Join(",", pair.Value), StringComparer.OrdinalIgnoreCase)));
            return await callback(request, cancellationToken);
        }
    }

    private sealed record RequestSnapshot(
        Uri? Uri,
        string Body,
        IReadOnlyDictionary<string, string> Headers);
}
