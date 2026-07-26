using System;
using System.Numerics;
using DesktopBuddy.Domain.Physics;
using Xunit;

namespace DesktopBuddy.Domain.Tests.Physics;

public sealed class GrabStretchLimiterTests
{
    private const float HandRadius = 15.0f;   // lab rig: one hand width is 30 px
    private const float Limit = 150.0f;       // 5 hand widths
    private static readonly Vector2 Anchor = new(200.0f, 200.0f);

    private static GrabStretchLimiter New(GrabStretchTuning? tuning = null) =>
        new(tuning ?? GrabStretchTuning.Default);

    private static Vector2 Right(float distance) => Anchor + new Vector2(distance, 0.0f);

    [Fact]
    public void LimitIsFiveHandWidths()
    {
        Assert.Equal(Limit, New().LimitFor(HandRadius), 0.001f);
        Assert.Equal(5.0f, GrabStretchTuning.Default.LimitHandWidths);
    }

    [Fact]
    public void InsideTheLimit_PullsStraightToTheCursor()
    {
        GrabStretchLimiter limiter = New();

        GrabStretchResult result = limiter.Tick(Anchor, Right(80.0f), HandRadius);

        Assert.Equal(GrabStretchState.Slack, result.State);
        Assert.Equal(Right(80.0f), result.ClampedTarget);
        Assert.Equal(Vector2.Zero, result.ShakeOffset);
        Assert.Equal(0.0f, result.Overpull);
    }

    [Fact]
    public void BeyondTheLimit_ClampsTheTargetOntoTheLimitCircle()
    {
        GrabStretchLimiter limiter = New();

        GrabStretchResult result = limiter.Tick(Anchor, Right(400.0f), HandRadius);

        Assert.Equal(GrabStretchState.Straining, result.State);
        // The arm stops at 5 hand widths no matter how far the cursor goes.
        Assert.Equal(Limit, (result.ClampedTarget - Anchor).Length(), 0.01f);
        Assert.Equal(250.0f, result.Overpull, 0.01f);
    }

    [Fact]
    public void ClampHoldsInEveryDirection()
    {
        GrabStretchLimiter limiter = New();
        Vector2[] cursors =
        {
            Anchor + new Vector2(-500.0f, 0.0f),
            Anchor + new Vector2(0.0f, 400.0f),
            Anchor + new Vector2(-300.0f, -300.0f),
        };

        foreach (Vector2 cursor in cursors)
        {
            limiter.Reset();
            GrabStretchResult result = limiter.Tick(Anchor, cursor, HandRadius);
            Assert.Equal(Limit, (result.ClampedTarget - Anchor).Length(), 0.01f);
        }
    }

    [Fact]
    public void SnapsAfterExactlyThreeSecondsOfStrain()
    {
        GrabStretchLimiter limiter = New();
        int ticks = GrabStretchTuning.Default.ShakeTicks;
        Assert.Equal(360, ticks); // 3 s at 120 Hz

        for (int tick = 1; tick < ticks; tick++)
        {
            GrabStretchResult straining = limiter.Tick(Anchor, Right(300.0f), HandRadius);
            Assert.Equal(GrabStretchState.Straining, straining.State);
            Assert.Equal(0.0f, straining.SnapImpulse);
        }

        GrabStretchResult snap = limiter.Tick(Anchor, Right(300.0f), HandRadius);

        Assert.Equal(GrabStretchState.Snapped, snap.State);
        Assert.True(snap.SnapImpulse > 0.0f);
    }

    [Fact]
    public void CountdownReportsRemainingTicks()
    {
        GrabStretchLimiter limiter = New();
        int total = GrabStretchTuning.Default.ShakeTicks;

        GrabStretchResult first = limiter.Tick(Anchor, Right(300.0f), HandRadius);
        Assert.Equal(total - 1, first.ShakeTicksRemaining);

        GrabStretchResult later = default;
        for (int tick = 2; tick <= 60; tick++)
        {
            later = limiter.Tick(Anchor, Right(300.0f), HandRadius);
        }

        Assert.Equal(60, limiter.StrainTicks);
        Assert.Equal(total - 60, later.ShakeTicksRemaining);
    }

    [Fact]
    public void EasingOffCancelsTheCountdown()
    {
        GrabStretchLimiter limiter = New();
        for (int tick = 0; tick < 100; tick++)
        {
            limiter.Tick(Anchor, Right(300.0f), HandRadius);
        }

        Assert.Equal(100, limiter.StrainTicks);

        GrabStretchResult eased = limiter.Tick(Anchor, Right(50.0f), HandRadius);

        Assert.Equal(GrabStretchState.Slack, eased.State);
        Assert.Equal(0, limiter.StrainTicks);
        Assert.Equal(0.0f, limiter.PeakOverpull);
    }

    [Fact]
    public void HoveringAtTheLimitKeepsStrainingRatherThanFlickering()
    {
        // Without hysteresis a limb sitting exactly at the limit would arm and disarm every
        // tick and never snap.
        GrabStretchLimiter limiter = New();
        limiter.Tick(Anchor, Right(300.0f), HandRadius);

        for (int tick = 0; tick < 50; tick++)
        {
            GrabStretchResult result = limiter.Tick(Anchor, Right(Limit - 2.0f), HandRadius);
            Assert.Equal(GrabStretchState.Straining, result.State);
        }

        Assert.Equal(51, limiter.StrainTicks);
    }

    [Fact]
    public void HarderPullFlingsHarder()
    {
        float gentle = SnapImpulseFor(overpull: 10.0f);
        float firm = SnapImpulseFor(overpull: 120.0f);

        Assert.True(firm > gentle * 1.5f, $"gentle={gentle} firm={firm}");
    }

    [Fact]
    public void FlingUsesThePeakPullNotTheFinalOne()
    {
        // Yank hard, then relax to just past the limit without easing off: the stored energy
        // is what the player put in, so the big pull still earns the big launch.
        GrabStretchLimiter limiter = New();
        limiter.Tick(Anchor, Right(Limit + 200.0f), HandRadius);
        Assert.Equal(200.0f, limiter.PeakOverpull, 0.01f);

        GrabStretchResult snap = default;
        for (int tick = 1; tick < GrabStretchTuning.Default.ShakeTicks; tick++)
        {
            snap = limiter.Tick(Anchor, Right(Limit + 1.0f), HandRadius);
        }

        Assert.Equal(GrabStretchState.Snapped, snap.State);
        Assert.Equal(200.0f, limiter.PeakOverpull, 0.01f);
        Assert.True(snap.SnapImpulse > SnapImpulseFor(overpull: 1.0f));
    }

    [Fact]
    public void FlingIsCapped()
    {
        float absurd = SnapImpulseFor(overpull: 100_000.0f);

        Assert.Equal(GrabStretchTuning.Default.MaximumSnapImpulse, absurd, 0.01f);
    }

    [Fact]
    public void FlingPointsAlongTheStretchSoTheBuddyFollowsItsHand()
    {
        GrabStretchLimiter limiter = New();
        GrabStretchResult snap = default;
        for (int tick = 0; tick < GrabStretchTuning.Default.ShakeTicks; tick++)
        {
            snap = limiter.Tick(Anchor, Right(400.0f), HandRadius);
        }

        Assert.Equal(GrabStretchState.Snapped, snap.State);
        Assert.Equal(1.0f, snap.SnapDirection.X, 0.001f);
        Assert.Equal(0.0f, snap.SnapDirection.Y, 0.001f);
        Assert.Equal(1.0f, snap.SnapDirection.Length(), 0.001f);
    }

    [Fact]
    public void ShakeBuzzesAcrossTheStretchNotAlongIt()
    {
        GrabStretchLimiter limiter = New();
        bool sawOffset = false;

        for (int tick = 0; tick < 24; tick++)
        {
            GrabStretchResult result = limiter.Tick(Anchor, Right(300.0f), HandRadius);
            // Stretch is along +X, so the buzz must be vertical.
            Assert.Equal(0.0f, result.ShakeOffset.X, 0.001f);
            sawOffset |= MathF.Abs(result.ShakeOffset.Y) > 0.5f;
            Assert.True(MathF.Abs(result.ShakeOffset.Y) <= GrabStretchTuning.Default.ShakeAmplitude + 0.001f,
                "early strain must buzz at the base amplitude, before any escalation");
        }

        Assert.True(sawOffset, "strained limb never vibrated");
    }

    [Fact]
    public void SnapHappensOnceUntilReset()
    {
        GrabStretchLimiter limiter = New();
        for (int tick = 0; tick < GrabStretchTuning.Default.ShakeTicks; tick++)
        {
            limiter.Tick(Anchor, Right(400.0f), HandRadius);
        }

        Assert.Equal(GrabStretchState.Snapped, limiter.State);

        // A snapped limiter must not keep firing impulses while the caller tears down the grab.
        GrabStretchResult after = limiter.Tick(Anchor, Right(400.0f), HandRadius);
        Assert.Equal(0.0f, after.SnapImpulse);

        limiter.Reset();
        Assert.Equal(GrabStretchState.Slack, limiter.State);
        Assert.Equal(GrabStretchState.Straining, limiter.Tick(Anchor, Right(400.0f), HandRadius).State);
    }

    [Fact]
    public void ZeroLengthReachDoesNotProduceNaN()
    {
        GrabStretchLimiter limiter = New(GrabStretchTuning.Default with { LimitHandWidths = 0.0001f });

        GrabStretchResult result = limiter.Tick(Anchor, Anchor, HandRadius);

        Assert.True(float.IsFinite(result.ClampedTarget.X));
        Assert.True(float.IsFinite(result.ClampedTarget.Y));
    }

    private static float SnapImpulseFor(float overpull)
    {
        GrabStretchLimiter limiter = New();
        GrabStretchResult snap = default;
        for (int tick = 0; tick < GrabStretchTuning.Default.ShakeTicks; tick++)
        {
            snap = limiter.Tick(Anchor, Right(Limit + overpull), HandRadius);
        }

        return snap.SnapImpulse;
    }

    [Fact]
    public void BuzzEscalatesOverTheFinalSecondBeforeSnapping()
    {
        // Owner feel request 2026-07-25: the pop must be telegraphed, not arbitrary.
        GrabStretchTuning tuning = GrabStretchTuning.Default;
        float early = PeakShakeInWindow(fromStrainTick: 1, ticks: 60);
        float late = PeakShakeInWindow(
            fromStrainTick: tuning.ShakeTicks - tuning.ShakeRampTicks + 1,
            ticks: tuning.ShakeRampTicks - 1);

        Assert.True(late > early * 2.0f, $"early={early} late={late}");
        Assert.True(
            late <= tuning.ShakeAmplitude * tuning.ShakeRampMultiplier + 0.001f,
            $"late={late} exceeded the ramped envelope");
    }

    [Fact]
    public void RampIsFlatUntilTheFinalWindow()
    {
        GrabStretchLimiter limiter = New();
        GrabStretchTuning tuning = GrabStretchTuning.Default;

        Assert.Equal(1.0f, limiter.RampFactor(tuning.ShakeTicks), 0.001f);
        Assert.Equal(1.0f, limiter.RampFactor(tuning.ShakeRampTicks), 0.001f);
        Assert.True(limiter.RampFactor(tuning.ShakeRampTicks - 1) > 1.0f);
    }

    [Fact]
    public void RampPeaksAtTheSnapTick()
    {
        GrabStretchLimiter limiter = New();

        Assert.Equal(
            GrabStretchTuning.Default.ShakeRampMultiplier,
            limiter.RampFactor(0),
            0.001f);
    }

    [Fact]
    public void RampIsMonotonic()
    {
        GrabStretchLimiter limiter = New();
        float previous = 0.0f;
        for (int remaining = GrabStretchTuning.Default.ShakeRampTicks; remaining >= 0; remaining--)
        {
            float factor = limiter.RampFactor(remaining);
            Assert.True(factor >= previous, $"remaining={remaining} went backwards");
            previous = factor;
        }
    }

    [Fact]
    public void ZeroRampTicks_DisablesEscalation()
    {
        GrabStretchLimiter limiter = New(GrabStretchTuning.Default with { ShakeRampTicks = 0 });

        Assert.Equal(1.0f, limiter.RampFactor(0), 0.001f);
        Assert.Equal(1.0f, limiter.RampFactor(50), 0.001f);
    }

    /// <summary>Peak buzz magnitude across a window of strain ticks.</summary>
    private static float PeakShakeInWindow(int fromStrainTick, int ticks)
    {
        GrabStretchLimiter limiter = New();
        float peak = 0.0f;
        for (int tick = 1; tick < fromStrainTick + ticks; tick++)
        {
            GrabStretchResult result = limiter.Tick(Anchor, Right(300.0f), HandRadius);
            if (tick >= fromStrainTick)
            {
                peak = MathF.Max(peak, MathF.Abs(result.ShakeOffset.Y));
            }
        }

        return peak;
    }
}
