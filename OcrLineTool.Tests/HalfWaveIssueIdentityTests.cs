using OcrLineTool;
using Xunit;

namespace OcrLineTool.Tests;

[Trait("Category", "ReauditAccuracy")]
public sealed class HalfWaveIssueIdentityTests
{
    private static OcrRule RedHalfWaveRule() => RuleCatalog.Load(Path.Combine(
        ResultFilePaths.ConfigurationDirectory(AppContext.BaseDirectory), "黄大仙新澳.json"))
        .Single(rule => rule.Id == "红红半波");

    [Fact]
    public void SelectedIssueWithExactHalfWaveIdentityCanExtractItsOwnValue()
    {
        Assert.Equal("绿单", RuleEngine.ExtractFinalValue(
            ["红红半波", "245期红红半波【绿单】开"],
            245,
            RedHalfWaveRule()));
    }

    [Fact]
    public void PreviousIssueHeadingCannotAuthorizeGenericHalfWaveFieldForSelectedIssue()
    {
        Assert.Null(RuleEngine.ExtractFinalValue(
            [
                "红红半波",
                "244期红红半波【红双】开",
                "245期杀半波【绿单】开"
            ],
            245,
            RedHalfWaveRule()));
    }

    [Fact]
    public void SelectedIssueBelongingToSiblingHalfWaveCannotBeBorrowed()
    {
        Assert.Null(RuleEngine.ExtractFinalValue(
            [
                "杀半波",
                "红红半波",
                "244期红红半波【红双】开",
                "245期粉红半波【绿单】开"
            ],
            245,
            RedHalfWaveRule()));
    }
}
