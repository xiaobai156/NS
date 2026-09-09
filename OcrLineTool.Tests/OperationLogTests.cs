using OcrLineTool;

namespace OcrLineTool.Tests;

public sealed class OperationLogTests : IDisposable
{
    private readonly string appDirectory = Path.Combine(Path.GetTempPath(), $"OcrLineTool-log-{Guid.NewGuid():N}");

    [Fact]
    public void AppendsMultipleOperationsForTheSameDay()
    {
        OperationLog.Append(appDirectory, new DateTime(2026, 9, 3, 8, 1, 2), "开始识别", "新澳六合彩资料", 246);
        OperationLog.Append(appDirectory, new DateTime(2026, 9, 3, 8, 2, 3), "复制结果", "新澳六合彩资料", 246);

        string[] lines = File.ReadAllLines(ResultFilePaths.ForOperationLog(appDirectory));

        Assert.Equal(2, lines.Length);
        Assert.Equal("2026-09-03 08:01:02\t开始识别\t群=新澳六合彩资料\t期=246", lines[0]);
        Assert.Equal("2026-09-03 08:02:03\t复制结果\t群=新澳六合彩资料\t期=246", lines[1]);
    }

    [Fact]
    public void ANewDayDiscardsThePreviousDaysOperations()
    {
        OperationLog.Append(appDirectory, new DateTime(2026, 9, 3, 23, 59, 59), "开始识别", "嫣然心水", 246);
        OperationLog.Append(appDirectory, new DateTime(2026, 9, 4, 0, 0, 1), "手动复抓缺失", "嫣然心水", 247);

        string content = File.ReadAllText(ResultFilePaths.ForOperationLog(appDirectory));

        Assert.DoesNotContain("开始识别", content);
        Assert.Contains("2026-09-04 00:00:01\t手动复抓缺失\t群=嫣然心水\t期=247", content);
    }

    [Fact]
    public void ClearsExpiredLogWithoutAFollowingOperation()
    {
        OperationLog.Append(appDirectory, new DateTime(2026, 9, 3, 23, 59, 59), "开始识别", null, 246);

        OperationLog.ClearIfExpired(appDirectory, new DateTime(2026, 9, 4, 0, 0, 1));

        Assert.Empty(File.ReadAllText(ResultFilePaths.ForOperationLog(appDirectory)));
    }

    [Fact]
    public void DoesNotClearTheCurrentDaysLog()
    {
        OperationLog.Append(appDirectory, new DateTime(2026, 9, 3, 8, 0, 0), "开始识别", null, 246);

        OperationLog.ClearIfExpired(appDirectory, new DateTime(2026, 9, 3, 23, 59, 59));

        Assert.Contains("开始识别", File.ReadAllText(ResultFilePaths.ForOperationLog(appDirectory)));
    }

    [Fact]
    public void SanitizesControlCharactersAndCreatesTheLogDirectory()
    {
        OperationLog.Append(appDirectory, new DateTime(2026, 9, 3, 8, 0, 0), "复\r\n制\t结果", "群\n名", 246);

        string[] lines = File.ReadAllLines(ResultFilePaths.ForOperationLog(appDirectory));

        Assert.Single(lines);
        Assert.Equal("2026-09-03 08:00:00\t复  制 结果\t群=群 名\t期=246", lines[0]);
    }

    [Fact]
    public void ALogWriteFailureDoesNotInterruptTheUserOperation()
    {
        File.WriteAllText(appDirectory, "路径被文件占用");

        Exception? exception = Record.Exception(() =>
            OperationLog.Append(appDirectory, new DateTime(2026, 9, 3, 8, 0, 0), "开始识别", null, 246));

        Assert.Null(exception);
    }

    [Fact]
    public void RecordsPerItemFailureDetailsWithoutCreatingExtraLines()
    {
        OperationLog.Append(
            appDirectory,
            new DateTime(2026, 9, 3, 8, 0, 0),
            "识别缺失：金钱网；图片=sample.jpg；原因=已找到候选图片和246期文字，但未通过12个不同号码校验",
            "新澳六合彩资料",
            246);

        string line = Assert.Single(File.ReadAllLines(ResultFilePaths.ForOperationLog(appDirectory)));
        Assert.Contains("识别缺失：金钱网", line);
        Assert.Contains("图片=sample.jpg", line);
        Assert.Contains("12个不同号码校验", line);
    }

    [Fact]
    public void ExceptionLogRecordsLocationButNeverExceptionPayloads()
    {
        Exception failure = Assert.Throws<InvalidOperationException>(ThrowSensitiveFailure);

        OperationLog.AppendException(appDirectory, "开始识别", "嫣然心水", 250, failure);

        string line = Assert.Single(File.ReadAllLines(ResultFilePaths.ForOperationLog(appDirectory)));
        Assert.Contains("System.InvalidOperationException", line);
        Assert.Contains(nameof(ThrowSensitiveFailure), line);
        Assert.Contains("IL=", line);
        Assert.Contains("MVID=", line);
        Assert.Contains("群=嫣然心水", line);
        Assert.DoesNotContain("PRIVATE", line);
    }

    [Fact]
    public void ExceptionLogIncludesInnerTypeWithoutRequiringAStack()
    {
        var failure = new InvalidOperationException("PRIVATE outer", new IOException("PRIVATE inner"));

        OperationLog.AppendException(appDirectory, "本地主识别", null, 250, failure);

        string line = Assert.Single(File.ReadAllLines(ResultFilePaths.ForOperationLog(appDirectory)));
        Assert.Contains("System.InvalidOperationException", line);
        Assert.Contains("System.IO.IOException", line);
        Assert.DoesNotContain("PRIVATE", line);
    }

    [Fact]
    public void ExceptionLogWriteFailureDoesNotReplaceTheOriginalFailure()
    {
        File.WriteAllText(appDirectory, "路径被文件占用");
        Assert.Null(Record.Exception(() => OperationLog.AppendException(
            appDirectory, "开始识别", null, 250, new IOException("PRIVATE"))));
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static void ThrowSensitiveFailure()
    {
        var failure = new InvalidOperationException("PRIVATE secret OCR payload https://example.test?token=PRIVATE");
        failure.Data["PRIVATE"] = "PRIVATE";
        failure.Source = "PRIVATE";
        throw failure;
    }

    public void Dispose()
    {
        if (Directory.Exists(appDirectory))
            Directory.Delete(appDirectory, recursive: true);
        else if (File.Exists(appDirectory))
            File.Delete(appDirectory);
    }
}
