using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Text.Json;
using OcrLineTool;

namespace OcrLineTool.Tests;

public sealed class AuditPipelineRegressionTests
{
    [Fact]
    public void ConflictsAreStickyAndNotFormattedAsSuccess()
    {
        var values = new ResultValues(StringComparer.Ordinal);
        ResultValues.AddTo(values, "测试", "鸡");
        ResultValues.AddTo(values, "测试", "狗");
        ResultValues.AddTo(values, "测试", "鸡");
        Assert.False(values.ContainsKey("测试"));
        Assert.Contains("冲突", Assert.Single(RuleEngine.FormatOutput([new("测试", "生肖")], values)));
    }

    [Fact]
    public void ReorderedIdenticalNumberSetsAreNotConflicts()
    {
        var values = new ResultValues(StringComparer.Ordinal);
        ResultValues.AddTo(values, "测试", "01 02 03");
        ResultValues.AddTo(values, "测试", "03 02 01");
        Assert.Empty(values.Conflicts);
        Assert.Equal("01 02 03", values["测试"]);
    }

    [Fact]
    public void ReplacedImageInvalidatesCloudCacheEvenWithSameMetadata()
    {
        using var files = new TemporaryFiles();
        string image = files.File("image.jpg", "abc");
        DateTime stamp = File.GetLastWriteTimeUtc(image);
        CloudOcrCacheStore.SaveEntry(files.Root, "群", 251, image, ["251期 狗"]);
        Assert.Single(CloudOcrCacheStore.Load(files.Root, "群", 251));
        File.WriteAllText(image, "xyz");
        File.SetLastWriteTimeUtc(image, stamp);
        Assert.Empty(CloudOcrCacheStore.Load(files.Root, "群", 251));
    }

    [Fact]
    public void CroppedInputCannotImpersonateAWholeImageCacheEntry()
    {
        using var files = new TemporaryFiles();
        string image = files.File("image.jpg", "original");
        string crop = files.File("crop.jpg", "crop");
        CloudOcrCacheStore.SaveEntry(files.Root, "群", 251, image, ["251期 狗"], crop);
        Assert.Empty(CloudOcrCacheStore.Load(files.Root, "群", 251));
    }

    [Fact]
    public void EmptyCloudResultsAreNotSuccessfulCacheEntries()
    {
        using var files = new TemporaryFiles();
        string image = files.File("image.jpg", "abc");
        CloudOcrCacheStore.SaveEntry(files.Root, "群", 251, image, []);
        Assert.Empty(CloudOcrCacheStore.Load(files.Root, "群", 251));
    }

    [Fact]
    public void IncompleteRetryCacheIsRejectedForEveryGroup()
    {
        MethodInfo method = typeof(MainForm).GetMethod("CanReuseRetryCloudLines", BindingFlags.NonPublic | BindingFlags.Static)!;
        foreach (string group in new[] { "嫣然心水", "新澳高级会员", "新澳六合彩资料", "黄大仙新澳" })
            Assert.False((bool)method.Invoke(null, new object[] { group, new OcrRule[] { new("测试", "生肖") }, new[] { "250期 测试 狗" }, 251 })!);
    }

    [Fact]
    public void IdentifiedDedicatedFolderDoesNotRequireSmallToReadTheValue()
    {
        var rule = new OcrRule("小苹果", "生肖", RequiredKeyword: "小苹果", Folder: "小苹果");
        string image = Path.Combine(Path.GetTempPath(), "结果", "嫣然心水", "小苹果", "当前.jpg");
        Assert.Single(RuleEngine.FindMatches(image, ["小苹果", "看不清"], [rule]));
    }

    [Fact]
    public async Task CorrectionUpdatesOnlyOwnedRowInsteadOfAppendingContradiction()
    {
        using var files = new TemporaryFiles();
        string target = files.File("251.txt", "外部原文\n");
        await OwnedResultWriter.ApplyAsync(target, "群A", ["鸡 作者"], null, false);
        await OwnedResultWriter.ApplyAsync(target, "群A", ["狗 作者"], null, false);
        Assert.Equal(new[] { "外部原文", "狗 作者" }, await System.IO.File.ReadAllLinesAsync(target));
    }

    [Fact]
    public async Task UnownedConflictingRowIsNotRemovedOrOverwritten()
    {
        using var files = new TemporaryFiles();
        string target = files.File("251.txt", "鸡 作者\n外部原文\n");
        byte[] before = await System.IO.File.ReadAllBytesAsync(target);
        await Assert.ThrowsAsync<OcrException>(() => OwnedResultWriter.ApplyAsync(target, "群A", ["狗 作者"], null, false));
        Assert.Equal(before, await System.IO.File.ReadAllBytesAsync(target));
    }

    [Fact]
    public async Task IdenticalReplayDoesNotClaimAnExternalRow()
    {
        using var files = new TemporaryFiles();
        string target = files.File("251.txt", "鸡 作者\n");
        await OwnedResultWriter.ApplyAsync(target, "群A", ["鸡 作者"], null, false);
        await Assert.ThrowsAsync<OcrException>(() => OwnedResultWriter.ApplyAsync(target, "群A", ["狗 作者"], null, false));
        Assert.Equal(new[] { "鸡 作者" }, await System.IO.File.ReadAllLinesAsync(target));
    }

    [Fact]
    public async Task ExternalEditToOwnedRowIsPreserved()
    {
        using var files = new TemporaryFiles();
        string target = files.File("251.txt", "");
        await OwnedResultWriter.ApplyAsync(target, "群A", ["鸡 作者"], null, false);
        await System.IO.File.WriteAllTextAsync(target, "虎 作者\n");
        await Assert.ThrowsAsync<OcrException>(() => OwnedResultWriter.ApplyAsync(target, "群A", ["狗 作者"], null, false));
        Assert.Equal(new[] { "虎 作者" }, await System.IO.File.ReadAllLinesAsync(target));
    }

    [Fact]
    public async Task CanceledAtomicWritePreservesPreviousFile()
    {
        using var files = new TemporaryFiles();
        string target = files.File("result.txt", "旧结果");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => AtomicFile.WriteAllTextAsync(target, "新结果", null, cancellation.Token));
        Assert.Equal("旧结果", await System.IO.File.ReadAllTextAsync(target));
    }

    [Fact]
    public async Task NonZeroExitStillPreservesCudaError()
    {
        using var files = new TemporaryFiles();
        string image = files.File("image.png", "test");
        var runner = new ErrorRunner();
        var client = new PaddleLocalOcrClient(runner, Path.Combine(files.Root, "cache.json"));
        OcrException exception = await Assert.ThrowsAsync<OcrException>(() => client.RecognizeBatchAsync([image], useCache: false));
        Assert.Equal(PaddleLocalOcrClient.CudaUnavailableCode, exception.Code);
        Assert.Contains("CUDA", exception.Message);
    }

    [Fact]
    public void OnlyConfiguredSecretSlotsAreRequired()
    {
        using var files = new TemporaryFiles();
        string path = files.File("secrets.json", "{\"tencent\":{\"A\":{\"secretId\":\"test-id\",\"secretKey\":\"test-secret\"}}}");
        OcrSecrets secrets = OcrSecretsLoader.Load(path);
        Assert.Equal("test-id", secrets.Get(OcrProvider.Tencent, "A").Id);
        Assert.Throws<OcrException>(() => secrets.Get(OcrProvider.Baidu, "D"));
    }

    [Fact]
    public void DescribingSlotsDoesNotResolveSecrets()
    {
        Assert.Equal("腾讯云 A", CredentialSchedule.DescribeSlot(0).DisplayName);
        Assert.Empty(CredentialSchedule.DescribeSlot(0).Secret);
    }

    private sealed class ErrorRunner : IProcessRunner
    {
        public Task<ProcessResult> RunAsync(ProcessStartInfo startInfo, Action<string>? output, CancellationToken cancellationToken)
        {
            string path = System.Text.RegularExpressions.Regex.Match(startInfo.Arguments, "--output \"(?<path>[^\"]+)\"").Groups["path"].Value;
            System.IO.File.WriteAllText(path, JsonSerializer.Serialize(new { error = "无法启用 NVIDIA CUDA 设备" }));
            return Task.FromResult(new ProcessResult(true, 3, "", ""));
        }
    }

    private sealed class TemporaryFiles : IDisposable
    {
        internal string Root { get; } = Path.Combine(Path.GetTempPath(), "ns-audit-" + Guid.NewGuid().ToString("N"));
        internal TemporaryFiles() => Directory.CreateDirectory(Root);
        internal string File(string name, string text)
        {
            string path = Path.Combine(Root, name);
            System.IO.File.WriteAllText(path, text, new UTF8Encoding(false));
            return path;
        }
        public void Dispose() => Directory.Delete(Root, true);
    }
}
