using OcrLineTool;

namespace OcrLineTool.Tests;

public sealed class FailureLogTests
{
    private static readonly DateOnly Day = new(2026, 9, 15);
    private static readonly DateTime Stamp = new(2026, 9, 15, 22, 40, 0);

    [Fact]
    public void WriteGroupCreatesOneFilePerGroupAndRefreshesItsIssueSection()
    {
        string directory = Directory.CreateTempSubdirectory("ocr-failure-log-").FullName;
        try
        {
            FailureLog.WriteGroup(
                directory, Day, "嫣然心水", 258, Stamp,
                [
                    ("华林尾", null, "未找到对应图片"),
                    ("雨后星星", "20260915_184943_82797.jpg", "已找到图片和文字，但未识别到第258期"),
                    ("爱晚亭", "20260915_185228_82842.jpg", "已找到候选图片和258期文字，但未通过5个两位数号码校验（要求01-49且不重复）")
                ]);
            FailureLog.WriteGroup(
                directory, Day, "嫣然心水", 257, Stamp,
                [("大哥6688", null, "未找到对应图片")]);
            FailureLog.WriteGroup(
                directory, Day, "新澳高手", 258, Stamp,
                [("高山流水", "20260915_204422_157003.jpg", "已找到258期文字，但未通过9个不同生肖校验")]);

            string yanran = FailureLog.PathFor(directory, Day, "嫣然心水");
            string gaoshou = FailureLog.PathFor(directory, Day, "新澳高手");
            Assert.True(File.Exists(yanran));
            Assert.True(File.Exists(gaoshou));
            string first = File.ReadAllText(yanran, System.Text.Encoding.UTF8);
            Assert.Contains("258期", first, StringComparison.Ordinal);
            Assert.Contains("257期", first, StringComparison.Ordinal);
            Assert.Contains("未找到对应图片（1）", first, StringComparison.Ordinal);
            Assert.Contains("华林尾", first, StringComparison.Ordinal);
            Assert.Contains("图片=20260915_184943_82797.jpg", first, StringComparison.Ordinal);
            Assert.Contains("未通过校验（1）", first, StringComparison.Ordinal);
            Assert.DoesNotContain("高山流水", first, StringComparison.Ordinal);

            // 只刷新 258 期那一段：257 期保留，258 期换成最新内容。
            FailureLog.WriteGroup(
                directory, Day, "嫣然心水", 258, Stamp, [("青苹果", null, "未找到对应图片")]);
            string refreshed = File.ReadAllText(yanran, System.Text.Encoding.UTF8);
            Assert.Contains("大哥6688", refreshed, StringComparison.Ordinal);
            Assert.Contains("青苹果", refreshed, StringComparison.Ordinal);
            Assert.DoesNotContain("华林尾", refreshed, StringComparison.Ordinal);
            Assert.Equal(2, refreshed.Split("==========", StringSplitOptions.RemoveEmptyEntries).Length / 2);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void WriteGroupRecordsAnEmptyRun()
    {
        string directory = Directory.CreateTempSubdirectory("ocr-failure-log-empty-").FullName;
        try
        {
            FailureLog.WriteGroup(directory, Day, "黄大仙新澳", 258, Stamp, []);

            string content = File.ReadAllText(FailureLog.PathFor(directory, Day, "黄大仙新澳"), System.Text.Encoding.UTF8);

            Assert.Contains("失败 0 条", content, StringComparison.Ordinal);
            Assert.Contains("（本次没有失败项）", content, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void CleanupBeforeKeepsOnlyTheRequestedDay()
    {
        string directory = Directory.CreateTempSubdirectory("ocr-failure-log-clean-").FullName;
        try
        {
            FailureLog.WriteGroup(directory, Day, "嫣然心水", 258, Stamp, [("青苹果", null, "未找到对应图片")]);
            FailureLog.WriteGroup(directory, Day.AddDays(1), "嫣然心水", 258, Stamp, [("华林尾", null, "未找到对应图片")]);

            FailureLog.CleanupBefore(directory, Day.AddDays(1));

            Assert.False(Directory.Exists(FailureLog.DirectoryFor(directory, Day)));
            Assert.True(File.Exists(FailureLog.PathFor(directory, Day.AddDays(1), "嫣然心水")));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void CategoryForMapsTheKnownReasons()
    {
        Assert.Equal("未找到对应图片", FailureLog.CategoryFor("未找到对应图片"));
        Assert.Equal("图片文字识别失败", FailureLog.CategoryFor("图片文字识别失败"));
        Assert.Equal("同一期结果冲突", FailureLog.CategoryFor("同一期结果冲突，待核对"));
        Assert.Equal("未识别到目标数据", FailureLog.CategoryFor("已找到图片和文字，但未识别到第258期"));
        Assert.Equal("未通过校验", FailureLog.CategoryFor("已找到258期文字，但未通过9个不同生肖校验"));
        Assert.Equal("其他失败", FailureLog.CategoryFor("意外情况"));
    }
}
