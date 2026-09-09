using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using OcrLineTool;

namespace OcrLineTool.Tests;

public sealed class PaddleProcessTests
{
    [Theory]
    [InlineData(PaddleOcrModel.Small, "small")]
    [InlineData(PaddleOcrModel.Medium, "medium")]
    public async Task RecognizeBatchAsyncUsesInjectedProcessAndCurrentModelArguments(
        PaddleOcrModel model,
        string modelArgument)
    {
        string imagePath = CreateImagePath();
        string cachePath = CreateTempPath("cache", ".json");
        var progress = new List<LocalOcrProgress>();
        var runner = new FakeProcessRunner((startInfo, reportOutput, _) =>
        {
            WriteResult(startInfo, imagePath, ["识别结果"]);
            reportOutput?.Invoke($"OCR_PROGRESS|1|1|{imagePath}");
            return Task.FromResult(new ProcessResult(true, 0, "", "Paddle warning"));
        });

        try
        {
            var client = new PaddleLocalOcrClient(runner, cachePath);

            IReadOnlyDictionary<string, IReadOnlyList<string>> result =
                await client.RecognizeBatchAsync(
                    [imagePath],
                    new Progress<LocalOcrProgress>(progress.Add),
                    titleRatio: 0.4,
                    detectionMaxSide: 960,
                    useCache: false,
                    model: model);

            Assert.Equal(["识别结果"], result[imagePath]);
            ProcessStartInfo request = Assert.Single(runner.Requests);
            Assert.Contains("paddle_local_ocr.py", request.Arguments, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("--device gpu:0", request.Arguments);
            Assert.Contains($"--model {modelArgument}", request.Arguments);
            Assert.Contains("--cpu-threads 6", request.Arguments);
            Assert.Contains("--top-ratio 0.4", request.Arguments);
            Assert.Contains("--det-max-side 960", request.Arguments);
            Assert.Contains("--compact-folder 杰少", request.Arguments);
            Assert.Contains(progress, item => item.Completed == 1 && item.Total == 1 && item.Path == imagePath);
        }
        finally
        {
            DeleteIfExists(imagePath);
            DeleteIfExists(cachePath);
        }
    }

    [Fact]
    public async Task RecognizeBatchAsyncMapsNonZeroProcessExitWithoutOutput()
    {
        string imagePath = CreateImagePath();
        var runner = new FakeProcessRunner((_, _, _) =>
            Task.FromResult(new ProcessResult(true, 7, "", "python failed")));

        try
        {
            var client = new PaddleLocalOcrClient(runner, CreateTempPath("cache", ".json"));

            OcrException exception = await Assert.ThrowsAsync<OcrException>(() =>
                client.RecognizeBatchAsync([imagePath], useCache: false));

            Assert.Equal("PaddleOCR 执行失败（代码 7）。", exception.Message);
            Assert.NotEqual(PaddleLocalOcrClient.CudaUnavailableCode, exception.Code);
        }
        finally
        {
            DeleteIfExists(imagePath);
        }
    }

    [Fact]
    public async Task RecognizeBatchAsyncRejectsNonZeroExitEvenWhenOutputExists()
    {
        string imagePath = CreateImagePath();
        var runner = new FakeProcessRunner((startInfo, _, _) =>
        {
            WriteResult(startInfo, imagePath, ["不应使用"]);
            return Task.FromResult(new ProcessResult(true, 7, "", "python failed"));
        });
        try
        {
            var client = new PaddleLocalOcrClient(runner, CreateTempPath("cache", ".json"));
            OcrException exception = await Assert.ThrowsAsync<OcrException>(() =>
                client.RecognizeBatchAsync([imagePath], useCache: false));
            Assert.Equal("PaddleOCR 执行失败（代码 7）。", exception.Message);
        }
        finally { DeleteIfExists(imagePath); }
    }

    [Fact]
    public async Task RecognizeBatchAsyncRejectsMalformedOutputJson()
    {
        string imagePath = CreateImagePath();
        var runner = new FakeProcessRunner((startInfo, _, _) =>
        {
            File.WriteAllText(OutputPath(startInfo), "{");
            return Task.FromResult(new ProcessResult(true, 0, "", ""));
        });

        try
        {
            var client = new PaddleLocalOcrClient(runner, CreateTempPath("cache", ".json"));

            OcrException exception = await Assert.ThrowsAsync<OcrException>(() =>
                client.RecognizeBatchAsync([imagePath], useCache: false));

            Assert.Equal("PaddleOCR 返回格式异常。", exception.Message);
        }
        finally
        {
            DeleteIfExists(imagePath);
        }
    }

    [Fact]
    public async Task RecognizeBatchAsyncKeepsCurrentEmptyResultsBehaviorWhenResultsFieldIsMissing()
    {
        string imagePath = CreateImagePath();
        var runner = new FakeProcessRunner((startInfo, _, _) =>
        {
            File.WriteAllText(OutputPath(startInfo), "{}");
            return Task.FromResult(new ProcessResult(true, 0, "", ""));
        });

        try
        {
            var client = new PaddleLocalOcrClient(runner, CreateTempPath("cache", ".json"));

            OcrException exception = await Assert.ThrowsAsync<OcrException>(() =>
                client.RecognizeBatchAsync([imagePath], useCache: false));
            Assert.Equal("PaddleOCR 返回结果缺少 results。", exception.Message);
        }
        finally
        {
            DeleteIfExists(imagePath);
        }
    }

    [Fact]
    public async Task RecognizeBatchAsyncSkipsPerImageErrorAndKeepsOtherImages()
    {
        string imagePath = CreateImagePath();
        string otherImagePath = CreateImagePath();
        var runner = new FakeProcessRunner((startInfo, _, _) =>
        {
            File.WriteAllText(OutputPath(startInfo), $"{{\"results\":[{{\"path\":\"{imagePath.Replace("\\", "\\\\")}\",\"error\":\"图片处理失败\"}},{{\"path\":\"{otherImagePath.Replace("\\", "\\\\")}\",\"texts\":[\"250期测试\"]}}]}}");
            return Task.FromResult(new ProcessResult(true, 0, "", ""));
        });
        try
        {
            var client = new PaddleLocalOcrClient(runner, CreateTempPath("cache", ".json"));
            IReadOnlyDictionary<string, IReadOnlyList<string>> result = await client.RecognizeBatchAsync([imagePath, otherImagePath], useCache: false);
            Assert.DoesNotContain(imagePath, result.Keys);
            Assert.Equal(new[] { "250期测试" }, result[otherImagePath]);
        }
        finally { DeleteIfExists(imagePath); DeleteIfExists(otherImagePath); }
    }

    [Fact]
    public async Task RecognizeBatchAsyncMapsCudaErrorFromProcessResult()
    {
        string imagePath = CreateImagePath();
        var runner = new FakeProcessRunner((startInfo, _, _) =>
        {
            WriteError(startInfo, "无法初始化 NVIDIA CUDA PaddleOCR：无法启用 NVIDIA GPU 0");
            return Task.FromResult(new ProcessResult(true, 0, "", ""));
        });

        try
        {
            var client = new PaddleLocalOcrClient(runner, CreateTempPath("cache", ".json"));

            OcrException exception = await Assert.ThrowsAsync<OcrException>(() =>
                client.RecognizeBatchAsync([imagePath], useCache: false));

            Assert.Equal(PaddleLocalOcrClient.CudaUnavailableCode, exception.Code);
            Assert.True(PaddleLocalOcrClient.IsCudaUnavailable(exception));
        }
        finally
        {
            DeleteIfExists(imagePath);
        }
    }

    [Fact]
    public async Task EnsureCudaAvailableAsyncUsesInjectedProcess()
    {
        var runner = new FakeProcessRunner((_, _, _) =>
            Task.FromResult(new ProcessResult(true, 0, "CUDA available", "")));
        var client = new PaddleLocalOcrClient(runner, CreateTempPath("cache", ".json"));

        await client.EnsureCudaAvailableAsync();

        ProcessStartInfo request = Assert.Single(runner.Requests);
        Assert.Contains("检查NVIDIA-CUDA环境.py", request.Arguments);
        Assert.Equal("utf-8", request.Environment["PYTHONIOENCODING"]);
        Assert.Equal(AppContext.BaseDirectory, request.WorkingDirectory);
    }

    [Fact]
    public async Task EnsureCudaAvailableAsyncMapsUnavailableExit()
    {
        var runner = new FakeProcessRunner((_, _, _) =>
            Task.FromResult(new ProcessResult(true, 1, "", "CUDA unavailable")));
        var client = new PaddleLocalOcrClient(runner, CreateTempPath("cache", ".json"));

        OcrException exception = await Assert.ThrowsAsync<OcrException>(
            () => client.EnsureCudaAvailableAsync());

        Assert.Equal(PaddleLocalOcrClient.CudaUnavailableCode, exception.Code);
        Assert.Contains("未检测到可用的 NVIDIA CUDA 设备", exception.Message);
    }

    [Fact]
    public async Task EnsureCudaAvailableAsyncMapsProcessStartFailure()
    {
        var runner = new FakeProcessRunner((_, _, _) =>
            Task.FromResult(new ProcessResult(false, -1, "", "")));
        var client = new PaddleLocalOcrClient(runner, CreateTempPath("cache", ".json"));

        OcrException exception = await Assert.ThrowsAsync<OcrException>(
            () => client.EnsureCudaAvailableAsync());

        Assert.Equal(PaddleLocalOcrClient.CudaUnavailableCode, exception.Code);
        Assert.Equal("无法启动 NVIDIA CUDA 自检。", exception.Message);
    }

    [Fact]
    public async Task EnsureCudaAvailableAsyncMapsMissingPython()
    {
        var runner = new FakeProcessRunner((_, _, _) =>
            throw new Win32Exception("python not found"));
        var client = new PaddleLocalOcrClient(runner, CreateTempPath("cache", ".json"));

        OcrException exception = await Assert.ThrowsAsync<OcrException>(
            () => client.EnsureCudaAvailableAsync());

        Assert.Equal(PaddleLocalOcrClient.CudaUnavailableCode, exception.Code);
        Assert.Contains("未找到 Python", exception.Message);
    }

    [Fact]
    public async Task RecognizeBatchAsyncPropagatesProcessCancellation()
    {
        string imagePath = CreateImagePath();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var canceled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var runner = new FakeProcessRunner(async (_, _, cancellationToken) =>
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

            throw new InvalidOperationException("The fake process should be canceled.");
        });

        try
        {
            var client = new PaddleLocalOcrClient(runner, CreateTempPath("cache", ".json"));
            using var cancellation = new CancellationTokenSource();
            Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> pending = client.RecognizeBatchAsync(
                [imagePath],
                useCache: false,
                cancellationToken: cancellation.Token);

            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            cancellation.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                async () => await pending.WaitAsync(TimeSpan.FromSeconds(5)));
            await canceled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            DeleteIfExists(imagePath);
        }
    }

    [Fact]
    public async Task RecognizeBatchAsyncUsesValidCacheWithoutStartingProcess()
    {
        string imagePath = CreateImagePath();
        string cachePath = CreateTempPath("cache", ".json");
        string key = CacheKey(imagePath, 1.0, null, PaddleOcrModel.Small);
        File.WriteAllText(cachePath, JsonSerializer.Serialize(new[]
        {
            new { key, texts = new[] { "缓存结果" } }
        }));
        var runner = new FakeProcessRunner((_, _, _) =>
            throw new InvalidOperationException("A cache hit must not start Python."));

        try
        {
            var client = new PaddleLocalOcrClient(runner, cachePath);

            IReadOnlyDictionary<string, IReadOnlyList<string>> result =
                await client.RecognizeBatchAsync([imagePath]);

            Assert.Equal(["缓存结果"], result[imagePath]);
            Assert.Empty(runner.Requests);
        }
        finally
        {
            DeleteIfExists(imagePath);
            DeleteIfExists(cachePath);
        }
    }

    [Fact]
    public async Task RecognizeBatchAsyncReprocessesAfterCorruptCache()
    {
        string imagePath = CreateImagePath();
        string cachePath = CreateTempPath("cache", ".json");
        File.WriteAllText(cachePath, "{");
        var runner = new FakeProcessRunner((startInfo, _, _) =>
        {
            WriteResult(startInfo, imagePath, ["重新识别"]);
            return Task.FromResult(new ProcessResult(true, 0, "", ""));
        });

        try
        {
            var client = new PaddleLocalOcrClient(runner, cachePath);

            IReadOnlyDictionary<string, IReadOnlyList<string>> result =
                await client.RecognizeBatchAsync([imagePath]);

            Assert.Equal(["重新识别"], result[imagePath]);
            Assert.Single(runner.Requests);
            Assert.True(File.Exists(cachePath));
        }
        finally
        {
            DeleteIfExists(imagePath);
            DeleteIfExists(cachePath);
        }
    }

    [Fact]
    public async Task RecognizeBatchAsyncCreatesCacheAfterProcessResult()
    {
        string imagePath = CreateImagePath();
        string cachePath = CreateTempPath("cache", ".json");
        var runner = new FakeProcessRunner((startInfo, _, _) =>
        {
            WriteResult(startInfo, imagePath, ["写入缓存"]);
            return Task.FromResult(new ProcessResult(true, 0, "", ""));
        });

        try
        {
            var client = new PaddleLocalOcrClient(runner, cachePath);

            await client.RecognizeBatchAsync([imagePath]);

            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(cachePath));
            Assert.Contains(document.RootElement.EnumerateArray(), item =>
                item.GetProperty("Texts")[0].GetString() == "写入缓存");
        }
        finally
        {
            DeleteIfExists(imagePath);
            DeleteIfExists(cachePath);
        }
    }

    [Fact]
    public void BuildsExpectedCacheKeyForEachModel()
    {
        string imagePath = CreateImagePath();

        try
        {
            string small = CacheKey(imagePath, 1.0, null, PaddleOcrModel.Small);
            string medium = CacheKey(imagePath, 1.0, null, PaddleOcrModel.Medium);

            Assert.DoesNotContain("model=", small);
            Assert.Contains("model=medium", medium);
            Assert.NotEqual(small, medium);
        }
        finally
        {
            DeleteIfExists(imagePath);
        }
    }

    private static string CreateImagePath()
    {
        string path = CreateTempPath("paddle-image", ".png");
        File.WriteAllBytes(path, [1, 2, 3]);
        return path;
    }

    private static string CreateTempPath(string prefix, string extension) =>
        Path.Combine(Path.GetTempPath(), $"{prefix}-{Guid.NewGuid():N}{extension}");

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
            File.Delete(path);
    }

    private static string OutputPath(ProcessStartInfo startInfo) =>
        ArgumentValue(startInfo.Arguments, "--output");

    private static string ArgumentValue(string arguments, string name)
    {
        string prefix = name + " \"";
        int start = arguments.IndexOf(prefix, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Missing {name} argument: {arguments}");
        start += prefix.Length;
        int end = arguments.IndexOf('"', start);
        Assert.True(end >= start, $"Unterminated {name} argument: {arguments}");
        return arguments[start..end].Replace("\\\"", "\"");
    }

    private static void WriteResult(ProcessStartInfo startInfo, string imagePath, string[] texts)
    {
        string json = JsonSerializer.Serialize(new
        {
            results = new[] { new { path = imagePath, texts } }
        });
        File.WriteAllText(OutputPath(startInfo), json);
    }

    private static void WriteError(ProcessStartInfo startInfo, string message)
    {
        File.WriteAllText(OutputPath(startInfo), JsonSerializer.Serialize(new { error = message }));
    }

    private static string CacheKey(string imagePath, double titleRatio, int? detectionMaxSide, PaddleOcrModel model)
    {
        MethodInfo method = typeof(PaddleLocalOcrClient).GetMethod(
            "CacheKeyForModel",
            BindingFlags.NonPublic | BindingFlags.Static)!;
        return (string)method.Invoke(null, [imagePath, titleRatio, detectionMaxSide, model])!;
    }

    private sealed class FakeProcessRunner(
        Func<ProcessStartInfo, Action<string>?, CancellationToken, Task<ProcessResult>> handler) : IProcessRunner
    {
        public List<ProcessStartInfo> Requests { get; } = [];

        public async Task<ProcessResult> RunAsync(
            ProcessStartInfo startInfo,
            Action<string>? reportStandardOutputLine,
            CancellationToken cancellationToken)
        {
            Requests.Add(startInfo);
            return await handler(startInfo, reportStandardOutputLine, cancellationToken);
        }
    }
}
