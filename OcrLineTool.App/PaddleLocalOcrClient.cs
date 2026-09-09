using System.Diagnostics;
using System.ComponentModel;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace OcrLineTool;

public sealed record LocalOcrProgress(int Completed, int Total, string Path, string Stage)
{
    public static readonly LocalOcrProgress Empty = new(0, 0, string.Empty, string.Empty);
}

public enum PaddleOcrModel
{
    Small,
    Medium
}

public static class PaddleOcrModels
{
    public const PaddleOcrModel Default = PaddleOcrModel.Small;
    public const PaddleOcrModel LocalPrimary = PaddleOcrModel.Medium;
}

internal interface IProcessRunner
{
    Task<ProcessResult> RunAsync(
        ProcessStartInfo startInfo,
        Action<string>? reportStandardOutputLine,
        CancellationToken cancellationToken);
}

internal sealed record ProcessResult(
    bool Started,
    int ExitCode,
    string StandardOutput,
    string StandardError);

internal sealed class SystemProcessRunner : IProcessRunner
{
    public async Task<ProcessResult> RunAsync(
        ProcessStartInfo startInfo,
        Action<string>? reportStandardOutputLine,
        CancellationToken cancellationToken)
    {
        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
            return new ProcessResult(false, -1, string.Empty, string.Empty);

        Task<string> standardErrorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        Task<string> standardOutputTask = reportStandardOutputLine is null
            ? process.StandardOutput.ReadToEndAsync(cancellationToken)
            : ReadOutputLinesAsync(process.StandardOutput, reportStandardOutputLine, cancellationToken);

        try
        {
            await process.WaitForExitAsync(cancellationToken);
            await Task.WhenAll(standardOutputTask, standardErrorTask);
        }
        catch
        {
            // Own only the process started here. Never kill unrelated Python processes.
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
            catch (Win32Exception) { }
            try { await process.WaitForExitAsync(CancellationToken.None); }
            catch (InvalidOperationException) { }
            try { await Task.WhenAll(standardOutputTask, standardErrorTask); } catch { }
            throw;
        }
        return new ProcessResult(
            true,
            process.ExitCode,
            standardOutputTask.Result,
            standardErrorTask.Result);
    }

    private static async Task<string> ReadOutputLinesAsync(
        StreamReader reader,
        Action<string> reportStandardOutputLine,
        CancellationToken cancellationToken)
    {
        var output = new StringBuilder();
        while (await reader.ReadLineAsync(cancellationToken) is string line)
        {
            output.AppendLine(line);
            reportStandardOutputLine(line);
        }

        return output.ToString();
    }
}

public sealed class PaddleLocalOcrClient
{
    public const string CudaDevice = "gpu:0";
    public const string CudaUnavailableCode = "NVIDIA_CUDA_UNAVAILABLE";

    private readonly string cachePath;
    private readonly IProcessRunner processRunner;
    private readonly Dictionary<string, string> imageErrors = new(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyDictionary<string, string> LastImageErrors => imageErrors;

    public PaddleLocalOcrClient()
        : this(new SystemProcessRunner())
    {
    }

    internal PaddleLocalOcrClient(IProcessRunner processRunner, string? cachePath = null)
    {
        this.processRunner = processRunner ?? throw new ArgumentNullException(nameof(processRunner));
        this.cachePath = cachePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "OcrLineTool-NVIDIA-CUDA", "paddle-v6-cuda-cache.json");
    }

    public async Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> RecognizeBatchAsync(
        IReadOnlyList<string> imagePaths,
        IProgress<LocalOcrProgress>? progress = null,
        double titleRatio = 1.0,
        int? detectionMaxSide = null,
        bool useCache = true,
        CancellationToken cancellationToken = default,
        PaddleOcrModel model = PaddleOcrModel.Small)
    {
        imageErrors.Clear();
        cancellationToken.ThrowIfCancellationRequested();
        string pipeline = LocalOcrIdentity.Pipeline(model);
        var initialKeys = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        progress?.Report(new LocalOcrProgress(0, imagePaths.Count, string.Empty, "正在检查本地 OCR 缓存……"));
        var cache = useCache
            ? await ReadCacheAsync(cancellationToken)
            : new Dictionary<string, CacheEntry>(StringComparer.OrdinalIgnoreCase);
        var results = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        var pending = new List<string>();
        var cached = new List<string>();

        foreach (string path in imagePaths)
        {
            string key = CacheKeyCore(path, titleRatio, detectionMaxSide, model, pipeline);
            initialKeys[path] = key;
            if (cache.TryGetValue(key, out CacheEntry? entry))
            {
                results[path] = entry.Texts;
                cached.Add(path);
            }
            else
                pending.Add(path);
        }

        for (int index = 0; index < cached.Count; index++)
        {
            progress?.Report(new LocalOcrProgress(
                index + 1, imagePaths.Count, cached[index], "正在读取本地 OCR 缓存……"));
        }

        if (pending.Count > 0)
        {
            progress?.Report(new LocalOcrProgress(
                cached.Count, imagePaths.Count, string.Empty, "正在启动 PaddleOCR，正在加载本地模型……"));
            string scriptPath = Path.Combine(
                ResultFilePaths.RuntimeDirectory(AppContext.BaseDirectory),
                "paddle_local_ocr.py");
            if (!File.Exists(scriptPath))
                throw new OcrException("未找到 PaddleOCR 脚本，请重新发布软件。");

            string tempFolder = Path.Combine(Path.GetTempPath(), "OcrLineTool-NVIDIA-CUDA", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempFolder);
            try
            {
                int workerCount = WorkerCountFor(pending.Count);
                int cpuThreads = workerCount == 1 ? 6 : 3;
                var jobs = new List<(string ListPath, string OutputPath)>();
                for (int worker = 0; worker < workerCount; worker++)
                {
                    string[] chunk = pending.Where((_, index) => index % workerCount == worker).ToArray();
                    string listPath = Path.Combine(tempFolder, $"images-{worker}.txt");
                    string outputPath = Path.Combine(tempFolder, $"result-{worker}.json");
                    await File.WriteAllLinesAsync(listPath, chunk, new UTF8Encoding(false), cancellationToken);
                    jobs.Add((listPath, outputPath));
                }

                int completed = 0;
                await Task.WhenAll(jobs.Select(job => RunPaddleAsync(
                    scriptPath, job.ListPath, job.OutputPath, cpuThreads, titleRatio, detectionMaxSide, model, item =>
                    {
                        int current = Interlocked.Increment(ref completed);
                        progress?.Report(new LocalOcrProgress(
                            cached.Count + current,
                            imagePaths.Count,
                            item.Path,
                            "正在使用本机 PaddleOCR 识别……"));
                    }, cancellationToken)));

                foreach ((string _, string outputPath) in jobs)
                {
                    PaddleResponse response;
                    try
                    {
                        await using FileStream stream = File.OpenRead(outputPath);
                        response = await JsonSerializer.DeserializeAsync<PaddleResponse>(stream, new JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive = true
                        }, cancellationToken)
                            ?? throw new OcrException("PaddleOCR 没有返回结果。");
                    }
                    catch (JsonException)
                    {
                        throw new OcrException("PaddleOCR 返回格式异常。");
                    }

                    if (response.Error is not null)
                        throw new OcrException(response.Error, ErrorCodeFor(response.Error));
                    if (response.Results is null)
                        throw new OcrException("PaddleOCR 返回结果缺少 results。");
                    foreach (PaddleResult result in response.Results)
                    {
                        if (!initialKeys.TryGetValue(result.Path, out string? initialKey))
                            throw new OcrException("PaddleOCR 返回了未请求的图片路径。", "OCR_PROTOCOL_ERROR");
                        if (!string.IsNullOrWhiteSpace(result.Error))
                        {
                            imageErrors[result.Path] = result.Error;
                            if (ErrorCodeFor(result.Error) == CudaUnavailableCode)
                                throw new OcrException(result.Error, CudaUnavailableCode);
                            continue;
                        }
                        string key = CacheKeyCore(result.Path, titleRatio, detectionMaxSide, model, pipeline);
                        if (key != initialKey)
                        {
                            imageErrors[result.Path] = "图片在识别过程中发生变化，请重新识别。";
                            continue;
                        }
                        string[] texts = result.Texts ?? [];
                        results[result.Path] = texts;
                        // Empty results are not durable success-cache entries.
                        if (texts.Any(line => !string.IsNullOrWhiteSpace(line)))
                            cache[key] = new CacheEntry(key, texts);
                    }
                }
                if (useCache)
                    await WriteCacheAsync(cache, cancellationToken);
            }
            finally
            {
                try { Directory.Delete(tempFolder, recursive: true); } catch { }
            }
        }

        return results;
    }

    public async Task EnsureCudaAvailableAsync(CancellationToken cancellationToken = default)
    {
        string checkerPath = Path.Combine(
            ResultFilePaths.RuntimeDirectory(AppContext.BaseDirectory),
            "检查NVIDIA-CUDA环境.py");
        if (!File.Exists(checkerPath))
            throw new OcrException("未找到 NVIDIA CUDA 自检脚本，请重新发布软件。", CudaUnavailableCode);

        var startInfo = new ProcessStartInfo
        {
            FileName = PythonExecutable(),
            Arguments = $"-u {Quote(checkerPath)}",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = AppContext.BaseDirectory
        };
        startInfo.Environment["PYTHONIOENCODING"] = "utf-8";

        ProcessResult result;
        try
        {
            result = await processRunner.RunAsync(startInfo, null, cancellationToken);
        }
        catch (Win32Exception)
        {
            throw new OcrException("未找到 Python，无法执行 NVIDIA CUDA 自检。", CudaUnavailableCode);
        }

        if (!result.Started)
            throw new OcrException("无法启动 NVIDIA CUDA 自检。", CudaUnavailableCode);

        if (result.ExitCode != 0)
            throw new OcrException(
                "未检测到可用的 NVIDIA CUDA 设备；请安装 RTX 2070 SUPER 驱动后再进行本地识别。",
                CudaUnavailableCode);
    }

    private async Task RunPaddleAsync(
        string scriptPath,
        string listPath,
        string outputPath,
        int cpuThreads,
        double titleRatio,
        int? detectionMaxSide,
        PaddleOcrModel model,
        Action<LocalOcrProgress> reportProgress,
        CancellationToken cancellationToken)
    {
        string detectionArgument = detectionMaxSide is int maxSide
            ? $" --det-max-side {maxSide}"
            : string.Empty;
        if (detectionMaxSide == 960)
            detectionArgument += " --compact-folder 杰少";
        string modelArgument = model == PaddleOcrModel.Medium ? "medium" : "small";
        var startInfo = new ProcessStartInfo
        {
            FileName = PythonExecutable(),
            Arguments = $"-u {Quote(scriptPath)} --list {Quote(listPath)} --output {Quote(outputPath)} --cpu-threads {cpuThreads} --device {CudaDevice} --top-ratio {titleRatio.ToString(CultureInfo.InvariantCulture)} --model {modelArgument}{detectionArgument}",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = AppContext.BaseDirectory
        };
        startInfo.Environment["PADDLE_PDX_DISABLE_MODEL_SOURCE_CHECK"] = "True";
        startInfo.Environment["PYTHONIOENCODING"] = "utf-8";
        ProcessResult result;
        try
        {
            result = await processRunner.RunAsync(
                startInfo,
                line =>
                {
                    if (TryParseProgress(line, out LocalOcrProgress item))
                        reportProgress(item);
                },
                cancellationToken);
        }
        catch (Win32Exception)
        {
            throw new OcrException("未找到 Python，无法运行 PaddleOCR。");
        }

        if (!result.Started)
            throw new OcrException("无法启动本机 Python/PaddleOCR。");

        if (result.ExitCode != 0)
        {
            // The worker writes its useful error before exiting non-zero.
            // Do not accept partial success on non-zero exit, or expose raw stderr/secrets.
            if (File.Exists(outputPath))
            {
                try
                {
                    using JsonDocument document = JsonDocument.Parse(await File.ReadAllTextAsync(outputPath, cancellationToken));
                    if (document.RootElement.TryGetProperty("error", out JsonElement error) &&
                        error.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(error.GetString()))
                    {
                        string message = error.GetString()!;
                        throw new OcrException(message, ErrorCodeFor(message));
                    }
                }
                catch (JsonException) { }
                catch (IOException) { }
            }
            throw new OcrException($"PaddleOCR 执行失败（代码 {result.ExitCode}）。");
        }
    }

    public static bool TryParseProgress(string line, out LocalOcrProgress progress)
    {
        const string prefix = "OCR_PROGRESS|";
        progress = LocalOcrProgress.Empty;
        if (!line.StartsWith(prefix, StringComparison.Ordinal))
            return false;

        string[] parts = line.Split('|', 4);
        if (parts.Length != 4 ||
            !int.TryParse(parts[1], out int completed) ||
            !int.TryParse(parts[2], out int total) ||
            completed < 0 || total < 0 || completed > total)
            return false;

        progress = new LocalOcrProgress(completed, total, parts[3], "正在使用本机 PaddleOCR 识别……");
        return true;
    }

    private async Task<Dictionary<string, CacheEntry>> ReadCacheAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (!File.Exists(cachePath))
                return new(StringComparer.OrdinalIgnoreCase);
            await using FileStream stream = File.OpenRead(cachePath);
            var entries = await JsonSerializer.DeserializeAsync<List<CacheEntry>>(stream, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }, cancellationToken) ?? [];
            return entries.Where(entry => entry.Texts is not null && entry.Texts.Any(line => !string.IsNullOrWhiteSpace(line)))
                .GroupBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.Last(), StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is IOException or JsonException)
        {
            return new(StringComparer.OrdinalIgnoreCase);
        }
    }

    private async Task WriteCacheAsync(Dictionary<string, CacheEntry> cache, CancellationToken cancellationToken)
    {
        try
        {
            string? folder = Path.GetDirectoryName(cachePath);
            if (folder is not null)
                Directory.CreateDirectory(folder);
            await AtomicFile.WriteAllTextAsync(cachePath, JsonSerializer.Serialize(cache.Values.ToArray()),
                new UTF8Encoding(false), cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // 缓存不可写不影响本次识别。
        }
    }

    public static int WorkerCountFor(int imageCount) => 1;

    public static bool IsCudaUnavailable(OcrException exception) =>
        exception.Code == CudaUnavailableCode;

    public static string? ErrorCodeFor(string message) =>
        message.Contains("CUDA", StringComparison.OrdinalIgnoreCase) ||
        message.Contains("NVIDIA GPU", StringComparison.OrdinalIgnoreCase)
            ? CudaUnavailableCode
            : null;

    public static double TitleRatioFor(string selectedFolder) =>
        VisualTemplateMatcher.Supports(selectedFolder) ? 0.4 : 1.0;

    public static int? DetectionMaxSideFor(string selectedFolder) =>
        RuleCatalog.IsGroupFolder(selectedFolder, "嫣然心水")
            ? 960
            : null;

    private static string CacheKey(string path, double titleRatio, int? detectionMaxSide) =>
        CacheKeyForModel(path, titleRatio, detectionMaxSide, PaddleOcrModel.Small);

    private static string CacheKeyForModel(string path, double titleRatio, int? detectionMaxSide, PaddleOcrModel model) =>
        CacheKeyCore(path, titleRatio, detectionMaxSide, model, LocalOcrIdentity.Pipeline(model));

    private static string CacheKeyCore(string path, double titleRatio, int? detectionMaxSide, PaddleOcrModel model, string pipeline)
    {
        FileInfo file = new(path);
        string key = $"backend=cuda|device={CudaDevice}|{path}|{file.Length}|{file.LastWriteTimeUtc.Ticks}|top={titleRatio.ToString(CultureInfo.InvariantCulture)}";
        key += $"|sha256={LocalOcrIdentity.Image(path)}|pipeline={pipeline}";
        if (model != PaddleOcrModel.Small)
            key += $"|model={model.ToString().ToLowerInvariant()}";
        if (detectionMaxSide == 960 && (Path.GetDirectoryName(path) ?? string.Empty)
            .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Contains("杰少", StringComparer.OrdinalIgnoreCase))
            key += "|compact=0.55-header=0.28";
        return detectionMaxSide is int maxSide ? $"{key}|det-max={maxSide}" : key;
    }

    private static string Quote(string value) => "\"" + value.Replace("\"", "\\\"") + "\"";

    private static string PythonExecutable()
    {
        string? configured = Environment.GetEnvironmentVariable("OCR_NVIDIA_PYTHON");
        if (!string.IsNullOrWhiteSpace(configured))
            return configured.Trim().Trim('"');

        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            string localPython = Path.Combine(directory.FullName, ".venv", "Scripts", "python.exe");
            if (File.Exists(localPython))
                return localPython;
        }

        return "python";
    }

    private sealed record PaddleResponse(List<PaddleResult>? Results, string? Error);
    private sealed record PaddleResult(string Path, string[]? Texts, string? Error);
    private sealed record CacheEntry(string Key, string[] Texts);
}
