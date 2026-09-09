using OcrLineTool;
using System.Text;

namespace OcrLineTool.Tests;

public sealed class MissingResultSummaryTests : IDisposable
{
    private readonly string appDirectory = Directory.CreateTempSubdirectory("ocr-missing-summary-").FullName;

    [Fact]
    public async Task GroupsAllResultFilesByGroupThenCategoryWithoutIssueSections()
    {
        string first = Write("甲群_245期.txt",
            "【尾】\n缺失（未找到对应图片） 尾资料\n8尾 正常尾（已分流）\n\n【一肖】\n虎 未分流肖\n牛 正常肖（已分流）\n【头】\n1头 正常头（已分流）");
        string second = Write("甲群_246期.txt", "【尾】\n2尾 第二份尾\n【九肖】\n缺失（未识别到当期目标数据） 九肖资料");
        string third = Write("新增群_246期.txt", "【半波】\n蓝单 半波资料\n【五行】\n缺失（图片文字识别失败） 五行资料");
        Write("备注.txt", "不是群结果");
        byte[] original = File.ReadAllBytes(first);

        var result = await MissingResultSummary.WriteAsync(appDirectory);

        Assert.Equal(6, result.Count);
        Assert.Equal(ResultFilePaths.ForMissingSummary(appDirectory), result.Path);
        Assert.Equal(
        [
            "新增群", "【半波】", "蓝单 半波资料 —— 新增群", "", "【五行】", "缺失（图片文字识别失败） 五行资料 —— 新增群",
            "", "甲群", "【尾】", "缺失（未找到对应图片） 尾资料 —— 甲群", "2尾 第二份尾 —— 甲群",
            "", "【一肖】", "虎 未分流肖 —— 甲群", "", "【九肖】", "缺失（未识别到当期目标数据） 九肖资料 —— 甲群"
        ], await File.ReadAllLinesAsync(result.Path));
        Assert.Equal(original, File.ReadAllBytes(first));
        Assert.True(File.Exists(second) && File.Exists(third));
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, File.ReadAllBytes(result.Path).Take(3));
    }

    [Fact]
    public async Task RebuildsTheSameFileWithoutReadingItsOwnOldContents()
    {
        Write("甲群_245期.txt", "【尾】\n3尾 资料");
        var first = await MissingResultSummary.WriteAsync(appDirectory);
        byte[] previous = File.ReadAllBytes(first.Path);
        var second = await MissingResultSummary.WriteAsync(appDirectory);
        Assert.Equal(first, second);
        Assert.Equal(previous, File.ReadAllBytes(second.Path));

        Write("甲群_245期.txt", "【尾】\n3尾 资料（已分流）");
        var cleared = await MissingResultSummary.WriteAsync(appDirectory);
        Assert.Equal(0, cleared.Count);
        Assert.Equal(["没有缺失或未分流的数据。"], await File.ReadAllLinesAsync(cleared.Path));
        Assert.Equal(2, Directory.GetFiles(ResultFilePaths.GroupResultsDirectory(appDirectory)).Length);
    }

    [Fact]
    public async Task HandlesEmptyDirectoryAndPreservesUnknownCategoriesAndUncategorizedData()
    {
        var empty = await MissingResultSummary.WriteAsync(appDirectory);
        Assert.Equal(0, empty.Count);
        Write("新群_1期.TXT", "缺失 旧格式资料\n【新种类】\n数据 新资料\n\n【尾】\n4尾 已分流尾（已分流）  ");
        var result = await MissingResultSummary.WriteAsync(appDirectory);
        Assert.Equal(2, result.Count);
        Assert.Equal(["新群", "【其他】", "缺失 旧格式资料 —— 新群", "", "【新种类】", "数据 新资料 —— 新群"], await File.ReadAllLinesAsync(result.Path));
    }

    [Fact]
    public async Task LeavesPreviousSummaryAndSourcesIntactIfAnySourceCannotBeRead()
    {
        string source = Write("甲群_245期.txt", "【尾】\n3尾 资料");
        var previous = await MissingResultSummary.WriteAsync(appDirectory);
        byte[] oldSummary = File.ReadAllBytes(previous.Path);
        using (FileStream locked = File.Open(source, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            await Assert.ThrowsAsync<IOException>(() => MissingResultSummary.WriteAsync(appDirectory));
        Assert.Equal(oldSummary, File.ReadAllBytes(previous.Path));
        Assert.Equal("【尾】\n3尾 资料", File.ReadAllText(source));
    }

    private string Write(string name, string text)
    {
        string directory = ResultFilePaths.GroupResultsDirectory(appDirectory);
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, name);
        File.WriteAllText(path, text, new UTF8Encoding(true));
        return path;
    }

    [Fact]
    public async Task UsesEachRulesChildFolderAndIgnoresLegacyGroupFolderSuffixes()
    {
        string config = ResultFilePaths.ConfigurationDirectory(appDirectory);
        Directory.CreateDirectory(config);
        File.WriteAllText(Path.Combine(config, "嫣然心水.json"), """
            {"group":"嫣然心水","rules":[
              {"keyword":"齐天大圣","type":"头","folder":"天机阁杀料"},
              {"keyword":"永卟弃","label":"永卟弃杀头","type":"头","folder":"乖乖团队"},
              {"keyword":"蓝色","type":"号码:36","folder":"36码"}]}
            """);
        string path = Write("嫣然心水_251期.txt", """
            【头】
            3头 齐天大圣 —— 子文件夹=9.8-嫣然心水
            缺失（未找到对应图片） 永卟弃杀头 —— 子文件夹=9.8-嫣然心水
             —— 子文件夹=9.8-嫣然心水
            1头 齐天大圣（已分流） —— 子文件夹=9.8-嫣然心水
            【30个以上数字】
            01,02 蓝色
            """);
        byte[] original = File.ReadAllBytes(path);
        var result = await MissingResultSummary.WriteAsync(appDirectory);
        string[] lines = File.ReadAllLines(result.Path);
        Assert.Equal(3, result.Count);
        Assert.Contains("3头 齐天大圣 —— 嫣然心水 —— 天机阁杀料", lines);
        Assert.Contains("缺失（未找到对应图片） 永卟弃杀头 —— 嫣然心水 —— 乖乖团队", lines);
        Assert.Contains("01,02 蓝色 —— 嫣然心水 —— 36码", lines);
        Assert.DoesNotContain(lines, line => line.Contains("9.8-") || line.Contains("子文件夹=") || line.Contains("（已分流）"));
        Assert.Equal(original, File.ReadAllBytes(path));
        await MissingResultSummary.WriteAsync(appDirectory);
        Assert.Equal(lines, File.ReadAllLines(result.Path));
    }

    [Fact]
    public async Task MatchesWholeLabelAndRetainsExplicitUnroutedStatus()
    {
        Directory.CreateDirectory(ResultFilePaths.ConfigurationDirectory(appDirectory));
        File.WriteAllText(Path.Combine(ResultFilePaths.ConfigurationDirectory(appDirectory), "甲群.json"), """
            {"rules":[{"keyword":"资料","type":"尾","folder":"专属目录"}]}
            """);
        Write("甲群_252期.txt", "【尾】\n3尾 资料（未分流）\n2尾 其他资料");
        var result = await MissingResultSummary.WriteAsync(appDirectory);
        Assert.Contains("3尾 资料（未分流） —— 甲群 —— 专属目录", File.ReadAllLines(result.Path));
        Assert.Contains("2尾 其他资料 —— 甲群", File.ReadAllLines(result.Path));
    }

    [Fact]
    public async Task ResolvesUnconfiguredFolderFromUniqueDiagnosticImageWithoutOcr()
    {
        string config = ResultFilePaths.ConfigurationDirectory(appDirectory);
        Directory.CreateDirectory(config);
        File.WriteAllText(Path.Combine(config, "甲群.json"), """
            {"rules":[{"keyword":"资料","type":"尾"}]}
            """);
        string imageRoot = Path.Combine(appDirectory, "images");
        string group = Path.Combine(imageRoot, "9.8-甲群");
        string folder = Path.Combine(group, "真实子目录");
        Directory.CreateDirectory(folder);
        File.WriteAllBytes(Path.Combine(folder, "sample.jpg"), [1, 2]);
        string diagnostic = ResultFilePaths.ForDiagnostic(appDirectory, "甲群", 251);
        Directory.CreateDirectory(Path.GetDirectoryName(diagnostic)!);
        File.WriteAllText(diagnostic, """
            {"images":[{"file":"sample.jpg","rules":["资料"]}]}
            """);
        Write("甲群_251期.txt", "【尾】\n3尾 资料");
        var result = await MissingResultSummary.WriteAsync(appDirectory, imageRoot);
        Assert.Contains("3尾 资料 —— 甲群 —— 真实子目录", File.ReadAllLines(result.Path));
        string second = Path.Combine(group, "不同目录");
        Directory.CreateDirectory(second);
        File.WriteAllBytes(Path.Combine(second, "sample.jpg"), [1]);
        result = await MissingResultSummary.WriteAsync(appDirectory, imageRoot);
        Assert.Contains("3尾 资料 —— 甲群", File.ReadAllLines(result.Path));
    }

    [Fact]
    public async Task FailedReplacementPreservesThePreviousSummaryAndRemovesItsTemporaryFile()
    {
        Write("甲群_245期.txt", "【尾】\n3尾 资料");
        var result = await MissingResultSummary.WriteAsync(appDirectory);
        byte[] previous = File.ReadAllBytes(result.Path);
        Write("甲群_245期.txt", "【尾】\n3尾 资料（已分流）");
        using (FileStream locked = File.Open(result.Path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Exception? error = await Record.ExceptionAsync(() => MissingResultSummary.WriteAsync(appDirectory));
            Assert.True(error is IOException or UnauthorizedAccessException);
        }
        Assert.Equal(previous, File.ReadAllBytes(result.Path));
        Assert.Empty(Directory.GetFiles(ResultFilePaths.GroupResultsDirectory(appDirectory), "*.tmp"));
    }

    public void Dispose() => Directory.Delete(appDirectory, recursive: true);
}
