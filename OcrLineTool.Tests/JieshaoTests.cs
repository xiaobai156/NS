using System.Reflection;
using OcrLineTool;

namespace OcrLineTool.Tests;

public sealed class JieshaoTests
{
    private static readonly string[][] Samples =
    [
        ["新澳六合彩", "原创杰少", "{244期}新澳杀①肖：蛇开鸡46准", "{245期}新澳杀①肖：鼠开猫50准"],
        ["新澳六合彩", "原创杰少", "{244期}新澳加杀尾{0尾}开46鸡准", "{245期}新澳加杀尾{0尾}开50尾准"],
        ["新澳六合彩", "原创杰少", "{244期}新澳禁一尾{5尾}开46鸡对", "{245期}新澳禁一尾{5尾}开50尾对"],
        ["新澳六合彩", "原创杰少", "{245期新澳彩九肖中特}{开猫}准", "{九肖}{龙猴羊蛇虎兔鸡马狗}",
            "{六肖}{龙猴羊蛇虎兔}", "{三肖}{龙猴羊}", "{一肖}{龙}", "{244期新澳九肖}兔羊猪牛鸡猴鼠马龙{开鸡}准"]
    ];
    private static readonly string[] Labels = ["杰少杀一肖", "杰少杀一尾", "杰少禁一尾", "杰少九肖"];
    private static IReadOnlyList<OcrRule> LoadRules() => RuleCatalog.Load(
        Path.Combine(ResultFilePaths.ConfigurationDirectory(AppContext.BaseDirectory), "嫣然心水.json"));

    // Actual saved cloud response, not the successful local OCR transcription.
    private static readonly string[] CloudNine =
    [
        "原创杰少", "准准错准准准准准准", "{244期新澳九肖}兔羊猪牛鸡猴鼠马龙{开鸡}准",
        "{243期新澳九肖}虎马鸡牛兔狗蛇猪猴{开狗}", "鸡狗狗马龙虎虎羊猴", "开开开开开开开开开",
        "{245期新澳彩九肖中特}{开猫}准", "龙猴猴牛虎猪猴鼠龙", "{九肖}{龙猴羊蛇虎兔鸡马狗}",
        "猪兔兔马狗龙猪牛", "{六肖}{龙猴羊蛇虎兔}", "蛇猪鸡猪马兔牛猪", "狗羊鼠龙虎虎马蛇",
        "{三肖}{龙猴羊}", "新澳六合彩", "鸡兔马狗鼠龙羊猴狗", "{一肖}{龙}", "牛龙猴牛羊鼠羊鼠",
        "羊马鸡龙鸡鸡马兔马", "兔虎蛇马蛇鼠鸡虎鸡", "肖肖肖肖肖肖肖肖肖", "九九九九九九九九九",
        "原创杰少", "澳澳澳澳澳澳澳澳澳", "新新新新新新新新新", "期期期期期期期期"
    ];
    private static readonly string[] CloudTailColumns =
    [
        "原创杰少", "准准准准准准准准准准准准准准准准准准准准准错准准准准准准准准准准准准准准",
        "马马牛猴兔蛇马蛇虎鼠羊马蛇猴狗马虎兔猴蛇兔猪牛鼠龙猪猴羊虎虎龙马狗狗鸡尾",
        "41654172144152911638140321276749148",
        "开开开开开开开开开开开开开开开开开开开升升升升升升升开开开开开开开升开开",
        "尾尾尾尾尾尾尾尾尾尾尾尾尾尾尾尾尾尾尾尾尾尾尾尾尾尾尾尾尾尾尾尾尾尾尾尾", "新澳大合彩",
        "000000000000000000000000000000000000",
        "尾尾尾尾尾尾尾尾尾尾尾尾尾尾尾尾尾尾尾尾尾尾尾尾尾尾尾尾尾尾尾尾尾尾尾尾",
        "杀杀杀杀杀杀杀杀杀杀杀杀杀杀杀杀杀杀杀杀杀杀杀杀杀杀杀杀杀杀杀杀杀杀杀杀",
        "加加加加加加加加加加加加加加加加加加加加加加加加加加加加加加加加加加加加",
        "澳澳澳澳澳澳澳澳澳澳澳澳澳澳澳澳澳澳澳澳澳澳澳澳澳澳澳澳澳澳澳澳澳澳澳澳",
        "新新新新新新新新新新新新新新新新新新新新新新新新新新新新新新新新新新新新", "原创杰少",
        "卜卜卜卜卜卜卜卜卜卜卜卜卜卜卜卜卜卜卜卜卜卜卜卜卜卜卜卜卜卜卜卜卜卜卜卜",
        "期期期期期期期期期期期期期期期期期期期期期期期期期期期期期期期期期期期期",
        "012345678901234567390123456789012345", "111111111122222222223333333333444444",
        "222222222222222222222222222222222222"
    ];

    [Fact]
    public void ActualCloudNineCanRestoreTheMissingValueWithoutBorrowingOtherTiers()
    {
        OcrRule rule = Assert.Single(LoadRules(), rule => rule.Id == "杰少九肖");
        var values = new Dictionary<string, string>();
        typeof(MainForm).GetMethod("AddExtractedValues", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, [CloudNine, new[] { rule }, 245, values]);
        Assert.Equal("龙猴羊蛇虎兔鸡马狗", values[rule.Id]);
        Assert.Null(RuleEngine.ExtractFinalValue(CloudNine, 246, rule));
        Assert.Null(RuleEngine.ExtractFinalValue(CloudNine.Where(line => !line.StartsWith("{九肖}")), 245, rule));
    }

    [Fact]
    public void FailedTailCacheMustNotPreventAnExplicitCloudRetry()
    {
        OcrRule tail = Assert.Single(LoadRules(), rule => rule.Id == "杰少杀一尾");
        OcrRule nine = Assert.Single(LoadRules(), rule => rule.Id == "杰少九肖");
        Assert.Null(RuleEngine.ExtractFinalValue(CloudTailColumns, 245, tail));
        MethodInfo? method = typeof(MainForm).GetMethod("CanReuseRetryCloudLines", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);
        bool Reuse(string group, OcrRule rule, string[] lines, int issue = 245) =>
            (bool)method.Invoke(null, [group, new[] { rule }, lines, issue])!;
        Assert.False(Reuse("9.2-嫣然心水", tail, CloudTailColumns));
        Assert.False(Reuse("9.2-嫣然心水", tail, []));
        Assert.False(Reuse("9.2-嫣然心水", tail, Samples[1], 246));
        Assert.True(Reuse("9.2-嫣然心水", tail, Samples[1]));
        Assert.True(Reuse("9.2-嫣然心水", nine, CloudNine));
        Assert.True(Reuse("其他群", tail, CloudTailColumns));
        OcrRule forbiddenTail = Assert.Single(LoadRules(), rule => rule.Id == "杰少禁一尾");
        Assert.False(Reuse("9.2-嫣然心水", forbiddenTail, CloudTailColumns));
    }

    [Theory]
    [InlineData("杰少杀一尾", "0尾")]
    [InlineData("杰少九肖", "龙猴羊蛇虎兔鸡马狗")]
    public async Task FreshPrimaryRetryResponseUpdatesValuesBeforeFallback(string label, string expected)
    {
        OcrRule rule = Assert.Single(LoadRules(), rule => rule.Id == label);
        string[] lines = label == "杰少九肖" ? CloudNine : Samples[1];
        var client = new FakeCloudClient(lines);
        var values = new Dictionary<string, string>();
        var constructor = typeof(MainForm).GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic,
            null, [typeof(string), typeof(Action<System.Diagnostics.ProcessStartInfo>)], null)!;
        using var form = (MainForm)constructor.Invoke([Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")),
            (Action<System.Diagnostics.ProcessStartInfo>)(_ => throw new Exception("Must not launch a process"))]);
        MethodInfo method = typeof(MainForm).GetMethod("RecognizeRetryAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var credential = new OcrCredential(OcrProvider.Baidu, "test", "unused", "unused");
        var response = (Task<IReadOnlyList<string>>)method.Invoke(form,
            [client, credential, "test.png", 1, 1, new[] { rule }, 245, values])!;
        Assert.Equal(lines, await response);
        Assert.True(values.ContainsKey(label), "The primary cloud text must be extracted, not merely cached.");
        Assert.Equal(expected, values[label]);
        Assert.Equal(1, client.Calls);
        // Replaying a provider response must not replace an already accepted value.
        values[label] = "已接受的结果";
        await (Task<IReadOnlyList<string>>)method.Invoke(form,
            [client, credential, "test.png", 1, 1, new[] { rule }, 245, values])!;
        Assert.Equal("已接受的结果", values[label]);
    }

    private sealed class FakeCloudClient(string[] lines) : IOcrClient
    {
        public int Calls { get; private set; }
        public Task<IReadOnlyList<string>> RecognizeAsync(string imagePath, CancellationToken cancellationToken = default)
        {
            Calls++;
            Assert.Equal("test.png", imagePath);
            return Task.FromResult<IReadOnlyList<string>>(lines);
        }
    }

    [Theory]
    [InlineData("杰少杀一肖")]
    [InlineData("杰少杀一尾")]
    [InlineData("杰少禁一尾")]
    public void DenseJieshaoTablesGetACompactCloudCopyWithoutChangingOriginal(string id)
    {
        OcrRule rule = Assert.Single(LoadRules(), rule => rule.Id == id);
        MethodInfo? method = typeof(MainForm).GetMethod("PrepareLocalCloudImage", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);
        string root = Path.Combine(Path.GetTempPath(), "jieshao-cloud-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string source = Path.Combine(root, "original.png");
        string? generatedFolder = null;
        try
        {
            using (var bitmap = new Bitmap(400, 800))
            {
                using var graphics = Graphics.FromImage(bitmap);
                graphics.Clear(Color.White);
                graphics.FillRectangle(Brushes.Red, 0, 760, 400, 40);
                bitmap.Save(source, System.Drawing.Imaging.ImageFormat.Png);
            }
            byte[] original = File.ReadAllBytes(source);
            object?[] untouched = ["其他群", source, new[] { rule }, null];
            Assert.Equal(source, method.Invoke(null, untouched));
            Assert.Null(untouched[3]);

            object?[] args = ["9.2-嫣然心水", source, new[] { rule }, null];
            string output = (string)method.Invoke(null, args)!;
            generatedFolder = (string?)args[3];
            Assert.NotEqual(source, output);
            using var compact = new Bitmap(output);
            Assert.Equal(300, compact.Width);
            Assert.Equal(800, compact.Height);
            Assert.Equal(Color.Red.ToArgb(), compact.GetPixel(110, 780).ToArgb());
            Assert.Equal(original, File.ReadAllBytes(source));
        }
        finally
        {
            Directory.Delete(root, true);
            if (generatedFolder is not null) Directory.Delete(generatedFolder, true);
        }
    }

    [Theory]
    [InlineData(0, "鼠")]
    [InlineData(1, "0尾")]
    [InlineData(2, "5尾")]
    [InlineData(3, "龙猴羊蛇虎兔鸡马狗")]
    public void SelectsSeparateMaterialsWithinJieshaoFolder(int index, string expected)
    {
        OcrRule[] rules = LoadRules().Where(rule => Labels.Contains(rule.Id)).ToArray();
        Assert.Equal(Labels, rules.Select(rule => rule.Id));
        OcrRule rule = rules[index];
        Assert.Equal("杰少", rule.Folder);
        Assert.Equal(rule, Assert.Single(RuleEngine.FindMatches(@"C:\图片\9.2-嫣然心水\杰少\图.jpg", Samples[index], rules)));
        Assert.Empty(RuleEngine.FindMatches(@"C:\图片\9.2-嫣然心水\其他\图.jpg", Samples[index], rules));
        Assert.Equal(expected, RuleEngine.ExtractValue(Samples[index], 245, rule));
        Assert.Equal(expected, RuleEngine.ExtractFinalValue(Samples[index], 245, rule));
        Assert.Null(RuleEngine.ExtractFinalValue(Samples[index], 246, rule));
        if (index < 3)
        {
            string[] changed = Samples[index].Select(line => line.Replace("鼠开猫", "牛开猫").Replace("0尾", "7尾").Replace("5尾", "2尾")).ToArray();
            Assert.Equal(new[] { "牛", "7尾", "2尾" }[index], RuleEngine.ExtractFinalValue(changed, 245, rule));
        }
    }

    [Fact]
    public void DoesNotUseQingpingguoKillZodiacImageInTheSameFolder()
    {
        OcrRule[] rules = LoadRules().Where(rule => Labels.Contains(rule.Id)).ToArray();
        Assert.Equal(4, rules.Length);
        Assert.Empty(RuleEngine.FindMatches(@"C:\图片\嫣然心水\杰少\图.jpg",
            ["原创青苹果", "{245期}新澳杀①肖狗开猫50对"], rules));
    }

    [Fact]
    public void NineZodiacsStayInsideTheCurrentNineZodiacRow()
    {
        OcrRule rule = Assert.Single(LoadRules(), rule => rule.Id == "杰少九肖");
        Assert.Equal("兔羊猪牛鸡猴鼠马龙", RuleEngine.ExtractFinalValue(Samples[3], 244, rule));
        Assert.Equal("龙猴羊蛇虎兔鸡马狗", RuleEngine.ExtractFinalValue(
            ["原创杰少", "245期新澳彩九肖中特开猫准", "九肖", "龙猴羊蛇虎兔鸡马狗", "六肖龙猴羊蛇虎兔"], 245, rule));
        foreach (string invalid in new[] { "", "龙猴羊蛇虎兔鸡马", "龙猴羊蛇虎兔鸡马龙", "龙猴羊蛇虎兔鸡马狗牛" })
        {
            string[] lines = (string[])Samples[3].Clone();
            lines[3] = "九肖" + invalid;
            Assert.Null(RuleEngine.ExtractFinalValue(lines, 245, rule));
        }
        Assert.Null(RuleEngine.ExtractFinalValue(
            ["原创杰少", "245期新澳九肖开猫准", "六肖龙猴羊蛇虎兔", "三肖鸡马狗", "244期新澳九肖兔羊猪牛鸡猴鼠马龙"], 245, rule));
    }

    [Fact]
    public void CompactLocalCacheOnlyChangesForJieshaoInYanranMode()
    {
        string root = Path.Combine(Path.GetTempPath(), "jieshao-cache-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "杰少"));
        try
        {
            string path = Path.Combine(root, "杰少", "image.jpg");
            File.WriteAllBytes(path, [1]);
            MethodInfo method = typeof(PaddleLocalOcrClient).GetMethod("CacheKey", BindingFlags.NonPublic | BindingFlags.Static)!;
            string key = (string)method.Invoke(null, [path, 1.0, 960])!;
            Assert.Contains("compact", key);
            Assert.DoesNotContain("compact", (string)method.Invoke(null, [path, 1.0, null])!);
            string other = Path.Combine(root, "image.jpg");
            File.WriteAllBytes(other, [1]);
            Assert.DoesNotContain("compact", (string)method.Invoke(null, [other, 1.0, 960])!);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void ExtractsAllFourJieshaoOutputsFromOneCard()
    {
        OcrRule[] rules = LoadRules().Where(rule => rule.Folder == "杰少").ToArray();
        string[] lines = ["原创杰少", "{245期}新澳杀①肖：鼠开猫50准", "{245期}新澳加杀尾{0尾}开50尾准", "{245期}新澳禁一尾{5尾}开50尾对", "{245期新澳九肖}兔羊猪牛鸡猴鼠马龙{开鸡}准"];
        Assert.Equal("鼠", RuleEngine.ExtractFinalValue(lines, 245, Assert.Single(rules, r => r.Id == "杰少杀一肖")));
        Assert.Equal("0尾", RuleEngine.ExtractFinalValue(lines, 245, Assert.Single(rules, r => r.Id == "杰少杀一尾")));
        Assert.Equal("5尾", RuleEngine.ExtractFinalValue(lines, 245, Assert.Single(rules, r => r.Id == "杰少禁一尾")));
        Assert.Equal("兔羊猪牛鸡猴鼠马龙", RuleEngine.ExtractFinalValue(lines, 245, Assert.Single(rules, r => r.Id == "杰少九肖")));
    }

    [Theory]
    [InlineData("{244期}新澳杀①肖：鼠开猫50准")]
    [InlineData("{245期}新澳杀①肖：猫开猫50准")]
    [InlineData("{245期}新澳杀①肖：开猫50准")]
    [InlineData("{245期}新澳加杀尾{}开50尾准")]
    [InlineData("{245期}新澳禁一尾{尾}开50尾对")]
    [InlineData("{245期}新澳禁一尾{0尾}开50尾对")]
    [InlineData("{245期新澳九肖}开猫准")]
    [InlineData("{244期新澳九肖}兔羊猪牛鸡猴鼠马龙")]
    [InlineData("{245期}其他栏目：鼠开猫50准")]
    [InlineData("{244期}新澳九肖兔羊猪牛鸡猴鼠马龙")]
    [InlineData("{245期新澳九肖}兔羊猪牛鸡猴鼠马")]
    public void RejectsSixInvalidJieshaoRows(string invalidLine)
    {
        OcrRule[] rules = LoadRules().Where(rule => rule.Folder == "杰少").ToArray();
        string id = invalidLine.Contains("九肖", StringComparison.Ordinal) ? "杰少九肖" : "杰少杀一肖";
        Assert.Null(RuleEngine.ExtractFinalValue(["原创杰少", invalidLine], 245, Assert.Single(rules, rule => rule.Id == id)));
    }

    [Fact]
    public async Task RoutesAllFourItemsAndDeduplicatesManualReplay()
    {
        string root = Path.Combine(Path.GetTempPath(), "jieshao-routes-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            foreach (string category in new[] { "肖", "尾", "生肖" })
                await File.WriteAllLinesAsync(Path.Combine(root, $"245期-{category}.txt"), ["原有内容"]);
            string[] lines = ["鼠 杰少杀一肖", "0尾 杰少杀一尾", "5尾 杰少禁一尾", "龙猴羊蛇虎兔鸡马狗 杰少九肖"];
            string config = ResultFilePaths.ConfigurationDirectory(AppContext.BaseDirectory);
            for (int run = 0; run < 2; run++)
            {
                DistributionResult result = await ResultDistributor.DistributeAllAsync(@"C:\图片\9.2-嫣然心水", 245, lines, root, config);
                Assert.Empty(result.Errors);
                Assert.Equal(lines.Order(), result.DistributedLines.Order());
            }
            Assert.Equal(["原有内容", lines[0]], await File.ReadAllLinesAsync(Path.Combine(root, "245期-肖.txt")));
            Assert.Equal(["原有内容", lines[1], lines[2]], await File.ReadAllLinesAsync(Path.Combine(root, "245期-尾.txt")));
            Assert.Equal(["原有内容", lines[3]], await File.ReadAllLinesAsync(Path.Combine(root, "245期-生肖.txt")));
            Assert.Equal(3, Directory.GetFiles(root).Length);
        }
        finally { Directory.Delete(root, true); }
    }
}
