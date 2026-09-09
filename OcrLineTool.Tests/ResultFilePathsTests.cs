using OcrLineTool;

namespace OcrLineTool.Tests;

public sealed class ResultFilePathsTests
{
    [Fact]
    public void KeepsTheMissingSummaryBesideGroupResults()
    {
        Assert.Equal(@"C:\工具\重要结果\群结果\缺失及未分流汇总.txt", ResultFilePaths.ForMissingSummary(@"C:\工具"));
    }

    [Theory]
    [InlineData(@"C:\工具", @"C:\图片\新澳六合彩资料", 242, @"C:\工具\重要结果\群结果\新澳六合彩资料_242期.txt")]
    [InlineData(@"C:\工具", @"C:\图片\嫣然心水\", 243, @"C:\工具\重要结果\群结果\嫣然心水_243期.txt")]
    [InlineData(@"C:\工具", @"C:\图片\新澳高级会员", 244, @"C:\工具\重要结果\群结果\新澳高级会员_244期.txt")]
    [InlineData(@"C:\工具", @"C:\图片\8.31-新澳六合彩资料", 245, @"C:\工具\重要结果\群结果\新澳六合彩资料_245期.txt")]
    [InlineData(@"C:\工具", @"C:\图片\242期-嫣然心水\", 246, @"C:\工具\重要结果\群结果\嫣然心水_246期.txt")]
    public void UsesTheSelectedFolderNameAsTheTextFileName(
        string outputDirectory, string selectedDirectory, int issue, string expected)
    {
        Assert.Equal(expected, ResultFilePaths.ForGroup(outputDirectory, selectedDirectory, issue));
    }

    [Fact]
    public void SeparatesImportantResultsAndTemporaryDiagnostics()
    {
        Assert.Equal(@"C:\工具\临时文件\诊断\OCR诊断_242期.json", ResultFilePaths.ForDiagnostic(@"C:\工具", 242));
        Assert.Equal(
            @"C:\工具\临时文件\诊断\OCR诊断_新澳六合彩资料_242期.json",
            ResultFilePaths.ForDiagnostic(@"C:\工具", @"C:\图片\9.3-新澳六合彩资料", 242));
    }

    [Fact]
    public void UsesTheConfiguredCanonicalGroupNameForDatedFolders()
    {
        string path = ResultFilePaths.ForGroup(
            AppContext.BaseDirectory,
            @"C:\图片\8.31-新澳高级会员",
            243);

        Assert.EndsWith(
            Path.Combine("重要结果", "群结果", "新澳高级会员_243期.txt"),
            path,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void KeepsAnUnmatchedSuffixOutOfTheCanonicalGroupResultName()
    {
        string path = ResultFilePaths.ForGroup(
            AppContext.BaseDirectory,
            @"C:\图片\8.31-新澳六合彩资料-备份",
            243);

        Assert.EndsWith(
            Path.Combine("重要结果", "群结果", "新澳六合彩资料-备份_243期.txt"),
            path,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ProvidesTheExactFoldersUsedByOpenAndClearButtons()
    {
        Assert.Equal(@"C:\工具\重要结果\群结果", ResultFilePaths.GroupResultsDirectory(@"C:\工具"));
        Assert.Equal(@"C:\工具\重要结果", ResultFilePaths.ImportantResultsDirectory(@"C:\工具"));
        Assert.Equal(@"C:\工具\临时文件", ResultFilePaths.TemporaryFilesDirectory(@"C:\工具"));
        Assert.Equal(@"C:\工具\本地日志\操作日志.txt", ResultFilePaths.ForOperationLog(@"C:\工具"));
    }

    [Fact]
    public void SeparatesConfigurationAndRuntimeComponents()
    {
        Assert.Equal(@"C:\工具\配置文件", ResultFilePaths.ConfigurationDirectory(@"C:\工具"));
        Assert.Equal(@"C:\工具\运行组件", ResultFilePaths.RuntimeDirectory(@"C:\工具"));
    }
}
