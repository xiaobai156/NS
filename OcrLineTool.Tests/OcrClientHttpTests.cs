using System.Net;
using System.Text;
using OcrLineTool;

namespace OcrLineTool.Tests;

public sealed class OcrClientHttpTests
{
    [Fact]
    public async Task TencentRecognizeAsyncUsesInjectedHttpClient()
    {
        string imagePath = CreateTestImage();
        var handler = new FakeHttpMessageHandler((_, _) => Task.FromResult(JsonResponse("""
            {"Response":{"TextDetections":[{"DetectedText":"240期：腾讯测试"}]}}
            """)));

        try
        {
            using var httpClient = new HttpClient(handler);
            var client = new TencentOcrClient(CreateTencentCredential(), httpClient);

            Assert.Equal(["240期：腾讯测试"], await client.RecognizeAsync(imagePath));

            RequestSnapshot request = Assert.Single(handler.Requests);
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("ocr.tencentcloudapi.com", request.Uri!.Host);
            Assert.Equal("/", request.Uri.AbsolutePath);
            Assert.Contains("\"ImageBase64\":\"AQID\"", request.Body);
            Assert.Contains("\"EnableDetectSplit\":true", request.Body);
            Assert.Equal("GeneralAccurateOCR", request.Headers["X-TC-Action"]);
            Assert.StartsWith("TC3-HMAC-SHA256 Credential=TEST_SECRET_ID/", request.Headers["Authorization"]);
        }
        finally
        {
            File.Delete(imagePath);
        }
    }

    [Fact]
    public async Task TencentRecognizeAsyncMapsHttpFailure()
    {
        string imagePath = CreateTestImage();
        var handler = new FakeHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)));

        try
        {
            using var httpClient = new HttpClient(handler);
            var client = new TencentOcrClient(CreateTencentCredential(), httpClient);

            OcrException exception = await Assert.ThrowsAsync<OcrException>(
                () => client.RecognizeAsync(imagePath));

            Assert.Equal("腾讯云请求失败（HTTP 500）。", exception.Message);
        }
        finally
        {
            File.Delete(imagePath);
        }
    }

    [Fact]
    public async Task TencentRecognizeAsyncPreservesApiErrorCode()
    {
        string imagePath = CreateTestImage();
        var handler = new FakeHttpMessageHandler((_, _) => Task.FromResult(JsonResponse("""
            {"Response":{"Error":{"Code":"RequestLimitExceeded.Test","Message":"请求过于频繁"}}}
            """)));

        try
        {
            using var httpClient = new HttpClient(handler);
            var client = new TencentOcrClient(CreateTencentCredential(), httpClient);

            OcrException exception = await Assert.ThrowsAsync<OcrException>(
                () => client.RecognizeAsync(imagePath));

            Assert.Equal("RequestLimitExceeded.Test", exception.Code);
            Assert.Contains("请求过于频繁", exception.Message);
        }
        finally
        {
            File.Delete(imagePath);
        }
    }

    [Fact]
    public async Task TencentRecognizeAsyncObservesCancellationAndMapsConnectionFailure()
    {
        string imagePath = CreateTestImage();
        var started = NewSignal();
        var canceled = NewSignal();
        var handler = new FakeHttpMessageHandler(async (_, cancellationToken) =>
        {
            started.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                canceled.TrySetResult();
                throw;
            }

            throw new InvalidOperationException("The fake request should be canceled.");
        });

        try
        {
            using var httpClient = new HttpClient(handler);
            var client = new TencentOcrClient(CreateTencentCredential(), httpClient);
            using var cancellation = new CancellationTokenSource();
            Task<IReadOnlyList<string>> pending = client.RecognizeAsync(imagePath, cancellation.Token);

            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            cancellation.Cancel();

            OcrException exception = await Assert.ThrowsAsync<OcrException>(
                async () => await pending.WaitAsync(TimeSpan.FromSeconds(5)));
            await canceled.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Contains("腾讯云连接失败", exception.Message);
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
            Assert.Equal("/rest/2.0/ocr/v1/accurate_basic", ocrRequest.Uri!.AbsolutePath);
            Assert.Contains("access_token=TEST_TOKEN", ocrRequest.Uri.Query);
            Assert.Contains("language_type=CHN_ENG", ocrRequest.Body);
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
                request.Uri!.AbsolutePath == "/rest/2.0/ocr/v1/accurate_basic"));
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

    [Fact]
    public async Task BaiduMalformedTokenJsonMapsToConnectionFailure()
    {
        string imagePath = CreateTestImage();
        var handler = new FakeHttpMessageHandler((_, _) => Task.FromResult(JsonResponse("{")));

        try
        {
            using var httpClient = new HttpClient(handler);
            var client = new BaiduOcrClient(CreateBaiduCredential(), httpClient);

            OcrException exception = await Assert.ThrowsAsync<OcrException>(
                () => client.RecognizeAsync(imagePath));

            Assert.Equal("百度鉴权连接失败，请检查网络后重试。", exception.Message);
        }
        finally
        {
            File.Delete(imagePath);
        }
    }

    [Fact]
    public async Task BaiduMissingTokenMapsToUnknownAuthFailure()
    {
        string imagePath = CreateTestImage();
        var handler = new FakeHttpMessageHandler((_, _) => Task.FromResult(JsonResponse("{}")));

        try
        {
            using var httpClient = new HttpClient(handler);
            var client = new BaiduOcrClient(CreateBaiduCredential(), httpClient);

            OcrException exception = await Assert.ThrowsAsync<OcrException>(
                () => client.RecognizeAsync(imagePath));

            Assert.Equal("百度鉴权失败：未知错误", exception.Message);
        }
        finally
        {
            File.Delete(imagePath);
        }
    }

    [Fact]
    public async Task BaiduRecognizeAsyncMapsOcrHttpFailure()
    {
        string imagePath = CreateTestImage();
        var handler = new FakeHttpMessageHandler((request, _) =>
            request.RequestUri!.AbsolutePath == "/oauth/2.0/token"
                ? Task.FromResult(JsonResponse("""{"access_token":"TEST_TOKEN","expires_in":3600}"""))
                : Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));

        try
        {
            using var httpClient = new HttpClient(handler);
            var client = new BaiduOcrClient(CreateBaiduCredential(), httpClient);

            OcrException exception = await Assert.ThrowsAsync<OcrException>(
                () => client.RecognizeAsync(imagePath));

            Assert.Equal("百度 OCR 请求失败（HTTP 503）。", exception.Message);
        }
        finally
        {
            File.Delete(imagePath);
        }
    }

    [Fact]
    public async Task BaiduRecognizeAsyncPreservesOcrErrorCode()
    {
        string imagePath = CreateTestImage();
        var handler = new FakeHttpMessageHandler((request, _) =>
            request.RequestUri!.AbsolutePath == "/oauth/2.0/token"
                ? Task.FromResult(JsonResponse("""{"access_token":"TEST_TOKEN","expires_in":3600}"""))
                : Task.FromResult(JsonResponse("""{"error_code":18,"error_msg":"qps"}""")));

        try
        {
            using var httpClient = new HttpClient(handler);
            var client = new BaiduOcrClient(CreateBaiduCredential(), httpClient);

            OcrException exception = await Assert.ThrowsAsync<OcrException>(
                () => client.RecognizeAsync(imagePath));

            Assert.Equal("18", exception.Code);
            Assert.Contains("qps", exception.Message);
        }
        finally
        {
            File.Delete(imagePath);
        }
    }

    [Fact]
    public async Task ReadImageAsyncRejectsMissingFile()
    {
        string imagePath = Path.Combine(Path.GetTempPath(), "ocr-http-missing-" + Guid.NewGuid().ToString("N") + ".png");

        OcrException exception = await Assert.ThrowsAsync<OcrException>(
            () => OcrHttp.ReadImageAsync(imagePath, 100, CancellationToken.None));

        Assert.Equal("图片不存在。", exception.Message);
    }

    [Fact]
    public async Task ReadImageAsyncRejectsEmptyFile()
    {
        string imagePath = Path.Combine(Path.GetTempPath(), "ocr-http-empty-" + Guid.NewGuid().ToString("N") + ".png");
        File.WriteAllBytes(imagePath, []);

        try
        {
            OcrException exception = await Assert.ThrowsAsync<OcrException>(
                () => OcrHttp.ReadImageAsync(imagePath, 100, CancellationToken.None));

            Assert.Contains("1 字节到", exception.Message);
        }
        finally
        {
            File.Delete(imagePath);
        }
    }

    [Fact]
    public void BuildsDeterministicTencentAuthorizationForFixedTimestamp()
    {
        using var httpClient = new HttpClient(new FakeHttpMessageHandler((_, _) =>
            Task.FromResult(JsonResponse("{}"))));
        var client = new TencentOcrClient(CreateTencentCredential(), httpClient);
        const string payload = "{\"ImageBase64\":\"AQID\",\"EnableDetectSplit\":true,\"ConfigID\":\"OCR\",\"WordsType\":\"2\"}";

        string authorization = client.BuildAuthorization(payload, 1_725_000_000);

        Assert.Equal(
            "TC3-HMAC-SHA256 Credential=TEST_SECRET_ID/2024-08-30/ocr/tc3_request, " +
            "SignedHeaders=content-type;host;x-tc-action, " +
            "Signature=e203aed392236e1c8d8b6072409abb0269215fef41e6ed7c1612c1463055fe56",
            authorization);
    }

    private static OcrCredential CreateTencentCredential() =>
        new(OcrProvider.Tencent, "TEST", "TEST_SECRET_ID", "TEST_SECRET_KEY");

    private static OcrCredential CreateBaiduCredential() =>
        new(OcrProvider.Baidu, "TEST", "TEST_BAIDU_ID", "TEST_BAIDU_SECRET");

    private static string CreateTestImage()
    {
        string path = Path.Combine(Path.GetTempPath(), "ocr-http-image-" + Guid.NewGuid().ToString("N") + ".png");
        File.WriteAllBytes(path, [1, 2, 3]);
        return path;
    }

    private static TaskCompletionSource NewSignal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static HttpResponseMessage JsonResponse(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

    private sealed class FakeHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder) : HttpMessageHandler
    {
        public List<RequestSnapshot> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            string body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(CancellationToken.None);
            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, IEnumerable<string>> header in request.Headers)
                headers[header.Key] = string.Join(",", header.Value);
            if (request.Content is not null)
            {
                foreach (KeyValuePair<string, IEnumerable<string>> header in request.Content.Headers)
                    headers[header.Key] = string.Join(",", header.Value);
            }

            Requests.Add(new RequestSnapshot(request.Method, request.RequestUri, body, headers));
            return await responder(request, cancellationToken);
        }
    }

    private sealed record RequestSnapshot(
        HttpMethod Method,
        Uri? Uri,
        string Body,
        IReadOnlyDictionary<string, string> Headers);
}
