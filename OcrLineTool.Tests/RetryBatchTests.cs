using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using OcrLineTool;

namespace OcrLineTool.Tests;

public sealed class RetryBatchTests
{
    [Fact]
    public void RetryBatchInputsDropBlankAndDuplicatePaths()
    {
        string[] inputs = MainForm.RetryBatchInputs(
            [@"C:\结果\a.png", "", "   ", @"C:\结果\A.PNG", @"C:\结果\b.png"]);

        Assert.Equal([@"C:\结果\a.png", @"C:\结果\b.png"], inputs);
    }

    [Fact]
    public void SourceRetryRunsOnlyForStillMissingRulesAndADifferentView()
    {
        Assert.True(MainForm.NeedsSourceRetry(@"C:\结果\裁剪.png", @"C:\结果\原图.jpg", [true]));
        Assert.True(MainForm.NeedsSourceRetry(@"C:\结果\裁剪.png", @"C:\结果\原图.jpg", [false, true]));
        Assert.False(MainForm.NeedsSourceRetry(@"C:\结果\裁剪.png", @"C:\结果\原图.jpg", [false, false]));
        Assert.False(MainForm.NeedsSourceRetry(@"C:\结果\原图.jpg", @"C:\结果\原图.jpg", [true]));
    }

    [Fact]
    public async Task OneBatchCallUsesOneProcessAndOneCacheWrite()
    {
        string[] images = Enumerable.Range(0, 3)
            .Select(index => CreateTempImage($"image-{index}.png"))
            .ToArray();
        string cachePath = CreateTempPath("cache", ".json");
        var runner = new FakeProcessRunner();
        try
        {
            var client = new PaddleLocalOcrClient(runner, cachePath);

            IReadOnlyDictionary<string, IReadOnlyList<string>> first = await client.RecognizeBatchAsync(
                images, null, titleRatio: 0.4, detectionMaxSide: null, useCache: true,
                model: PaddleOcrModel.Medium);

            // 复抓批量化后的关键性质：三张图只起一次本机 OCR 进程、缓存只写一次。
            Assert.Single(runner.Requests);
            Assert.Equal(images.Order(), first.Keys.Order());
            using (JsonDocument cache = JsonDocument.Parse(File.ReadAllText(cachePath)))
                Assert.Equal(3, cache.RootElement.GetArrayLength());

            IReadOnlyDictionary<string, IReadOnlyList<string>> second = await client.RecognizeBatchAsync(
                images, null, titleRatio: 0.4, detectionMaxSide: null, useCache: true,
                model: PaddleOcrModel.Medium);

            // 三个条目一次读入缓存，第二遍全部命中，不再起进程。
            Assert.Single(runner.Requests);
            Assert.Equal(images.Order(), second.Keys.Order());
        }
        finally
        {
            foreach (string image in images)
                DeleteIfExists(image);
            DeleteIfExists(cachePath);
        }
    }

    private sealed class FakeProcessRunner : IProcessRunner
    {
        public List<ProcessStartInfo> Requests { get; } = [];

        public Task<ProcessResult> RunAsync(
            ProcessStartInfo startInfo,
            Action<string>? reportStandardOutputLine,
            CancellationToken cancellationToken)
        {
            Requests.Add(startInfo);
            string listPath = ArgumentValue(startInfo.Arguments, "--list");
            string outputPath = ArgumentValue(startInfo.Arguments, "--output");
            string[] paths = File.ReadAllLines(listPath, Encoding.UTF8)
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .ToArray();
            var results = paths.Select(path => new
            {
                path,
                texts = new[] { "亮剑团队", "9肖中特", "牛龍虎", "羊猴豬", "蛇兔鼠" },
                items = Array.Empty<object>()
            });
            File.WriteAllText(outputPath, JsonSerializer.Serialize(new { results }));
            reportStandardOutputLine?.Invoke($"OCR_PROGRESS|1|{paths.Length}|{paths[0]}");
            return Task.FromResult(new ProcessResult(true, 0, string.Empty, string.Empty));
        }
    }

    private static string ArgumentValue(string arguments, string name)
    {
        Match match = Regex.Match(arguments, $"--{Regex.Escape(name.TrimStart('-'))}\\s+\"(?<value>[^\"]+)\"");
        Assert.True(match.Success, $"{name} 未出现在本机 OCR 参数里：{arguments}");
        return match.Groups["value"].Value;
    }

    private static string CreateTempImage(string name)
    {
        string path = Path.Combine(
            Path.GetTempPath(), "ocr-retry-batch-" + Guid.NewGuid().ToString("N"), name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        return path;
    }

    private static string CreateTempPath(string prefix, string extension) =>
        Path.Combine(Path.GetTempPath(), $"{prefix}-{Guid.NewGuid():N}{extension}");

    private static void DeleteIfExists(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
            string? folder = Path.GetDirectoryName(path);
            if (folder is not null && Directory.Exists(folder)
                && Path.GetFileName(folder).StartsWith("ocr-retry-batch-", StringComparison.Ordinal))
                Directory.Delete(folder, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
