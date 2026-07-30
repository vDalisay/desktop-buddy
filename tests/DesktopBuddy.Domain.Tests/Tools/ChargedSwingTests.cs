using System;
using System.Numerics;
using DesktopBuddy.Domain.Tools;
using Xunit;

namespace DesktopBuddy.Domain.Tests.Tools;

/// <summary>
/// Pure rules for the Home-Run Bat's grip/charge/swing cycle
/// (`docs/M5_TASK4_HOME_RUN_BAT_FEEL_PLAN.md` §4.1–§4.6). These pin the
/// arithmetic the feel depends on — the exact five-second cap, the swing
/// duration derived from tip speed, and the one-hit-per-epoch gate — so a later
/// tuning pass cannot quietly change what the owner confirmed.
/// </summary>
public sealed class ChargedSwingTests
{
    private const float Tolerance = 0.0005f;

    /// <summary>
    /// The authored Baseball Bat (§4.9), mirrored here so the derived-tick
    /// assertions below pin the shipping numbers and not a convenient fixture.
    /// Lever arm is <c>Length - Radius = 90 - 7</c>, derived from the collider.
    /// </summary>
    private static readonly ChargedSwingConstants Bat = new(
        TicksPerSecond: 120,
        MaxChargeTicks: 600,
        WindupTicks: 14,
        FollowThroughTicks: 10,
        RecoveryTicks: 42,
        LeanDegrees: 35.0f,
        WindupDegrees: 70.0f,
        SweepDegrees: 245.0f,
        FollowThroughDegrees: 25.0f,
        TipSpeedUncharged: 1800.0f,
        TipSpeedFull: 5500.0f,
        MinimumSweepTicks: 5,
        MaximumSweepTicks: 60);

    private const float TipRadius = 83.0f;

    // ---------------------------------------------------------------- states

    [Fact]
    public void AFreshToolFollowsTheCursorAndAdmitsTheWeakAttack()
    {
        ChargedSwingPhase phase = ChargedSwingPhase.Initial;

        ChargedSwingResult result = Tick(ref phase, grip: false, charge: false);

        Assert.True(result.IsValid);
        Assert.Equal(ChargedSwingState.Follow, phase.State);
        Assert.Equal(SwingImpactMode.WeakFreeSwing, result.ImpactMode);
        Assert.Equal(0, result.SwingEpoch);
    }

    [Fact]
    public void GrippingLeavesTheFreeSwingAndScoresNothing()
    {
        ChargedSwingPhase phase = ChargedSwingPhase.Initial;

        ChargedSwingResult result = Tick(ref phase, grip: true, charge: false);

        Assert.Equal(ChargedSwingState.Gripped, phase.State);
        Assert.Equal(SwingImpactMode.None, result.ImpactMode);
    }

    [Fact]
    public void ReleasingTheGripReturnsToTheWeakFreeSwing()
    {
        ChargedSwingPhase phase = Gripped();

        ChargedSwingResult result = Tick(ref phase, grip: false, charge: false);

        Assert.Equal(ChargedSwingState.Follow, phase.State);
        Assert.Equal(SwingImpactMode.WeakFreeSwing, result.ImpactMode);
    }

    [Fact]
    public void ChargingRequiresTheGripFirst()
    {
        ChargedSwingPhase phase = ChargedSwingPhase.Initial;

        // Charge alone, with no grip, is not a chord the bat answers.
        Tick(ref phase, grip: false, charge: true);

        Assert.Equal(ChargedSwingState.Follow, phase.State);
    }

    [Fact]
    public void ChargingBeginsWhileGrippedAndAdmitsNoPain()
    {
        ChargedSwingPhase phase = Gripped();

        ChargedSwingResult result = Tick(ref phase, grip: true, charge: true);

        Assert.Equal(ChargedSwingState.Charging, phase.State);
        Assert.Equal(SwingImpactMode.None, result.ImpactMode);
        Assert.Equal(0.0f, result.Charge, Tolerance);
    }

    /// <summary>The owner-confirmed bail-out: let go of the grip, nothing happens.</summary>
    [Fact]
    public void ReleasingTheGripMidChargeCancelsWithoutASwing()
    {
        ChargedSwingPhase phase = Charging(ticks: 300);

        ChargedSwingResult result = Tick(ref phase, grip: false, charge: true);

        Assert.Equal(ChargedSwingState.Follow, phase.State);
        Assert.False(result.SwingReleased);
        Assert.Equal(0, result.SwingEpoch);
        Assert.Equal(0.0f, result.Charge, Tolerance);
    }

    [Fact]
    public void ReleasingTheChargeSwingsAndOpensAnEpoch()
    {
        ChargedSwingPhase phase = Charging(ticks: 600);

        ChargedSwingResult result = Tick(ref phase, grip: true, charge: false);

        Assert.Equal(ChargedSwingState.Swinging, phase.State);
        Assert.True(result.SwingReleased);
        Assert.Equal(1, result.SwingEpoch);
        Assert.Equal(1.0f, result.ReleasedCharge, Tolerance);
        Assert.Equal(SwingImpactMode.HomeRun, result.ImpactMode);
    }

    /// <summary>
    /// A tap is still a charged-mode swing, just a modest one — distinct from
    /// the weak free swing, which never opens an epoch at all.
    /// </summary>
    [Fact]
    public void AChargeTapStillSwings()
    {
        ChargedSwingPhase phase = Gripped();
        Tick(ref phase, grip: true, charge: true);

        ChargedSwingResult result = Tick(ref phase, grip: true, charge: false);

        Assert.Equal(ChargedSwingState.Swinging, phase.State);
        Assert.True(result.SwingReleased);
        Assert.Equal(0.0f, result.ReleasedCharge, Tolerance);
    }

    [Fact]
    public void TheSwingRunsForItsDerivedDurationThenRecovers()
    {
        ChargedSwingPhase phase = Swinging(chargeTicks: 0);
        SwingPlan plan = ChargedSwing.SwingPlanFor(0.0f, TipRadius, Bat);

        for (int tick = 1; tick < plan.TotalTicks; tick++)
        {
            ChargedSwingResult mid = Tick(ref phase, grip: true, charge: false);
            Assert.Equal(ChargedSwingState.Swinging, phase.State);
            Assert.Equal(SwingImpactMode.HomeRun, mid.ImpactMode);
        }

        ChargedSwingResult result = Tick(ref phase, grip: true, charge: false);

        Assert.Equal(ChargedSwingState.Recovery, phase.State);
        Assert.Equal(SwingImpactMode.None, result.ImpactMode);
    }

    [Theory]
    [InlineData(true, ChargedSwingState.Gripped)]
    [InlineData(false, ChargedSwingState.Follow)]
    public void RecoverySettlesBackIntoWhicheverStateTheGripImplies(
        bool gripHeld,
        ChargedSwingState expected)
    {
        ChargedSwingPhase phase = Recovering();

        for (int tick = 1; tick < Bat.RecoveryTicks; tick++)
        {
            Tick(ref phase, gripHeld, charge: false);
            Assert.Equal(ChargedSwingState.Recovery, phase.State);
        }

        Tick(ref phase, gripHeld, charge: false);

        Assert.Equal(expected, phase.State);
    }

    /// <summary>
    /// Charging is locked out until the bat has been re-gripped, so a held
    /// charge button cannot chain swings straight out of recovery.
    /// </summary>
    [Fact]
    public void AHeldChargeCannotRestartTheSwingDuringRecovery()
    {
        ChargedSwingPhase phase = Recovering();

        for (int tick = 0; tick < Bat.RecoveryTicks * 2; tick++)
        {
            Tick(ref phase, grip: true, charge: true);
            Assert.NotEqual(ChargedSwingState.Swinging, phase.State);
        }

        Assert.Equal(ChargedSwingState.Charging, phase.State);
    }

    [Fact]
    public void EachSwingGetsItsOwnEpoch()
    {
        ChargedSwingPhase phase = Swinging(chargeTicks: 0);
        int first = phase.SwingEpoch;

        RunOut(ref phase, gripHeld: true);
        Tick(ref phase, grip: true, charge: true);
        ChargedSwingResult second = Tick(ref phase, grip: true, charge: false);

        Assert.Equal(1, first);
        Assert.Equal(2, second.SwingEpoch);
    }

    // ---------------------------------------------------------------- charge

    /// <summary>
    /// The five-second cap is confirmed product behaviour, not a tuning knob:
    /// 599 routed ticks is short of full and 601 is no stronger than 600.
    /// </summary>
    [Fact]
    public void ChargeCapsOnTickSixHundredAndNotBefore()
    {
        ChargedSwingPhase phase = Gripped();
        Tick(ref phase, grip: true, charge: true);

        float at599 = ChargeAfter(ref phase, ticks: 599);
        ChargedSwingResult at600 = Tick(ref phase, grip: true, charge: true);
        ChargedSwingResult at601 = Tick(ref phase, grip: true, charge: true);

        Assert.Equal(599.0f / 600.0f, at599, Tolerance);
        Assert.True(at599 < 1.0f);
        Assert.Equal(1.0f, at600.Charge, Tolerance);
        Assert.Equal(1.0f, at601.Charge, Tolerance);
        Assert.Equal(600, phase.ChargeTicks);
    }

    [Fact]
    public void HoldingPastTheCapKeepsFullChargeIndefinitely()
    {
        ChargedSwingPhase phase = Charging(ticks: 600);

        for (int tick = 0; tick < 1200; tick++)
        {
            ChargedSwingResult held = Tick(ref phase, grip: true, charge: true);
            Assert.Equal(1.0f, held.Charge, Tolerance);
        }

        ChargedSwingResult released = Tick(ref phase, grip: true, charge: false);

        Assert.Equal(1.0f, released.ReleasedCharge, Tolerance);
    }

    /// <summary>The glint is one event, not a per-tick state — it must not stutter.</summary>
    [Fact]
    public void TheChargeCompletedEdgeFiresExactlyOncePerCharge()
    {
        ChargedSwingPhase phase = Gripped();
        Tick(ref phase, grip: true, charge: true);

        int fired = 0;
        for (int tick = 0; tick < 900; tick++)
        {
            if (Tick(ref phase, grip: true, charge: true).ChargeCompleted)
            {
                fired++;
            }
        }

        Assert.Equal(1, fired);
    }

    [Fact]
    public void ASecondChargeEarnsASecondGlint()
    {
        ChargedSwingPhase phase = Charging(ticks: 600);

        // Bail out and wind up again from scratch.
        Tick(ref phase, grip: false, charge: false);
        Tick(ref phase, grip: true, charge: false);
        Tick(ref phase, grip: true, charge: true);

        int fired = 0;
        for (int tick = 0; tick < 700; tick++)
        {
            if (Tick(ref phase, grip: true, charge: true).ChargeCompleted)
            {
                fired++;
            }
        }

        Assert.Equal(1, fired);
    }

    [Fact]
    public void CancellingMidChargeDiscardsTheChargeEntirely()
    {
        ChargedSwingPhase phase = Charging(ticks: 400);

        Tick(ref phase, grip: false, charge: true);
        Tick(ref phase, grip: true, charge: false);
        ChargedSwingResult restarted = Tick(ref phase, grip: true, charge: true);

        Assert.Equal(ChargedSwingState.Charging, restarted.Phase.State);
        Assert.Equal(0, restarted.Phase.ChargeTicks);
        Assert.Equal(0.0f, restarted.Charge, Tolerance);
    }

    [Theory]
    [InlineData(-5, 600, 0.0f)]
    [InlineData(0, 600, 0.0f)]
    [InlineData(300, 600, 0.5f)]
    [InlineData(600, 600, 1.0f)]
    [InlineData(9000, 600, 1.0f)]
    [InlineData(10, 0, 0.0f)]
    public void ChargeProgressIsLinearAndClamped(int ticks, int maxTicks, float expected)
    {
        Assert.Equal(expected, ChargedSwing.ChargeProgress(ticks, maxTicks), Tolerance);
    }

    // ----------------------------------------------------------------- shake

    [Fact]
    public void ShakeIsStillAtZeroChargeAndMaximalExactlyAtTheCap()
    {
        Assert.Equal(0.0f, ChargedSwing.ShakeAmplitude(0.0f, 3.5f), Tolerance);
        Assert.Equal(3.5f, ChargedSwing.ShakeAmplitude(1.0f, 3.5f), Tolerance);
        Assert.Equal(3.5f, ChargedSwing.ShakeAmplitude(4.0f, 3.5f), Tolerance);
    }

    [Fact]
    public void ShakeRampsMonotonicallyAndLagsBehindLinear()
    {
        float previous = -1.0f;
        for (int step = 0; step <= 100; step++)
        {
            float charge = step / 100.0f;
            float amplitude = ChargedSwing.ShakeAmplitude(charge, 3.5f);

            Assert.True(amplitude >= previous, $"shake fell back at charge {charge}");
            previous = amplitude;
        }

        // Ease-in: half a charge is a quarter of the shake, so the tell is the
        // last second rather than a linear meter the player reads early.
        Assert.Equal(0.875f, ChargedSwing.ShakeAmplitude(0.5f, 3.5f), Tolerance);
    }

    [Fact]
    public void ShakeOffsetStaysInsideTheAuthoredAmplitude()
    {
        for (int step = 0; step < 500; step++)
        {
            Vector2 offset = ChargedSwing.ShakeOffset(step * 0.004f, 3.5f, 33.0f, 41.0f);

            Assert.True(MathF.Abs(offset.X) <= 3.5f + Tolerance);
            Assert.True(MathF.Abs(offset.Y) <= 3.5f + Tolerance);
        }
    }

    [Fact]
    public void ShakeOffsetIsDeterministicButDoesNotRepeatOnThePrimaryPeriod()
    {
        Vector2 first = ChargedSwing.ShakeOffset(0.25f, 3.5f, 33.0f, 41.0f);
        Vector2 again = ChargedSwing.ShakeOffset(0.25f, 3.5f, 33.0f, 41.0f);
        Vector2 aPrimaryPeriodLater =
            ChargedSwing.ShakeOffset(0.25f + (1.0f / 33.0f), 3.5f, 33.0f, 41.0f);

        Assert.Equal(first, again);
        Assert.True(Vector2.Distance(first, aPrimaryPeriodLater) > 0.1f);
    }

    [Fact]
    public void ShakeOffsetVanishesWithoutAmplitude()
    {
        Assert.Equal(Vector2.Zero, ChargedSwing.ShakeOffset(1.0f, 0.0f, 33.0f, 41.0f));
    }

    [Theory]
    [InlineData(float.NaN, 3.5f)]
    [InlineData(1.0f, float.NaN)]
    [InlineData(1.0f, float.NegativeInfinity)]
    public void MalformedShakeInputProducesNoOffset(float timeSeconds, float amplitude)
    {
        Assert.Equal(Vector2.Zero, ChargedSwing.ShakeOffset(timeSeconds, amplitude, 33.0f, 41.0f));
        Assert.Equal(0.0f, ChargedSwing.ShakeAmplitude(float.NaN, amplitude));
    }

    // ------------------------------------------------------------ swing plan

    /// <summary>
    /// Guards §4.6's derivation: <c>sweep_ticks_derive_from_tip_speed</c>. The
    /// authored bat's endpoints are 24 and 8 ticks, computed from the tip speed
    /// and the lever arm. An edit that reintroduces an independently authored
    /// tick count fails this and its sibling below.
    /// </summary>
    [Fact]
    public void SweepTicksDeriveFromTipSpeed()
    {
        SwingPlan uncharged = ChargedSwing.SwingPlanFor(0.0f, TipRadius, Bat);
        SwingPlan full = ChargedSwing.SwingPlanFor(1.0f, TipRadius, Bat);

        Assert.True(uncharged.IsValid);
        Assert.True(full.IsValid);
        Assert.Equal(24, uncharged.SweepTicks);
        Assert.Equal(8, full.SweepTicks);
        Assert.Equal(1800.0f, uncharged.TargetTipSpeed, 0.01f);
        Assert.Equal(5500.0f, full.TargetTipSpeed, 0.01f);
        Assert.Equal(1800.0f / TipRadius, uncharged.TargetAngularVelocity, 0.001f);
        Assert.Equal(5500.0f / TipRadius, full.TargetAngularVelocity, 0.001f);
    }

    /// <summary>
    /// <c>raising_tip_speed_shortens_the_sweep</c>: the two move together
    /// because they are one number. Duration is never authored beside a speed.
    /// </summary>
    [Fact]
    public void RaisingTipSpeedShortensTheSweep()
    {
        int previousTicks = int.MaxValue;
        float previousSpeed = -1.0f;

        for (int step = 0; step <= 20; step++)
        {
            SwingPlan plan = ChargedSwing.SwingPlanFor(step / 20.0f, TipRadius, Bat);

            Assert.True(plan.IsValid);
            Assert.True(plan.TargetTipSpeed > previousSpeed);
            Assert.True(plan.SweepTicks <= previousTicks);
            previousTicks = plan.SweepTicks;
            previousSpeed = plan.TargetTipSpeed;
        }

        Assert.True(previousTicks < 24);
    }

    [Fact]
    public void TotalSwingLengthIsTheWindupSweepAndTail()
    {
        SwingPlan uncharged = ChargedSwing.SwingPlanFor(0.0f, TipRadius, Bat);
        SwingPlan full = ChargedSwing.SwingPlanFor(1.0f, TipRadius, Bat);

        Assert.Equal(14 + 24 + 10, uncharged.TotalTicks);
        Assert.Equal(14 + 8 + 10, full.TotalTicks);
    }

    /// <summary>
    /// An absurd authored tip speed must not produce a one-tick swing that no
    /// contact could ever be observed inside.
    /// </summary>
    [Fact]
    public void AnAbsurdTipSpeedIsBoundedByTheDerivedSweepLimits()
    {
        ChargedSwingConstants silly = Bat with { TipSpeedFull = 500_000.0f };
        ChargedSwingConstants sluggish = Bat with
        {
            TipSpeedUncharged = 1.0f,
            TipSpeedFull = 2.0f,
        };

        Assert.Equal(Bat.MinimumSweepTicks, ChargedSwing.SwingPlanFor(1.0f, TipRadius, silly).SweepTicks);
        Assert.Equal(Bat.MaximumSweepTicks, ChargedSwing.SwingPlanFor(0.0f, TipRadius, sluggish).SweepTicks);
    }

    [Theory]
    [InlineData(float.NaN, TipRadius)]
    [InlineData(0.5f, 0.0f)]
    [InlineData(0.5f, float.NaN)]
    [InlineData(0.5f, -83.0f)]
    public void AMalformedPlanRequestIsRejected(float charge, float tipRadius)
    {
        Assert.False(ChargedSwing.SwingPlanFor(charge, tipRadius, Bat).IsValid);
    }

    [Fact]
    public void MisorderedTipSpeedsAreRejectedRatherThanInverted()
    {
        ChargedSwingConstants backwards = Bat with { TipSpeedFull = 900.0f };

        Assert.False(backwards.IsWellFormed());
        Assert.False(ChargedSwing.SwingPlanFor(1.0f, TipRadius, backwards).IsValid);
    }

    [Fact]
    public void AWindupThatDoesNotClearTheLeanIsRejected()
    {
        ChargedSwingConstants backwards = Bat with { WindupDegrees = 20.0f };

        Assert.False(backwards.IsWellFormed());
    }

    // ------------------------------------------------------------ trajectory

    [Fact]
    public void TheArcStartsAtTheChargeLeanAndEndsPastTheFollowThrough()
    {
        SwingPlan plan = ChargedSwing.SwingPlanFor(1.0f, TipRadius, Bat);

        float start = AngleAt(0, plan, 1);
        float windupEnd = AngleAt(plan.WindupTicks, plan, 1);
        float sweepEnd = AngleAt(plan.WindupTicks + plan.SweepTicks, plan, 1);
        float settled = AngleAt(plan.TotalTicks + 20, plan, 1);

        Assert.Equal(Radians(-35.0f), start, Tolerance);
        Assert.Equal(Radians(-70.0f), windupEnd, Tolerance);
        Assert.Equal(Radians(-315.0f), sweepEnd, Tolerance);
        Assert.Equal(Radians(-340.0f), settled, Tolerance);
    }

    /// <summary>
    /// A swing that stalls or backs up mid-arc reads as a glitch and would let
    /// the contact zone be entered twice. The arc only ever goes one way.
    /// </summary>
    [Fact]
    public void TheArcNeverBacktracksAndItsCommandedRateStaysFinite()
    {
        SwingPlan plan = ChargedSwing.SwingPlanFor(1.0f, TipRadius, Bat);
        float previous = float.MaxValue;

        for (int tick = 0; tick <= plan.TotalTicks + 5; tick++)
        {
            SwingTrajectoryPoint point =
                ChargedSwing.SwingTrajectoryAt(tick, plan, 1, Bat);

            Assert.True(point.IsValid);
            Assert.True(float.IsFinite(point.BarrelAngle));
            Assert.True(float.IsFinite(point.TargetAngularVelocity));
            Assert.True(point.TargetAngularVelocity <= 0.0f, $"rate reversed at tick {tick}");
            Assert.True(point.BarrelAngle <= previous + Tolerance, $"arc backed up at tick {tick}");
            previous = point.BarrelAngle;
        }
    }

    [Fact]
    public void TheArcIsContinuousAcrossItsPhaseBoundaries()
    {
        SwingPlan plan = ChargedSwing.SwingPlanFor(0.0f, TipRadius, Bat);
        float sweepStep = Radians(Bat.SweepDegrees) / plan.SweepTicks;

        for (int tick = 1; tick <= plan.TotalTicks; tick++)
        {
            float step = MathF.Abs(AngleAt(tick, plan, 1) - AngleAt(tick - 1, plan, 1));

            // No phase change may jump farther than one plateau step.
            Assert.True(step <= sweepStep + Tolerance, $"arc jumped at tick {tick}");
        }
    }

    [Fact]
    public void TheSweepHoldsTheCommandedRateItsTickCountWasDerivedFrom()
    {
        SwingPlan plan = ChargedSwing.SwingPlanFor(1.0f, TipRadius, Bat);
        float realized = Radians(Bat.SweepDegrees) / (plan.SweepTicks / (float)Bat.TicksPerSecond);

        for (int tick = plan.WindupTicks; tick < plan.WindupTicks + plan.SweepTicks; tick++)
        {
            SwingTrajectoryPoint point = ChargedSwing.SwingTrajectoryAt(tick, plan, 1, Bat);

            Assert.Equal(-realized, point.TargetAngularVelocity, 0.001f);
        }

        // Tick rounding is the only gap between intent and realized rate.
        Assert.True(MathF.Abs(realized - plan.TargetAngularVelocity) / plan.TargetAngularVelocity < 0.05f);
    }

    [Fact]
    public void AFullChargeArcIsFasterThanAnUnchargedOne()
    {
        SwingPlan slow = ChargedSwing.SwingPlanFor(0.0f, TipRadius, Bat);
        SwingPlan fast = ChargedSwing.SwingPlanFor(1.0f, TipRadius, Bat);

        float slowRate = MathF.Abs(
            ChargedSwing.SwingTrajectoryAt(slow.WindupTicks, slow, 1, Bat).TargetAngularVelocity);
        float fastRate = MathF.Abs(
            ChargedSwing.SwingTrajectoryAt(fast.WindupTicks, fast, 1, Bat).TargetAngularVelocity);

        Assert.True(fastRate > slowRate * 2.5f);
    }

    [Fact]
    public void MirroredDragsProduceMirroredArcs()
    {
        SwingPlan plan = ChargedSwing.SwingPlanFor(0.5f, TipRadius, Bat);

        for (int tick = 0; tick <= plan.TotalTicks; tick++)
        {
            SwingTrajectoryPoint right = ChargedSwing.SwingTrajectoryAt(tick, plan, 1, Bat);
            SwingTrajectoryPoint left = ChargedSwing.SwingTrajectoryAt(tick, plan, -1, Bat);

            Assert.Equal(-right.BarrelAngle, left.BarrelAngle, Tolerance);
            Assert.Equal(-right.TargetAngularVelocity, left.TargetAngularVelocity, Tolerance);
        }
    }

    [Fact]
    public void ANegativeTickIsTreatedAsTheStartOfTheArc()
    {
        SwingPlan plan = ChargedSwing.SwingPlanFor(0.5f, TipRadius, Bat);

        Assert.Equal(AngleAt(0, plan, 1), AngleAt(-9, plan, 1), Tolerance);
    }

    [Fact]
    public void AnInvalidPlanYieldsNoTrajectory()
    {
        SwingPlan broken = ChargedSwing.SwingPlanFor(0.5f, 0.0f, Bat);

        Assert.False(ChargedSwing.SwingTrajectoryAt(3, broken, 1, Bat).IsValid);
    }

    // ---------------------------------------------------------------- aiming

    [Theory]
    [InlineData(20.0f, 1, 1)]
    [InlineData(-20.0f, 1, -1)]
    [InlineData(6.0f, -1, 1)]      // exactly at the threshold counts as aiming
    [InlineData(-6.0f, 1, -1)]
    [InlineData(5.9f, 1, 1)]       // dead zone: the previous aim persists
    [InlineData(-5.9f, 1, 1)]
    [InlineData(5.9f, -1, -1)]
    [InlineData(0.0f, -1, -1)]
    [InlineData(0.0f, 0, 1)]       // never aimed yet: default right
    [InlineData(float.NaN, -1, -1)]
    public void AimFollowsSignificantCursorTravelAndOtherwiseHolds(
        float travelX,
        int lastSign,
        int expected)
    {
        Assert.Equal(expected, ChargedSwing.SwingDirectionSign(travelX, 6.0f, lastSign));
    }

    [Fact]
    public void TheLastSignificantDragBeforeReleaseIsTheOneThatCounts()
    {
        ChargedSwingPhase phase = Gripped();
        Tick(ref phase, grip: true, charge: true, direction: 1);
        Tick(ref phase, grip: true, charge: true, direction: 1);
        Tick(ref phase, grip: true, charge: true, direction: -1);

        ChargedSwingResult released = Tick(ref phase, grip: true, charge: false, direction: -1);

        Assert.Equal(-1, released.DirectionSign);
    }

    [Fact]
    public void PointerMotionAfterReleaseCannotChangeDirection()
    {
        ChargedSwingPhase phase = Swinging(chargeTicks: 600, direction: 1);

        for (int tick = 0; tick < 10; tick++)
        {
            ChargedSwingResult result = Tick(ref phase, grip: true, charge: false, direction: -1);
            Assert.Equal(1, result.DirectionSign);
        }
    }

    [Fact]
    public void ThePivotIsLatchedAtReleaseAndDoesNotChaseTheCursor()
    {
        ChargedSwingPhase phase = Charging(ticks: 600);

        ChargedSwingResult released = Tick(
            ref phase, grip: true, charge: false, handle: new Vector2(12.0f, -34.0f));
        ChargedSwingResult later = Tick(
            ref phase, grip: true, charge: false, handle: new Vector2(900.0f, 900.0f));

        Assert.Equal(new Vector2(12.0f, -34.0f), released.Pivot);
        Assert.Equal(new Vector2(12.0f, -34.0f), later.Pivot);
    }

    [Fact]
    public void TheChargeLeanTiltsAwayFromTheSwingSide()
    {
        Assert.Equal(
            Radians(-35.0f), ChargedSwing.RestAngleFor(ChargedSwingState.Charging, 1, Bat), Tolerance);
        Assert.Equal(
            Radians(35.0f), ChargedSwing.RestAngleFor(ChargedSwingState.Charging, -1, Bat), Tolerance);
        Assert.Equal(
            0.0f, ChargedSwing.RestAngleFor(ChargedSwingState.Gripped, 1, Bat), Tolerance);
        Assert.Equal(
            0.0f, ChargedSwing.RestAngleFor(ChargedSwingState.Recovery, -1, Bat), Tolerance);
    }

    // -------------------------------------------------------------- hit lag

    [Theory]
    [InlineData(0.0f, 6)]
    [InlineData(1.0f, 60)]
    [InlineData(0.5f, 33)]
    [InlineData(-4.0f, 6)]
    [InlineData(7.0f, 60)]
    [InlineData(float.NaN, 6)]
    public void HitLagScalesLinearlyBetweenTheAuthoredEndpoints(float charge, int expected)
    {
        Assert.Equal(expected, ChargedSwing.HitLagTicks(charge, 6, 60));
    }

    [Fact]
    public void HitLagSurvivesMisorderedEndpoints()
    {
        Assert.Equal(6, ChargedSwing.HitLagTicks(1.0f, 6, 2));
        Assert.Equal(0, ChargedSwing.HitLagTicks(1.0f, -5, -1));
    }

    // ------------------------------------------------------------- admission

    [Fact]
    public void GripAndRecoveryContactsAreNotScoredAtAll()
    {
        SwingImpactAdmissionResult result =
            SwingImpactAdmission.Evaluate(SwingImpactMode.None, 3, false, true);

        Assert.True(result.IsValid);
        Assert.False(result.Admitted);
        Assert.False(result.ClaimsEpoch);
    }

    [Fact]
    public void TheWeakFreeSwingIsAdmittedWithoutConsumingAnything()
    {
        SwingImpactAdmissionResult result =
            SwingImpactAdmission.Evaluate(SwingImpactMode.WeakFreeSwing, 0, false, true);

        Assert.True(result.Admitted);
        Assert.False(result.ClaimsEpoch);
    }

    [Fact]
    public void AHomeRunEpochIsSpentOnItsFirstHitThatHurt()
    {
        SwingImpactAdmissionResult first =
            SwingImpactAdmission.Evaluate(SwingImpactMode.HomeRun, 1, alreadyClaimed: false, scoredPain: true);
        SwingImpactAdmissionResult second =
            SwingImpactAdmission.Evaluate(SwingImpactMode.HomeRun, 1, alreadyClaimed: true, scoredPain: true);

        Assert.True(first.Admitted);
        Assert.True(first.ClaimsEpoch);
        Assert.False(second.Admitted);
        Assert.False(second.ClaimsEpoch);
    }

    /// <summary>A graze costs the player nothing — the attack is still live.</summary>
    [Fact]
    public void AZeroPainGrazeDoesNotConsumeTheEpoch()
    {
        SwingImpactAdmissionResult graze =
            SwingImpactAdmission.Evaluate(SwingImpactMode.HomeRun, 1, alreadyClaimed: false, scoredPain: false);

        Assert.True(graze.Admitted);
        Assert.False(graze.ClaimsEpoch);
    }

    [Fact]
    public void AHomeRunWithoutAnEpochIsRejectedAsMalformed()
    {
        SwingImpactAdmissionResult result =
            SwingImpactAdmission.Evaluate(SwingImpactMode.HomeRun, 0, false, true);

        Assert.False(result.IsValid);
        Assert.False(result.Admitted);
    }

    // -------------------------------------------------------- malformed input

    [Fact]
    public void AMalformedProfileLeavesTheToolInert()
    {
        ChargedSwingPhase phase = ChargedSwingPhase.Initial;
        ChargedSwingConstants broken = Bat with { MaxChargeTicks = 0 };

        ChargedSwingResult result = ChargedSwingMachine.Tick(new ChargedSwingInput(
            phase, true, true, 1, Vector2.Zero, TipRadius, broken));

        Assert.False(result.IsValid);
        Assert.Equal(ChargedSwingState.Follow, result.Phase.State);
        Assert.Equal(SwingImpactMode.None, result.ImpactMode);
    }

    [Theory]
    [InlineData(0.0f, 0.0f, 0.0f)]
    [InlineData(83.0f, float.NaN, 0.0f)]
    [InlineData(83.0f, 0.0f, float.PositiveInfinity)]
    public void AMalformedGeometryOrCursorLeavesTheToolInert(
        float tipRadius,
        float handleX,
        float handleY)
    {
        ChargedSwingResult result = ChargedSwingMachine.Tick(new ChargedSwingInput(
            Gripped(), true, true, 1, new Vector2(handleX, handleY), tipRadius, Bat));

        Assert.False(result.IsValid);
        Assert.Equal(ChargedSwingState.Gripped, result.Phase.State);
    }

    [Theory]
    [InlineData(ChargedSwingState.Follow, SwingImpactMode.WeakFreeSwing)]
    [InlineData(ChargedSwingState.Gripped, SwingImpactMode.None)]
    [InlineData(ChargedSwingState.Charging, SwingImpactMode.None)]
    [InlineData(ChargedSwingState.Swinging, SwingImpactMode.HomeRun)]
    [InlineData(ChargedSwingState.Recovery, SwingImpactMode.None)]
    public void EveryStateDeclaresWhatItsContactsMayBecome(
        ChargedSwingState state,
        SwingImpactMode expected)
    {
        Assert.Equal(expected, ChargedSwingMachine.ModeFor(state));
    }

    // ----------------------------------------------------------------- setup

    private static float Radians(float degrees) => degrees * MathF.PI / 180.0f;

    private static float AngleAt(int tick, in SwingPlan plan, int sign) =>
        ChargedSwing.SwingTrajectoryAt(tick, plan, sign, Bat).BarrelAngle;

    private static ChargedSwingResult Tick(
        ref ChargedSwingPhase phase,
        bool grip,
        bool charge,
        int direction = 1,
        Vector2 handle = default)
    {
        ChargedSwingResult result = ChargedSwingMachine.Tick(new ChargedSwingInput(
            phase, grip, charge, direction, handle, TipRadius, Bat));
        phase = result.Phase;
        return result;
    }

    private static ChargedSwingPhase Gripped()
    {
        ChargedSwingPhase phase = ChargedSwingPhase.Initial;
        Tick(ref phase, grip: true, charge: false);
        return phase;
    }

    private static ChargedSwingPhase Charging(int ticks, int direction = 1)
    {
        ChargedSwingPhase phase = Gripped();
        Tick(ref phase, grip: true, charge: true, direction: direction);
        for (int tick = 0; tick < ticks; tick++)
        {
            Tick(ref phase, grip: true, charge: true, direction: direction);
        }

        return phase;
    }

    private static ChargedSwingPhase Swinging(int chargeTicks, int direction = 1)
    {
        ChargedSwingPhase phase = Charging(chargeTicks, direction);
        Tick(ref phase, grip: true, charge: false, direction: direction);
        return phase;
    }

    private static ChargedSwingPhase Recovering()
    {
        ChargedSwingPhase phase = Swinging(chargeTicks: 0);
        while (phase.State == ChargedSwingState.Swinging)
        {
            Tick(ref phase, grip: true, charge: false);
        }

        return phase;
    }

    /// <summary>Run a swing and its recovery out to whatever comes next.</summary>
    private static void RunOut(ref ChargedSwingPhase phase, bool gripHeld)
    {
        for (int tick = 0; tick < 500; tick++)
        {
            if (phase.State is not (ChargedSwingState.Swinging or ChargedSwingState.Recovery))
            {
                return;
            }

            Tick(ref phase, gripHeld, charge: false);
        }

        Assert.Fail("the swing never finished");
    }

    private static float ChargeAfter(ref ChargedSwingPhase phase, int ticks)
    {
        float charge = 0.0f;
        for (int tick = 0; tick < ticks; tick++)
        {
            charge = Tick(ref phase, grip: true, charge: true).Charge;
        }

        return charge;
    }
}
