using OcrLineTool;

namespace OcrLineTool.Tests;

public sealed class LineExtractorTests
{
    [Fact]
    public void ExtractsTheWholeRequestedIssueLine()
    {
        string[] lines =
        [
            "240期：新澳门【天机阁论坛●南国挽心㊣㊣㊣杀一肖】鸡 开龙 对",
            "241期：新澳门【天机阁论坛●南国挽心㊣㊣㊣杀一肖】猴"
        ];

        var actual = LineExtractor.Extract(lines, 241);

        Assert.Equal("241期：新澳门【天机阁论坛●南国挽心㊣㊣㊣杀一肖】猴", actual);
    }

    [Fact]
    public void ToleratesOcrSpacesAroundIssueMarker()
    {
        string[] lines = [" 241 期 : 新澳门【天机阁论坛●南国挽心㊣㊣㊣杀一肖】猴 "];

        var actual = LineExtractor.Extract(lines, 241);

        Assert.Equal("241期：新澳门【天机阁论坛●南国挽心㊣㊣㊣杀一肖】猴", actual);
    }

    [Fact]
    public void NormalizesAsciiBracketsCommonlyReturnedForChineseBrackets()
    {
        string[] lines = ["241期:新澳门[天机阁论坛●南国挽心㊣㊣㊣杀一肖]猴"];

        var actual = LineExtractor.Extract(lines, 241);

        Assert.Equal("241期：新澳门【天机阁论坛●南国挽心㊣㊣㊣杀一肖】猴", actual);
    }

    [Fact]
    public void RestoresCircledZhengInTheKnownForumPhrase()
    {
        string[] lines = ["241期:新澳门[天机阁论坛●南国挽心正正正杀一肖]猴"];

        var actual = LineExtractor.Extract(lines, 241);

        Assert.Equal("241期：新澳门【天机阁论坛●南国挽心㊣㊣㊣杀一肖】猴", actual);
    }

    [Fact]
    public void DoesNotMatchAnotherIssueContainingTheSameDigits()
    {
        string[] lines = ["1241期：错误行", "2410期：错误行"];

        var actual = LineExtractor.Extract(lines, 241);

        Assert.Null(actual);
    }

    [Fact]
    public void ReturnsNullWhenIssueIsMissing()
    {
        var actual = LineExtractor.Extract(["240期：内容"], 241);

        Assert.Null(actual);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void RejectsInvalidIssueNumber(int issue)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => LineExtractor.Extract([], issue));
    }
}
