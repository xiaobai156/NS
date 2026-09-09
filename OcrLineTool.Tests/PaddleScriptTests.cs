namespace OcrLineTool.Tests;

public sealed class PaddleScriptTests
{
    [Fact]
    public void UsesCudaModelsAndGpuAcceleration()
    {
        string script = File.ReadAllText(ScriptPath);

        Assert.Contains("PP-OCRv6_small_det", script);
        Assert.Contains("PP-OCRv6_small_rec", script);
        Assert.Contains("_local_model_dir", script);
        Assert.Contains("text_detection_model_dir", script);
        Assert.Contains("text_recognition_model_dir", script);
        Assert.Contains("--device", script);
        Assert.Contains("default=\"gpu:0\"", script);
        Assert.Contains("device=args.device", script);
        Assert.Contains("enable_mkldnn=False", script);
        Assert.Contains("cpu_threads=args.cpu_threads", script);
        Assert.Contains("text_recognition_batch_size=32", script);
    }

    [Fact]
    public void SupportsMediumModelForTheSeparateLocalPrimaryMode()
    {
        string script = File.ReadAllText(ScriptPath);

        Assert.Contains("--model", script);
        Assert.Contains("PP-OCRv6_medium_det", script);
        Assert.Contains("PP-OCRv6_medium_rec", script);
        Assert.Equal(PaddleOcrModel.Small, PaddleOcrModels.Default);
        Assert.Equal(PaddleOcrModel.Medium, PaddleOcrModels.LocalPrimary);
    }

    [Fact]
    public void CropsToConfiguredTitleRegionBeforeLocalOcr()
    {
        string script = File.ReadAllText(ScriptPath);

        Assert.Contains("--top-ratio", script);
        Assert.Contains("crop_height", script);
        Assert.Contains("image.crop", script);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(19, 1)]
    [InlineData(20, 1)]
    [InlineData(800, 1)]
    public void UsesOneGpuWorkerForEveryBatch(int imageCount, int expected)
    {
        Assert.Equal(expected, PaddleLocalOcrClient.WorkerCountFor(imageCount));
    }

    [Theory]
    [InlineData("无法初始化 NVIDIA CUDA PaddleOCR：无法启用 NVIDIA GPU 0", PaddleLocalOcrClient.CudaUnavailableCode)]
    [InlineData("PaddleOCR 返回格式异常。", null)]
    public void DistinguishesCudaInitializationFailures(string message, string? expectedCode)
    {
        Assert.Equal(expectedCode, PaddleLocalOcrClient.ErrorCodeFor(message));
    }

    [Fact]
    public void StopsLocalPrimaryRecognitionForCudaFailure()
    {
        string source = File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "OcrLineTool.App", "MainForm.cs"));

        Assert.Contains("PaddleLocalOcrClient.IsCudaUnavailable(exception)", source);
        Assert.Contains("不能把整批任务伪装成云 OCR 完成", source);
    }

    [Fact]
    public void UsesTitleCropOnlyForFixedLayoutGroups()
    {
        Assert.Equal(0.4, PaddleLocalOcrClient.TitleRatioFor(@"C:\结果\新澳六合彩资料"));
        Assert.Equal(0.4, PaddleLocalOcrClient.TitleRatioFor(@"C:\结果\新澳高级会员"));
        Assert.Equal(1.0, PaddleLocalOcrClient.TitleRatioFor(@"C:\结果\嫣然心水"));
    }

    [Fact]
    public void LimitsDetectionSizeOnlyForYanranWhileKeepingTheWholeImage()
    {
        Assert.Equal(960, PaddleLocalOcrClient.DetectionMaxSideFor(@"C:\结果\嫣然心水"));
        Assert.Equal(960, PaddleLocalOcrClient.DetectionMaxSideFor(@"C:\结果\242期-嫣然心水"));
        Assert.Null(PaddleLocalOcrClient.DetectionMaxSideFor(@"C:\结果\242期-嫣然心水-临时"));
        Assert.Null(PaddleLocalOcrClient.DetectionMaxSideFor(@"C:\结果\新澳六合彩资料"));
        Assert.Null(PaddleLocalOcrClient.DetectionMaxSideFor(@"C:\结果\其他群"));
    }

    [Fact]
    public void SupportsMaximumDetectionSideWithoutCropping()
    {
        string script = File.ReadAllText(ScriptPath);

        Assert.Contains("--det-max-side", script);
        Assert.Contains("text_det_limit_side_len", script);
        Assert.Contains("text_det_limit_type=\"max\"", script);
    }

    [Fact]
    public void EmitsMachineReadableProgressAfterEveryImage()
    {
        string script = File.ReadAllText(ScriptPath);

        Assert.Contains("OCR_PROGRESS|", script);
        Assert.Contains("flush=True", script);
    }

    [Fact]
    public void ForcesProgressOutputToUtf8ForChinesePaths()
    {
        string script = File.ReadAllText(ScriptPath);

        Assert.Contains("sys.stdout.reconfigure(encoding=\"utf-8\"", script);
    }

    [Fact]
    public void ParsesProgressLineIncludingWindowsPath()
    {
        bool parsed = PaddleLocalOcrClient.TryParseProgress(
            @"OCR_PROGRESS|37|106|C:\Users\Administrator\Desktop\结果\小苹果\241.jpg",
            out LocalOcrProgress progress);

        Assert.True(parsed);
        Assert.Equal(37, progress.Completed);
        Assert.Equal(106, progress.Total);
        Assert.Equal(@"C:\Users\Administrator\Desktop\结果\小苹果\241.jpg", progress.Path);
    }

    [Fact]
    public void IgnoresOrdinaryPaddleLogLine()
    {
        bool parsed = PaddleLocalOcrClient.TryParseProgress(
            "Creating model: PP-OCRv6_small_det",
            out LocalOcrProgress progress);

        Assert.False(parsed);
        Assert.Equal(LocalOcrProgress.Empty, progress);
    }

    [Theory]
    [InlineData("")]
    [InlineData("OCR_PROGRESS|1|2")]
    [InlineData("OCR_PROGRESS|-1|2|C:\\image.png")]
    [InlineData("OCR_PROGRESS|3|2|C:\\image.png")]
    [InlineData("OCR_PROGRESS|x|2|C:\\image.png")]
    public void RejectsMalformedProgressLine(string line)
    {
        bool parsed = PaddleLocalOcrClient.TryParseProgress(line, out LocalOcrProgress progress);

        Assert.False(parsed);
        Assert.Equal(LocalOcrProgress.Empty, progress);
    }

    private static string ScriptPath => Path.Combine(
        ResultFilePaths.RuntimeDirectory(AppContext.BaseDirectory),
        "paddle_local_ocr.py");
}
