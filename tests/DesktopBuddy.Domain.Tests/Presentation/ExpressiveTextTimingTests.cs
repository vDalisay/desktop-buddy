using System;
using DesktopBuddy.Domain.Presentation;
using Xunit;

namespace DesktopBuddy.Domain.Tests.Presentation;

public sealed class ExpressiveSemanticTagsTests
{
    [Theory]
    [InlineData(ExpressiveSemanticRole.Input, "input")]
    [InlineData(ExpressiveSemanticRole.Action, "action")]
    [InlineData(ExpressiveSemanticRole.Money, "money")]
    [InlineData(ExpressiveSemanticRole.Impact, "impact")]
    [InlineData(ExpressiveSemanticRole.Playful, "playful")]
    public void NameAndTryParse_RoundTrip(ExpressiveSemanticRole role, string tag)
    {
        Assert.Equal(tag, ExpressiveSemanticTags.Name(role));
        Assert.True(ExpressiveSemanticTags.TryParse(tag, out ExpressiveSemanticRole parsed));
        Assert.Equal(role, parsed);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("wave")]
    [InlineData("shake")]
    [InlineData("Input")]
    public void UnknownOrVisualTag_DoesNotParse(string? tag) =>
        Assert.False(ExpressiveSemanticTags.TryParse(tag, out _));

    [Fact]
    public void UnknownRole_Throws() =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ExpressiveSemanticTags.Name((ExpressiveSemanticRole)999));
}

public sealed class ExpressiveRevealTimingTests
{
    [Fact]
    public void Default_IsFastButAddsReadablePunctuationPauses()
    {
        ExpressiveRevealTiming timing = ExpressiveRevealTiming.Default;

        Assert.Equal(timing.BaseElementSeconds, timing.DelayAfter("A"), precision: 8);
        Assert.Equal(
            timing.BaseElementSeconds + timing.CommaPauseSeconds,
            timing.DelayAfter(","),
            precision: 8);
        Assert.Equal(
            timing.BaseElementSeconds + timing.SentencePauseSeconds,
            timing.DelayAfter("!"),
            precision: 8);
        Assert.Equal(
            timing.BaseElementSeconds + timing.SentencePauseSeconds,
            timing.DelayAfter("…"),
            precision: 8);
    }

    [Theory]
    [InlineData(",")]
    [InlineData(";")]
    [InlineData(":")]
    public void MidSentencePunctuation_UsesCommaPause(string punctuation)
    {
        var timing = new ExpressiveRevealTiming(0.01, 0.05, 0.1);
        Assert.Equal(0.06, timing.DelayAfter(punctuation), precision: 8);
    }

    [Theory]
    [InlineData(".")]
    [InlineData("!")]
    [InlineData("?")]
    [InlineData("…")]
    [InlineData("\n")]
    public void SentenceBoundary_UsesLongPause(string punctuation)
    {
        var timing = new ExpressiveRevealTiming(0.01, 0.05, 0.1);
        Assert.Equal(0.11, timing.DelayAfter(punctuation), precision: 8);
    }

    [Theory]
    [InlineData(0.0, 0.0, 0.0)]
    [InlineData(-0.01, 0.0, 0.0)]
    [InlineData(0.01, -0.1, 0.0)]
    [InlineData(0.01, 0.0, -0.1)]
    public void InvalidTiming_Throws(double baseSeconds, double commaSeconds, double sentenceSeconds) =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ExpressiveRevealTiming(baseSeconds, commaSeconds, sentenceSeconds));
}

public sealed class ExpressiveVoiceCadenceTests
{
    [Theory]
    [InlineData("A", true)]
    [InlineData("7", true)]
    [InlineData("é", true)]
    [InlineData("🙂", true)]
    [InlineData(" ", false)]
    [InlineData("\t", false)]
    [InlineData(".", false)]
    [InlineData("!", false)]
    [InlineData("—", false)]
    public void SpeakableClassification_SkipsWhitespaceAndPunctuation(string element, bool expected) =>
        Assert.Equal(expected, ExpressiveVoiceCadence.IsSpeakable(element));

    [Fact]
    public void DefaultCadence_ChirpsEveryThirdSpeakableElementStartingWithFirst()
    {
        Assert.True(ExpressiveVoiceCadence.ShouldChirp(1, "H"));
        Assert.False(ExpressiveVoiceCadence.ShouldChirp(2, "e"));
        Assert.False(ExpressiveVoiceCadence.ShouldChirp(3, "l"));
        Assert.True(ExpressiveVoiceCadence.ShouldChirp(4, "l"));
        Assert.True(ExpressiveVoiceCadence.ShouldChirp(7, "o"));
    }

    [Fact]
    public void PunctuationNeverChirpsEvenOnCadenceBoundary() =>
        Assert.False(ExpressiveVoiceCadence.ShouldChirp(4, "!"));

    [Theory]
    [InlineData(0, 3)]
    [InlineData(-1, 3)]
    [InlineData(1, 0)]
    [InlineData(1, -1)]
    public void InvalidCadenceArguments_Throw(int ordinal, int every)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ExpressiveVoiceCadence.ShouldChirp(ordinal, "A", every));
    }
}
