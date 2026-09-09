using OcrLineTool;
using Xunit;

namespace OcrLineTool.Tests;

[Trait("Category", "ReauditAccuracy")]
public sealed class RetryTemplateEvidenceScopeTests
{
    [Fact]
    public void RetrySubsetStillComparesEveryRuleDeclaredByThePhysicalTemplate()
    {
        var first = new OcrRule("作者A", "生肖", "规则A");
        var second = new OcrRule("作者B", "尾", "规则B");
        IReadOnlyList<OcrRule> selected = [first];
        IReadOnlyList<OcrRule> all = [first, second];

        IReadOnlyList<OcrRule> expanded = MainForm.ExpandDeclaredEvidenceRules(
            selected, all, [first.Id, second.Id]);

        Assert.Equal([first.Id, second.Id], expanded.Select(rule => rule.Id).ToArray());
    }

    [Fact]
    public void InvalidTemplateDeclarationNeverBroadensToAnApproximateRuleSet()
    {
        var first = new OcrRule("作者A", "生肖", "规则A");
        var second = new OcrRule("作者B", "尾", "规则B");

        IReadOnlyList<OcrRule> expanded = MainForm.ExpandDeclaredEvidenceRules(
            [first], [first, second], [first.Id, "不存在的规则"]);

        Assert.Equal([first.Id], expanded.Select(rule => rule.Id).ToArray());
    }
}
