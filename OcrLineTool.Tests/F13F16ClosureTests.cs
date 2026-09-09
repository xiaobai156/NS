using System.Reflection;
using OcrLineTool;
using Xunit;

namespace OcrLineTool.Tests;

[Trait("Category", "ReauditAccuracy")]
public sealed class F13F16ClosureTests
{
    [Fact]
    public void TwoAlreadyReceivedProviderValuesForTheSameRuleMustConflict()
    {
        OcrRule rule = RuleCatalog.Load(Path.Combine(
            ResultFilePaths.ConfigurationDirectory(AppContext.BaseDirectory), "嫣然心水.json"))
            .Single(item => item.Id == "南国挽心");
        var values = new ResultValues(StringComparer.Ordinal);
        MethodInfo merge = typeof(MainForm).GetMethod(
            "AddExtractedValues", BindingFlags.Static | BindingFlags.NonPublic)!;

        merge.Invoke(null, new object[]
        {
            new string[] { "251期 南国挽心 鸡" }, new OcrRule[] { rule }, 251, values
        });
        merge.Invoke(null, new object[]
        {
            new string[] { "251期 南国挽心 狗" }, new OcrRule[] { rule }, 251, values
        });

        Assert.False(values.ContainsKey(rule.Id));
        Assert.True(ResultValues.IsConflict(values, rule.Id));
    }

    [Fact]
    public void InvalidSavedSingleZodiacCannotBeRestoredAsTrustedSuccess()
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        using var form = new MainForm();
        Type type = typeof(MainForm);
        string rulePath = Path.Combine(
            ResultFilePaths.ConfigurationDirectory(AppContext.BaseDirectory), "嫣然心水.json");
        type.GetField("selectedRulePath", flags)!.SetValue(form, rulePath);
        var issue = (NumericUpDown)type.GetField("issueInput", flags)!.GetValue(form)!;
        issue.Value = 251;

        type.GetMethod("RestoreRetryState", flags)!.Invoke(form, new object[]
        {
            new string[] { "鸡狗 南国挽心" }
        });

        var values = (Dictionary<string, string>)type.GetField("lastValues", flags)!.GetValue(form)!;
        var reasons = (Dictionary<string, string>)type.GetField("lastMissingReasons", flags)!.GetValue(form)!;
        Assert.DoesNotContain("南国挽心", values.Keys);
        Assert.Equal("保存结果未通过当前规则校验", reasons["南国挽心"]);
    }

    [Fact]
    public void EvenValidLookingTxtIsDisplayOnlyWithoutEvidenceState()
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        using var form = new MainForm();
        Type type = typeof(MainForm);
        string rulePath = Path.Combine(
            ResultFilePaths.ConfigurationDirectory(AppContext.BaseDirectory), "嫣然心水.json");
        type.GetField("selectedRulePath", flags)!.SetValue(form, rulePath);
        var issue = (NumericUpDown)type.GetField("issueInput", flags)!.GetValue(form)!;
        issue.Value = 251;

        type.GetMethod("RestoreRetryState", flags)!.Invoke(form, new object[]
        {
            new string[] { "鸡 南国挽心" }
        });

        var values = (Dictionary<string, string>)type.GetField("lastValues", flags)!.GetValue(form)!;
        var reasons = (Dictionary<string, string>)type.GetField("lastMissingReasons", flags)!.GetValue(form)!;
        Assert.DoesNotContain("南国挽心", values.Keys);
        Assert.Equal("保存TXT仅用于展示，未找到有效来源状态", reasons["南国挽心"]);
    }
}
