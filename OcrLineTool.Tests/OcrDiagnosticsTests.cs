using System.Text;
using System.Text.Json;
using OcrLineTool;

namespace OcrLineTool.Tests;

public sealed class OcrDiagnosticsTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), $"ocr-diagnostics-{Guid.NewGuid():N}");

    [Fact]
    public async Task WritesTheLatestTaskStateAndReportsNoError()
    {
        string path = Path.Combine(root, "临时文件", "诊断", "OCR诊断_嫣然心水_269期.json");

        string? error = await OcrDiagnostics.TryWriteAsync(path, new
        {
            group = "嫣然心水",
            issue = 269,
            status = "完成",
            recovered = 3
        });

        Assert.Null(error);
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path, Encoding.UTF8));
        Assert.Equal("嫣然心水", document.RootElement.GetProperty("group").GetString());
        Assert.Equal(269, document.RootElement.GetProperty("issue").GetInt32());
        Assert.Equal(3, document.RootElement.GetProperty("recovered").GetInt32());
    }

    [Fact]
    public async Task KeepsTheLastGoodDiagnosticWhenTheTargetIsLockedAndExplainsWhy()
    {
        string path = Path.Combine(root, "临时文件", "诊断", "OCR诊断_嫣然心水_269期.json");
        Assert.Null(await OcrDiagnostics.TryWriteAsync(path, new { issue = 268, status = "完成" }));

        string? error;
        using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            error = await OcrDiagnostics.TryWriteAsync(path, new { issue = 269, status = "完成" });

        Assert.False(string.IsNullOrWhiteSpace(error));
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path, Encoding.UTF8));
        Assert.Equal(268, document.RootElement.GetProperty("issue").GetInt32());
    }

    [Fact]
    public void StatusSuffixSaysWhetherTheDiagnosticWasUpdatedThisRound()
    {
        string path = @"C:\发布\临时文件\诊断\OCR诊断_嫣然心水_269期.json";

        Assert.Equal($"诊断：{path}", OcrDiagnostics.StatusSuffix(path, null));
        Assert.Equal($"诊断：{path}（本轮诊断未更新：拒绝访问）", OcrDiagnostics.StatusSuffix(path, "拒绝访问"));
    }

    [Fact]
    public void AbortReasonsExposeOnlySafeCategoriesAndWhitelistedCodes()
    {
        Assert.Equal("取消", MainForm.RetryAbortReason.Cancelled.Category);
        Assert.Null(MainForm.RetryAbortReason.Cancelled.Code);

        MainForm.RetryAbortReason gpu = MainForm.RetryAbortReason.FromException(
            new OcrException("cuda 初始化失败：out of memory", PaddleLocalOcrClient.CudaUnavailableCode));
        Assert.Equal("GPU 不可用", gpu.Category);
        Assert.Equal(PaddleLocalOcrClient.CudaUnavailableCode, gpu.Code);

        MainForm.RetryAbortReason changed = MainForm.RetryAbortReason.FromException(
            new OcrException("图片在识别过程中发生变化，请重新识别。", "OCR_IMAGE_CHANGED"));
        Assert.Equal("OCR 失败", changed.Category);
        Assert.Equal("OCR_IMAGE_CHANGED", changed.Code);

        // 外部/未知错误码可能是服务端原文，一律不进诊断。
        Assert.Null(MainForm.RetryAbortReason.FromException(
            new OcrException("vendor said: token=SECRET", "VENDOR_401")).Code);
        Assert.Equal("未预期异常", MainForm.RetryAbortReason.FromException(
            new InvalidOperationException("boom")).Category);
    }

    [Fact]
    public async Task AbortDiagnosticNeverWritesExceptionTextOrInnerException()
    {
        const string secret = "TEST_SECRET_MARKER";
        var exception = new InvalidOperationException(
            $"POST https://ocr.example.com/v1?token={secret} body={{\"key\":\"{secret}\"}}",
            new HttpRequestException($"response body {secret}"));
        string path = Path.Combine(root, "临时文件", "诊断", "OCR诊断_嫣然心水_269期.json");

        MainForm.RetryAbortReason reason = MainForm.RetryAbortReason.FromException(exception);
        object payload = MainForm.RetryAbortDiagnosticPayload(
            "嫣然心水", 269, "复抓（本机优先 → 云兜底）", "GPU", "task-1", DateTimeOffset.Now,
            "失败", reason, 3, 1.5, 10.25,
            new Dictionary<string, double> { ["复抓本机 OCR：识别"] = 4.5 },
            [new MainForm.LocalOcrStageStat("候选筛选（small）", 7, 9, 2)],
            []);

        string? error = await OcrDiagnostics.TryWriteAsync(path, payload);

        Assert.Null(error);
        string written = File.ReadAllText(path, Encoding.UTF8);
        Assert.DoesNotContain(secret, written);
        Assert.DoesNotContain("ocr.example.com", written);
        using JsonDocument document = JsonDocument.Parse(written);
        Assert.Equal("未预期异常", document.RootElement.GetProperty("error").GetString());
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("error_code").ValueKind);
        Assert.False(document.RootElement.GetProperty("published").GetBoolean());
        Assert.Equal("失败", document.RootElement.GetProperty("status").GetString());
        Assert.Equal(3, document.RootElement.GetProperty("cloud_request_count").GetInt32());
        Assert.Equal(4.5, document.RootElement.GetProperty("stage_seconds")
            .GetProperty("复抓本机 OCR：识别").GetDouble());
        // 中止诊断同样要带本次任务已发生的阶段计数，否则「复抓为什么慢」会被读成 0 次启动。
        Assert.Equal(2, document.RootElement.GetProperty("python_start_count").GetInt32());
        Assert.Equal("候选筛选（small）", document.RootElement.GetProperty("local_ocr_stages")[0]
            .GetProperty("Stage").GetString());
    }

    [Fact]
    public void OperationLogRecordsExceptionMetadataWithoutItsMessage()
    {
        const string secret = "TEST_SECRET_MARKER";
        var exception = new InvalidOperationException(
            $"body={{\"token\":\"{secret}\"}}", new IOException($"path C:\\秘密\\{secret}.json"));

        OperationLog.AppendException(root, "手动复抓", "嫣然心水", 269, exception);

        string log = File.ReadAllText(ResultFilePaths.ForOperationLog(root), Encoding.UTF8);
        Assert.Contains("未预期异常", log);
        Assert.Contains("System.InvalidOperationException", log);
        Assert.Contains("System.IO.IOException", log);
        Assert.DoesNotContain(secret, log);
        Assert.DoesNotContain("token", log);
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
            Directory.Delete(root, recursive: true);
    }
}
