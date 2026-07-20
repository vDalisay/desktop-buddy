using System;
using DesktopBuddy.Domain.Autonomy;
using DesktopBuddy.Domain.Presentation;
using Xunit;

namespace DesktopBuddy.Domain.Tests.Presentation;

public sealed class FaceExpressionCatalogTests
{
    [Fact]
    public void EveryAuthoritativeFace_Resolves()
    {
        foreach (string face in FaceExpressionCatalog.Faces)
        {
            Assert.True(FaceExpressionCatalog.TryResolve(face, out _), $"'{face}' must resolve");
        }
    }

    [Fact]
    public void AuthoritativeList_HasExactlyTheTenResolverStrings()
    {
        Assert.Equal(10, FaceExpressionCatalog.Faces.Count);
        Assert.Equal(
            new[] { "x_x", ">_<", ">:(", "o_o", ":)", ":3", "^_^", ":(", ":/", ":|" },
            FaceExpressionCatalog.Faces);
    }

    [Fact]
    public void UnknownFace_ThrowsOnResolve_AndFailsTryResolve()
    {
        Assert.False(FaceExpressionCatalog.TryResolve(":D", out _));
        Assert.Throws<ArgumentOutOfRangeException>(() => FaceExpressionCatalog.Resolve(":D"));
    }

    [Theory]
    [InlineData(":|", true, true)]
    [InlineData(">:(", true, true)]
    [InlineData("o_o", false, true)]
    [InlineData("^_^", false, false)]
    [InlineData(">_<", false, false)]
    [InlineData("x_x", false, false)]
    public void BlinkAndPupilCapability_FollowTheEyePose(
        string face, bool blinkable, bool hasPupils)
    {
        FaceFeaturePose pose = FaceExpressionCatalog.Resolve(face);
        Assert.Equal(blinkable, pose.EyesBlinkable);
        Assert.Equal(hasPupils, pose.HasPupils);
    }

    [Fact]
    public void PainAndKnockout_UseClosedSpecialEyes()
    {
        Assert.Equal(FaceEyePose.Scrunch, FaceExpressionCatalog.Resolve(">_<").Eyes);
        Assert.Equal(FaceEyePose.Cross, FaceExpressionCatalog.Resolve("x_x").Eyes);
        Assert.Equal(FaceEyePose.HappyArc, FaceExpressionCatalog.Resolve("^_^").Eyes);
    }
}

public sealed class BlinkModelTests
{
    private static readonly BlinkParameters Parameters = new(
        IntervalMinimumTicks: 240,
        IntervalMaximumTicks: 720,
        ClosedTicks: 14);

    // Scripted tests need a floor below their scripted intervals: ScriptedRandomSource
    // clamps every draw into the requested range.
    private static readonly BlinkParameters ScriptedParameters = new(
        IntervalMinimumTicks: 2,
        IntervalMaximumTicks: 200,
        ClosedTicks: 14);

    [Theory]
    [InlineData(0, 720, 14)]
    [InlineData(240, 240, 14)]
    [InlineData(240, 720, 0)]
    public void InvalidParameters_Throw(int minimum, int maximum, int closed) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new BlinkModel(
            new SeededRandomSource(1),
            new BlinkParameters(minimum, maximum, closed)));

    [Fact]
    public void NullRandom_Throws() =>
        Assert.Throws<ArgumentNullException>(() => new BlinkModel(null!, Parameters));

    [Fact]
    public void NegativeTicks_Throw()
    {
        var model = new BlinkModel(new SeededRandomSource(1), Parameters);
        Assert.Throws<ArgumentOutOfRangeException>(() => model.Update(false, -1));
    }

    [Fact]
    public void Blink_OpensAfterExactlyTheClosedHold()
    {
        // Scripted interval 100: closed on the tick the countdown crosses zero, open
        // again exactly ClosedTicks later.
        var model = new BlinkModel(new ScriptedRandomSource(100, 100), ScriptedParameters);
        for (int tick = 0; tick < 100; tick++)
        {
            model.Update(false, 1);
        }

        Assert.True(model.EyesClosed);
        for (int tick = 0; tick < 13; tick++)
        {
            model.Update(false, 1);
            Assert.True(model.EyesClosed);
        }

        model.Update(false, 1);
        Assert.False(model.EyesClosed);
    }

    [Fact]
    public void SameSeed_SameBlinkSequence()
    {
        var first = new BlinkModel(new SeededRandomSource(7), Parameters);
        var second = new BlinkModel(new SeededRandomSource(7), Parameters);
        for (int tick = 0; tick < 5000; tick++)
        {
            first.Update(false, 1);
            second.Update(false, 1);
            Assert.Equal(first.EyesClosed, second.EyesClosed);
        }
    }

    [Fact]
    public void Suppression_OpensEyesAndDisarms()
    {
        var model = new BlinkModel(new ScriptedRandomSource(10, 10, 10), ScriptedParameters);
        for (int tick = 0; tick < 10; tick++)
        {
            model.Update(false, 1);
        }

        Assert.True(model.EyesClosed);
        model.Update(true, 1);
        Assert.False(model.EyesClosed);

        // While suppressed nothing counts down and nothing is drawn from the stream.
        for (int tick = 0; tick < 500; tick++)
        {
            model.Update(true, 1);
            Assert.False(model.EyesClosed);
        }

        // Unsuppressing re-arms with a FRESH interval before any blink can land.
        model.Update(false, 1);
        Assert.False(model.EyesClosed);
        for (int tick = 0; tick < 9; tick++)
        {
            model.Update(false, 1);
        }

        Assert.True(model.EyesClosed);
    }

    [Fact]
    public void ZeroTickUpdate_HoldsState()
    {
        var model = new BlinkModel(new ScriptedRandomSource(10, 10), ScriptedParameters);
        for (int tick = 0; tick < 10; tick++)
        {
            model.Update(false, 1);
        }

        Assert.True(model.EyesClosed);
        for (int i = 0; i < 100; i++)
        {
            model.Update(false, 0);
            Assert.True(model.EyesClosed);
        }
    }
}

public sealed class ChewCycleTests
{
    [Fact]
    public void AlternatesHalfCycles()
    {
        Assert.Equal(0, ChewCycle.FrameAt(0, 40));
        Assert.Equal(0, ChewCycle.FrameAt(19, 40));
        Assert.Equal(1, ChewCycle.FrameAt(20, 40));
        Assert.Equal(1, ChewCycle.FrameAt(39, 40));
        Assert.Equal(0, ChewCycle.FrameAt(40, 40));
    }

    [Fact]
    public void InvalidInputs_Throw()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ChewCycle.FrameAt(0, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => ChewCycle.FrameAt(-1, 40));
    }
}

public sealed class FaceComposerTests
{
    private static readonly FaceFeaturePose Neutral = FaceExpressionCatalog.Resolve(":|");
    private static readonly FaceFeaturePose Pain = FaceExpressionCatalog.Resolve(">_<");
    private static readonly FaceFeaturePose Startle = FaceExpressionCatalog.Resolve("o_o");

    [Fact]
    public void PlainPose_PassesThrough()
    {
        FaceRenderState state = FaceComposer.Compose(
            Neutral, blinkClosed: false, chewActive: false, chewFrame: 0,
            faceSuppressed: false, pupilX: 0.5f, pupilY: -0.25f);
        Assert.Equal(FaceEyePose.Open, state.Eyes);
        Assert.Equal(FaceBrowPose.Neutral, state.Brows);
        Assert.Equal(FaceMouthPose.Flat, state.Mouth);
        Assert.False(state.Blinking);
        Assert.Equal(0.5f, state.PupilX);
        Assert.Equal(-0.25f, state.PupilY);
    }

    [Fact]
    public void Blink_ClosesBlinkableEyes_AndHidesPupils()
    {
        FaceRenderState state = FaceComposer.Compose(
            Neutral, blinkClosed: true, chewActive: false, chewFrame: 0,
            faceSuppressed: false, pupilX: 1.0f, pupilY: 1.0f);
        Assert.True(state.Blinking);
        Assert.Equal(0.0f, state.PupilX);
        Assert.Equal(0.0f, state.PupilY);
    }

    [Fact]
    public void Blink_NeverClosesNonBlinkableEyes()
    {
        FaceRenderState state = FaceComposer.Compose(
            Pain, blinkClosed: true, chewActive: false, chewFrame: 0,
            faceSuppressed: true, pupilX: 0.0f, pupilY: 0.0f);
        Assert.False(state.Blinking);
        Assert.Equal(FaceEyePose.Scrunch, state.Eyes);
    }

    [Fact]
    public void Chew_ReplacesTheMouth_UnlessTheFaceIsSuppressionPriority()
    {
        FaceRenderState chewing = FaceComposer.Compose(
            Neutral, blinkClosed: false, chewActive: true, chewFrame: 1,
            faceSuppressed: false, pupilX: 0.0f, pupilY: 0.0f);
        Assert.Equal(FaceMouthPose.ChewClosed, chewing.Mouth);

        FaceRenderState pained = FaceComposer.Compose(
            Pain, blinkClosed: false, chewActive: true, chewFrame: 1,
            faceSuppressed: true, pupilX: 0.0f, pupilY: 0.0f);
        Assert.Equal(FaceMouthPose.Squiggle, pained.Mouth);
    }

    [Fact]
    public void PupilChangeUnderClosedLids_DoesNotChangeTheRenderKey()
    {
        FaceRenderState first = FaceComposer.Compose(
            Neutral, blinkClosed: true, chewActive: false, chewFrame: 0,
            faceSuppressed: false, pupilX: 0.25f, pupilY: 0.0f);
        FaceRenderState second = FaceComposer.Compose(
            Neutral, blinkClosed: true, chewActive: false, chewFrame: 0,
            faceSuppressed: false, pupilX: -0.75f, pupilY: 0.5f);
        Assert.Equal(first, second);
    }

    [Fact]
    public void StartleEyes_KeepPupils()
    {
        FaceRenderState state = FaceComposer.Compose(
            Startle, blinkClosed: false, chewActive: false, chewFrame: 0,
            faceSuppressed: false, pupilX: 0.5f, pupilY: 0.5f);
        Assert.Equal(0.5f, state.PupilX);
    }

    [Fact]
    public void NonFinitePupils_ComposeToZero()
    {
        FaceRenderState state = FaceComposer.Compose(
            Neutral, blinkClosed: false, chewActive: false, chewFrame: 0,
            faceSuppressed: false, pupilX: float.NaN, pupilY: float.PositiveInfinity);
        Assert.Equal(0.0f, state.PupilX);
        Assert.Equal(0.0f, state.PupilY);
    }

    [Fact]
    public void InvalidChewFrame_Throws() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => FaceComposer.Compose(
            Neutral, blinkClosed: false, chewActive: true, chewFrame: 2,
            faceSuppressed: false, pupilX: 0.0f, pupilY: 0.0f));
}
