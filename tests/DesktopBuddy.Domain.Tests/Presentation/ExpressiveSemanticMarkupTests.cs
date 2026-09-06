using DesktopBuddy.Domain.Presentation;
using Xunit;

namespace DesktopBuddy.Domain.Tests.Presentation;

public sealed class ExpressiveSemanticMarkupTests
{
    [Fact]
    public void ValidTags_BecomeSemanticRunsAndPlainText()
    {
        ExpressiveTextDocument document = ExpressiveSemanticMarkup.Parse(
            "Hold [input]right mouse button[/input] for a [impact]big swing[/impact].");

        Assert.Equal("Hold right mouse button for a big swing.", document.PlainText);
        Assert.Collection(document.Runs,
            run => { Assert.Null(run.Role); Assert.Equal("Hold ", run.Text); },
            run => { Assert.Equal(ExpressiveSemanticRole.Input, run.Role); Assert.Equal("right mouse button", run.Text); },
            run => { Assert.Null(run.Role); Assert.Equal(" for a ", run.Text); },
            run => { Assert.Equal(ExpressiveSemanticRole.Impact, run.Role); Assert.Equal("big swing", run.Text); },
            run => { Assert.Null(run.Role); Assert.Equal(".", run.Text); });
    }

    [Fact]
    public void RepeatedPlainRuns_AreCoalesced()
    {
        ExpressiveTextDocument document = ExpressiveSemanticMarkup.Parse("A[unknown]B[/unknown]C");
        Assert.Equal("A[unknown]B[/unknown]C", document.PlainText);
        Assert.Single(document.Runs);
        Assert.Null(document.Runs[0].Role);
    }

    [Fact]
    public void MissingClosingTag_RemainsFullyVisible()
    {
        const string source = "Press [input]D to drop it.";
        ExpressiveTextDocument document = ExpressiveSemanticMarkup.Parse(source);

        Assert.Equal(source, document.PlainText);
        Assert.DoesNotContain(document.Runs, static run => run.Role.HasValue);
    }

    [Fact]
    public void StrayClosingTag_RemainsLiteral()
    {
        const string source = "Press D[/input] now.";
        Assert.Equal(source, ExpressiveSemanticMarkup.Parse(source).PlainText);
    }

    [Fact]
    public void NestedSemanticMarkup_IsRejectedAsLiteral()
    {
        const string source = "[impact]very [playful]big[/playful][/impact]";
        ExpressiveTextDocument document = ExpressiveSemanticMarkup.Parse(source);

        Assert.Equal(source, document.PlainText);
        Assert.DoesNotContain(document.Runs, static run => run.Role.HasValue);
    }

    [Fact]
    public void NullSource_IsEmptyAndSafe()
    {
        ExpressiveTextDocument document = ExpressiveSemanticMarkup.Parse(null);
        Assert.Equal(string.Empty, document.PlainText);
        Assert.Empty(document.Runs);
    }
}
