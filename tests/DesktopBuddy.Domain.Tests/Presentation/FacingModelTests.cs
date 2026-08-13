using System;
using System.Collections.Generic;
using DesktopBuddy.Domain.Autonomy;
using DesktopBuddy.Domain.Presentation;
using Xunit;

namespace DesktopBuddy.Domain.Tests.Presentation;

/// <summary>Scripted random source so idle-variety decisions are test-deterministic.</summary>
internal sealed class ScriptedRandomSource : IRandomSource
{
    private readonly Queue<int> _values;

    public ScriptedRandomSource(params int[] values) => _values = new Queue<int>(values);

    public int NextInt(int minimumInclusive, int maximumExclusive)
    {
        int value = _values.Count > 0 ? _values.Dequeue() : minimumInclusive;
        return Math.Clamp(value, minimumInclusive, maximumExclusive - 1);
    }
}

public sealed class FacingModelTests
{
    private static readonly FacingParameters Parameters = new(
        YawDegrees: 30.0f,
        TurnSeconds: 0.5f,
        WalkCommitTicks: 36,
        WalkDeadband: 0.05f,
        IdleFlipMinimumTicks: 720,
        IdleFlipMaximumTicks: 1920);

    private static FacingModel NewModel(params int[] scripted) =>
        new(new ScriptedRandomSource(scripted), Parameters);

    private static readonly FacingInputs Idle = new(false, 0.0f, 0.0f);
    private static readonly FacingInputs WalkRight = new(false, 0.0f, 1.0f);
    private static readonly FacingInputs WalkLeft = new(false, 0.0f, -1.0f);

    [Fact]
    public void StartsFrontalAtZeroYaw()
    {
        FacingModel model = NewModel();
        Assert.Equal(FacingSide.Frontal, model.CommittedSide);
        Assert.Equal(0.0f, model.Update(Idle, 1, 1.0 / 120.0));
    }

    [Fact]
    public void SustainedWalk_CommitsMatchingSideAfterHysteresis()
    {
        FacingModel model = NewModel();
        model.Update(WalkRight, 35, 0.29);
        Assert.Equal(FacingSide.Frontal, model.CommittedSide);
        model.Update(WalkRight, 1, 1.0 / 120.0);
        Assert.Equal(FacingSide.Right, model.CommittedSide);
    }

    [Fact]
    public void JitteringWalkDirection_NeverCommits()
    {
        FacingModel model = NewModel();
        for (int i = 0; i < 200; i++)
        {
            model.Update(i % 2 == 0 ? WalkRight : WalkLeft, 10, 10.0 / 120.0);
        }

        Assert.Equal(FacingSide.Frontal, model.CommittedSide);
    }

    [Fact]
    public void WalkBelowDeadband_IsIdleNotWalk()
    {
        FacingModel model = NewModel();
        model.Update(new FacingInputs(false, 0.0f, 0.04f), 500, 4.0);
        Assert.Equal(FacingSide.Frontal, model.CommittedSide);
    }

    [Fact]
    public void EngagedInteraction_CommitsCursorSideAfterItsShortStreak()
    {
        FacingModel model = NewModel();
        model.Update(new FacingInputs(true, -1.0f, 0.0f), 1, 1.0 / 120.0);
        Assert.Equal(FacingSide.Frontal, model.CommittedSide);
        model.Update(new FacingInputs(true, -1.0f, 0.0f), 24, 24.0 / 120.0);
        Assert.Equal(FacingSide.Left, model.CommittedSide);
    }

    [Fact]
    public void EngagedInteraction_OutranksSustainedWalk()
    {
        FacingModel model = NewModel();
        model.Update(WalkRight, 40, 0.33);
        Assert.Equal(FacingSide.Right, model.CommittedSide);
        model.Update(new FacingInputs(true, -1.0f, 1.0f), 25, 25.0 / 120.0);
        Assert.Equal(FacingSide.Left, model.CommittedSide);
    }

    [Fact]
    public void EngagedCursorSideJitter_NeverFlipsTheCommittedSide()
    {
        // The reported wobble: a cursor sitting roughly above a walking buddy made
        // Sign(cursorX - torsoX) alternate per rendered frame.
        FacingModel model = NewModel();
        model.Update(new FacingInputs(true, 1.0f, 0.0f), 30, 0.25);
        Assert.Equal(FacingSide.Right, model.CommittedSide);

        for (int frame = 0; frame < 240; frame++)
        {
            model.Update(
                new FacingInputs(true, frame % 2 == 0 ? -1.0f : 1.0f, 0.0f), 2, 2.0 / 120.0);
            Assert.Equal(FacingSide.Right, model.CommittedSide);
        }
    }

    [Fact]
    public void YawEasesToTargetAndNeverOvershoots()
    {
        FacingModel model = NewModel();
        model.Update(WalkRight, 40, 0.0);
        float previous = 0.0f;
        for (int frame = 0; frame < 120; frame++)
        {
            float yaw = model.Update(WalkRight, 1, 1.0 / 120.0);
            Assert.True(yaw >= previous - 0.0001f, $"yaw regressed at frame {frame}");
            Assert.True(yaw <= 30.0001f, $"yaw overshot at frame {frame}: {yaw}");
            previous = yaw;
        }

        Assert.Equal(30.0f, model.CurrentYawDegrees, 3);
    }

    [Fact]
    public void SideFlip_CrossesZeroMonotonicallyWithoutOvershoot()
    {
        FacingModel model = NewModel();
        model.Update(WalkRight, 40, 0.6);
        Assert.Equal(30.0f, model.CurrentYawDegrees, 3);

        model.Update(WalkLeft, 40, 0.0);
        Assert.Equal(FacingSide.Left, model.CommittedSide);
        bool crossedZero = false;
        float previous = model.CurrentYawDegrees;
        for (int frame = 0; frame < 120; frame++)
        {
            float yaw = model.Update(WalkLeft, 1, 1.0 / 120.0);
            Assert.True(yaw <= previous + 0.0001f, $"yaw regressed at frame {frame}");
            Assert.True(MathF.Abs(yaw) <= 30.0001f, $"yaw overshot at frame {frame}: {yaw}");
            crossedZero |= yaw < 0.0f && previous >= 0.0f;
            previous = yaw;
        }

        Assert.True(crossedZero, "turn never crossed zero");
        Assert.Equal(-30.0f, model.CurrentYawDegrees, 3);
    }

    [Fact]
    public void ForcedFrontal_TurnsToZeroAndRestoresCommittedSideAfterRelease()
    {
        FacingModel model = NewModel();
        model.Update(WalkRight, 40, 0.6);
        Assert.Equal(FacingSide.Right, model.CommittedSide);
        Assert.Equal(30.0f, model.CurrentYawDegrees, 3);

        var forced = new FacingInputs(false, 0.0f, 0.0f, ForceFrontal: true);
        model.Update(forced, 1, 0.5);
        Assert.Equal(FacingSide.Right, model.CommittedSide);
        Assert.Equal(0.0f, model.CurrentYawDegrees, 3);

        model.Update(Idle, 1, 0.5);
        Assert.Equal(FacingSide.Right, model.CommittedSide);
        Assert.Equal(30.0f, model.CurrentYawDegrees, 3);
    }

    [Fact]
    public void IdleVariety_FlipsSideOnSeededScheduleOnly()
    {
        // Scripted stream: first interval 720 ticks, initial side pick Right, next interval 720.
        FacingModel model = new(new ScriptedRandomSource(720, 1, 720), Parameters);
        model.Update(Idle, 719, 6.0);
        Assert.Equal(FacingSide.Frontal, model.CommittedSide);
        model.Update(Idle, 1, 1.0 / 120.0);
        Assert.Equal(FacingSide.Right, model.CommittedSide);

        // The next seeded expiry flips to the opposite side.
        model.Update(Idle, 720, 6.0);
        Assert.Equal(FacingSide.Left, model.CommittedSide);
    }

    [Fact]
    public void WalkingResetsTheIdleTimer()
    {
        FacingModel model = new(new ScriptedRandomSource(720, 1, 720, 1, 720), Parameters);
        model.Update(Idle, 700, 5.8);
        model.Update(WalkRight, 10, 0.08);
        // Idle again: the timer re-arms from scratch, so 700 more idle ticks do not flip.
        model.Update(Idle, 700, 5.8);
        Assert.Equal(FacingSide.Frontal, model.CommittedSide);
    }

    [Fact]
    public void NegativeTicks_Throw()
    {
        FacingModel model = NewModel();
        Assert.Throws<ArgumentOutOfRangeException>(() => model.Update(Idle, -1, 0.01));
    }

    [Fact]
    public void InvalidParameters_Throw() =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new FacingModel(new ScriptedRandomSource(), Parameters with { TurnSeconds = 0.0f }));
}
