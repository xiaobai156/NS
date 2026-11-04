using System.IO;
using System.Linq;
using System.Text.Json;
using OcrLineTool;
using Xunit;

namespace OcrLineTool.Tests;

public sealed class LiulianDanceKillCodeTests
{
    private static OcrRule Rule => RuleCatalog.Load(Path.Combine(
        ResultFilePaths.ConfigurationDirectory(AppContext.BaseDirectory), "嫣然心水.json"))
        .Single(rule => rule.Id == "榴莲上跳舞");

    private static readonly string[] Head =
    [
        "【261期起: ☆☆☆☆☆☆★☆☆】",
        "【271期起: ☆☆☆☆☆☆☆☆☆】",
        "【301期起: ☆☆☆☆☆☆】",
        "【305期】【天机阁团队在榴莲上跳舞新澳杀码】20,21,23,31,(4码)开26 对"
    ];

    [Theory]
    [InlineData("306期 【天机阁团队在榴莲上跳舞新澳杀码】 30,29,33,36")]
    [InlineData("306期 【天机阁团队在榴莲上跳舞新澳杀码】 30 29 33 36 (4码)开15 对")]
    [InlineData("306期 【天机阁团队在榴莲上跳舞新澳杀码】 30 29 33 36 (4码)")]
    [InlineData("306期 【天机阁团队在榴莲上跳舞新澳杀码】 30,29,33,36,(4码)开15 对")]
    [InlineData("306期 【天机阁团队在榴莲上跳舞新澳杀码】 30 29 33 36 （4码）开15 对")]
    [InlineData("【306期】【天机阁团队在榴莲上跳舞新澳杀码】30 29 33 36")]
    public void KillsFourNumbersEvenWithThePrintedCardinality(string row)
    {
        string? value = RuleEngine.ExtractFinalValue([.. Head, row], 306, Rule);

        Assert.Equal("30 29 33 36", value);
    }

    [Fact]
    public void ReadsThisIssueInsteadOfThePreviousOne()
    {
        string? value = RuleEngine.ExtractFinalValue(
            [.. Head, "306期 【天机阁团队在榴莲上跳舞新澳杀码】 30,29,33,36,(4码)开15 对"], 306, Rule);

        Assert.Equal("30 29 33 36", value);
    }

    [Fact]
    public async Task FourNumbersReachTheKillFiveFileAndOtherCountsAreRejected()
    {
        string folder = CreateTempFolder();
        try
        {
            string config = Path.Combine(folder, "杀五码分发规则.json");
            string source = Path.Combine(
                ResultFilePaths.ConfigurationDirectory(AppContext.BaseDirectory), "杀五码分发规则.json");
            using JsonDocument document = JsonDocument.Parse(await File.ReadAllTextAsync(source));
            await File.WriteAllTextAsync(config, document.RootElement.GetRawText());
            string target = Path.Combine(folder, "306期-杀五码-成功.txt");
            await File.WriteAllTextAsync(target, "原有内容\r\n");

            IReadOnlySet<string> distributed = await ResultDistributor.DistributeAsync(
                @"C:\图片\嫣然心水",
                306,
                ["30,29,33,36 榴莲上跳舞", "30,29,33 榴莲上跳舞", "30,29,33,36,45 榴莲上跳舞"],
                folder,
                config);

            Assert.Equal(["30,29,33,36 榴莲上跳舞"], distributed);
            Assert.Equal(
                ["原有内容", "30,29,33,36 榴莲上跳舞"],
                await File.ReadAllLinesAsync(target));
        }
        finally
        {
            if (Directory.Exists(folder))
                Directory.Delete(folder, recursive: true);
        }
    }

    private static string CreateTempFolder()
    {
        string path = Path.Combine(Path.GetTempPath(), "liulian-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
