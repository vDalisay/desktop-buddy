using System;
using System.Numerics;
using DesktopBuddy.Domain.Physics;
using Xunit;

namespace DesktopBuddy.Domain.Tests.Physics;

public sealed class PanicFlailTests
{
    private static readonly PanicFlailTuning Tuning =
        new(Amplitude: 34.0f, Lift: 26.0f, CycleTicks: 132, Asymmetry: 0.55f, ReachBias: 24.0f);

    private const float Away = 1.0f;

    [Fact]
    public void ZeroFear_ProducesNoThrash()
    {
        // A calm grab must look exactly as it did before this feature existed.
        for (int tick = 0; tick < 200; tick++)
        {
            PanicFlailSample sample = PanicFlail.Sample(tick, 0.0f, Away, Tuning);
            Assert.Equal(Vector2.Zero, sample.LeftHandOffset);
            Assert.Equal(Vector2.Zero, sample.RightHandOffset);
        }
    }

    [Fact]
    public void OffsetsStayInsideTheTunedEnvelope()
    {
        for (int tick = 0; tick < 500; tick++)
        {
            PanicFlailSample sample = PanicFlail.Sample(tick, 1.0f, Away, Tuning);

            float span = Tuning.Amplitude + Tuning.ReachBias;
            Assert.InRange(sample.LeftHandOffset.X, -span, span);
            Assert.InRange(sample.RightHandOffset.X, -span, span);
            // Hands only thrash upward from the shoulder anchor (negative Y is up).
            Assert.InRange(sample.LeftHandOffset.Y, -Tuning.Lift, 0.0f);
            Assert.InRange(sample.RightHandOffset.Y, -Tuning.Lift, 0.0f);
        }
    }

    [Fact]
    public void AmplitudeScalesWithFear()
    {
        float mild = PeakHorizontal(0.25f);
        float terrified = PeakHorizontal(1.0f);

        Assert.True(terrified > mild * 2.0f, $"mild={mild} terrified={terrified}");
    }

    [Fact]
    public void HandsAreNotMirrorImages()
    {
        // Perfectly mirrored hands read as a deliberate wave, not panic.
        bool sawAsymmetry = false;
        for (int tick = 0; tick < 60 && !sawAsymmetry; tick++)
        {
            PanicFlailSample sample = PanicFlail.Sample(tick, 1.0f, Away, Tuning);
            sawAsymmetry = MathF.Abs(sample.LeftHandOffset.X + sample.RightHandOffset.X) > 2.0f;
        }

        Assert.True(sawAsymmetry, "hands moved as exact mirrors for a whole cycle");
    }

    [Fact]
    public void FearDoesNotSpeedUpTheSweep()
    {
        // Owner feel note 2026-07-25: an earlier version shortened the cycle with fear and it
        // read as random twitching. Fear may widen the reach; it may never make it faster.
        Assert.Equal(ZeroCrossings(0.2f), ZeroCrossings(1.0f));
    }

    [Fact]
    public void SweepIsSlow_OneArcPerCycleNotMany()
    {
        // Two horizontal zero crossings per cycle is one clean out-and-back sweep. More than
        // that is the spam the owner rejected.
        int crossings = ZeroCrossingsOver(1.0f, Tuning.CycleTicks);

        Assert.InRange(crossings, 1, 2);
    }

    [Fact]
    public void FreeHandsReachTowardTheStrainDirection()
    {
        // The buddy is hauling itself loose: the arc must sit on the far side of the shoulder
        // from the grab, not stay centred on it.
        float rightMean = MeanHorizontal(+1.0f);
        float leftMean = MeanHorizontal(-1.0f);

        Assert.True(rightMean > Tuning.ReachBias * 0.5f, $"reach right mean={rightMean}");
        Assert.True(leftMean < -Tuning.ReachBias * 0.5f, $"reach left mean={leftMean}");
    }

    [Fact]
    public void ReachDirectionIsClampedAndZeroLeavesTheArcCentred()
    {
        float centred = MeanHorizontal(0.0f);

        Assert.True(MathF.Abs(centred) < 1.0f, $"mean={centred}");
        Assert.Equal(
            PanicFlail.Sample(5, 1.0f, 1.0f, Tuning),
            PanicFlail.Sample(5, 1.0f, 99.0f, Tuning));
    }

    [Fact]
    public void SampleIsDeterministicForTheSameTick()
    {
        // No RNG: the same routed tick must reproduce the same pose in a replayed scenario.
        for (int tick = 0; tick < 50; tick++)
        {
            Assert.Equal(
                PanicFlail.Sample(tick, 0.7f, Away, Tuning),
                PanicFlail.Sample(tick, 0.7f, Away, Tuning));
        }
    }

    [Fact]
    public void NonPositiveCycleTicks_ProducesNoThrashRatherThanDividingByZero()
    {
        PanicFlailSample sample = PanicFlail.Sample(10, 1.0f, Away, Tuning with { CycleTicks = 0 });

        Assert.Equal(Vector2.Zero, sample.LeftHandOffset);
        Assert.Equal(Vector2.Zero, sample.RightHandOffset);
    }

    [Fact]
    public void ThrashActuallyMoves()
    {
        // Guard against a shape that is technically bounded but visually static.
        float peak = PeakHorizontal(1.0f);

        Assert.True(peak > Tuning.Amplitude * 0.5f, $"peak={peak}");
    }

    /// <summary>Peak sweep away from the arc's own centre, so the reach bias is excluded.</summary>
    private static float PeakHorizontal(float fear)
    {
        float centre = Tuning.ReachBias * fear;
        float peak = 0.0f;
        for (int tick = 0; tick < 400; tick++)
        {
            PanicFlailSample sample = PanicFlail.Sample(tick, fear, Away, Tuning);
            peak = MathF.Max(peak, MathF.Abs(sample.LeftHandOffset.X - centre));
        }

        return peak;
    }

    private static float MeanHorizontal(float reachDirection)
    {
        float sum = 0.0f;
        for (int tick = 0; tick < Tuning.CycleTicks; tick++)
        {
            sum += PanicFlail.Sample(tick, 1.0f, reachDirection, Tuning).LeftHandOffset.X;
        }

        return sum / Tuning.CycleTicks;
    }

    private static int ZeroCrossings(float fear) => ZeroCrossingsOver(fear, 240);

    private static int ZeroCrossingsOver(float fear, int ticks)
    {
        int crossings = 0;
        // Measured about the arc's own centre so the reach bias is not mistaken for a sweep.
        float centre = MeanHorizontal(Away) * (fear / 1.0f);
        float previous = PanicFlail.Sample(0, fear, Away, Tuning).LeftHandOffset.X - centre;
        for (int tick = 1; tick < ticks; tick++)
        {
            float current = PanicFlail.Sample(tick, fear, Away, Tuning).LeftHandOffset.X - centre;
            if ((previous < 0.0f && current >= 0.0f) || (previous > 0.0f && current <= 0.0f))
            {
                crossings++;
            }

            previous = current;
        }

        return crossings;
    }
}
