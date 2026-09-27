using OcrLineTool;
using System.Collections;
using System.Reflection;
using System.Text.Json;

namespace OcrLineTool.Tests;

// These tests replace only the test output's catalog; serialize against other catalog readers.
[CollectionDefinition("Template selection", DisableParallelization = true)]
public sealed class TemplateSelectionCollection { }

[Collection("Template selection")]
[Trait("Category", "CUDA")]
public sealed class TemplateSelectionTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task FixedGroupSelectionKeepsLaterIssueRowsInTheSharedCrop(bool premium, bool retry)
    {
        using var fixture = new SelectionFixture(premium: premium, strict: true);
        object selection = await fixture.SelectAsync(retry);
        object candidate = Assert.Single(((IEnumerable)Get(selection, "Candidates")).Cast<object>());
        using var source = new Bitmap(fixture.ImagePath);
        using var crop = new Bitmap((string)Get(candidate, "OcrPath"));
        Assert.Equal(source.Height - 200, crop.Height);
        Assert.Equal(Color.Red.ToArgb(), crop.GetPixel(20, crop.Height - 10).ToArgb());
        Assert.Equal("标题模板", Get(selection, "DisplayName"));
    }

    [Fact]
    public async Task PartialFirstRunKeepsMatchedCandidateAndLeavesOtherRuleMissing()
    {
        using var fixture = new SelectionFixture();
        object selection = await fixture.SelectAsync();
        object candidate = Assert.Single(((IEnumerable)Get(selection, "Candidates")).Cast<object>());
        Assert.Equal(fixture.ImagePath, Get(candidate, "SourcePath"));
        Assert.Equal("标题模板", Get(candidate, "SelectionMode"));
        Assert.Equal(["已匹配"], ((IReadOnlyList<OcrRule>)Get(candidate, "Rules")).Select(rule => rule.Id));
        Assert.Contains("待手动复抓", (string)Get(selection, "DisplayName"));
        string crop = (string)Get(candidate, "OcrPath");
        Assert.True(File.Exists(crop));
        Assert.NotEqual(fixture.ImagePath, crop);
        var values = new Dictionary<string, string> { ["已匹配"] = "牛" };
        var reasons = new Dictionary<string, string>
        {
            ["未匹配"] = RuleEngine.DescribeMissing(foundImage: false, recognizedText: false)
        };
        Assert.Contains("缺失（未找到对应图片） 未匹配", RuleEngine.FormatOutput(fixture.Rules, values, reasons));
    }

    [Theory]
    [InlineData("invalid-json")]
    [InlineData("missing-file")]
    [InlineData("wrong-rule-ids")]
    public async Task BrokenFirstRunCatalogStopsInsteadOfLaunchingLocalOcr(string failure)
    {
        using var fixture = new SelectionFixture();
        if (failure == "invalid-json")
            File.WriteAllText(fixture.CatalogPath, "{");
        else if (failure == "missing-file")
            File.Delete(fixture.CatalogPath);
        else
        {
            var catalog = VisualTemplateMatcher.Load(fixture.CatalogPath);
            catalog.Templates[1] = catalog.Templates[1] with { RuleIds = ["错误名称"] };
            File.WriteAllText(fixture.CatalogPath, JsonSerializer.Serialize(catalog));
        }

        await Assert.ThrowsAsync<OcrException>(() => fixture.SelectAsync());
    }

    // 匹配完成之后、候选身份捕获之前图片才失效：只丢这一张，其余候选保留，
    // 完整性状态跟着实际候选走，不能继续报成完整命中。
    [Fact]
    public async Task ImageLostAfterMatchingOnlyDropsItsOwnTemplateCandidate()
    {
        using var fixture = new SelectionFixture(complete: true);
        object selection = await fixture.SelectAsync(checkpoint: path =>
        {
            if (path.Equals(fixture.SecondImagePath, StringComparison.OrdinalIgnoreCase))
                File.Delete(path);
        });

        object candidate = Assert.Single(((IEnumerable)Get(selection, "Candidates")).Cast<object>());
        Assert.Equal(fixture.ImagePath, Get(candidate, "SourcePath"));
        Assert.Equal(["已匹配"], ((IReadOnlyList<OcrRule>)Get(candidate, "Rules")).Select(rule => rule.Id));
        string display = (string)Get(selection, "DisplayName");
        Assert.NotEqual("标题模板", display);
        Assert.Contains("待手动复抓", display);
    }

    // 模板全部命中但候选全部创建失败：与「模板未命中」同等对待，不得报成部分命中。
    [Fact]
    public async Task DroppingEveryTemplateCandidateIsNotReportedAsPartialMatch()
    {
        using var fixture = new SelectionFixture(complete: true);
        object selection = await fixture.SelectAsync(checkpoint: path => File.Delete(path));

        Assert.Empty(((IEnumerable)Get(selection, "Candidates")).Cast<object>());
        string display = (string)Get(selection, "DisplayName");
        Assert.Contains("未命中", display);
        Assert.DoesNotContain("部分命中", display);
    }

    // 逐图隔离只吞图片读取/身份失败：取消与 GPU 故障必须继续向任务入口传播。
    [Fact]
    public async Task TemplateCandidateIsolationStillPropagatesCancellationAndGpuFaults()
    {
        using (var cancelled = new SelectionFixture(complete: true))
            await Assert.ThrowsAsync<OperationCanceledException>(() => cancelled.SelectAsync(
                checkpoint: _ => throw new OperationCanceledException()));

        using (var gpu = new SelectionFixture(complete: true))
        {
            OcrException failure = await Assert.ThrowsAsync<OcrException>(() => gpu.SelectAsync(
                checkpoint: _ => throw new OcrException("GPU 不可用", PaddleLocalOcrClient.CudaUnavailableCode)));
            Assert.Equal(PaddleLocalOcrClient.CudaUnavailableCode, failure.Code);
        }
    }

    private static object Get(object value, string name) => value.GetType().GetProperty(name)!.GetValue(value)!;

    private sealed class SelectionFixture : IDisposable
    {
        private readonly string folder = Path.Combine(Path.GetTempPath(), "ocr-selection-" + Guid.NewGuid().ToString("N"));
        private readonly byte[] originalCatalog;
        private readonly string group;
        private readonly bool complete;
        private string? cropFolder;
        public string CatalogPath { get; }
        public string ImagePath => Path.Combine(folder, "matched.png");
        public string SecondImagePath => Path.Combine(folder, "second.png");
        public OcrRule[] Rules { get; }

        public SelectionFixture(bool premium = false, bool strict = false, bool complete = false)
        {
            this.complete = complete;
            group = premium ? "新澳高级会员" : "新澳六合彩资料";
            CatalogPath = VisualTemplateMatcher.ConfigPath(AppContext.BaseDirectory, group);
            bool configuredStrict = RuleCatalog.Load(Path.Combine(
                ResultFilePaths.ConfigurationDirectory(AppContext.BaseDirectory), group + ".json"))[0].StrictIssueBlock;
            Rules = strict ? [new("已匹配", "生肖", StrictIssueBlock: configuredStrict)]
                : [new("已匹配", "生肖"), new("未匹配", "生肖")];
            originalCatalog = File.ReadAllBytes(CatalogPath);
            Directory.CreateDirectory(folder);
            using (var bitmap = new Bitmap(800, 600))
            {
                using var graphics = Graphics.FromImage(bitmap);
                graphics.Clear(Color.White);
                graphics.FillRectangle(Brushes.Black, 60, 108, 680, 18);
                graphics.FillRectangle(Brushes.Black, 180, 145, 440, 22);
                graphics.FillRectangle(Brushes.Red, 0, 570, 800, 30);
                bitmap.Save(ImagePath, System.Drawing.Imaging.ImageFormat.Png);
            }
            if (complete)
            {
                using var bitmap = new Bitmap(800, 600);
                using var graphics = Graphics.FromImage(bitmap);
                graphics.Clear(Color.White);
                graphics.FillRectangle(Brushes.Black, 60, 108, 680, 12);
                graphics.FillRectangle(Brushes.Black, 240, 145, 320, 22);
                graphics.FillRectangle(Brushes.Red, 0, 570, 800, 30);
                bitmap.Save(SecondImagePath, System.Drawing.Imaging.ImageFormat.Png);
            }
            string fingerprint = VisualTemplateMatcher.CreateFingerprint(ImagePath);
            string second = complete
                ? VisualTemplateMatcher.CreateFingerprint(SecondImagePath)
                : Convert.ToHexString(Convert.FromHexString(fingerprint).Select(value => (byte)~value).ToArray());
            var catalog = new VisualTemplateSet(1, group, 1,
            [
                new("已匹配", ["已匹配"], fingerprint, 0.25, 0.55),
                new("未匹配", ["未匹配"], second, 0.25, 0.55)
            ]);
            if (strict)
                catalog.Templates.RemoveAt(1);
            File.WriteAllText(CatalogPath, JsonSerializer.Serialize(catalog));
        }

        public async Task<object> SelectAsync(bool retry = false, Action<string>? checkpoint = null)
        {
            using var form = new MainForm();
            var context = SynchronizationContext.Current;
            try
            {
                SynchronizationContext.SetSynchronizationContext(null);
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
                typeof(MainForm).GetField("selectedImageDirectory", flags)!
                    .SetValue(form, Path.Combine(folder, "9.2-" + group));
                // If fallback regresses, this absent file fails CacheKey before any model starts.
                typeof(MainForm).GetField("imagePaths", flags)!
                    .SetValue(form, complete
                        ? new[] { ImagePath, SecondImagePath }
                        : new[] { ImagePath, Path.Combine(folder, "must-not-enter-local-ocr.png") });
                MainForm.TemplateCandidateCheckpoint = checkpoint;
                var task = (Task)typeof(MainForm).GetMethod("SelectCandidatesAsync", flags)!
                    .Invoke(form, [Rules, 318, retry, Rules.Select(rule => rule.Id).ToHashSet(StringComparer.Ordinal), null, null])!;
                await task;
                object result = Get(task, "Result");
                cropFolder = (string?)result.GetType().GetProperty("TemporaryCropFolder")!.GetValue(result);
                return result;
            }
            finally
            {
                MainForm.TemplateCandidateCheckpoint = null;
                SynchronizationContext.SetSynchronizationContext(context);
            }
        }

        public void Dispose()
        {
            File.WriteAllBytes(CatalogPath, originalCatalog);
            if (cropFolder is not null && Directory.Exists(cropFolder))
                Directory.Delete(cropFolder, recursive: true);
            Directory.Delete(folder, recursive: true);
        }
    }
}
