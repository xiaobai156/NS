using System.Drawing;
using System.Text.Json;
using OcrLineTool;

namespace OcrLineTool.Tests;

public sealed class VisualTemplateMatcherTests
{
    [Theory]
    [InlineData(LocalOcrDevice.Cpu)]
    [InlineData(LocalOcrDevice.Gpu)]
    public void CancelledTemplateBatchDoesNotReadImagesOrLoadCuda(LocalOcrDevice device)
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var template = new VisualTemplateDefinition("test", ["test"], "invalid", 0.25, 0.55);
        var catalog = new VisualTemplateSet(1, "test", 300, [template]);
        Assert.ThrowsAny<OperationCanceledException>(() => VisualTemplateMatcher.Match(
            ["missing.png"], [template], 300, device: device, cancellationToken: cancellation.Token));
        Assert.ThrowsAny<OperationCanceledException>(() => VisualTemplateMatcher.Match(
            ["missing.png"], catalog, device: device, cancellationToken: cancellation.Token));
    }

    [Fact]
    public void CancellationDuringFingerprintingStopsBeforeReturningMatches()
    {
        using var files = new ImageFixture();
        string image = files.Create("card.png", 1, 1);
        var template = new VisualTemplateDefinition("test", ["test"],
            VisualTemplateMatcher.CreateFingerprint(image, device: LocalOcrDevice.Cpu), 0.25, 0.55);
        using var cancellation = new CancellationTokenSource();
        var progress = new CancelOnProgress(cancellation);
        Assert.ThrowsAny<OperationCanceledException>(() => VisualTemplateMatcher.Match(
            [image], [template], 300, progress, LocalOcrDevice.Cpu, cancellation.Token));
    }

    [Fact]
    public void CancelledCropDoesNotCreateOutputOrReadSource()
    {
        using var files = new ImageFixture();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var template = new VisualTemplateDefinition("test", ["test"], "", 0.25, 0.55);
        string destination = Path.Combine(files.Folder, "crop.png");
        Assert.ThrowsAny<OperationCanceledException>(() => VisualTemplateMatcher.CreateCrop(
            new VisualTemplateMatch("missing.png", template, 0, 0), destination,
            cancellationToken: cancellation.Token));
        Assert.False(File.Exists(destination));
    }

    private sealed class CancelOnProgress(CancellationTokenSource cancellation) : IProgress<VisualTemplateProgress>
    {
        public void Report(VisualTemplateProgress value) => cancellation.Cancel();
    }

    [Fact]
    public void CpuTemplateMatchesExistingCatalogWithoutCuda()
    {
        string image = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Fixtures", "leifeng.jpg"));
        VisualTemplateSet catalog = VisualTemplateMatcher.Load(VisualTemplateMatcher.ConfigPath(AppContext.BaseDirectory));
        var match = Assert.Single(VisualTemplateMatcher.Match([image], catalog, device: LocalOcrDevice.Cpu));
        Assert.Equal("雷锋", match.Template.Id);
        Assert.Equal(0, match.Distance);
    }

    [Fact]
    public void CpuTemplateKeepsShiftAndUniquenessRules()
    {
        using var files = new ImageFixture();
        string original = files.Create("original.png", 1, 1);
        string shifted = files.Create("shifted.png", 1, 2, 8);
        string other = files.Create("other.png", 2, 1);
        var template = new VisualTemplateDefinition("test", ["test"],
            VisualTemplateMatcher.CreateFingerprint(original, device: LocalOcrDevice.Cpu), 0.25, 0.55);
        var match = Assert.Single(VisualTemplateMatcher.Match([shifted, other], [template], 300, device: LocalOcrDevice.Cpu));
        Assert.Equal(shifted, match.SourcePath);
        Assert.Equal(0.01, match.VerticalShiftWidthRatio);
        // 同内容的重复图（抓图端重复下载同一张）只算一次，不能因此把这条否掉。
        string duplicate = Path.Combine(files.Folder, "original-copy.png");
        File.Copy(original, duplicate);
        Assert.Equal(original, Assert.Single(VisualTemplateMatcher.Match(
            [original, duplicate], [template], 300, device: LocalOcrDevice.Cpu)).SourcePath);
        // 内容不同、又同样接近的两张图仍然不猜（标题带一样、只有下半部分不同）。
        string sameTitle = files.Create("same-title.png", 1, 2);
        Assert.Empty(VisualTemplateMatcher.Match(
            [original, sameTitle], [template], 300, device: LocalOcrDevice.Cpu));
    }

    // 同一张卡在当天文件夹里存了两份（字节完全相同）时，必须照常配上模板：
    // 否则两份副本距离相同，会被"次佳与最佳差 <4 就丢弃"的规则整条判成缺失。
    [Fact]
    public void IdenticalDuplicateImagesDoNotCancelTheTemplate()
    {
        using var files = new ImageFixture();
        string card = files.Create("card.png", 1, 1);
        string duplicate = Path.Combine(files.Folder, "card-again.png");
        File.Copy(card, duplicate);
        var template = new VisualTemplateDefinition("目标", ["目标规则"],
            VisualTemplateMatcher.CreateFingerprint(card, device: LocalOcrDevice.Cpu), 0.25, 0.55);

        VisualTemplateMatch match = Assert.Single(VisualTemplateMatcher.Match(
            [card, duplicate], [template], 60, device: LocalOcrDevice.Cpu));

        Assert.Equal(card, match.SourcePath);
        Assert.Equal(0, match.Distance);
    }

    [Theory]
    [Trait("Category", "CUDA")]
    [InlineData(19, 17)]
    [InlineData(65, 16)]
    [InlineData(317, 483)]
    [InlineData(800, 600)]
    public void CpuFingerprintEqualsCudaForEveryShiftAndRegion(int width, int height)
    {
        using var files = new ImageFixture();
        string path = Path.Combine(files.Folder, "colored.png");
        using (var bitmap = new Bitmap(width, height))
        {
            var random = new Random(137);
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                    bitmap.SetPixel(x, y, Color.FromArgb(random.Next(256), random.Next(256), random.Next(256)));
            bitmap.Save(path);
        }
        foreach ((double top, double bottom) in new[] { (0.0, 0.1), (0.1125, 0.2125), (0.3, 0.4), (1.3, 1.4) })
            foreach (double shift in new[] { -0.02, -0.01, 0, 0.01, 0.02 })
                Assert.Equal(VisualTemplateMatcher.CreateFingerprint(path, top, bottom, shift),
                    VisualTemplateMatcher.CreateFingerprint(path, top, bottom, shift, LocalOcrDevice.Cpu));
    }

    [Fact]
    public void EnablesVisualTemplatesForBothFixedLayoutGroups()
    {
        Assert.True(VisualTemplateMatcher.Supports(@"C:\结果\新澳六合彩资料"));
        Assert.True(VisualTemplateMatcher.Supports(@"C:\结果\新澳高级会员"));
        Assert.True(VisualTemplateMatcher.Supports(@"C:\结果\8.31-新澳六合彩资料"));
        Assert.True(VisualTemplateMatcher.Supports(@"C:\结果\8.31-新澳高级会员"));
        Assert.False(VisualTemplateMatcher.Supports(@"C:\结果\嫣然心水"));
        Assert.False(VisualTemplateMatcher.Supports(@"C:\结果\8.31-新澳六合彩资料-备份"));
        Assert.False(VisualTemplateMatcher.Supports(@"C:\结果\新澳高级会员-临时"));

        Assert.EndsWith(
            Path.Combine("配置文件", "新澳六合彩资料.templates.json"),
            VisualTemplateMatcher.ConfigPath(@"C:\程序"),
            StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(
            Path.Combine("配置文件", "新澳高级会员.templates.json"),
            VisualTemplateMatcher.ConfigPath(@"C:\程序", @"C:\结果\新澳高级会员"),
            StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(
            Path.Combine("配置文件", "新澳高级会员.templates.json"),
            VisualTemplateMatcher.ConfigPath(@"C:\程序", @"C:\结果\8.31-新澳高级会员"),
            StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(
            Path.Combine("配置文件", "8.31-新澳六合彩资料-备份.templates.json"),
            VisualTemplateMatcher.ConfigPath(@"C:\程序", @"C:\结果\8.31-新澳六合彩资料-备份"),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("Category", "CUDA")]
    public void MatchesFixedTitleWhenTheCurrentRowChanges()
    {
        using var files = new ImageFixture();
        string reference = files.Create("reference.png", titleKind: 1, rowKind: 1);
        string changedRow = files.Create("changed-row.png", titleKind: 1, rowKind: 2);
        string distractor = files.Create("distractor.png", titleKind: 2, rowKind: 1);
        var template = new VisualTemplateDefinition(
            "目标", ["规则一"], VisualTemplateMatcher.CreateFingerprint(reference), 0.25, 0.55);

        VisualTemplateMatch match = Assert.Single(VisualTemplateMatcher.Match(
            [changedRow, distractor], [template], maxDistance: 300));

        Assert.Equal(changedRow, match.SourcePath);
        Assert.Equal("目标", match.Template.Id);
    }

    [Fact]
    public void RejectsPartialTemplateMatchesSoMissingRulesFallBackToLocalScan()
    {
        var first = new VisualTemplateDefinition("一", ["规则一"], new string('0', 256), 0.2, 0.4);
        var second = new VisualTemplateDefinition("二", ["规则二"], new string('0', 256), 0.2, 0.4);
        var firstMatch = new VisualTemplateMatch("one.jpg", first, 10, 0);
        var secondMatch = new VisualTemplateMatch("two.jpg", second, 10, 0);
        var requested = new HashSet<string>(["规则一", "规则二"], StringComparer.Ordinal);

        Assert.True(VisualTemplateMatcher.HasUsableMatches(
            [firstMatch, secondMatch], [first, second], requested));
        Assert.False(VisualTemplateMatcher.HasUsableMatches(
            [firstMatch], [first, second], requested));
        Assert.False(VisualTemplateMatcher.HasUsableMatches(
            [firstMatch, secondMatch], [first, second], new HashSet<string>(["其他"], StringComparer.Ordinal)));
        Assert.False(VisualTemplateMatcher.HasUsableMatches(
            [firstMatch, firstMatch], [first, first], new HashSet<string>(["规则一"], StringComparer.Ordinal)));
    }

    [Fact]
    public void ValidatesTemplateRuleSetAgainstCompleteCatalogRules()
    {
        var first = new VisualTemplateDefinition("一", ["规则一"], new string('0', 256), 0.2, 0.4);
        var second = new VisualTemplateDefinition("二", ["规则二"], new string('0', 256), 0.2, 0.4);

        Assert.True(VisualTemplateMatcher.HasExactRuleCoverage(
            [first, second], new HashSet<string>(["规则一", "规则二"], StringComparer.Ordinal)));
        Assert.False(VisualTemplateMatcher.HasExactRuleCoverage(
            [first], new HashSet<string>(["规则一", "规则二"], StringComparer.Ordinal)));
        Assert.False(VisualTemplateMatcher.HasExactRuleCoverage(
            [first, second], new HashSet<string>(["规则一", "额外规则"], StringComparer.Ordinal)));
    }

    [Fact]
    public void SelectsOnlyRequestedRulesFromSharedTemplateForRetry()
    {
        var shared = new VisualTemplateDefinition(
            "祖师公", ["祖师公肖", "祖师公尾"], new string('0', 256), 0.2, 0.4);
        var other = new VisualTemplateDefinition(
            "其他", ["其他规则"], new string('1', 256), 0.2, 0.4);

        IReadOnlyList<VisualTemplateDefinition> selected = VisualTemplateMatcher.SelectForRules(
            [shared, other],
            new HashSet<string>(["祖师公肖"], StringComparer.Ordinal));

        var selectedTemplate = Assert.Single(selected);
        Assert.Equal("祖师公", selectedTemplate.Id);
        Assert.Equal(["祖师公肖"], selectedTemplate.RuleIds);
        Assert.NotSame(shared.RuleIds, selectedTemplate.RuleIds);
    }

    [Fact]
    public void RejectsRetrySubsetWhenAnyRuleHasNoTemplateOrOverlaps()
    {
        var first = new VisualTemplateDefinition("一", ["规则一"], new string('0', 256), 0.2, 0.4);
        var duplicate = new VisualTemplateDefinition("二", ["规则一", "规则二"], new string('1', 256), 0.2, 0.4);

        Assert.Empty(VisualTemplateMatcher.SelectForRules(
            [first], new HashSet<string>(["规则一", "规则二"], StringComparer.Ordinal)));
        Assert.Empty(VisualTemplateMatcher.SelectForRules(
            [first, duplicate], new HashSet<string>(["规则一"], StringComparer.Ordinal)));
    }

    [Fact]
    [Trait("Category", "CUDA")]
    public void FollowsSmallVerticalTitleShiftWhenCropping()
    {
        using var files = new ImageFixture();
        string reference = files.Create("reference.png", titleKind: 1, rowKind: 1);
        string shifted = files.Create("shifted.png", titleKind: 1, rowKind: 2, verticalShift: 8);
        string crop = Path.Combine(files.Folder, "crop.png");
        var template = new VisualTemplateDefinition(
            "目标", ["规则一", "规则二"], VisualTemplateMatcher.CreateFingerprint(reference), 0.25, 0.55);

        VisualTemplateMatch match = Assert.Single(VisualTemplateMatcher.Match(
            [shifted], [template], maxDistance: 300));
        VisualTemplateMatcher.CreateCrop(match, crop);

        Assert.InRange(match.VerticalShiftWidthRatio, 0.009, 0.011);
        Assert.Equal(["规则一", "规则二"], match.Template.RuleIds);
        using var cropped = new Bitmap(crop);
        Assert.Equal(240, cropped.Height);
        Assert.Equal(Color.Black.ToArgb(), cropped.GetPixel(20, 18).ToArgb());
    }

    [Fact]
    [Trait("Category", "CUDA")]
    public void RestrictsTitleSearchToTheConfiguredSmallOffsetRange()
    {
        using var files = new ImageFixture();
        string reference = files.Create("reference.png", titleKind: 1, rowKind: 1);
        string shifted = files.Create("shifted-large.png", titleKind: 1, rowKind: 2, verticalShift: 100);
        var template = new VisualTemplateDefinition("目标", ["规则一"],
            VisualTemplateMatcher.CreateFingerprint(reference), 0.25, 0.55);

        VisualTemplateMatch match = Assert.Single(VisualTemplateMatcher.Match(
            [shifted], [template], maxDistance: 300));
        Assert.InRange(match.VerticalShiftWidthRatio, -0.021, 0.021);
    }

    [Fact]
    [Trait("Category", "CUDA")]
    public void MatchesTemplatesUsingCatalogAndPerTemplateFingerprintRegions()
    {
        using var files = new ImageFixture();
        string upperReference = files.CreateWithTwoTitles("upper-reference.png", upperKind: 1, lowerKind: 2);
        string lowerReference = files.CreateWithTwoTitles("lower-reference.png", upperKind: 2, lowerKind: 1);
        string upperCandidate = files.CreateWithTwoTitles("upper-candidate.png", upperKind: 1, lowerKind: 3);
        string lowerCandidate = files.CreateWithTwoTitles("lower-candidate.png", upperKind: 3, lowerKind: 1);
        var upperTemplate = new VisualTemplateDefinition(
            "上标题", ["规则一"],
            VisualTemplateMatcher.CreateFingerprint(upperReference, 0.1125, 0.2125),
            0.20, 0.40);
        var lowerTemplate = new VisualTemplateDefinition(
            "下标题", ["规则二"],
            VisualTemplateMatcher.CreateFingerprint(lowerReference, 0.30, 0.40),
            0.30, 0.55,
            FingerprintTopWidthRatio: 0.30,
            FingerprintBottomWidthRatio: 0.40);
        var catalog = new VisualTemplateSet(
            1, "新澳高级会员", 20, [upperTemplate, lowerTemplate],
            FingerprintTopWidthRatio: 0.1125,
            FingerprintBottomWidthRatio: 0.2125);

        IReadOnlyList<VisualTemplateMatch> matches = VisualTemplateMatcher.Match(
            [lowerCandidate, upperCandidate], catalog);

        Assert.Collection(
            matches,
            match =>
            {
                Assert.Equal("上标题", match.Template.Id);
                Assert.Equal(upperCandidate, match.SourcePath);
            },
            match =>
            {
                Assert.Equal("下标题", match.Template.Id);
                Assert.Equal(lowerCandidate, match.SourcePath);
            });
    }

    [Fact]
    public void LoadsPremiumCatalogAndRejectsInvalidFingerprintRegions()
    {
        using var files = new ImageFixture();
        string validPath = Path.Combine(files.Folder, "valid.templates.json");
        string invalidPath = Path.Combine(files.Folder, "invalid.templates.json");
        var template = new VisualTemplateDefinition(
            "目标", ["规则一"], new string('0', 256), 0.2, 0.5,
            FingerprintTopWidthRatio: 0.30,
            FingerprintBottomWidthRatio: 0.40);
        File.WriteAllText(validPath, JsonSerializer.Serialize(new VisualTemplateSet(
            1, "新澳高级会员", 300, [template],
            FingerprintTopWidthRatio: 0.1125,
            FingerprintBottomWidthRatio: 0.2125)));
        File.WriteAllText(invalidPath, JsonSerializer.Serialize(new VisualTemplateSet(
            1, "新澳高级会员", 300, [template],
            FingerprintTopWidthRatio: 0.4,
            FingerprintBottomWidthRatio: 0.3)));

        VisualTemplateSet loaded = VisualTemplateMatcher.Load(validPath);

        Assert.Equal("新澳高级会员", loaded.Folder);
        Assert.Equal(0.30, loaded.Templates[0].FingerprintTopWidthRatio);
        Assert.Throws<OcrException>(() => VisualTemplateMatcher.Load(invalidPath));
    }

    [Fact]
    [Trait("Category", "CUDA")]
    public void ProductionCatalogHasSixtySixTemplatesCoveringSixtySevenRules()
    {
        VisualTemplateSet catalog = VisualTemplateMatcher.Load(
            VisualTemplateMatcher.ConfigPath(AppContext.BaseDirectory));

        Assert.Equal("新澳六合彩资料", catalog.Folder);
        Assert.Equal(100, catalog.Templates.Count);
        Assert.Equal(103, catalog.Templates.SelectMany(item => item.RuleIds).Distinct(StringComparer.Ordinal).Count());
        Assert.Contains(catalog.Templates, item =>
            item.RuleIds.SequenceEqual(["祖师公肖", "祖师公尾"], StringComparer.Ordinal));
        Assert.Contains(catalog.Templates, item =>
            item.Id == "大懒趴" && item.RuleIds.SequenceEqual(["大懒趴"], StringComparer.Ordinal));
        Assert.Contains(catalog.Templates, item =>
            item.Id == "铁甲小宝" && item.RuleIds.SequenceEqual(["铁甲小宝"], StringComparer.Ordinal));
        Assert.Contains(catalog.Templates, item =>
            item.Id == "伯公绝杀" && item.RuleIds.SequenceEqual(["伯公绝杀"], StringComparer.Ordinal));
        Assert.Contains(catalog.Templates, item =>
            item.Id == "绿杀" && item.RuleIds.SequenceEqual(["绿杀"], StringComparer.Ordinal));
        Assert.Contains(catalog.Templates, item =>
            item.Id == "摇钱树蓝杀" && item.RuleIds.SequenceEqual(["摇钱树蓝杀"], StringComparer.Ordinal));
        Assert.Contains(catalog.Templates, item =>
            item.Id == "通天资料双尾" && item.RuleIds.SequenceEqual(["通天资料双尾"], StringComparer.Ordinal));
        Assert.Contains(catalog.Templates, item =>
            item.Id == "心水杀段" && item.RuleIds.SequenceEqual(["心水杀段"], StringComparer.Ordinal));
        foreach (string province in new[] { "广东", "福建", "广西", "贵州", "海南", "江西", "湖南", "上海", "深圳", "云南", "四川" })
            Assert.Contains(catalog.Templates, item =>
                item.Id == province + "两肖" &&
                item.RuleIds.SequenceEqual([province + "两肖"], StringComparer.Ordinal));
        Assert.Contains(catalog.Templates, item =>
            item.Id == "特头杀" && item.RuleIds.SequenceEqual(["特头杀"], StringComparer.Ordinal));
        Assert.Contains(catalog.Templates, item =>
            item.Id == "特头必中" && item.RuleIds.SequenceEqual(["特头必中"], StringComparer.Ordinal));
        string leifengFingerprint = VisualTemplateMatcher.CreateFingerprint(Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory, "..", "..", "..", "Fixtures", "leifeng.jpg")), 0.14, 0.24);
        Assert.Equal(leifengFingerprint, Assert.Single(catalog.Templates, item => item.Id == "雷锋").Fingerprint);
        VisualTemplateDefinition dragonKing = Assert.Single(catalog.Templates, item =>
            item.Id == "龙王" && item.RuleIds.SequenceEqual(["龙王"], StringComparer.Ordinal));
        Assert.InRange(dragonKing.CropBottomWidthRatio, 0.45, 0.47);
        Assert.DoesNotContain(catalog.Templates, item =>
            item.Id == "龙王36码" || item.RuleIds.Contains("龙王36码", StringComparer.Ordinal));
        Assert.True(
            Assert.Single(catalog.Templates, item => item.Id == "水哥肖").CropBottomWidthRatio >= 1.25,
            "水哥肖裁剪必须包含画面中央的大生肖字。");
        Assert.Equal(
        [
            "水哥肖", "包公肖肖", "九宫格肖肖", "佛祖肖肖", "禁止肖肖", "三怪肖肖",
            "王者肖肖", "小精肖肖", "时点半", "红禁肖", "关公又来了肖"
        ],
        catalog.Templates.Where(item =>
            item.Id is "水哥肖" or "包公肖肖" or "九宫格肖肖" or "佛祖肖肖" or "禁止肖肖" or "三怪肖肖" or
            "王者肖肖" or "小精肖肖" or "时点半" or "红禁肖" or "关公又来了肖")
            .Select(item => item.Id));
    }

    [Fact]
    public void PremiumProductionCatalogCoversAllTwelveRulesWithDedicatedFingerprintRegions()
    {
        string[] expectedRuleIds =
        [
            "会员特供杀十码",
            "表弟",
            "翩翩公子尾",
            "翩翩公子肖",
            "祥瑞阁",
            "翩翩公子半波",
            "翩翩公子五行",
            "翩翩公子杀十码",
            "翩翩公子头",
            "祥瑞阁二肖",
            "藏宝九肖",
            "会员暴打"
        ];
        VisualTemplateSet catalog = VisualTemplateMatcher.Load(
            VisualTemplateMatcher.ConfigPath(
                AppContext.BaseDirectory, @"C:\结果\新澳高级会员"));

        Assert.Equal("新澳高级会员", catalog.Folder);
        Assert.Equal(12, catalog.Templates.Count);
        Assert.Equal(
            expectedRuleIds.Order(StringComparer.Ordinal),
            catalog.Templates.SelectMany(item => item.RuleIds).Order(StringComparer.Ordinal));
        Assert.All(catalog.Templates, template =>
        {
            Assert.NotNull(template.FingerprintTopWidthRatio);
            Assert.NotNull(template.FingerprintBottomWidthRatio);
            Assert.True(template.FingerprintTopWidthRatio >= 0);
            Assert.True(template.FingerprintBottomWidthRatio > template.FingerprintTopWidthRatio);
        });
        Assert.All(
            catalog.Templates.Where(template =>
                template.Id is not "会员特供杀十码" and not "表弟" and not "翩翩公子尾"),
            template => Assert.Equal(0.41, template.CropBottomWidthRatio));
        Assert.Equal(
            0.45,
            Assert.Single(catalog.Templates, template => template.Id == "表弟").CropBottomWidthRatio);
        Assert.Equal(
            0.43,
            Assert.Single(catalog.Templates, template => template.Id == "翩翩公子尾").CropBottomWidthRatio);
    }

    private sealed class ImageFixture : IDisposable
    {
        public string Folder { get; } = Path.Combine(
            Path.GetTempPath(), "ocr-visual-template-" + Guid.NewGuid().ToString("N"));

        public ImageFixture() => Directory.CreateDirectory(Folder);

        public string Create(string name, int titleKind, int rowKind, int verticalShift = 0)
        {
            string path = Path.Combine(Folder, name);
            using var image = new Bitmap(800, 600);
            using Graphics graphics = Graphics.FromImage(image);
            graphics.Clear(Color.White);
            graphics.FillRectangle(Brushes.LightGray, 0, 0, 800, 90);

            int titleY = 100 + verticalShift;
            if (titleKind == 1)
            {
                graphics.FillRectangle(Brushes.Black, 60, titleY + 8, 680, 18);
                graphics.FillRectangle(Brushes.Black, 180, titleY + 45, 440, 22);
            }
            else
            {
                graphics.FillRectangle(Brushes.Black, 40, titleY + 8, 180, 65);
                graphics.FillRectangle(Brushes.Black, 580, titleY + 8, 180, 65);
            }

            int rowY = 200 + verticalShift;
            graphics.FillRectangle(Brushes.Black, 20, rowY + 18, rowKind == 1 ? 220 : 500, 16);
            graphics.FillRectangle(Brushes.DarkRed, 20, rowY + 60, rowKind == 1 ? 500 : 220, 16);
            image.Save(path);
            return path;
        }

        public string CreateWithTwoTitles(string name, int upperKind, int lowerKind)
        {
            string path = Path.Combine(Folder, name);
            using var image = new Bitmap(800, 600);
            using Graphics graphics = Graphics.FromImage(image);
            graphics.Clear(Color.White);
            DrawTitle(graphics, 100, upperKind);
            DrawTitle(graphics, 248, lowerKind);
            image.Save(path);
            return path;
        }

        private static void DrawTitle(Graphics graphics, int top, int kind)
        {
            switch (kind)
            {
                case 1:
                    graphics.FillRectangle(Brushes.Black, 60, top + 5, 680, 18);
                    graphics.FillRectangle(Brushes.Black, 180, top + 42, 440, 22);
                    break;
                case 2:
                    graphics.FillRectangle(Brushes.Black, 40, top + 5, 180, 65);
                    graphics.FillRectangle(Brushes.Black, 580, top + 5, 180, 65);
                    break;
                default:
                    graphics.FillEllipse(Brushes.Black, 80, top + 5, 110, 60);
                    graphics.FillEllipse(Brushes.Black, 610, top + 5, 110, 60);
                    graphics.FillRectangle(Brushes.Black, 335, top + 5, 130, 60);
                    break;
            }
        }

        public void Dispose() => Directory.Delete(Folder, recursive: true);
    }
}
