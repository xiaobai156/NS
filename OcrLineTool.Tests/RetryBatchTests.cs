using System.Diagnostics;
using System.Reflection;
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
    public void RetryImageScopeUnionsTheFoldersOfEveryMissingRule()
    {
        string[] images =
        [
            @"C:\结果\9.26-新澳六合彩资料\资料A\a1.jpg",
            @"C:\结果\9.26-新澳六合彩资料\资料B\b1.jpg",
            @"C:\结果\9.26-新澳六合彩资料\资料C\c1.jpg",
            @"C:\结果\9.26-新澳六合彩资料\资料B\b2.jpg"
        ];

        IReadOnlyList<string> scoped = MainForm.RetryImageScope(images,
            [
                new OcrRule("作者A", "生肖", "规则A", Folder: "资料A"),
                new OcrRule("作者B", "尾", "规则B", Folder: "资料B")
            ]);

        // 两个资料文件夹取并集，顺序照原图片列表，其他资料不参与本机 OCR。
        Assert.Equal([images[0], images[1], images[3]], scoped);
    }

    [Fact]
    public void RetryImageScopeMatchesTheMaterialFolderUnderDateAndIssueWrappers()
    {
        string[] images =
        [
            @"C:\结果\2026年9月26日-新澳六合彩资料\资料A\x.jpg",
            @"C:\结果\269期-新澳六合彩资料\资料A\y.jpg",
            @"C:\结果\新澳六合彩资料\资料B\z.jpg"
        ];

        IReadOnlyList<string> scoped = MainForm.RetryImageScope(images,
            [new OcrRule("作者A", "生肖", "规则A", Folder: "资料A")]);

        Assert.Equal([images[0], images[1]], scoped);
    }

    [Fact]
    public void RetryImageScopeFallsBackToEveryImageWhenARuleHasNoFolder()
    {
        string[] images =
        [
            @"C:\结果\9.26-新澳六合彩资料\资料A\a1.jpg",
            @"C:\结果\9.26-新澳六合彩资料\资料B\b1.jpg"
        ];

        // 王者肖肖/九宫格肖肖这类没有 folder 的规则无法确认身份，只能按完整列表复抓。
        Assert.Equal(images, MainForm.RetryImageScope(images,
            [
                new OcrRule("作者A", "生肖", "规则A", Folder: "资料A"),
                new OcrRule("王者九点禁一肖", "生肖", "王者肖肖")
            ]));
        Assert.Equal(images, MainForm.RetryImageScope(images, []));
    }

    [Fact]
    public void RetryImageScopeFallsBackToEveryImageWhenTheFolderHasNoImages()
    {
        string[] images =
        [
            @"C:\结果\9.26-新澳六合彩资料\资料B\b1.jpg",
            @"C:\结果\9.26-新澳六合彩资料\资料C\c1.jpg"
        ];

        Assert.Equal(images, MainForm.RetryImageScope(images,
            [new OcrRule("作者A", "生肖", "规则A", Folder: "资料A")]));
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

    [Fact]
    public void UnusableImagesAreDroppedFromTheCandidateTextMapButOrdinaryOcrFailuresStay()
    {
        string unreadable = @"C:\结果\9.26-新澳六合彩资料\杰少\gone.jpg";
        string ocrFailed = @"C:\结果\9.26-新澳六合彩资料\杰少\blur.jpg";
        string healthy = @"C:\结果\9.26-新澳六合彩资料\杰少\ok.jpg";
        var localResults = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            [unreadable] = [],
            [ocrFailed] = [],
            [healthy] = ["原创杰少", "{245期}新澳杀①肖狗开猫50准"]
        };
        var errors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [unreadable] = "图片已被删除或移动，无法读取，已跳过该图片。",
            [ocrFailed] = "识别失败：图像内容异常"
        };

        IReadOnlyDictionary<string, IReadOnlyList<string>> map =
            MainForm.CandidateTextMap(localResults, errors, [unreadable]);

        // 已删除/占用/无权/识别期间已变化的图不能进候选，否则随后的身份读取会再读盘失败中断整轮。
        Assert.False(map.ContainsKey(unreadable));
        // 文件仍可读、只是这次没读出字的图保留空文字候选，仍可走云兜底。
        Assert.Empty(map[ocrFailed]);
        Assert.Equal(["原创杰少", "{245期}新澳杀①肖狗开猫50准"], map[healthy]);
    }

    [Fact]
    public void UnreadableImageIsNotPlannedAsACandidateWhileTheReadableOneStillIs()
    {
        string folder = @"C:\结果\9.26-嫣然心水\杰少";
        string gone = Path.Combine(folder, "gone.jpg");
        string readable = Path.Combine(folder, "ok.jpg");
        // 嫣然心水的无标题卡靠「文件夹 + allow_folder_identity」定身份，空文字也算命中；
        // 所以被删/被占的图一旦进了候选文字表就会被排进候选，随后身份读取必然再失败。
        var rule = new OcrRule("", "生肖", "杰少杀一肖", null, null, "杰少") with { AllowFolderIdentity = true };
        IReadOnlyDictionary<string, IReadOnlyList<string>> map = MainForm.CandidateTextMap(
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
            {
                [gone] = [],
                [readable] = []
            },
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [gone] = "图片已被删除或移动，无法读取，已跳过该图片。"
            },
            [gone]);

        IReadOnlyList<LocalCandidatePlan> plans =
            LocalCandidatePlanner.Build([gone, readable], map, [rule], 245, [rule]);

        Assert.DoesNotContain(plans, plan => plan.Path.Equals(gone, StringComparison.OrdinalIgnoreCase));
        Assert.Contains(plans, plan => plan.Path.Equals(readable, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void SingleImageProtectionIsolatesReadFailuresButNotGpuOrCancellation()
    {
        Assert.True(MainForm.TryRunForSingleImage(() => { }, out string? none));
        Assert.Null(none);

        Assert.False(MainForm.TryRunForSingleImage(
            () => throw new IOException("磁盘错误"), out string? readError));
        Assert.Equal("图片无法读取（可能已被删除、移动或被占用），已跳过该图片。", readError);

        Assert.False(MainForm.TryRunForSingleImage(
            () => throw new OcrException("无法固定 OCR 图片版本，请重新识别。", "OCR_IMAGE_IDENTITY_ERROR"),
            out string? identityError));
        Assert.Equal("无法固定 OCR 图片版本，请重新识别。", identityError);

        // 取消、GPU 故障、模型/协议错误绝不吞掉，必须继续上抛。
        Assert.Throws<OperationCanceledException>(() =>
            MainForm.TryRunForSingleImage(() => throw new OperationCanceledException(), out _));
        Assert.Throws<OcrException>(() => MainForm.TryRunForSingleImage(
            () => throw new OcrException("GPU 不可用", PaddleLocalOcrClient.CudaUnavailableCode), out _));
        Assert.Throws<OcrException>(() => MainForm.TryRunForSingleImage(
            () => throw new OcrException("PaddleOCR 返回结果缺少 results。", "OCR_PROTOCOL_ERROR"), out _));
    }

    [Fact]
    public void FailedImageDoesNotLeaveItsTemporaryCandidateViewBehind()
    {
        string source = CreateTempImage("source.png");
        string view = CreateTempImage("compact-view.png");
        try
        {
            MethodInfo remove = typeof(MainForm).GetMethod(
                "RemoveFailedCandidateView", BindingFlags.NonPublic | BindingFlags.Static)!;
            remove.Invoke(null, [source, view]);
            Assert.False(File.Exists(view));

            // 回退成原图（识别视图就是原图本身）时不能把用户的图删掉。
            remove.Invoke(null, [source, source]);
            Assert.True(File.Exists(source));
        }
        finally { DeleteIfExists(source); DeleteIfExists(view); }
    }

    [Fact]
    public void RetryRefreshesTheStaleMissingReasonWhenTheImageIsNowUnreadable()
    {
        var rule = new OcrRule("原创杰少", "九肖", "杰少九肖", Folder: "杰少");
        var reasons = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [rule.Id] = RuleEngine.DescribeMissing(foundImage: false, recognizedText: false)
        };

        MainForm.RefreshRetryMissingReasons(
            [rule],
            reasons,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [@"C:\结果\9.26-嫣然心水\杰少\gone.jpg"] =
                    "图片无法读取（可能已被删除、移动或被占用），已跳过该图片。"
            },
            [],
            []);

        // 上一轮记的是「未找到对应图片」，本轮复抓已确认图片就在那里、只是读不出来，
        // 失败记录与界面必须换成本轮实测原因，不能再停留在旧结论上。
        Assert.Equal(
            "图片读取失败：图片无法读取（可能已被删除、移动或被占用），已跳过该图片。", reasons[rule.Id]);
        Assert.NotEqual(RuleEngine.DescribeMissing(foundImage: false, recognizedText: false), reasons[rule.Id]);
    }

    [Fact]
    public void RetryRefreshesOnlyItsOwnScopeAndKeepsValuesAndConflictsIntact()
    {
        var refreshed = new OcrRule("原创杰少", "九肖", "杰少九肖", Folder: "杰少");
        var untouched = new OcrRule("作者B", "生肖", "规则B", Folder: "资料B");
        var concluded = new OcrRule("作者C", "生肖", "规则C", Folder: "资料C");
        var conflicted = new OcrRule("作者D", "生肖", "规则D", Folder: "资料D");
        var reasons = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [refreshed.Id] = "无法确定当期目标字段，未通过9个不同生肖校验",
            [untouched.Id] = "无法确定当期目标字段，未通过9个不同生肖校验",
            [concluded.Id] = "无法确定当期目标字段，未通过9个不同生肖校验",
            [conflicted.Id] = "无法确定当期目标字段，未通过9个不同生肖校验"
        };
        var errors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [@"C:\结果\9.26-嫣然心水\杰少\gone.jpg"] = "图片已被删除或移动，无法读取，已跳过该图片。",
            [@"C:\结果\9.26-嫣然心水\资料C\c.jpg"] = "图片已被删除或移动，无法读取，已跳过该图片。",
            [@"C:\结果\9.26-嫣然心水\资料D\d.jpg"] = "图片已被删除或移动，无法读取，已跳过该图片。"
        };

        MainForm.RefreshRetryMissingReasons(
            [refreshed, concluded, conflicted], reasons, errors, [concluded.Id], [conflicted.Id]);

        Assert.Equal("图片读取失败：图片已被删除或移动，无法读取，已跳过该图片。", reasons[refreshed.Id]);
        // 未参与本次复抓的规则不能清空、也不能替它猜一个原因。
        Assert.Equal("无法确定当期目标字段，未通过9个不同生肖校验", reasons[untouched.Id]);
        // 已有值或已标记冲突的条目不是「缺失」结论，失败原因保持原样。
        Assert.Equal("无法确定当期目标字段，未通过9个不同生肖校验", reasons[concluded.Id]);
        Assert.Equal("无法确定当期目标字段，未通过9个不同生肖校验", reasons[conflicted.Id]);
    }

    [Fact]
    public void RecoveryProgressTreatsLoadingAndCachingAsPreparationEvenWithPhasePrefixes()
    {
        // 补读消息带着「定位…/裁剪重读…」前缀，但加载模型与读缓存只是准备动作，
        // 一旦被当成推理，进度条与剩余时间就会在模型还没起来时提前推进（C5）。
        Assert.Equal(("加载模型", false),
            MainForm.ClassifyRecoveryProgress("定位·正在启动 PaddleOCR，正在加载本地模型……"));
        Assert.Equal(("缓存校验", false),
            MainForm.ClassifyRecoveryProgress("定位·正在检查本地 OCR 缓存……"));
        Assert.Equal(("缓存校验", false),
            MainForm.ClassifyRecoveryProgress("定位（小模型）·正在读取本地 OCR 缓存……"));
        Assert.Equal(("加载模型", false),
            MainForm.ClassifyRecoveryProgress("裁剪重读·正在启动 PaddleOCR，正在加载本地模型……"));

        // 真实推理消息保留阶段说明，并且是唯一参与估算的那一类。
        Assert.Equal(("定位", true),
            MainForm.ClassifyRecoveryProgress("定位·正在使用本机 PaddleOCR 识别……"));
        Assert.Equal(("裁剪重读", true),
            MainForm.ClassifyRecoveryProgress("裁剪重读·正在使用本机 PaddleOCR 识别……"));
        Assert.Equal(("识别", true),
            MainForm.ClassifyRecoveryProgress("正在使用本机 PaddleOCR 识别……"));
    }

    [Fact]
    public async Task LocalOcrStageStatsCountEachStageOnceAndKeepItsLabel()
    {
        string[] images = [CreateTempImage("a.png"), CreateTempImage("b.png")];
        string cachePath = CreateTempPath("stats", ".json");
        try
        {
            var candidates = new PaddleLocalOcrClient(new FakeProcessRunner(), cachePath);
            var cached = new PaddleLocalOcrClient(new FakeProcessRunner(), cachePath);
            await candidates.RecognizeBatchAsync(
                images, null, titleRatio: 0.4, detectionMaxSide: null, useCache: true,
                model: PaddleOcrModel.Small);
            // 第二遍同模型：两两图都命缓存，一张图也不推理、不起新进程。
            await cached.RecognizeBatchAsync(
                images, null, titleRatio: 0.4, detectionMaxSide: null, useCache: true,
                model: PaddleOcrModel.Small);

            MainForm.LocalOcrStageStat[] stats = MainForm.LocalOcrStageStatsFor(
            [
                ("候选筛选（small）", candidates),
                ("复抓本机 OCR（medium）", cached),
                // 同一个客户端重复登记（例如阶段重入）不能把它的累计值算两遍。
                ("复抓本机 OCR（medium）", cached)
            ]);

            Assert.Equal(2, stats.Length);
            Assert.Equal("候选筛选（small）", stats[0].Stage);
            Assert.Equal(2, stats[0].InferenceCount);
            Assert.Equal(1, stats[0].PythonStartCount);
            Assert.Equal("复抓本机 OCR（medium）", stats[1].Stage);
            Assert.Equal(2, stats[1].CacheHitCount);
            Assert.Equal(0, stats[1].InferenceCount);
            Assert.Equal(0, stats[1].PythonStartCount);
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
