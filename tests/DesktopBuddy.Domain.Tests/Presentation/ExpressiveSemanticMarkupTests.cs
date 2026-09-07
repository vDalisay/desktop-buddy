using System.Collections.Generic;
using DesktopBuddy.Domain.Presentation;
using Xunit;

namespace DesktopBuddy.Domain.Tests.Presentation;

public sealed class ExpressiveSemanticMarkupTests
{
    [Fact]
    public void ValidTags_BecomeSemanticRunsAndPlainText()
    {
        IReadOnlyList<ExpressiveTextRun> runs = ExpressiveSemanticMarkup.Parse(
            "Hold [input]right mouse button[/input] for a [impact]big swing[/impact].");

        Assert.Equal("Hold right mouse button for a big swing.", ExpressiveSemanticMarkup.PlainText(runs));
        Assert.Collection(runs,
            run => { Assert.Null(run.Role); Assert.Equal("Hold ", run.Text); },
            run => { Assert.Equal(ExpressiveSemanticRole.Input, run.Role); Assert.Equal("right mouse button", run.Text); },
            run => { Assert.Null(run.Role); Assert.Equal(" for a ", run.Text); },
            run => { Assert.Equal(ExpressiveSemanticRole.Impact, run.Role); Assert.Equal("big swing", run.Text); },
            run => { Assert.Null(run.Role); Assert.Equal(".", run.Text); });
    }

    [Fact]
    public void RepeatedPlainRuns_AreCoalesced()
    {
        IReadOnlyList<ExpressiveTextRun> runs = ExpressiveSemanticMarkup.Parse("A[unknown]B[/unknown]C");
        Assert.Equal("A[unknown]B[/unknown]C", ExpressiveSemanticMarkup.PlainText(runs));
        Assert.Single(runs);
        Assert.Null(runs[0].Role);
    }

    [Fact]
    public void MissingClosingTag_RemainsFullyVisible()
    {
        const string source = "Press [input]D to drop it.";
        IReadOnlyList<ExpressiveTextRun> runs = ExpressiveSemanticMarkup.Parse(source);

        Assert.Equal(source, ExpressiveSemanticMarkup.PlainText(runs));
        Assert.DoesNotContain(runs, static run => run.Role.HasValue);
    }

    [Fact]
    public void StrayClosingTag_RemainsLiteral()
    {
        const string source = "Press D[/input] now.";
        IReadOnlyList<ExpressiveTextRun> runs = ExpressiveSemanticMarkup.Parse(source);
        Assert.Equal(source, ExpressiveSemanticMarkup.PlainText(runs));
    }

    [Fact]
    public void NestedSemanticMarkup_IsRejectedAsLiteral()
    {
        const string source = "[impact]very [playful]big[/playful][/impact]";
        IReadOnlyList<ExpressiveTextRun> runs = ExpressiveSemanticMarkup.Parse(source);

        Assert.Equal(source, ExpressiveSemanticMarkup.PlainText(runs));
        Assert.DoesNotContain(runs, static run => run.Role.HasValue);
    }

    [Fact]
    public void NullSource_IsEmptyAndSafe()
    {
        IReadOnlyList<ExpressiveTextRun> runs = ExpressiveSemanticMarkup.Parse(null);
        Assert.Equal(string.Empty, ExpressiveSemanticMarkup.PlainText(runs));
        Assert.Empty(runs);
    }
}
