using System.Text;
using System.Text.Json;

namespace OcrLineTool.Tests;

/// <summary>
/// 输出层：目标 TXT 与归属记录是两个各自原子的文件，这里钉住它们之间的中间状态
/// 必须是「可自愈」的，而不是把用户文件留在无主行上永久卡住。
/// </summary>
public sealed class OutputTransactionTests : IDisposable
{
    private readonly string directory =
        Path.Combine(Path.GetTempPath(), "ocr-output-" + Guid.NewGuid().ToString("N"));

    public OutputTransactionTests() => Directory.CreateDirectory(directory);

    public void Dispose()
    {
        try { Directory.Delete(directory, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    [Fact]
    public async Task OwnerRecordWrittenBeforeTheTargetFileStillHeals()
    {
        // 模拟两次写之间中断：归属记录已经落盘，目标文件还是旧的。
        string target = Path.Combine(directory, "303期-五行.txt");
        await File.WriteAllTextAsync(target, "水行 甲" + Environment.NewLine, new UTF8Encoding(true));
        await WriteOwnersAsync("303期-五行.txt", [("嫣然心水", "黑字杀行", "木行 黑字杀行")]);

        IReadOnlySet<string> applied = await OwnedResultWriter.ApplyAsync(
            target, "嫣然心水", ["木行 黑字杀行"], new HashSet<string>(StringComparer.Ordinal), null, false);

        Assert.Contains("木行 黑字杀行", applied);
        Assert.Equal(["水行 甲", "木行 黑字杀行"], await File.ReadAllLinesAsync(target));
    }

    [Fact]
    public async Task RevokingAnUnwrittenOwnedRowJustDropsTheRecord()
    {
        string target = Path.Combine(directory, "303期-五行.txt");
        await File.WriteAllTextAsync(target, "水行 甲" + Environment.NewLine, new UTF8Encoding(true));
        await WriteOwnersAsync("303期-五行.txt", [("嫣然心水", "黑字杀行", "木行 黑字杀行")]);

        await OwnedResultWriter.ApplyAsync(target, "嫣然心水", [],
            new HashSet<string>(StringComparer.Ordinal) { "黑字杀行" }, null, false);

        Assert.Equal(["水行 甲"], await File.ReadAllLinesAsync(target));
        Assert.Empty(await OwnedLinesAsync("303期-五行.txt"));
    }

    [Fact]
    public async Task ReapplyingTheSameValueIsIdempotentAndLeavesNoTemporaryFiles()
    {
        string target = Path.Combine(directory, "303期-五行.txt");
        await File.WriteAllTextAsync(target, "水行 甲" + Environment.NewLine, new UTF8Encoding(true));

        await OwnedResultWriter.ApplyAsync(target, "嫣然心水", ["木行 黑字杀行"],
            new HashSet<string>(StringComparer.Ordinal), null, false);
        await OwnedResultWriter.ApplyAsync(target, "嫣然心水", ["木行 黑字杀行"],
            new HashSet<string>(StringComparer.Ordinal), null, false);

        Assert.Equal(["水行 甲", "木行 黑字杀行"], await File.ReadAllLinesAsync(target));
        Assert.Empty(Directory.GetFiles(directory, "*.tmp*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task OwnershipNeverClaimsARowAnotherGroupAlreadyPrinted()
    {
        string target = Path.Combine(directory, "303期-五行.txt");
        await File.WriteAllTextAsync(target, "木行 黑字杀行" + Environment.NewLine, new UTF8Encoding(true));

        await OwnedResultWriter.ApplyAsync(target, "嫣然心水", ["木行 黑字杀行"],
            new HashSet<string>(StringComparer.Ordinal), null, false);

        // 行已经存在，就不该被认领，否则以后撤销会把别人的行删掉；
        // 没有任何改动，所以连归属记录都不该产生。
        Assert.Equal(["木行 黑字杀行"], await File.ReadAllLinesAsync(target));
        Assert.False(File.Exists(OwnersPath("303期-五行.txt")));
    }

    [Fact]
    public async Task UpdateThatCannotWriteTheTargetRestoresTheOwnerRecord()
    {
        // 目标 TXT 被 Excel/WPS 打开时读得出来、写不进去。这时候归属记录不能停在
        // 新值上，否则这一期之后每次重跑都会判成「不同值且归属不确定」而永久卡死。
        string target = Path.Combine(directory, "303期-五行.txt");
        await File.WriteAllTextAsync(target, "木行 黑字杀行" + Environment.NewLine, new UTF8Encoding(true));
        await WriteOwnersAsync("303期-五行.txt", [("嫣然心水", "黑字杀行", "木行 黑字杀行")]);
        File.SetAttributes(target, FileAttributes.ReadOnly);
        try
        {
            await Assert.ThrowsAsync<OcrException>(() => OwnedResultWriter.ApplyAsync(
                target, "嫣然心水", ["水行 黑字杀行"], new HashSet<string>(StringComparer.Ordinal), null, false));
        }
        finally
        {
            File.SetAttributes(target, FileAttributes.Normal);
        }

        Assert.Equal(["木行 黑字杀行"], await File.ReadAllLinesAsync(target));
        Assert.Equal(["木行 黑字杀行"], await OwnedLinesAsync("303期-五行.txt"));

        // 关掉占用它的程序之后，同一次改值必须能正常做完。
        await OwnedResultWriter.ApplyAsync(
            target, "嫣然心水", ["水行 黑字杀行"], new HashSet<string>(StringComparer.Ordinal), null, false);
        Assert.Equal(["水行 黑字杀行"], await File.ReadAllLinesAsync(target));
        Assert.Equal(["水行 黑字杀行"], await OwnedLinesAsync("303期-五行.txt"));
    }

    [Fact]
    public async Task RevokeThatCannotWriteTheTargetRestoresTheOwnerRecord()
    {
        // 撤销方向同样的道理：归属记录先删掉、目标写不进去，那一行就成了永远撤不掉的无主行。
        string target = Path.Combine(directory, "303期-五行.txt");
        await File.WriteAllTextAsync(target, "水行 甲" + Environment.NewLine + "木行 黑字杀行" + Environment.NewLine,
            new UTF8Encoding(true));
        await WriteOwnersAsync("303期-五行.txt", [("嫣然心水", "黑字杀行", "木行 黑字杀行")]);
        File.SetAttributes(target, FileAttributes.ReadOnly);
        try
        {
            await Assert.ThrowsAsync<OcrException>(() => OwnedResultWriter.ApplyAsync(
                target, "嫣然心水", [], new HashSet<string>(StringComparer.Ordinal) { "黑字杀行" }, null, false));
        }
        finally
        {
            File.SetAttributes(target, FileAttributes.Normal);
        }

        Assert.Contains("木行 黑字杀行", await File.ReadAllLinesAsync(target));
        Assert.Equal(["木行 黑字杀行"], await OwnedLinesAsync("303期-五行.txt"));

        await OwnedResultWriter.ApplyAsync(
            target, "嫣然心水", [], new HashSet<string>(StringComparer.Ordinal) { "黑字杀行" }, null, false);
        Assert.Equal(["水行 甲"], await File.ReadAllLinesAsync(target));
    }

    // 归属记录里的中文会被 JsonSerializer 转义，所以按 JSON 解析后比对，别直接搜原文。
    private async Task<string[]> OwnedLinesAsync(string fileName)
    {
        string path = OwnersPath(fileName);
        if (!File.Exists(path))
            return [];
        using JsonDocument document = JsonDocument.Parse(await File.ReadAllTextAsync(path));
        return document.RootElement.EnumerateArray()
            .Select(item => item.GetProperty("Line").GetString()!)
            .ToArray();
    }

    private string OwnersPath(string fileName) =>
        Path.Combine(directory, ".ocr-state", fileName + ".owners.json");

    private async Task WriteOwnersAsync(string fileName, (string SourceGroup, string Label, string Line)[] owners)
    {
        string path = OwnersPath(fileName);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string json = "[" + string.Join(",", owners.Select(item =>
            $"{{\"SourceGroup\":\"{item.SourceGroup}\",\"Label\":\"{item.Label}\",\"Line\":\"{item.Line}\"}}")) + "]";
        await File.WriteAllTextAsync(path, json, new UTF8Encoding(false));
    }
}
