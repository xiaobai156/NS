using OcrLineTool;
using Xunit;

namespace OcrLineTool.Tests;

[Trait("Category", "ReauditAccuracy")]
public sealed class ValueConflictClosureTests
{
    [Fact]
    public void ExplicitHeadValueConflictDoesNotReturnTheLastHead()
    {
        var rule = new OcrRule("杀一头", "头");

        Assert.Null(RuleEngine.ExtractFinalValue(
            ["241期 杀一头：2头 3头 开??"],
            241,
            rule));
    }

    [Fact]
    public void HeadInstructionCountBeforeOneExplicitValueStillSucceeds()
    {
        var rule = new OcrRule("杀一头", "头");

        Assert.Equal("2头", RuleEngine.ExtractFinalValue(
            ["241期 杀一头：2头 开??"],
            241,
            rule));
    }

    [Fact]
    public void TwoTailConflictDoesNotReturnTheFirstPair()
    {
        var rule = new OcrRule("两尾", "尾数组合");

        Assert.Null(RuleEngine.ExtractFinalValue(
            ["241期 两尾：4+7尾 2+3尾 开??"],
            241,
            rule));
    }

    [Fact]
    public void ReversedCopiesOfTheSameTailPairAreNotAConflict()
    {
        var rule = new OcrRule("两尾", "尾数组合");

        Assert.Equal("4尾+7尾", RuleEngine.ExtractFinalValue(
            ["241期 两尾：4+7尾 7+4尾 开??"],
            241,
            rule));
    }

    [Fact]
    public void OneCompactTailPairStillSucceeds()
    {
        var rule = new OcrRule("两尾", "尾数组合");

        Assert.Equal("1尾+5尾", RuleEngine.ExtractFinalValue(
            ["241期 两尾：15尾 开??"],
            241,
            rule));
    }
}
