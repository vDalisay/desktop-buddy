using System;
using System.Numerics;
using DesktopBuddy.Domain.Tools;
using Xunit;

namespace DesktopBuddy.Domain.Tests.Tools;

/// <summary>
/// The shared cursor-weapon aim contract (RAGDOLL §9.1) as refined by the M5 Task 5 feel
/// plan: the weapon follows the direction the pointer has lately been travelling, steering
/// toward it at a bounded rate rather than snapping to the latest delta; it holds still
/// when the hand does; the wheel offsets it up or down; and starting to aim again clears
/// that offset.
///
/// <para>The Pistol's authored constants are used throughout, so these are assertions about
/// a gun the game really ships rather than about round numbers.</para>
/// </summary>
public sealed class CursorAimModelTests
{
    private const float Tolerance = 0.0005f;

    private static readonly CursorAimConstants Constants = new(
        SmoothingHalfLifeTicks: 14.0f,
        MinimumAimSpeed: 0.35f,
        MaxTurnDegreesPerTick: 6.0f,
        DegreesPerWheelStep: 5.0f,
        MaximumOffsetDegrees: 60.0f);

    /// <summary>Ticks a bounded turn needs to cover an angle, with the plan's slack.</summary>
    private static int TicksToTurn(float degrees) =>
        (int)MathF.Ceiling(degrees / Constants.MaxTurnDegreesPerTick) +
        (int)MathF.Ceiling(Constants.SmoothingHalfLifeTicks * 3.0f);

    [Fact]
    public void AFreshWeaponHasNoAimUntilThePointerMoves()
    {
        var aim = new Aim();

        CursorAimResult result = aim.Move(Vector2.Zero);

        Assert.False(result.IsValid);
        Assert.False(result.IsSteering);
        Assert.False(aim.State.HasAim);
        Assert.Equal(Vector2.Zero, result.Forward);
    }

    [Fact]
    public void TheFirstRealTravelEstablishesTheAimAlongIt()
    {
        var aim = new Aim();

        // One brisk sweep is enough to cross the gate, and the aim it establishes is the
        // direction of travel exactly: there is nothing yet to steer away from.
        CursorAimResult right = aim.Move(new Vector2(40.0f, 0.0f));

        Assert.True(right.IsValid);
        Assert.True(right.IsSteering);
        AssertDirection(new Vector2(1.0f, 0.0f), right.Forward);
    }

    [Fact]
    public void AlternatingPixelDeltasSettleOnOneSteadyDirection()
    {
        // The defect this model exists to kill: a steady diagonal drag produces only the
        // deltas (1,0) and (1,1), and taking either one raw quantizes the aim to 0 or 45
        // degrees and flips between them every single tick. Smoothed, the pair averages to
        // the (1, 0.5) line the hand is really moving along — and, far more importantly,
        // the aim stops moving at all once it gets there.
        var aim = new Aim();
        for (int tick = 0; tick < 240; tick++)
        {
            aim.Move(tick % 2 == 0
                ? new Vector2(1.0f, 0.0f)
                : new Vector2(1.0f, 1.0f));
        }

        float lowest = float.MaxValue;
        float highest = float.MinValue;
        for (int tick = 0; tick < 40; tick++)
        {
            CursorAimResult result = aim.Move(tick % 2 == 0
                ? new Vector2(1.0f, 0.0f)
                : new Vector2(1.0f, 1.0f));
            Assert.True(result.IsValid);
            float angle = AngleDegrees(result.Forward);
            lowest = MathF.Min(lowest, angle);
            highest = MathF.Max(highest, angle);
        }

        float expected = AngleDegrees(new Vector2(1.0f, 0.5f));
        Assert.Equal(expected, lowest, 1.5f);
        Assert.Equal(expected, highest, 1.5f);
        // The unsmoothed model swung the full 45 degrees between these two deltas, every
        // tick. What is left is a wobble far below anything a player can see.
        Assert.True(
            highest - lowest < 2.0f,
            $"aim wandered {highest - lowest:F2} degrees between alternating deltas");
    }

    [Fact]
    public void SustainedSlowTravelStillSteersTheAim()
    {
        // Half a pixel per tick is 60 px/s: a deliberate, careful aim. The retired raw
        // threshold was a whole pixel per tick — 120 px/s — so it discarded every tick of
        // this and the weapon kept pointing where it used to, which is why a slow leftward
        // aim used to keep firing to the right. The claim here is that it steers at all.
        var aim = new Aim();
        aim.Move(new Vector2(40.0f, 0.0f));
        AssertDirection(new Vector2(1.0f, 0.0f), aim.State.Forward);

        CursorAimResult result = default;
        for (int tick = 0; tick < 300; tick++)
            result = aim.Move(new Vector2(-0.5f, 0.0f));

        Assert.True(result.IsValid);
        Assert.True(result.IsSteering);
        AssertDirection(new Vector2(-1.0f, 0.0f), result.Forward);
    }

    [Fact]
    public void AStationaryPointerKeepsTheLastAim()
    {
        var aim = new Aim();
        aim.Move(new Vector2(-30.0f, -30.0f));
        Vector2 established = aim.State.Forward;

        for (int tick = 0; tick < 600; tick++)
        {
            CursorAimResult still = aim.Move(Vector2.Zero);
            Assert.True(still.IsValid);
            AssertDirection(established, still.Forward);
        }

        // The aim held, and it stopped steering on the way: the smoothed velocity decays,
        // and an aim below the gate must not drift back toward anything.
        Assert.False(aim.Move(Vector2.Zero).IsSteering);
    }

    [Fact]
    public void AimDoesNotFlipOnTheJitterOfReleasingTheMouse()
    {
        var aim = new Aim();
        for (int tick = 0; tick < 30; tick++)
            aim.Move(new Vector2(3.0f, 0.0f));

        // A few ticks of tiny backward twitch, the way a hand does when it lets go.
        CursorAimResult result = default;
        for (int tick = 0; tick < 3; tick++)
            result = aim.Move(new Vector2(-1.0f, 0.0f));

        Assert.True(result.Forward.X > 0.9f);
        Assert.True(AngleDegrees(result.Forward) is > -20.0f and < 20.0f);
    }

    [Fact]
    public void ASustainedReversalCompletesWithinTheAuthoredTurnRate()
    {
        var aim = new Aim();
        for (int tick = 0; tick < 60; tick++)
            aim.Move(new Vector2(3.0f, 0.0f));

        int budget = TicksToTurn(180.0f);
        CursorAimResult result = default;
        for (int tick = 0; tick < budget; tick++)
        {
            result = aim.Move(new Vector2(-3.0f, 0.0f));
            Assert.Equal(1.0f, result.Forward.Length(), Tolerance);
        }

        AssertDirection(new Vector2(-1.0f, 0.0f), result.Forward);
    }

    [Fact]
    public void TheAimNeverTurnsFasterThanTheAuthoredRate()
    {
        var aim = new Aim();
        for (int tick = 0; tick < 60; tick++)
            aim.Move(new Vector2(3.0f, 0.0f));

        Vector2 previous = aim.State.Forward;
        for (int tick = 0; tick < 120; tick++)
        {
            // A hard reversal every tick: the worst case a mouse can ask for.
            CursorAimResult result = aim.Move(tick % 2 == 0
                ? new Vector2(-40.0f, -40.0f)
                : new Vector2(40.0f, 40.0f));
            float turned = MathF.Abs(SignedDegreesBetween(previous, result.State.Forward));
            Assert.True(
                turned <= Constants.MaxTurnDegreesPerTick + 0.001f,
                $"turned {turned:F3} degrees in one tick");
            previous = result.State.Forward;
        }
    }

    [Theory]
    [InlineData(170.0f)]
    [InlineData(-170.0f)]
    public void TheAimTurnsTheShortWayAround(float targetDegrees)
    {
        var aim = new Aim();
        for (int tick = 0; tick < 60; tick++)
            aim.Move(new Vector2(3.0f, 0.0f));

        // A flick violent enough to swing the smoothed velocity all the way over in one
        // tick, so this is the slew's own choice of direction and not the filter's path.
        // 170 degrees one way is 190 the other: the short way must win, whichever it is.
        CursorAimResult turned = aim.Move(FromDegrees(targetDegrees) * 1000.0f);

        float step = SignedDegreesBetween(new Vector2(1.0f, 0.0f), turned.State.Forward);
        Assert.Equal(
            MathF.Sign(targetDegrees) * Constants.MaxTurnDegreesPerTick, step, 0.01f);
    }

    [Fact]
    public void TheSmoothedSpeedHalvesOverTheAuthoredHalfLife()
    {
        var aim = new Aim();
        for (int tick = 0; tick < 240; tick++)
            aim.Move(new Vector2(2.0f, 0.0f));

        float settled = aim.Move(Vector2.Zero).SmoothedSpeed;
        CursorAimResult result = default;
        for (int tick = 0; tick < (int)Constants.SmoothingHalfLifeTicks; tick++)
            result = aim.Move(Vector2.Zero);

        Assert.Equal(settled * 0.5f, result.SmoothedSpeed, settled * 0.01f);
    }

    [Fact]
    public void WheelUpRaisesTheAimOfARightwardWeapon()
    {
        var aim = new Aim();
        aim.AimRight();

        CursorAimResult raised = aim.Wheel(2);

        Assert.True(raised.IsValid);
        Assert.Equal(10.0f, raised.OffsetDegrees, Tolerance);
        // Screen Y grows downward, so a raised aim has a negative Y component.
        Assert.True(raised.Forward.Y < 0.0f);
        Assert.Equal(-10.0f, ElevationDegrees(raised.Forward), 0.01f);
    }

    [Fact]
    public void WheelUpRaisesTheAimOfALeftwardWeaponToo()
    {
        var aim = new Aim();
        aim.AimAlong(new Vector2(-1.0f, 0.0f));

        CursorAimResult raised = aim.Wheel(2);

        Assert.True(raised.IsValid);
        Assert.Equal(10.0f, raised.OffsetDegrees, Tolerance);
        Assert.True(raised.Forward.Y < 0.0f);
        Assert.True(raised.Forward.X < 0.0f);
    }

    [Fact]
    public void WheelDownLowersTheAim()
    {
        var aim = new Aim();
        aim.AimRight();

        CursorAimResult lowered = aim.Wheel(-3);

        Assert.Equal(-15.0f, lowered.OffsetDegrees, Tolerance);
        Assert.True(lowered.Forward.Y > 0.0f);
    }

    [Fact]
    public void WheelStepsAccumulateAndClampAtTheAuthoredMaximum()
    {
        var aim = new Aim();
        aim.AimRight();

        for (int step = 0; step < 40; step++)
            aim.Wheel(1);

        Assert.Equal(Constants.MaximumOffsetDegrees, aim.State.OffsetDegrees, Tolerance);

        for (int step = 0; step < 80; step++)
            aim.Wheel(-1);

        Assert.Equal(-Constants.MaximumOffsetDegrees, aim.State.OffsetDegrees, Tolerance);
    }

    [Fact]
    public void AimingAgainClearsTheOffsetOnTheTickTheAimWakesUp()
    {
        var aim = new Aim();
        aim.AimRight();
        aim.Wheel(4);
        Assert.Equal(20.0f, aim.State.OffsetDegrees, Tolerance);

        // Still below the gate: the aim has not started steering again yet.
        CursorAimResult creep = aim.Move(new Vector2(0.0f, -0.05f));
        Assert.False(creep.OffsetCleared);
        Assert.Equal(20.0f, creep.OffsetDegrees, Tolerance);

        CursorAimResult woken = aim.Move(new Vector2(0.0f, -25.0f));
        Assert.True(woken.IsSteering);
        Assert.True(woken.OffsetCleared);
        Assert.Equal(0.0f, woken.OffsetDegrees, Tolerance);
    }

    [Fact]
    public void ClearingIsReportedOnlyWhenAnOffsetWasReallyInForce()
    {
        var aim = new Aim();

        Assert.False(aim.Move(new Vector2(40.0f, 0.0f)).OffsetCleared);
        Assert.False(aim.Move(new Vector2(40.0f, 0.0f)).OffsetCleared);

        aim.SettleAtRest();
        aim.Wheel(1);
        Assert.True(aim.Move(new Vector2(40.0f, 0.0f)).OffsetCleared);
    }

    [Fact]
    public void JitterDoesNotClearTheOffset()
    {
        var aim = new Aim();
        aim.AimRight();
        aim.Wheel(3);

        CursorAimResult jitter = aim.Move(new Vector2(0.3f, -0.2f));

        Assert.False(jitter.OffsetCleared);
        Assert.Equal(15.0f, jitter.OffsetDegrees, Tolerance);
    }

    [Fact]
    public void AWheelOffsetSetBeforeAnyMotionIsCarriedUntilTheFirstAim()
    {
        var aim = new Aim();

        CursorAimResult early = aim.Wheel(2);
        Assert.False(early.IsValid);
        Assert.Equal(10.0f, aim.State.OffsetDegrees, Tolerance);

        // The travel that establishes the aim is itself the aim waking up, so it clears
        // the offset it never had a direction to apply to.
        CursorAimResult first = aim.Move(new Vector2(40.0f, 0.0f));
        Assert.True(first.IsValid);
        Assert.True(first.OffsetCleared);
        Assert.Equal(0.0f, first.OffsetDegrees, Tolerance);
        AssertDirection(new Vector2(1.0f, 0.0f), first.Forward);
    }

    [Fact]
    public void AVerticalAimIsLeftAloneByTheWheel()
    {
        var aim = new Aim();
        aim.AimAlong(new Vector2(0.0f, -1.0f));

        CursorAimResult raised = aim.Wheel(4);

        // Straight up is already the extreme the offset reaches for, and it has no
        // horizontal side to pitch about, so the aim holds instead of spinning.
        Assert.True(raised.IsValid);
        AssertDirection(new Vector2(0.0f, -1.0f), raised.Forward);
    }

    [Fact]
    public void ForwardIsAlwaysAUnitVector()
    {
        var aim = new Aim();
        aim.AimAlong(Vector2.Normalize(new Vector2(400.0f, -900.0f)));
        Assert.Equal(1.0f, aim.Move(Vector2.Zero).Forward.Length(), Tolerance);

        aim.Wheel(6);
        Assert.Equal(1.0f, aim.Move(Vector2.Zero).Forward.Length(), Tolerance);
    }

    [Fact]
    public void TheSameInputSequenceProducesTheSameStates()
    {
        var first = new Aim();
        var second = new Aim();

        for (int tick = 0; tick < 200; tick++)
        {
            var motion = new Vector2(
                MathF.Sin(tick * 0.37f) * 4.0f, MathF.Cos(tick * 0.11f) * 3.0f);
            int wheel = tick % 17 == 0 ? 1 : 0;
            CursorAimResult a = first.Tick(motion, wheel);
            CursorAimResult b = second.Tick(motion, wheel);
            Assert.Equal(a.State, b.State);
            Assert.Equal(a.Forward, b.Forward);
        }
    }

    [Fact]
    public void NonFiniteMotionLeavesTheAimUntouched()
    {
        var aim = new Aim();
        aim.AimRight();
        Vector2 established = aim.State.Forward;

        CursorAimResult poisoned = aim.Move(new Vector2(float.NaN, 0.0f));

        Assert.False(poisoned.IsValid);
        AssertDirection(established, aim.State.Forward);
    }

    [Fact]
    public void AMalformedProfileYieldsAnInertAim()
    {
        var aim = new Aim(new CursorAimConstants(0.0f, 0.0f, 0.0f, 0.0f, -1.0f));

        CursorAimResult result = aim.Move(new Vector2(40.0f, 0.0f));

        Assert.False(result.IsValid);
        Assert.False(aim.State.HasAim);
    }

    /// <summary>Signed degrees above (negative) or below (positive) the horizon.</summary>
    private static float ElevationDegrees(Vector2 forward) =>
        MathF.Atan2(forward.Y, MathF.Abs(forward.X)) * 180.0f / MathF.PI;

    private static float AngleDegrees(Vector2 forward) =>
        MathF.Atan2(forward.Y, forward.X) * 180.0f / MathF.PI;

    private static Vector2 FromDegrees(float degrees)
    {
        float radians = degrees * MathF.PI / 180.0f;
        return new Vector2(MathF.Cos(radians), MathF.Sin(radians));
    }

    private static float SignedDegreesBetween(Vector2 from, Vector2 to)
    {
        float cross = (from.X * to.Y) - (from.Y * to.X);
        float dot = (from.X * to.X) + (from.Y * to.Y);
        return MathF.Atan2(cross, dot) * 180.0f / MathF.PI;
    }

    private static void AssertDirection(Vector2 expected, Vector2 actual)
    {
        Assert.Equal(expected.X, actual.X, Tolerance);
        Assert.Equal(expected.Y, actual.Y, Tolerance);
    }

    /// <summary>Test-side holder for the immutable aim state.</summary>
    private sealed class Aim
    {
        private readonly CursorAimConstants _constants;

        public Aim(CursorAimConstants? constants = null)
        {
            _constants = constants ?? Constants;
            State = CursorAimState.Initial;
        }

        public CursorAimState State { get; private set; }

        public CursorAimResult Move(Vector2 motion) => Tick(motion, 0);

        public CursorAimResult Wheel(int steps) => Tick(Vector2.Zero, steps);

        /// <summary>Aims along a unit direction and lets the hand come to rest there.</summary>
        public void AimAlong(Vector2 direction)
        {
            for (int tick = 0; tick < 60; tick++)
                Move(direction * 3.0f);
            SettleAtRest();
        }

        public void AimRight() => AimAlong(new Vector2(1.0f, 0.0f));

        /// <summary>
        /// Runs the pointer down below the aiming gate, which is where the wheel is used:
        /// the player stops moving, then scrolls to raise the shot.
        /// </summary>
        public void SettleAtRest()
        {
            for (int tick = 0; tick < 600 && Tick(Vector2.Zero, 0).IsSteering; tick++)
            {
            }

            // One more half-life below the gate, so a test about jitter is not accidentally
            // a test about sitting exactly on the threshold.
            for (int tick = 0; tick < (int)Constants.SmoothingHalfLifeTicks; tick++)
                Tick(Vector2.Zero, 0);
        }

        public CursorAimResult Tick(Vector2 motion, int wheelSteps)
        {
            CursorAimResult result = CursorAim.Tick(
                new CursorAimInput(State, motion, wheelSteps, _constants));
            State = result.State;
            return result;
        }
    }
}
