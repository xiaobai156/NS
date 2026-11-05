using System.Drawing.Imaging;
using System.Text.Json;
using OcrLineTool;

namespace OcrLineTool.Tests;

public sealed class ColorBallMatcherTests
{
    private static string Fixture => Path.Combine(AppContext.BaseDirectory, "Fixtures", "dayingjia-two-balls.jpg");
    private static OcrRule Rule => RuleCatalog.Load(Path.Combine(
        ResultFilePaths.ConfigurationDirectory(AppContext.BaseDirectory), "新澳六合彩资料.json"))
        .Single(rule => rule.Id == "大赢家双波");

    [Fact]
    public void ProductionRuleUsesItsOwnTemplateAndOneWaveDistribution()
    {
        Assert.Equal("波", Rule.Type);
        Assert.True(Rule.StrictIssueBlock);
        Assert.Equal("missing_wave_balls", Rule.Extractor);
        string directory = ResultFilePaths.ConfigurationDirectory(AppContext.BaseDirectory);
        var templates = VisualTemplateMatcher.Load(Path.Combine(directory, "新澳六合彩资料.templates.json"));
        var template = Assert.Single(templates.Templates, item => item.RuleIds.Contains(Rule.Id));
        Assert.Equal([Rule.Id], template.RuleIds);
        using var config = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "波分发规则.json")));
        Assert.Equal("{issue}期-一波.txt", config.RootElement.GetProperty("targetFile").GetString());
        Assert.Equal(@"C:\Users\Administrator\Desktop\每天工具\爬虫合集\七类数据统一归纳",
            config.RootElement.GetProperty("targetDirectory").GetString());
        var source = Assert.Single(config.RootElement.GetProperty("sources").EnumerateArray(),
            item => item.GetProperty("sourceGroup").GetString() == "新澳六合彩资料");
        Assert.Equal("新澳六合彩资料", source.GetProperty("sourceGroup").GetString());
        Assert.Equal([Rule.Id], source.GetProperty("labels").EnumerateArray().Select(item => item.GetString()));
    }

    [Fact]
    public void RealGpuOcrEvidenceReadsEveryCompletePeriodAndRejectsThePartialBottomRow()
    {
        var evidence = RealEvidence();
        string[] expected = ["绿波", "红波", "蓝波", "绿波", "绿波", "蓝波",
            "蓝波", "绿波", "蓝波", "蓝波", "蓝波", "蓝波"];
        for (int i = 0; i < expected.Length; i++)
            Assert.Equal(expected[i], RuleEngine.ExtractFinalValue(evidence, 305 - i, Rule));
        Assert.Null(RuleEngine.ExtractFinalValue(evidence, 293, Rule));
        Assert.Null(RuleEngine.ExtractFinalValue(evidence, 306, Rule));
    }

    [Theory]
    [InlineData("红", "蓝", "绿波")]
    [InlineData("蓝", "红", "绿波")]
    [InlineData("红", "绿", "蓝波")]
    [InlineData("绿", "红", "蓝波")]
    [InlineData("蓝", "绿", "红波")]
    [InlineData("绿", "蓝", "红波")]
    public void EveryPairAndOrderInvertsToTheMissingWave(string first, string second, string expected)
    {
        WithCard(first, second, (path, evidence) =>
        {
            string value = Assert.IsType<string>(RuleEngine.ExtractFinalValue(evidence, 418, Rule));
            Assert.Equal(expected, value);
            Assert.True(RuleEngine.IsCanonicalValueValid(Rule, value));
            string line = Assert.Single(RuleEngine.FormatOutput([Rule], new Dictionary<string, string> { [Rule.Id] = value }));
            Assert.Equal(expected + " 大赢家双波", line);
            Assert.Contains("【波】", GroupResultFormatter.Format([Rule], [line]));
        });
    }

    [Theory]
    [InlineData("红", "红")]
    [InlineData("红", null)]
    [InlineData(null, "蓝")]
    [InlineData("灰", "蓝")]
    [InlineData("黄", "蓝")]
    public void MissingDuplicateOrUnrecognizedBallsDoNotUseTextOrAnotherPeriod(string? first, string? second)
    {
        WithCard(first, second, (_, evidence) =>
        {
            evidence = evidence with { Items = [..evidence.Items, new("418期 特码买【红蓝】 开绿波", new OcrBox(160, 222, 620, 40))] };
            Assert.Null(RuleEngine.ExtractFinalValue(evidence, 418, Rule));
            Assert.Equal("蓝波", RuleEngine.ExtractFinalValue(evidence, 417, Rule));
        });
    }

    [Fact]
    public void AThirdBallOrMixedColorBallRemainsMissing()
    {
        WithCard("红", "蓝", (path, _) =>
        {
            using (var image = new Bitmap(path))
            {
                using var g = Graphics.FromImage(image);
                g.FillEllipse(Brushes.Lime, 479, 232, 36, 36);
                image.Save(path + ".changed.png", ImageFormat.Png);
            }
            File.Move(path + ".changed.png", path, true);
            Assert.Null(RuleEngine.ExtractFinalValue(SyntheticEvidence(path), 418, Rule));
        });
        WithCard("红", "蓝", (path, _) =>
        {
            using (var image = new Bitmap(path))
            {
                using var g = Graphics.FromImage(image);
                g.FillPie(Brushes.Lime, 437, 232, 36, 36, 90, 180);
                image.Save(path + ".changed.png", ImageFormat.Png);
            }
            File.Move(path + ".changed.png", path, true);
            Assert.Null(RuleEngine.ExtractFinalValue(SyntheticEvidence(path), 418, Rule));
        });
    }

    [Fact]
    public void PeriodAnchorsMustBePositionedConfidentAndConsistentWithinOneView()
    {
        WithCard("红", "蓝", (_, evidence) =>
        {
            Assert.Null(RuleEngine.ExtractFinalValue(evidence with { Items = evidence.Items.Take(1).ToArray() }, 418, Rule));
            Assert.Null(RuleEngine.ExtractFinalValue(evidence with { Items = evidence.Items.Select(item => item with { Box = null }).ToArray() }, 418, Rule));
            Assert.Null(RuleEngine.ExtractFinalValue(evidence with { Items = [evidence.Items[0], evidence.Items[1] with { Text = "416期" }] }, 418, Rule));
            Assert.Null(RuleEngine.ExtractFinalValue(evidence with { Items = [evidence.Items[0] with { Confidence = .3 }, evidence.Items[1]] }, 418, Rule));
            Assert.Null(RuleEngine.ExtractFinalValue(evidence with { Items = [evidence.Items[0], evidence.Items[1] with { ViewId = "other-view" }] }, 418, Rule));
            Assert.Null(RuleEngine.ExtractFinalValue(evidence with { Items = [evidence.Items[0] with { Box = new OcrBox(650, 226, 120, 40) }, evidence.Items[1]] }, 418, Rule));
        });
        Assert.Null(RuleEngine.ExtractFinalValue(["大赢家【双色球】付费版", "305期 特码买【红蓝】开绿波"], 305, Rule));
        Assert.Contains("色球校验", RuleEngine.DescribeExtractionFailure(["305期"], 305, Rule));
    }

    [Theory]
    [InlineData(0.9, false)]
    [InlineData(30, false)]
    [InlineData(84.9, false)]
    [InlineData(85, true)]
    [InlineData(99, true)]
    [InlineData(100, true)]
    [InlineData(101, false)]
    public void TencentPeriodConfidenceUsesPercentagesAfterParsingCachingAndBinding(double score, bool accepted)
    {
        WithCard("红", "蓝", (path, evidence) =>
        {
            string response = JsonSerializer.Serialize(new { Response = new { TextDetections = evidence.Items.Select(item => new
            {
                DetectedText = item.Text, Confidence = score,
                ItemPolygon = new { item.Box!.X, item.Box.Y, item.Box.Width, item.Box.Height }
            }) } });
            var items = TencentOcrClient.ParseEvidenceItems(response, out var tokens);
            Assert.All(tokens, item => Assert.Equal(score, item.Confidence));
            evidence = evidence with { Items = items, TokenItems = tokens };
            // Cached evidence preserves the provider's score units and view identity.
            string cacheDirectory = Path.Combine(Path.GetDirectoryName(path)!, "cache");
            CloudOcrCacheStore.SaveEntry(cacheDirectory, "新澳六合彩资料", 418, evidence);
            evidence = Assert.Single(CloudOcrCacheStore.LoadEvidence(cacheDirectory, "新澳六合彩资料", 418)).Value
                .Bind(OcrEvidenceIdentity.Capture(path, path, "cloud-primary"));
            Assert.All(evidence.TokenItems!, item => Assert.Equal("cloud-primary/tencent", item.ViewId));
            Assert.Equal(accepted ? "绿波" : null, RuleEngine.ExtractFinalValue(evidence, 418, Rule));
            if (!accepted)
            {
                evidence = evidence with { TokenItems = evidence.TokenItems!.Select(item => item with
                {
                    Text = item.Text == "418期" ? "419期" : "418期"
                }).ToArray() };
                Assert.Null(RuleEngine.ExtractFinalValue(evidence, 418, Rule));
            }
        });
    }

    [Theory]
    [InlineData(.3, false)]
    [InlineData(.849, false)]
    [InlineData(.85, true)]
    [InlineData(.99, true)]
    [InlineData(1, true)]
    [InlineData(30, false)]
    [InlineData(double.NaN, false)]
    [InlineData(double.PositiveInfinity, false)]
    public void NormalizedConfidenceUsesUnitScores(double score, bool accepted)
    {
        WithCard("红", "蓝", (_, evidence) =>
        {
            evidence = evidence with { Items = evidence.Items.Select(item => item with { Confidence = score }).ToArray() };
            Assert.Equal(accepted ? "绿波" : null, RuleEngine.ExtractFinalValue(evidence, 418, Rule));
        });
    }

    [Fact]
    public void ProviderWithoutConfidenceStillRequiresPositionedConsistentPeriods()
    {
        WithCard("红", "蓝", (_, evidence) =>
        {
            evidence = evidence with { Items = evidence.Items.Select(item => item with { Confidence = null, ViewId = "baidu" }).ToArray() };
            Assert.Equal("绿波", RuleEngine.ExtractFinalValue(evidence, 418, Rule));
            Assert.Null(RuleEngine.ExtractFinalValue(evidence with { Items = evidence.Items.Take(1).ToArray() }, 418, Rule));
        });
    }

    [Fact]
    public void IndependentViewsAndLaterImagesCannotHideConflictingPeriodValues()
    {
        WithCard("红", "蓝", (_, evidence) =>
        {
            OcrEvidence shifted = evidence with { Items = evidence.Items.Select(item => item with
            {
                Text = item.Text == "418期" ? "419期" : "418期", ViewId = "other-view"
            }).ToArray() };
            Assert.Equal(RuleExtractionStatus.Conflict, RuleEngine.ExtractFinalResult(
                evidence with { Items = [..evidence.Items, ..shifted.Items] }, 418, Rule).Status);
            var values = new ResultValues(StringComparer.Ordinal);
            var ledger = new ResultEvidenceLedger();
            MainForm.AddExtractedEvidenceValues(evidence, [Rule], 418, values, ledger);
            Assert.Equal("绿波", values[Rule.Id]);
            MainForm.AddExtractedEvidenceValues(shifted, [Rule], 418, values, ledger);
            Assert.Contains(Rule.Id, values.Conflicts);
            Assert.False(values.ContainsKey(Rule.Id));
        });
    }

    [WaveBallGpuFact]
    [Trait("Category", "CUDA")]
    public async Task RealGpuSelectionCroppedMediumExtractionAndIsolatedManualRouting()
    {
        string folder = TempFolder();
        try
        {
            string initialHash = LocalOcrIdentity.Image(Fixture);
            var client = new PaddleLocalOcrClient(new SystemProcessRunner(), Path.Combine(folder, "cache.json"), LocalOcrDevice.Gpu);
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
            var small = await client.RecognizeBatchAsync([Fixture], useCache: false, cancellationToken: timeout.Token);
            Assert.Empty(client.LastImageErrors);
            Assert.Equal(Rule.Id, Assert.Single(Assert.Single(LocalCandidatePlanner.Build([Fixture], small, [Rule], 305)).Rules).Id);
            string configDirectory = ResultFilePaths.ConfigurationDirectory(AppContext.BaseDirectory);
            var catalog = VisualTemplateMatcher.Load(Path.Combine(configDirectory, "新澳六合彩资料.templates.json"));
            var match = Assert.Single(VisualTemplateMatcher.Match([Fixture], catalog, device: LocalOcrDevice.Gpu));
            Assert.Equal(Rule.Id, match.Template.Id);
            Assert.Equal(0, match.Distance);
            string crop = Path.Combine(folder, "crop.png");
            VisualTemplateMatcher.CreateCrop(match, crop, includeRemainingRows: true);
            await client.RecognizeBatchAsync([Fixture, crop], useCache: false, model: PaddleOcrModel.Medium, cancellationToken: timeout.Token);
            Assert.Empty(client.LastImageErrors);
            var records = new List<object>();
            foreach (string input in new[] { Fixture, crop })
            {
                OcrEvidence evidence = client.LastEvidence[input].Bind(OcrEvidenceIdentity.Capture(Fixture, input, "local-primary/medium"));
                var values = new ResultValues(StringComparer.Ordinal);
                MainForm.AddExtractedEvidenceValues(evidence, [Rule], 305, values, new ResultEvidenceLedger());
                Assert.Equal("绿波", values[Rule.Id]);
                string[] expected = ["绿波", "红波", "蓝波", "绿波", "绿波", "蓝波", "蓝波", "绿波", "蓝波", "蓝波", "蓝波", "蓝波"];
                for (int i = 0; i < expected.Length; i++)
                    Assert.Equal(expected[i], RuleEngine.ExtractFinalValue(evidence, 305 - i, Rule));
                Assert.Null(RuleEngine.ExtractFinalValue(evidence, 293, Rule));
                Assert.Null(RuleEngine.ExtractFinalValue(evidence, 306, Rule));
                string target = Path.Combine(folder, "305期-一波.txt");
                if (!File.Exists(target)) await File.WriteAllTextAsync(target, "内容 名称\n\n305期排行\n");
                string[] lines = RuleEngine.FormatOutput([Rule], values);
                var routed = await ResultDistributor.DistributeAsync("11.1-新澳六合彩资料", 305, lines, folder,
                    Path.Combine(configDirectory, "波分发规则.json"));
                Assert.Equal("绿波 大赢家双波", Assert.Single(routed));
                records.Add(new { Input = Path.GetFileName(input), Values = values, HistoricalPeriods = 12, MissingPeriods = new[] { 293, 306 } });
            }
            Assert.Equal(initialHash, LocalOcrIdentity.Image(Fixture));
            string report = Environment.GetEnvironmentVariable("OCR_WAVE_BALL_GPU_REPORT")!;
            Directory.CreateDirectory(report);
            await File.WriteAllTextAsync(Path.Combine(report, "gpu-results.json"), JsonSerializer.Serialize(new
            {
                Device = "gpu:0", Records = records, SourceHash = initialHash, TemplateDistance = match.Distance,
                IsolatedTarget = await File.ReadAllTextAsync(Path.Combine(folder, "305期-一波.txt")), SourceUnchanged = true
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
        finally { Directory.Delete(folder, true); }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void ATemplateCropUsesItsOwnPixelsAndCoordinates(int scale)
    {
        string folder = TempFolder();
        try
        {
            var definition = VisualTemplateMatcher.Load(Path.Combine(
                ResultFilePaths.ConfigurationDirectory(AppContext.BaseDirectory), "新澳六合彩资料.templates.json"))
                .Templates.Single(item => item.RuleIds.Contains(Rule.Id));
            string crop = Path.Combine(folder, "crop.png");
            VisualTemplateMatcher.CreateCrop(new(Fixture, definition, 0, 0), crop, includeRemainingRows: true, scale: scale);
            int top = (int)Math.Round(800 * definition.CropTopWidthRatio);
            OcrEvidenceIdentity identity = OcrEvidenceIdentity.Capture(Fixture, crop, "template-crop");
            OcrEvidence evidence = RealEvidence() with
            {
                InputPath = crop,
                InputHash = identity.InputHash,
                Items = RealEvidence().Items.Where(item => item.Box!.Y >= top).Select(item => item with
                {
                    Box = new OcrBox(item.Box!.X * scale, (item.Box.Y - top) * scale, item.Box.Width * scale, item.Box.Height * scale)
                }).ToArray()
            };
            Assert.Equal("绿波", RuleEngine.ExtractFinalValue(evidence, 305, Rule));
            Assert.Equal("红波", RuleEngine.ExtractFinalValue(evidence, 304, Rule));
            Assert.Equal("蓝波", RuleEngine.ExtractFinalValue(evidence, 294, Rule));
        }
        finally { Directory.Delete(folder, true); }
    }

    [Theory]
    [InlineData(400)]
    [InlineData(1600)]
    public void ResizingAndJpegCompressionDoNotChangeTheDetectedColors(int width)
    {
        string folder = TempFolder();
        try
        {
            string path = Path.Combine(folder, "compressed.jpg");
            double scale = width / 800d;
            using (var original = new Bitmap(Fixture))
            using (var resized = new Bitmap(width, (int)(original.Height * scale)))
            using (var graphics = Graphics.FromImage(resized))
            using (var parameters = new EncoderParameters(1))
            {
                graphics.DrawImage(original, 0, 0, resized.Width, resized.Height);
                parameters.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 35L);
                resized.Save(path, ImageCodecInfo.GetImageEncoders().Single(codec => codec.MimeType == "image/jpeg"), parameters);
            }
            var identity = OcrEvidenceIdentity.Capture(Fixture, path, "resized");
            var evidence = RealEvidence() with
            {
                InputPath = path, InputHash = identity.InputHash,
                Items = RealEvidence().Items.Select(item => item with
                {
                    Box = new OcrBox((int)(item.Box!.X * scale), (int)(item.Box.Y * scale),
                        (int)(item.Box.Width * scale), (int)(item.Box.Height * scale))
                }).ToArray()
            };
            Assert.Equal("绿波", RuleEngine.ExtractFinalValue(evidence, 305, Rule));
            Assert.Equal("红波", RuleEngine.ExtractFinalValue(evidence, 304, Rule));
            Assert.Equal("蓝波", RuleEngine.ExtractFinalValue(evidence, 294, Rule));
        }
        finally { Directory.Delete(folder, true); }
    }

    [Fact]
    public void ChangingTheImageAfterOcrCannotProduceAValue()
    {
        WithCard("红", "蓝", (path, evidence) =>
        {
            File.WriteAllBytes(path, [1, 2, 3]);
            Assert.Throws<OcrException>(() => RuleEngine.ExtractFinalValue(evidence, 418, Rule));
        });
    }

    [Fact]
    public async Task ManualDistributionUsesTheExistingPlacementOwnershipAndConflictRules()
    {
        string folder = TempFolder();
        try
        {
            string configDirectory = Path.Combine(folder, "config");
            Directory.CreateDirectory(configDirectory);
            File.Copy(Path.Combine(ResultFilePaths.ConfigurationDirectory(AppContext.BaseDirectory), "波分发规则.json"),
                Path.Combine(configDirectory, "波分发规则.json"));
            string target = Path.Combine(folder, "418期-一波.txt");
            await File.WriteAllTextAsync(target, "内容 名称\n红波 原有资料\n\n418期排行\n保留内容\n");
            string[] lines = ["绿波 大赢家双波", "红蓝 大赢家双波", "红波 其他资料", "缺失 大赢家双波"];
            var result = await ResultDistributor.DistributeAllAsync("11.1-新澳六合彩资料", 418, lines, folder, configDirectory);
            Assert.Empty(result.Errors);
            Assert.Equal([lines[0]], result.DistributedLines);
            string written = await File.ReadAllTextAsync(target);
            Assert.Contains("内容 名称", written);
            Assert.Contains("红波 原有资料", written);
            Assert.True(written.IndexOf(lines[0], StringComparison.Ordinal) < written.IndexOf("418期排行", StringComparison.Ordinal));
            Assert.Equal([lines[0] + "（已分流）"], ResultDistributor.MarkDistributedLines([lines[0]], result.DistributedLines));
            await ResultDistributor.DistributeAllAsync("新澳六合彩资料", 418, [lines[0] + "（已分流）"], folder, configDirectory);
            Assert.Equal(written, await File.ReadAllTextAsync(target));
            await ResultDistributor.DistributeAllAsync("新澳六合彩资料", 418, ["蓝波 大赢家双波"], folder, configDirectory);
            Assert.DoesNotContain(lines[0], await File.ReadAllTextAsync(target));
            Assert.Contains("蓝波 大赢家双波", await File.ReadAllTextAsync(target));
            await ResultDistributor.DistributeAllAsync("新澳六合彩资料", 418, ["缺失（同一期结果冲突，待核对） 大赢家双波"], folder, configDirectory);
            Assert.DoesNotContain("大赢家双波", await File.ReadAllTextAsync(target));
            Assert.Contains("保留内容", await File.ReadAllTextAsync(target));
            var missingFile = await ResultDistributor.DistributeAllAsync("新澳六合彩资料", 419, [lines[0]], folder, configDirectory);
            Assert.Empty(missingFile.DistributedLines);
            Assert.NotEmpty(missingFile.Errors);
            Assert.False(File.Exists(Path.Combine(folder, "419期-一波.txt")));
        }
        finally { Directory.Delete(folder, true); }
    }

    [Theory]
    [InlineData("绿")]
    [InlineData("红蓝")]
    [InlineData("红波绿波")]
    [InlineData("蓝单")]
    [InlineData("黄波")]
    public void OutputValidationRejectsAnythingOtherThanOneWave(string value) =>
        Assert.False(RuleEngine.IsFormattedOutputValueValid(Rule, value));

    private static OcrEvidence RealEvidence()
    {
        using var json = JsonDocument.Parse(File.ReadAllText(Path.ChangeExtension(Fixture, ".ocr.json")));
        OcrLineEvidence[] items = json.RootElement.EnumerateArray().Select(item =>
        {
            int[] b = item.GetProperty("box").EnumerateArray().Select(v => v.GetInt32()).ToArray();
            return new OcrLineEvidence(item.GetProperty("text").GetString()!, new OcrBox(b[0], b[1], b[2], b[3]),
                item.GetProperty("confidence").GetDouble(), item.GetProperty("viewId").GetString()!);
        }).ToArray();
        var identity = OcrEvidenceIdentity.Capture(Fixture, Fixture, "original");
        return new(Fixture, Fixture, identity.SourceHash, identity.InputHash, identity.ViewId, items);
    }

    private static OcrEvidence SyntheticEvidence(string path)
    {
        var identity = OcrEvidenceIdentity.Capture(path, path, "original");
        return new(path, path, identity.SourceHash, identity.InputHash, identity.ViewId,
        [new("418期", new OcrBox(12, 226, 120, 40), .99), new("417期", new OcrBox(12, 302, 120, 40), .99)]);
    }

    private static void WithCard(string? first, string? second, Action<string, OcrEvidence> check)
    {
        string folder = TempFolder();
        string path = Path.Combine(folder, "card.png");
        try
        {
            using (var card = new Bitmap(800, 366))
            using (var graphics = Graphics.FromImage(card))
            {
                graphics.Clear(Color.FromArgb(176, 161, 63));
                foreach (int y in new[] { 210, 286, 362 }) graphics.FillRectangle(Brushes.White, 0, y, 800, 4);
                Ball(graphics, first, 377, 232); Ball(graphics, second, 437, 232);
                Ball(graphics, "红", 377, 308); Ball(graphics, "绿", 437, 308);
                card.Save(path, ImageFormat.Png);
            }
            check(path, SyntheticEvidence(path));
        }
        finally { Directory.Delete(folder, true); }
    }

    private static void Ball(Graphics graphics, string? color, int x, int y)
    {
        Brush? brush = color switch { "红" => Brushes.Red, "蓝" => Brushes.DeepSkyBlue, "绿" => Brushes.Lime,
            "灰" => Brushes.Gray, "黄" => Brushes.Yellow, _ => null };
        if (brush is not null) graphics.FillEllipse(brush, x, y, 36, 36);
    }

    private static string TempFolder()
    {
        string path = Path.Combine(Path.GetTempPath(), "wave-ball-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}

public sealed class WaveBallGpuFactAttribute : FactAttribute
{
    public WaveBallGpuFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OCR_WAVE_BALL_GPU_REPORT")))
            Skip = "Opt-in: set OCR_WAVE_BALL_GPU_REPORT to an isolated evidence directory.";
    }
}
