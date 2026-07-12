using System;
using System.Numerics;
using DesktopBuddy.Domain.Physics;
using Xunit;

namespace DesktopBuddy.Domain.Tests.Physics;

public sealed class GaitCycleTests
{
    private static readonly GaitTuning Tuning = new(
        StepLength: 20.0f, StepLift: 14.0f, TorsoBob: 6.0f, TorsoLean: 0.1f);

    [Fact]
    public void Idle_ProducesNoOffsets()
    {
        GaitSample sample = GaitCycle.Sample(0.3f, 0.0f, Tuning);
        Assert.Equal(Vector2.Zero, sample.LeftFootOffset);
        Assert.Equal(Vector2.Zero, sample.RightFootOffset);
        Assert.Equal(0.0f, sample.TorsoBobOffset);
        Assert.Equal(0.0f, sample.TorsoLeanOffset);
    }

    [Fact]
    public void Feet_AlternateStance_AcrossTheCycle()
    {
        // Left swings in the first half, right in the second: exactly one is planted.
        for (float phase = 0.0f; phase < 1.0f; phase += 0.05f)
        {
            GaitSample sample = GaitCycle.Sample(phase, 1.0f, Tuning);
            Assert.NotEqual(sample.LeftIsStance, sample.RightIsStance);
        }
    }

    [Fact]
    public void SwingFoot_LiftsOffFloor_AndStanceFootStaysDown()
    {
        // Mid first half: left foot is swinging (lifted), right is planted (y=0).
        GaitSample sample = GaitCycle.Sample(0.25f, 1.0f, Tuning);
        Assert.False(sample.LeftIsStance);
        Assert.True(sample.RightIsStance);
        Assert.True(sample.LeftFootOffset.Y < -1.0f, $"swing lift {sample.LeftFootOffset.Y}");
        Assert.Equal(0.0f, sample.RightFootOffset.Y, 3);
    }

    [Fact]
    public void SwingLift_PeaksNearMidSwing_WithinStepLift()
    {
        float peak = 0.0f;
        for (float p = 0.0f; p < 0.5f; p += 0.01f)
        {
            peak = MathF.Min(peak, GaitCycle.Sample(p, 1.0f, Tuning).LeftFootOffset.Y);
        }

        Assert.InRange(-peak, Tuning.StepLift - 0.5f, Tuning.StepLift + 0.001f);
    }

    [Fact]
    public void Direction_MirrorsForwardReach()
    {
        GaitSample right = GaitCycle.Sample(0.4f, 1.0f, Tuning);
        GaitSample left = GaitCycle.Sample(0.4f, -1.0f, Tuning);
        // Same phase, opposite direction -> mirrored horizontal reach and lean.
        Assert.Equal(-right.LeftFootOffset.X, left.LeftFootOffset.X, 3);
        Assert.Equal(-right.TorsoLeanOffset, left.TorsoLeanOffset, 3);
    }

    [Fact]
    public void Phase_WrapsContinuously()
    {
        GaitSample a = GaitCycle.Sample(0.99f, 1.0f, Tuning);
        GaitSample b = GaitCycle.Sample(1.99f, 1.0f, Tuning);
        Assert.Equal(a.LeftFootOffset.X, b.LeftFootOffset.X, 3);
        Assert.Equal(a.LeftFootOffset.Y, b.LeftFootOffset.Y, 3);
    }

    [Fact]
    public void SwingReach_StaysWithinHalfStepLength()
    {
        for (float p = 0.0f; p < 1.0f; p += 0.02f)
        {
            GaitSample s = GaitCycle.Sample(p, 1.0f, Tuning);
            Assert.InRange(s.LeftFootOffset.X, -Tuning.StepLength * 0.5f - 0.01f, Tuning.StepLength * 0.5f + 0.01f);
        }
    }
}
