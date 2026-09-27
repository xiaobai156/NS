using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using OcrLineTool;

namespace OcrLineTool.Tests;

public sealed class PaddleProcessTests
{
    [Theory]
    [InlineData(LocalOcrDevice.Cpu)]
    [InlineData(LocalOcrDevice.Gpu)]
    public async Task CacheRollsOverWithoutRestartingClient(LocalOcrDevice device)
    {
        string image = CreateImagePath();
        string cache = CreateTempPath("rollover-cache", ".json");
        DateOnly today = new(2026, 9, 24);
        var runner = new FakeProcessRunner((info, _, _) =>
        {
            WriteResult(info, image, [today.ToString("yyyy-MM-dd")]);
            return Task.FromResult(new ProcessResult(true, 0, "", ""));
        });
        try
        {
            var client = new PaddleLocalOcrClient(runner, cache, device, () => today);
            await client.RecognizeBatchAsync([image]);
            await client.RecognizeBatchAsync([image]);
            Assert.Single(runner.Requests);
            today = today.AddDays(1);
            var result = await client.RecognizeBatchAsync([image]);
            Assert.Equal([today.ToString("yyyy-MM-dd")], result[image]);
            Assert.Equal(2, runner.Requests.Count);
            using var document = JsonDocument.Parse(File.ReadAllText(cache));
            Assert.Equal(today.ToString("yyyy-MM-dd"),
                Assert.Single(document.RootElement.EnumerateArray()).GetProperty("Date").GetString());
        }
        finally { DeleteIfExists(image); DeleteIfExists(cache); }
    }

    [Fact]
    public async Task BatchFinishingAfterMidnightDoesNotReviveYesterdayCache()
    {
        string image = CreateImagePath();
        string cache = CreateTempPath("midnight-cache", ".json");
        DateOnly today = new(2026, 9, 24);
        var runner = new FakeProcessRunner((info, _, _) =>
        {
            WriteResult(info, image, ["completed"]);
            today = today.AddDays(1);
            return Task.FromResult(new ProcessResult(true, 0, "", ""));
        });
        try
        {
            var client = new PaddleLocalOcrClient(runner, cache, currentDate: () => today);
            Assert.Equal(["completed"], (await client.RecognizeBatchAsync([image]))[image]);
            using var document = JsonDocument.Parse(File.ReadAllText(cache));
            Assert.Empty(document.RootElement.EnumerateArray());
            await client.RecognizeBatchAsync([image]);
            Assert.Equal(2, runner.Requests.Count);
        }
        finally { DeleteIfExists(image); DeleteIfExists(cache); }
    }

    [Fact]
    public async Task OldCacheEntryIsNotReusedEvenWhenFileWasTouchedToday()
    {
        string imagePath = CreateImagePath();
        string cachePath = CreateTempPath("dated-cache", ".json");
        string key = CacheKey(imagePath, 1.0, null, PaddleOcrModel.Small);
        File.WriteAllText(cachePath, JsonSerializer.Serialize(new[]
        {
            new { Key = key, Texts = new[] { "old" }, Date = CredentialSchedule.TodayInBeijing().AddDays(-1) }
        }));
        var runner = new FakeProcessRunner((startInfo, _, _) =>
        {
            WriteResult(startInfo, imagePath, ["fresh"]);
            return Task.FromResult(new ProcessResult(true, 0, "", ""));
        });
        try
        {
            var client = new PaddleLocalOcrClient(runner, cachePath);
            var result = await client.RecognizeBatchAsync([imagePath]);
            Assert.Equal(["fresh"], result[imagePath]);
            Assert.Single(runner.Requests);
        }
        finally { DeleteIfExists(imagePath); DeleteIfExists(cachePath); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CacheCleanupUsesEntryDatesNotFileModificationTime(bool includeToday)
    {
        string cachePath = CreateTempPath("dated-cleanup", ".json");
        DateOnly today = CredentialSchedule.TodayInBeijing();
        var entries = new[]
        {
            new { Key = "old", Texts = new[] { "old" }, Date = today.AddDays(-1) },
            new { Key = "today", Texts = new[] { "today" }, Date = today }
        };
        File.WriteAllText(cachePath, JsonSerializer.Serialize(entries.Take(includeToday ? 2 : 1)));
        try
        {
            PaddleLocalOcrClient.ClearStaleCacheIfNeeded(cachePath, today);
            if (includeToday)
            {
                using var document = JsonDocument.Parse(File.ReadAllText(cachePath));
                Assert.Equal("today", Assert.Single(document.RootElement.EnumerateArray()).GetProperty("Key").GetString());
            }
            else
                Assert.False(File.Exists(cachePath));
        }
        finally { DeleteIfExists(cachePath); }
    }

    [Fact]
    public void ImageDigestStaysIdenticalAndHonoursCancellation()
    {
        string file = CreateTempPath("identity-digest", ".bin");
        byte[] content = new byte[(2 * 1024 * 1024) + 17];
        Random.Shared.NextBytes(content);
        File.WriteAllBytes(file, content);
        try
        {
            // 分块读取必须与整文件一次读取得到同一个摘要：缓存身份不能变。
            Assert.Equal(
                Convert.ToHexString(SHA256.HashData(content)),
                LocalOcrIdentity.Image(file));

            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            Assert.ThrowsAny<OperationCanceledException>(() => LocalOcrIdentity.Image(file, cancelled.Token));
            Assert.ThrowsAny<OperationCanceledException>(() => LocalOcrIdentity.Pipeline(PaddleOcrModel.Small, cancelled.Token));
        }
        finally { DeleteIfExists(file); }
    }

    [Fact]
    public void ImageDigestUsesContentNotSizeOrModificationTime()
    {
        string first = CreateTempPath("identity-a", ".bin");
        string second = CreateTempPath("identity-b", ".bin");
        try
        {
            File.WriteAllBytes(first, [1, 2, 3, 4]);
            File.WriteAllBytes(second, [1, 2, 3, 5]);
            File.SetLastWriteTimeUtc(second, File.GetLastWriteTimeUtc(first));
            Assert.Equal(new FileInfo(first).Length, new FileInfo(second).Length);
            Assert.NotEqual(LocalOcrIdentity.Image(first), LocalOcrIdentity.Image(second));

            File.WriteAllBytes(second, [1, 2, 3, 4]);
            File.SetLastWriteTimeUtc(second, File.GetLastWriteTimeUtc(first).AddSeconds(30));
            Assert.Equal(LocalOcrIdentity.Image(first), LocalOcrIdentity.Image(second));
        }
        finally { DeleteIfExists(first); DeleteIfExists(second); }
    }

    [Fact]
    public void DigestCancellationIsHonouredBetweenChunksAndNotOnlyAtEntry()
    {
        using var source = new CancellationTokenSource();
        using var stream = new CancelAfterFirstChunkStream(source);

        // 入口时令牌仍有效：只有分块循环内部的检查才能让第二次读块前抛取消。
        Assert.ThrowsAny<OperationCanceledException>(() => LocalOcrIdentity.Image(stream, source.Token));

        // 入口就取消同样抛取消，绝不能返回一个摘要值。
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => LocalOcrIdentity.Image(stream, cancelled.Token));
    }

    [Fact]
    public async Task CancellationAfterRecognitionThrowsInsteadOfRecordingSingleImageErrors()
    {
        string image = CreateImagePath();
        string cache = CreateTempPath("cancel-after-recognition", ".json");
        using var source = new CancellationTokenSource();
        var runner = new FakeProcessRunner((info, _, _) =>
        {
            WriteBatchResult(info, new Dictionary<string, string> { [image] = "识别文字" });
            // 识别进程已返回、复核尚未开始：此时取消必须让整批抛取消，
            // 不能把这张图记成单图失败，也不能留下成功缓存。
            source.Cancel();
            return Task.FromResult(new ProcessResult(true, 0, "", ""));
        });
        try
        {
            var client = new PaddleLocalOcrClient(runner, cache);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.RecognizeBatchAsync(
                [image], null, titleRatio: 1.0, detectionMaxSide: null, useCache: true,
                cancellationToken: source.Token, model: PaddleOcrModel.Small));

            Assert.Empty(client.LastImageErrors);
            Assert.Empty(client.LastInvalidImagePaths);
            Assert.False(File.Exists(cache), "取消后不能写出成功缓存。");
        }
        finally { DeleteIfExists(image); DeleteIfExists(cache); }
    }

    [Fact]
    public void FingerprintAndInitialImageCheckRunInTheBackgroundWithTheBatchToken()
    {
        // 指纹是逐批一次的纯函数：同一模型两次一致，不同模型必须不同。
        Assert.Equal(LocalOcrIdentity.Pipeline(PaddleOcrModel.Small), LocalOcrIdentity.Pipeline(PaddleOcrModel.Small));
        Assert.NotEqual(LocalOcrIdentity.Pipeline(PaddleOcrModel.Small), LocalOcrIdentity.Pipeline(PaddleOcrModel.Medium));

        // 模型指纹与逐图初始校验都必须交给后台线程并带上取消令牌：界面不在缓存校验阶段同步等读盘。
        string source = File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "OcrLineTool.App", "PaddleLocalOcrClient.cs"));
        string compact = string.Join(' ', source.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        Assert.Contains("await Task.Run( () => LocalOcrIdentity.Pipeline(model, cancellationToken), cancellationToken);", compact);
        Assert.DoesNotContain("LocalOcrIdentity.Pipeline(model);", compact);
        Assert.Contains("pending.Add(path); } }, cancellationToken);", compact);

        // 识别后的身份复核与证据构造同样在后台并带令牌，且写缓存前再确认一次取消。
        Assert.Contains("await Task.Run(() => { foreach (PaddleResult result in response.Results)", compact);
        Assert.Contains(
            "cancellationToken.ThrowIfCancellationRequested(); if (useCache) await WriteCacheAsync(cache, cancellationToken);",
            compact);
    }

    [Fact]
    public async Task CancelledBatchStopsBeforeStartingAnyWorker()
    {
        string image = CreateImagePath();
        string cache = CreateTempPath("cancelled-cache", ".json");
        var runner = new FakeProcessRunner((_, _, _) => Task.FromResult(new ProcessResult(true, 0, "", "")));
        try
        {
            var client = new PaddleLocalOcrClient(runner, cache);
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => client.RecognizeBatchAsync([image], cancellationToken: cancelled.Token));
            Assert.Empty(runner.Requests);
        }
        finally { DeleteIfExists(image); DeleteIfExists(cache); }
    }

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
            Assert.Equal(PaddleLocalOcrClient.CudaUnavailableCode, exception.Code);
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
            new { key, texts = new[] { "缓存结果" }, date = CredentialSchedule.TodayInBeijing() }
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
    public async Task WorkerTimingIsRecordedFromTheBatchResultAndStaysOptional()
    {
        string image = CreateImagePath();
        string cache = CreateTempPath("worker-timing-cache", ".json");
        string legacyCache = CreateTempPath("worker-timing-legacy", ".json");
        var runner = new FakeProcessRunner((startInfo, _, _) =>
        {
            File.WriteAllText(OutputPath(startInfo), JsonSerializer.Serialize(new
            {
                results = new[] { new { path = image, texts = new[] { "第269期" } } },
                timing = new
                {
                    paddle_import_seconds = 1.5,
                    device_seconds = 0.25,
                    paddleocr_import_seconds = 2.0,
                    model_staging_seconds = 0.75,
                    paddle_init_seconds = 6.5,
                    inference_seconds = 3.25,
                    worker_seconds_before_output = 14.0,
                    image_count = 1,
                    view_count = 1
                }
            }));
            return Task.FromResult(new ProcessResult(true, 0, "", ""));
        });
        try
        {
            var client = new PaddleLocalOcrClient(runner, cache);
            await client.RecognizeBatchAsync([image]);

            PaddleTiming timing = Assert.Single(client.WorkerTimings);
            Assert.Equal(0.75, timing.ModelStagingSeconds);
            Assert.Equal(6.5, timing.PaddleInitSeconds);
            Assert.Equal(14.0, timing.WorkerSecondsBeforeOutput);
            Assert.Equal(1, timing.ImageCount);

            // 没有 timing 的返回（旧版 worker）：不报错，也不记伪数据。
            var legacy = new PaddleLocalOcrClient(
                new FakeProcessRunner((startInfo, _, _) =>
                {
                    WriteBatchResult(startInfo, new Dictionary<string, string> { [image] = "第269期" });
                    return Task.FromResult(new ProcessResult(true, 0, "", ""));
                }),
                legacyCache);
            await legacy.RecognizeBatchAsync([image]);
            Assert.Empty(legacy.WorkerTimings);
        }
        finally
        {
            DeleteIfExists(image);
            DeleteIfExists(cache);
            DeleteIfExists(legacyCache);
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

    [Theory]
    [InlineData("start")]
    [InlineData("win32")]
    [InlineData("exit")]
    [InlineData("import")]
    [InlineData("missing")]
    [InlineData("malformed")]
    [InlineData("null")]
    [InlineData("shape")]
    [InlineData("null-item")]
    [InlineData("unknown-path")]
    [InlineData("invalid-exit-json")]
    public async Task InfrastructureErrorsAreFatalOnlyInGpuMode(string failure)
    {
        string image = CreateImagePath();
        try
        {
            foreach (LocalOcrDevice device in Enum.GetValues<LocalOcrDevice>())
            {
                var runner = new FakeProcessRunner((info, _, _) =>
                {
                    if (failure == "win32") throw new Win32Exception();
                    if (failure == "import") WriteError(info, "无法加载 PaddleOCR：DLL load failed");
                    if (failure is "malformed" or "null" or "shape")
                        File.WriteAllText(OutputPath(info), failure == "malformed" ? "{" : failure == "null" ? "null" : "{}");
                    if (failure == "null-item") File.WriteAllText(OutputPath(info), "{\"results\":[null]}");
                    if (failure == "unknown-path") WriteResult(info, "not-requested.png", ["bad"]);
                    if (failure == "invalid-exit-json") File.WriteAllText(OutputPath(info), "[]");
                    return Task.FromResult(new ProcessResult(failure != "start", failure is "exit" or "import" or "invalid-exit-json" ? 2 : 0, "", "private stderr"));
                });
                var client = new PaddleLocalOcrClient(runner, CreateTempPath("cache", ".json"), device);
                OcrException error = await Assert.ThrowsAsync<OcrException>(() => client.RecognizeBatchAsync([image], useCache: false));
                Assert.Equal(device == LocalOcrDevice.Gpu, PaddleLocalOcrClient.IsCudaUnavailable(error));
                Assert.DoesNotContain("private stderr", error.Message);
            }
        }
        finally { DeleteIfExists(image); }
    }

    [Fact]
    public void SelectsSeparateCpuAndGpuPythonRuntimes()
    {
        string root = Path.Combine(Path.GetTempPath(), "ocr-python-runtime-" + Guid.NewGuid().ToString("N"));
        string cpu = Path.Combine(root, ".venv-cpu", "Scripts", "python.exe");
        string gpu = Path.Combine(root, ".venv", "Scripts", "python.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(cpu)!);
        Directory.CreateDirectory(Path.GetDirectoryName(gpu)!);
        File.WriteAllText(cpu, string.Empty);
        File.WriteAllText(gpu, string.Empty);
        try
        {
            Assert.Equal(cpu, PaddleLocalOcrClient.ResolvePythonExecutable(LocalOcrDevice.Cpu, root, null, null));
            Assert.Equal(gpu, PaddleLocalOcrClient.ResolvePythonExecutable(LocalOcrDevice.Gpu, root, null, null));
            Assert.Equal("C:\\cpu\\python.exe", PaddleLocalOcrClient.ResolvePythonExecutable(
                LocalOcrDevice.Cpu, root, null, "C:\\cpu\\python.exe"));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void ClearsCacheWrittenBeforeTheCurrentBeijingDay()
    {
        string cachePath = CreateTempPath("stale-cache", ".json");
        try
        {
            File.WriteAllText(cachePath, "old");
            DateOnly today = new(2026, 9, 22);
            File.SetLastWriteTimeUtc(cachePath, new DateTime(2026, 9, 20, 0, 0, 0, DateTimeKind.Utc));

            PaddleLocalOcrClient.ClearStaleCacheIfNeeded(cachePath, today);

            Assert.False(File.Exists(cachePath));
        }
        finally { DeleteIfExists(cachePath); }
    }

    [Fact]
    public void KeepsCacheWrittenDuringTheCurrentBeijingDay()
    {
        string cachePath = CreateTempPath("current-cache", ".json");
        try
        {
            File.WriteAllText(cachePath, "current");
            File.SetLastWriteTimeUtc(cachePath, new DateTime(2026, 9, 21, 16, 0, 0, DateTimeKind.Utc));

            PaddleLocalOcrClient.ClearStaleCacheIfNeeded(cachePath, new DateOnly(2026, 9, 22));

            Assert.True(File.Exists(cachePath));
        }
        finally { DeleteIfExists(cachePath); }
    }

    [Fact]
    public async Task DeletedImageIsIsolatedAndTheRestOfTheBatchStillRuns()
    {
        string kept = CreateImagePath();
        string removed = CreateImagePath();
        string other = CreateImagePath();
        string cache = CreateTempPath("isolate-deleted", ".json");
        var texts = new Dictionary<string, string> { [kept] = "one", [other] = "two" };
        var requested = new List<string>();
        var runner = new FakeProcessRunner((info, _, _) =>
        {
            RecordRequestedImages(info, requested);
            WriteBatchResult(info, texts);
            return Task.FromResult(new ProcessResult(true, 0, "", ""));
        });
        File.Delete(removed);

        try
        {
            var client = new PaddleLocalOcrClient(runner, cache);
            IReadOnlyDictionary<string, IReadOnlyList<string>> result =
                await client.RecognizeBatchAsync([kept, removed, other]);

            Assert.Equal(["one"], result[kept]);
            Assert.Equal(["two"], result[other]);
            Assert.False(result.ContainsKey(removed));
            Assert.Equal("图片已被删除或移动，无法读取，已跳过该图片。", client.LastImageErrors[removed]);
            Assert.Contains(removed, client.LastInvalidImagePaths);
            Assert.DoesNotContain(kept, client.LastInvalidImagePaths);
            Assert.False(client.LastEvidence.ContainsKey(removed));
            Assert.DoesNotContain(removed, requested);
            AssertNoCacheEntryFor(cache, removed);
        }
        finally { DeleteIfExists(kept); DeleteIfExists(other); DeleteIfExists(cache); }
    }

    [Fact]
    public async Task ReadableImageThatOcrFailsKeepsItsCloudFallbackInsteadOfLookingUnreadable()
    {
        string failed = CreateImagePath();
        string kept = CreateImagePath();
        string cache = CreateTempPath("isolate-ocr-error", ".json");
        var requested = new List<string>();
        var runner = new FakeProcessRunner((info, _, _) =>
        {
            RecordRequestedImages(info, requested);
            var results = new List<object>
            {
                new { path = failed, error = "识别失败：图像内容异常" },
                new { path = kept, texts = new[] { "one" } }
            };
            File.WriteAllText(OutputPath(info), JsonSerializer.Serialize(new { results }));
            return Task.FromResult(new ProcessResult(true, 0, "", ""));
        });

        try
        {
            var client = new PaddleLocalOcrClient(runner, cache);
            IReadOnlyDictionary<string, IReadOnlyList<string>> result =
                await client.RecognizeBatchAsync([failed, kept]);

            Assert.Equal(["one"], result[kept]);
            Assert.False(result.ContainsKey(failed));
            Assert.Equal("识别失败：图像内容异常", client.LastImageErrors[failed]);
            // 文件本身可读，只是这次 OCR 没读出字：这张图仍要能进候选、走云兜底。
            Assert.DoesNotContain(failed, client.LastInvalidImagePaths);
            Assert.Contains(failed, requested);
        }
        finally { DeleteIfExists(failed); DeleteIfExists(kept); DeleteIfExists(cache); }
    }

    [Fact]
    public async Task LockedImageIsIsolatedAndTheRestOfTheBatchStillRuns()
    {
        string locked = CreateImagePath();
        string kept = CreateImagePath();
        string cache = CreateTempPath("isolate-locked", ".json");
        var texts = new Dictionary<string, string> { [kept] = "value" };
        var requested = new List<string>();
        var runner = new FakeProcessRunner((info, _, _) =>
        {
            RecordRequestedImages(info, requested);
            WriteBatchResult(info, texts);
            return Task.FromResult(new ProcessResult(true, 0, "", ""));
        });

        try
        {
            using (new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                var client = new PaddleLocalOcrClient(runner, cache);
                IReadOnlyDictionary<string, IReadOnlyList<string>> result =
                    await client.RecognizeBatchAsync([locked, kept]);

                Assert.Equal(["value"], result[kept]);
                Assert.False(result.ContainsKey(locked));
                Assert.Equal("图片被其他程序占用，已跳过该图片。", client.LastImageErrors[locked]);
                Assert.Contains(locked, client.LastInvalidImagePaths);
                Assert.DoesNotContain(locked, requested);
                AssertNoCacheEntryFor(cache, locked);
            }
        }
        finally { DeleteIfExists(locked); DeleteIfExists(kept); DeleteIfExists(cache); }
    }

    [Fact]
    public async Task ImageChangedDuringRecognitionIsRejectedInsteadOfCarryingStaleText()
    {
        string changed = CreateImagePath();
        string stable = CreateImagePath();
        string cache = CreateTempPath("isolate-changed", ".json");
        // 同样的长度和修改时间，只有内容不同：只有真正内容校验才能发现变化。
        DateTime originalWriteTime = File.GetLastWriteTimeUtc(changed);
        var texts = new Dictionary<string, string> { [changed] = "stale", [stable] = "fresh" };
        var runner = new FakeProcessRunner((info, _, _) =>
        {
            WriteBatchResult(info, texts);
            File.WriteAllBytes(changed, [9, 9, 9]);
            File.SetLastWriteTimeUtc(changed, originalWriteTime);
            return Task.FromResult(new ProcessResult(true, 0, "", ""));
        });
        try
        {
            var client = new PaddleLocalOcrClient(runner, cache);
            IReadOnlyDictionary<string, IReadOnlyList<string>> result =
                await client.RecognizeBatchAsync([changed, stable]);

            Assert.Equal(["fresh"], result[stable]);
            Assert.False(result.ContainsKey(changed));
            Assert.Equal("图片在识别过程中发生变化，请重新识别。", client.LastImageErrors[changed]);
            Assert.Contains(changed, client.LastInvalidImagePaths);
            Assert.DoesNotContain(stable, client.LastInvalidImagePaths);
            Assert.False(client.LastEvidence.ContainsKey(changed));
            AssertNoCacheEntryFor(cache, changed);
        }
        finally { DeleteIfExists(changed); DeleteIfExists(stable); DeleteIfExists(cache); }
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

    private static void RecordRequestedImages(ProcessStartInfo startInfo, List<string> requested)
    {
        lock (requested)
            requested.AddRange(File.ReadAllLines(ArgumentValue(startInfo.Arguments, "--list")));
    }

    private static void WriteBatchResult(ProcessStartInfo startInfo, IReadOnlyDictionary<string, string> texts)
    {
        var results = new List<object>();
        foreach (string path in File.ReadAllLines(ArgumentValue(startInfo.Arguments, "--list")))
        {
            if (texts.TryGetValue(path, out string? text))
                results.Add(new { path, texts = new[] { text } });
        }
        File.WriteAllText(OutputPath(startInfo), JsonSerializer.Serialize(new { results }));
    }

    private static void AssertNoCacheEntryFor(string cachePath, string imagePath)
    {
        Assert.True(File.Exists(cachePath), "缓存文件应保留其余成功图片的条目。");
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(cachePath));
        Assert.DoesNotContain(document.RootElement.EnumerateArray(),
            entry => entry.GetProperty("Key").GetString()!.Contains(imagePath, StringComparison.OrdinalIgnoreCase));
    }

    private static string CacheKey(string imagePath, double titleRatio, int? detectionMaxSide, PaddleOcrModel model)
    {
        MethodInfo method = typeof(PaddleLocalOcrClient).GetMethod(
            "CacheKeyForModel",
            BindingFlags.NonPublic | BindingFlags.Static)!;
        return (string)method.Invoke(null, [imagePath, titleRatio, detectionMaxSide, model])!;
    }

    // 只在第一个数据块之后取消的流：让「分块循环内部的取消检查」在没有 sleep、没有大文件的
    // 前提下确定性地触发——第一块总是读成功，第二轮循环必须先检查取消。
    private sealed class CancelAfterFirstChunkStream(CancellationTokenSource source) : Stream
    {
        private bool served;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (served)
                return 0;
            served = true;
            buffer.AsSpan(offset, count).Clear();
            source.Cancel();
            return count;
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
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
