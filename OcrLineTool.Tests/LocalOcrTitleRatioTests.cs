using OcrLineTool;

namespace OcrLineTool.Tests;

public sealed class LocalOcrTitleRatioTests
{
    [Fact]
    public void PosterRuleReadsTheWholeImageWhileTemplateTablesKeepTheTopRatio()
    {
        const string liuCai = @"C:\图片\9.17-新澳六合彩资料";

        Assert.Equal(0.4, PaddleLocalOcrClient.TitleRatioFor(liuCai));
        Assert.Equal(0.4, PaddleLocalOcrClient.TitleRatioFor(liuCai, ["小马哥", "通天九九肖"]));
        Assert.Equal(1.0, PaddleLocalOcrClient.TitleRatioFor(liuCai, ["小马哥", "亮剑九肖"]));
        Assert.Equal(1.0, PaddleLocalOcrClient.TitleRatioFor(@"C:\图片\9.17-嫣然心水"));
        Assert.Equal(1.0, PaddleLocalOcrClient.TitleRatioFor(@"C:\图片\9.17-嫣然心水", ["亮剑九肖"]));
    }
}
