using System.Text.Json;
using System.Text.RegularExpressions;
using OcrLineTool;
using Xunit.Abstractions;

namespace OcrLineTool.Tests;

public sealed class YanranAuthorCardTests(ITestOutputHelper output)
{
    private static IReadOnlyList<OcrRule> Catalog => RuleCatalog.Load(Path.Combine(
        ResultFilePaths.ConfigurationDirectory(AppContext.BaseDirectory), "嫣然心水.json"));
    private static OcrRule Rule(string id) => Catalog.Single(rule => rule.Id == id);
    private static readonly IReadOnlyDictionary<string, (string File, string Value)> Expected =
        new Dictionary<string, (string File, string Value)>
        {
            ["君军两肖"] = ("20261010_113808_aaa2aa2d_92569.jpg", "鸡蛇"),
            ["君军两尾"] = ("20261010_113808_aaa2aa2d_92569.jpg", "8尾+0尾"),
            ["君军合"] = ("20261010_113808_aaa2aa2d_92569.jpg", "02合"),
            ["大哥6688"] = ("20261010_113828_aaa2aa2d_92572.jpg", "牛"),
            ["紫燕儿杀一肖"] = ("20261010_113904_aaa2aa2d_92576.jpg", "龙")
        };

    [Theory]
    [InlineData(7)]
    [InlineData(283)]
    [InlineData(1001)]
    public void JunjunSharedCardWorksWithoutZodiacHeadingAndWithSplitWatermark(int issue)
    {
        string path = @"C:\图片\嫣然心水\君君\card.jpg";
        string[] lines = [$"{issue}期杀鸡+蛇开", "君", "杀一尾", $"{issue}期杀8+0尾开", "军",
            "杀一合", $"{issue}期杀02合开"];
        var rules = Catalog.Where(rule => rule.Keyword == "君军").ToArray();
        var plans = LocalCandidatePlanner.Build([path],
            new Dictionary<string, IReadOnlyList<string>> { [path] = lines }, rules, issue, Catalog);

        Assert.Equal(3, Assert.Single(plans).Rules.Count);
        Assert.Equal("鸡蛇", RuleEngine.ExtractFinalValue(lines, issue, Rule("君军两肖")));
        Assert.Equal("8尾+0尾", RuleEngine.ExtractFinalValue(lines, issue, Rule("君军两尾")));
        Assert.Equal("02合", RuleEngine.ExtractFinalValue(lines, issue, Rule("君军合")));
        Assert.Null(RuleEngine.ExtractFinalValue(lines, issue + 1, Rule("君军两肖")));
    }

    [Fact]
    public void DageKeepsOutputNameWhileUsingShortCardIdentityAndSplitFields()
    {
        var rule = Rule("大哥6688");
        string path = @"C:\图片\283期-嫣然心水\大哥\card.jpg";
        string[] lines = ["新澳彩杀肖", "哥", "283期杀肖", "（牛）", "开啥？？"];

        Assert.Single(RuleEngine.FindMatches(path, lines, [rule], Catalog));
        Assert.Equal("牛", RuleEngine.ExtractFinalValue(lines, 283, rule));
        Assert.Equal(["牛 大哥6688"], RuleEngine.FormatOutput([rule],
            new Dictionary<string, string> { [rule.Id] = "牛" }));
    }

    [Theory]
    [InlineData("君军两肖", "君君", "玩不转", "283期杀【龙】开？")]
    [InlineData("君军两尾", "君君", "玩不转", "283期杀8+0尾开？")]
    [InlineData("大哥6688", "大哥", "缘来如此", "283期杀肖（牛）开？")]
    [InlineData("紫燕儿杀一肖", "紫燕儿", "老兵惜缘", "283期新澳杀【龙】")]
    public void DedicatedFolderDoesNotAdmitAnotherAuthorsCard(string id, string folder, string author, string row)
    {
        Assert.Empty(RuleEngine.FindMatches($@"C:\图片\嫣然心水\{folder}\card.jpg",
            [author, row], [Rule(id)], Catalog));
        Assert.Empty(RuleEngine.FindMatches(@"C:\图片\嫣然心水\其他\card.jpg",
            [Rule(id).Keyword, row], [Rule(id)], Catalog));
    }

    [Theory]
    [InlineData("大哥6688", "大哥", "283期杀肖（牛）")]
    [InlineData("紫燕儿杀一肖", "紫燕儿", "283期新澳杀【龙】")]
    public void FolderAndRowWithoutAnyAuthorEvidenceStayMissing(string id, string folder, string row)
    {
        Assert.Empty(RuleEngine.FindMatches($@"C:\图片\嫣然心水\{folder}\card.jpg", [row], [Rule(id)], Catalog));
    }

    [Fact]
    public void ZiyanerCloudShortWatermarkAndCompleteTargetRowAreAccepted()
    {
        string path = @"C:\图片\嫣然心水\紫燕儿\card.jpg";
        OcrLineEvidence[] items = [new("279期新奥杀鼠√紫燕", new OcrBox(59, 395, 509, 307)),
            new("283期:新奥杀龙", new OcrBox(59, 658, 244, 34)),
            new("283期:杀一尾[2尾]", new OcrBox(59, 1241, 259, 37))];
        Assert.Equal("龙", RuleEngine.ExtractFinalValue(new OcrEvidence(path, path, "hash", "hash", "cloud", items, items),
            283, Rule("紫燕儿杀一肖")));
        Assert.Empty(RuleEngine.FindMatches(path, items.Select(item => item.Text).Append("老兵惜缘"),
            [Rule("紫燕儿杀一肖")], Catalog));
    }

    [Theory]
    [InlineData("君军两肖", "283期杀鸡+鸡开", null)]
    [InlineData("君军两肖", "283期杀鸡+蛇+龙开", null)]
    [InlineData("君军两肖", "283期杀鸡+待更新开蛇", null)]
    [InlineData("君军两尾", "283期杀8+8尾开", null)]
    [InlineData("君军两尾", "283期杀8+10尾开", null)]
    [InlineData("君军两尾", "283期杀8+0尾开", "8尾+0尾")]
    [InlineData("大哥6688", "283期杀肖（牛羊）开", null)]
    [InlineData("君军合", "283期杀14合开", null)]
    public void ReviewedRowsValidateCountRangeAndPlaceholder(string id, string row, string? expected)
    {
        Assert.Equal(expected, RuleEngine.ExtractFinalValue([Rule(id).Keyword, row], 283, Rule(id)));
    }

    [Theory]
    [InlineData("君军两肖", "283期杀鸡+蛇开", "283期杀牛+龙开")]
    [InlineData("君军两尾", "283期杀8+0尾开", "283期杀1+2尾开")]
    [InlineData("大哥6688", "283期杀肖（牛）开", "283期杀肖（兔）开")]
    public void ConflictingTargetRowsStayConflict(string id, string first, string second)
    {
        Assert.Equal(RuleExtractionStatus.Conflict,
            RuleEngine.ExtractFinalResult([Rule(id).Keyword, first, second], 283, Rule(id)).Status);
    }

    [Fact]
    public void InvalidRepeatedTargetFieldIsNotHiddenByAValidOne()
    {
        Assert.Null(RuleEngine.ExtractFinalValue(["大哥", "283期杀肖（牛羊）", "283期杀肖（牛）"],
            283, Rule("大哥6688")));
    }

    [Fact]
    public void InlineCloudIssueRowsKeepTheirOwnValuesAndConflicts()
    {
        OcrRule rule = Rule("大哥6688");
        string path = @"C:\图片\嫣然心水\大哥\card.jpg";
        OcrLineEvidence[] items = [new("大哥", new OcrBox(500, 0, 100, 30)),
            new("282期杀肖（兔）开 283期杀肖（牛）开", new OcrBox(20, 400, 600, 30))];
        var evidence = new OcrEvidence(path, path, "hash", "hash", "cloud", items, items);
        Assert.Equal("牛", RuleEngine.ExtractFinalValue(evidence, 283, rule));
        Assert.Equal("兔", RuleEngine.ExtractFinalValue(evidence, 282, rule));
        items[1] = items[1] with { Text = "283期杀肖（兔）开 283期杀肖（牛）开" };
        Assert.Equal(RuleExtractionStatus.Conflict, RuleEngine.ExtractFinalResult(evidence, 283, rule).Status);
    }

    [Theory]
    [InlineData("君军两肖", "君君", "君军", "玩不转", "283期杀鸡+蛇开")]
    [InlineData("大哥6688", "大哥", "大哥", "缘来如此", "283期杀肖（牛）开")]
    [InlineData("紫燕儿杀一肖", "紫燕儿", "紫燕", "老兵惜缘", "283期新澳杀【龙】")]
    public void ForeignAuthorEvidenceCannotBypassTheCandidateGuard(string id, string folder, string name, string foreign, string row)
    {
        string path = $@"C:\图片\嫣然心水\{folder}\card.jpg";
        OcrLineEvidence[] items = [new(name, new OcrBox(500, 0, 100, 30)),
            new(foreign, new OcrBox(500, 100, 100, 30)), new(row, new OcrBox(20, 400, 240, 30))];
        Assert.Null(RuleEngine.ExtractFinalValue(new OcrEvidence(path, path, "hash", "hash", "medium", items, items),
            283, Rule(id)));
    }

    [Fact]
    public void ZiyanerSplitVerticalNameAndDifferentSectionsStayIndependent()
    {
        string[] lines = ["紫燕", "282期新澳杀【兔】", "283期新奥杀", "【龙】", "儿",
            "283期杀一尾【2尾】"];
        Assert.Equal("龙", RuleEngine.ExtractFinalValue(lines, 283, Rule("紫燕儿杀一肖")));
        Assert.Equal("2尾", RuleEngine.ExtractFinalValue(lines, 283, Rule("紫燕儿尾")));
    }

    [Fact]
    public void PositionedZiyanerWatermarkCanBeSeparateFromThePhysicalDataRow()
    {
        string path = @"C:\图片\嫣然心水\紫燕儿\card.jpg";
        OcrLineEvidence[] items = [
            new("紫燕", new OcrBox(405, 363, 187, 357), ViewId: "medium", RegionId: "watermark"),
            new("283期:新奥杀", new OcrBox(56, 656, 182, 34), ViewId: "medium", RegionId: "issue"),
            new("【龙】", new OcrBox(222, 656, 84, 36), ViewId: "medium", RegionId: "value"),
            new("儿", new OcrBox(411, 671, 175, 196), ViewId: "medium", RegionId: "watermark"),
            new("283期:杀一尾【2尾】", new OcrBox(55, 1240, 268, 38), ViewId: "medium", RegionId: "tail")
        ];
        var evidence = new OcrEvidence(path, path, "hash", "hash", "medium", items, items);
        Assert.Equal("龙", RuleEngine.ExtractFinalValue(evidence, 283, Rule("紫燕儿杀一肖")));
    }

    [Fact]
    public void UnpositionedOrRemoteCellsCannotSupplyASplitValue()
    {
        string path = @"C:\图片\嫣然心水\紫燕儿\card.jpg";
        OcrLineEvidence[] items = [new("紫燕儿", RegionId: "name"),
            new("283期:新澳杀", RegionId: "issue"), new("【龙】", RegionId: "value")];
        Assert.Null(RuleEngine.ExtractFinalValue(new OcrEvidence(path, path, "hash", "hash", "medium", items),
            283, Rule("紫燕儿杀一肖")));
        var remote = items.Select((item, i) => item with { Box = new OcrBox(i * 1000, i * 500, 120, 30) }).ToArray();
        Assert.Null(RuleEngine.ExtractFinalValue(new OcrEvidence(path, path, "hash", "hash", "medium", remote, remote),
            283, Rule("紫燕儿杀一肖")));
    }

    [Fact]
    public void PositionedNextRowCannotFillAMissingTargetCell()
    {
        string path = @"C:\图片\嫣然心水\紫燕儿\card.jpg";
        OcrLineEvidence[] items = [new("紫燕儿", new OcrBox(500, 0, 100, 300)),
            new("283期新澳杀", new OcrBox(20, 400, 180, 30)),
            new("【龙】", new OcrBox(20, 500, 90, 30))];
        Assert.Null(RuleEngine.ExtractFinalValue(new OcrEvidence(path, path, "hash", "hash", "medium", items, items),
            283, Rule("紫燕儿杀一肖")));
    }

    [Theory]
    [InlineData(7)]
    [InlineData(283)]
    [InlineData(1001)]
    public void RealOcrSnapshotsPreserveCandidatesValuesAndDynamicIssue(int issue)
    {
        string fixture = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "Fixtures", "yanran-author-cards.json"));
        using var doc = JsonDocument.Parse(File.ReadAllText(fixture));
        var cards = doc.RootElement.EnumerateArray().ToDictionary(card =>
            $@"C:\图片\嫣然心水\{card.GetProperty("Folder").GetString()}\{card.GetProperty("File").GetString()}");
        var small = cards.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<string>)
            pair.Value.GetProperty("SmallLines").Deserialize<string[]>()!.Select(RewriteIssue).ToArray());
        var catalog = Catalog;
        OcrRule[] rules = catalog.Where(rule => Expected.ContainsKey(rule.Id)).ToArray();
        var plans = LocalCandidatePlanner.Build(cards.Keys.ToArray(), small, rules, issue, catalog);
        Assert.Equal(3, plans.Count);
        foreach (var rule in rules)
        {
            var plan = Assert.Single(plans, plan => plan.Rules.Any(item => item.Id == rule.Id));
            Assert.Equal(Expected[rule.Id].File, Path.GetFileName(plan.Path));
            var card = cards[plan.Path];
            var items = card.GetProperty("MediumItems").Deserialize<OcrLineEvidence[]>()!
                .Select(item => item with { Text = RewriteIssue(item.Text) }).ToArray();
            string hash = card.GetProperty("SourceSha256").GetString()!;
            var evidence = new OcrEvidence(plan.Path, plan.Path, hash, hash, "medium", items, items);
            Assert.Equal(Expected[rule.Id].Value, RuleEngine.ExtractFinalValue(evidence, issue, rule));
            Assert.Null(RuleEngine.ExtractFinalValue(evidence, 999, rule));
            if (rule.Id == "紫燕儿杀一肖")
            {
                var cloud = card.GetProperty("CloudItems").Deserialize<OcrLineEvidence[]>()!
                    .Select(item => item with { Text = RewriteIssue(item.Text) }).ToArray();
                Assert.Equal("龙", RuleEngine.ExtractFinalValue(evidence with { Items = cloud, TokenItems = cloud }, issue, rule));
            }
        }
        string RewriteIssue(string text) => Regex.Replace(text, @"(?<!\d)283(?=\s*期)", issue.ToString());
    }

    [YanranRealImageTheory]
    [InlineData(1)]
    public async Task FourAuthorFailuresUseFreshOcrAndRejectForeignSiblingImages(int deviceNumber)
    {
        string root = Environment.GetEnvironmentVariable("OCR_YANRAN_SAMPLE_DIRECTORY")!;
        string report = Environment.GetEnvironmentVariable("OCR_YANRAN_REPORT_DIRECTORY")!;
        Directory.CreateDirectory(report);
        var device = (LocalOcrDevice)deviceNumber;
        var expected = Expected;
        var catalog = Catalog;
        OcrRule[] rules = catalog.Where(rule => expected.ContainsKey(rule.Id)).ToArray();
        string[] paths = rules.Select(rule => rule.Folder!).Distinct()
            .SelectMany(folder => Directory.GetFiles(Path.Combine(root, folder), "*.jpg"))
            .Order(StringComparer.OrdinalIgnoreCase).ToArray();
        var hashes = paths.ToDictionary(path => path, path => LocalOcrIdentity.Image(path));
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(10));
        var client = new PaddleLocalOcrClient(new SystemProcessRunner(), Path.Combine(report, "isolated-cache.json"), device);
        var small = await client.RecognizeBatchAsync(paths, useCache: false,
            detectionMaxSide: PaddleLocalOcrClient.DetectionMaxSideFor(root), cancellationToken: timeout.Token);
        Assert.Empty(client.LastImageErrors);
        await File.WriteAllTextAsync(Path.Combine(report, "small.json"), JsonSerializer.Serialize(small));
        var plans = LocalCandidatePlanner.Build(paths, small, rules, 283, catalog);
        foreach (var rule in rules)
        {
            var plan = Assert.Single(plans, plan => plan.Rules.Any(item => item.Id == rule.Id));
            Assert.Equal(expected[rule.Id].File, Path.GetFileName(plan.Path));
            output.WriteLine($"small candidate {rule.Id}: {Path.GetFileName(plan.Path)}");
        }
        // Exercise both the initial medium call and its production detection-size retry.
        foreach (int? detectionMaxSide in new int?[] { null, 960 })
        {
            await client.RecognizeBatchAsync(plans.Select(plan => plan.Path).Distinct().ToArray(),
                useCache: false, model: PaddleOcrModel.Medium, detectionMaxSide: detectionMaxSide,
                cancellationToken: timeout.Token);
            Assert.Empty(client.LastImageErrors);
            string pass = detectionMaxSide?.ToString() ?? "default";
            await File.WriteAllTextAsync(Path.Combine(report, $"medium-{pass}.json"), JsonSerializer.Serialize(
                client.LastEvidence.Select(pair => new { Path = pair.Key, Evidence = pair.Value, Tokens = pair.Value.TokenItems })));
            var values = new ResultValues(StringComparer.Ordinal);
            var ledger = new ResultEvidenceLedger();
            foreach (var plan in plans)
            {
                var evidence = client.LastEvidence[plan.Path].Bind(
                    OcrEvidenceIdentity.Capture(plan.Path, plan.Path, "local-primary/medium"));
                MainForm.AddExtractedEvidenceValues(evidence, plan.Rules, 283, values, ledger);
            }
            Assert.Empty(values.Conflicts);
            foreach (var rule in rules)
            {
                output.WriteLine($"medium {pass} {rule.Id}: {values.GetValueOrDefault(rule.Id, "MISSING")}");
                Assert.Equal(expected[rule.Id].Value, values.GetValueOrDefault(rule.Id));
                Assert.Equal(expected[rule.Id].File, Path.GetFileName(ledger.Records[rule.Id].SourcePath));
            }
            await File.WriteAllLinesAsync(Path.Combine(report, $"results-{pass}.txt"),
                GroupResultFormatter.Format(rules, RuleEngine.FormatOutput(rules, values)));
        }
        Assert.All(paths, path => Assert.Equal(hashes[path], LocalOcrIdentity.Image(path)));

        string? cloudCache = Environment.GetEnvironmentVariable("OCR_YANRAN_CLOUD_CACHE");
        if (!string.IsNullOrWhiteSpace(cloudCache))
        {
            string cacheHash = LocalOcrIdentity.Image(cloudCache);
            using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(cloudCache));
            var entry = Assert.Single(doc.RootElement.GetProperty("Entries").EnumerateArray(), entry =>
                entry.GetProperty("ImagePath").GetString()!.EndsWith(expected["紫燕儿杀一肖"].File, StringComparison.Ordinal));
            string path = entry.GetProperty("ImagePath").GetString()!;
            var identity = OcrEvidenceIdentity.Capture(path, path, "cached-cloud-review");
            Assert.Equal(identity.SourceHash, entry.GetProperty("ImageFingerprint").GetString());
            Assert.Equal(identity.InputHash, entry.GetProperty("InputFingerprint").GetString());
            var items = entry.GetProperty("Items").Deserialize<OcrLineEvidence[]>()!;
            var evidence = new OcrEvidence(path, path, identity.SourceHash, identity.InputHash, identity.ViewId, items, items);
            Assert.Equal("龙", RuleEngine.ExtractFinalValue(evidence, 283, Rule("紫燕儿杀一肖")));
            Assert.Equal(cacheHash, LocalOcrIdentity.Image(cloudCache));
            await File.WriteAllTextAsync(Path.Combine(report, "cached-cloud-verification.txt"),
                $"283期 龙 紫燕儿杀一肖\nCacheUnchanged=true\nSourceHash={identity.SourceHash}");
        }
    }
}
