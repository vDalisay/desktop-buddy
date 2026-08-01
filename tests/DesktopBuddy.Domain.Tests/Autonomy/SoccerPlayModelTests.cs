using System;
using System.Collections.Generic;
using System.Numerics;
using DesktopBuddy.Domain.Autonomy;
using Xunit;

namespace DesktopBuddy.Domain.Tests.Autonomy;

public sealed class SoccerPlayModelTests
{
    private const int BallId = 42;

    private static readonly SoccerPlayTuning Fast = new(
        TrapDistance: 30.0f,
        TrapHeight: 24.0f,
        MinimumApproachSpeed: 40.0f,
        MaximumApproachSpeed: 800.0f,
        DwellTicks: 4,
        KickSpeed: 500.0f,
        MaximumKickLoftDegrees: 30.0f,
        KickLoftChoices: 3);

    /// <summary>A ball rolling at the buddy from its right, inside foot range.</summary>
    private static SoccerBallReading Rolling(
        float surfaceDistance = 20.0f,
        float closingSpeed = 200.0f,
        float height = 0.0f,
        float direction = 1.0f,
        bool available = true) =>
        new(BallId, available, surfaceDistance, direction, closingSpeed, height);

    /// <summary>A stream that always answers with the same option.</summary>
    private sealed class FixedRandom(int value) : IRandomSource
    {
        public int NextInt(int minimumInclusive, int maximumExclusive) =>
            Math.Clamp(value, minimumInclusive, maximumExclusive - 1);
    }

    private sealed class QueueRandom(params int[] values) : IRandomSource
    {
        private readonly Queue<int> _values = new(values);

        public int NextInt(int minimumInclusive, int maximumExclusive) =>
            _values.Count > 0 ? _values.Dequeue() : minimumInclusive;
    }

    private static SoccerPlayModel Model(IRandomSource? random = null) =>
        new(random ?? new FixedRandom(0));

    // ---- Tuning validation -------------------------------------------------

    [Fact]
    public void DefaultTuningIsValid() => Assert.True(SoccerPlayTuning.Default.IsValid);

    [Theory]
    [InlineData(0.0f, 24.0f, 40.0f, 800.0f, 4, 500.0f, 30.0f, 3)]   // no trap distance
    [InlineData(30.0f, 0.0f, 40.0f, 800.0f, 4, 500.0f, 30.0f, 3)]   // no trap height
    [InlineData(30.0f, 24.0f, 0.0f, 800.0f, 4, 500.0f, 30.0f, 3)]   // no minimum speed
    [InlineData(30.0f, 24.0f, 900.0f, 800.0f, 4, 500.0f, 30.0f, 3)] // inverted speed window
    [InlineData(30.0f, 24.0f, 40.0f, 800.0f, 0, 500.0f, 30.0f, 3)]  // no dwell
    [InlineData(30.0f, 24.0f, 40.0f, 800.0f, 4, 0.0f, 30.0f, 3)]    // no kick
    [InlineData(30.0f, 24.0f, 40.0f, 800.0f, 4, 500.0f, 90.0f, 3)]  // straight up is not a pass
    [InlineData(30.0f, 24.0f, 40.0f, 800.0f, 4, 500.0f, 30.0f, 0)]  // no choices at all
    public void MalformedTuningIsRejected(
        float trapDistance,
        float trapHeight,
        float minimumSpeed,
        float maximumSpeed,
        int dwell,
        float kickSpeed,
        float loft,
        int choices)
    {
        var tuning = new SoccerPlayTuning(
            trapDistance, trapHeight, minimumSpeed, maximumSpeed, dwell, kickSpeed, loft, choices);
        Assert.False(tuning.IsValid);
    }

    [Fact]
    public void InvalidTuningNeverTraps()
    {
        SoccerPlayModel model = Model();
        var broken = Fast with { DwellTicks = 0 };

        SoccerPlayIntent intent = model.Tick(broken, Rolling(), suppressed: false, conscious: true);

        Assert.Equal(SoccerPlayCommand.None, intent.Command);
        Assert.False(model.IsCommitted);
    }

    [Fact]
    public void NullRandomStreamIsRejected() =>
        Assert.Throws<ArgumentNullException>(() => new SoccerPlayModel(null!));

    // ---- What qualifies as trappable --------------------------------------

    [Fact]
    public void ARollingBallInRangeIsTrapped()
    {
        SoccerPlayModel model = Model();

        SoccerPlayIntent intent = model.Tick(Fast, Rolling(), suppressed: false, conscious: true);

        Assert.Equal(SoccerPlayCommand.Trap, intent.Command);
        Assert.Equal(SoccerPlayPhase.Trapping, intent.Phase);
        Assert.Equal(BallId, intent.RuntimeId);
        Assert.Equal(Fast.DwellTicks, intent.DwellTicksRemaining);
        Assert.Equal(1, model.TrapCount);
        Assert.True(model.IsCommitted);
    }

    [Fact]
    public void ABallOutOfFootRangeIsLeftAlone()
    {
        SoccerPlayModel model = Model();

        SoccerPlayIntent intent = model.Tick(
            Fast, Rolling(surfaceDistance: Fast.TrapDistance + 0.5f), suppressed: false, conscious: true);

        Assert.Equal(SoccerPlayCommand.None, intent.Command);
        Assert.Equal(0, model.TrapCount);
    }

    [Fact]
    public void ABallBarelyMovingIsLeftToTheOrdinaryPickup()
    {
        SoccerPlayModel model = Model();

        SoccerPlayIntent intent = model.Tick(
            Fast, Rolling(closingSpeed: Fast.MinimumApproachSpeed - 1.0f),
            suppressed: false, conscious: true);

        Assert.Equal(SoccerPlayCommand.None, intent.Command);
    }

    [Fact]
    public void ABallArrivingLikeABulletIsNotTrapped()
    {
        SoccerPlayModel model = Model();

        SoccerPlayIntent intent = model.Tick(
            Fast, Rolling(closingSpeed: Fast.MaximumApproachSpeed + 1.0f),
            suppressed: false, conscious: true);

        Assert.Equal(SoccerPlayCommand.None, intent.Command);
    }

    [Fact]
    public void ABallSailingOverheadIsNotTrapped()
    {
        SoccerPlayModel model = Model();

        SoccerPlayIntent intent = model.Tick(
            Fast, Rolling(height: Fast.TrapHeight + 1.0f), suppressed: false, conscious: true);

        Assert.Equal(SoccerPlayCommand.None, intent.Command);
    }

    [Fact]
    public void ABallRollingAwayIsNotTrapped()
    {
        SoccerPlayModel model = Model();

        // Negative closing speed: this is the buddy's own kick leaving, and re-trapping it
        // would lock the ball in a loop at the foot.
        SoccerPlayIntent intent = model.Tick(
            Fast, Rolling(closingSpeed: -200.0f), suppressed: false, conscious: true);

        Assert.Equal(SoccerPlayCommand.None, intent.Command);
    }

    [Fact]
    public void ABallSomebodyIsHoldingIsNotTrapped()
    {
        SoccerPlayModel model = Model();

        SoccerPlayIntent intent = model.Tick(
            Fast, Rolling(available: false), suppressed: false, conscious: true);

        Assert.Equal(SoccerPlayCommand.None, intent.Command);
    }

    [Fact]
    public void NoBallAtAllIsNotTrapped()
    {
        SoccerPlayModel model = Model();

        SoccerPlayIntent intent = model.Tick(
            Fast, SoccerBallReading.None, suppressed: false, conscious: true);

        Assert.Equal(SoccerPlayCommand.None, intent.Command);
        Assert.False(model.IsCommitted);
    }

    // ---- Reservation: which worker the ball belongs to ---------------------

    [Fact]
    public void ABallStillRollingInFromAcrossTheRoomIsAlreadyReserved()
    {
        // No distance term on purpose: the ordinary pickup must not commit to it and walk out
        // to meet it, or there is nothing left to trap by the time it arrives.
        SoccerBallReading far = Rolling(surfaceDistance: 400.0f);

        Assert.True(SoccerPlayModel.IsReserved(Fast, far));
        Assert.False(SoccerPlayModel.IsTrappable(Fast, far));
    }

    [Fact]
    public void ABallInFootRangeIsBothReservedAndTrappable()
    {
        SoccerBallReading near = Rolling();

        Assert.True(SoccerPlayModel.IsReserved(Fast, near));
        Assert.True(SoccerPlayModel.IsTrappable(Fast, near));
    }

    [Theory]
    [InlineData(200.0f, 100.0f)]  // sailing in above the foot line: still a catch
    [InlineData(-300.0f, 0.0f)]   // rolling away: nobody's
    [InlineData(5.0f, 0.0f)]      // barely moving: an ordinary pickup
    public void OnlyALowBallRollingInIsReserved(float closingSpeed, float height)
    {
        SoccerBallReading ball = Rolling(closingSpeed: closingSpeed, height: height);

        Assert.False(SoccerPlayModel.IsReserved(Fast, ball));
        Assert.False(SoccerPlayModel.IsTrappable(Fast, ball));
    }

    [Fact]
    public void NothingIsReservedWithoutValidTuning()
    {
        Assert.False(SoccerPlayModel.IsReserved(Fast with { KickSpeed = 0.0f }, Rolling()));
        Assert.False(SoccerPlayModel.IsReserved(Fast, SoccerBallReading.None));
        Assert.False(SoccerPlayModel.IsReserved(Fast, Rolling(available: false)));
    }

    // ---- Trap, dwell, kick -------------------------------------------------

    [Fact]
    public void TheTrapHoldsTheBallForTheWholeDwellAndThenKicks()
    {
        SoccerPlayModel model = Model();
        SoccerPlayIntent intent = model.Tick(Fast, Rolling(), false, true);
        Assert.Equal(SoccerPlayCommand.Trap, intent.Command);

        // A trapped ball reads as stopped from the next tick on.
        SoccerBallReading stopped = Rolling(closingSpeed: 0.0f);
        for (int tick = 1; tick < Fast.DwellTicks; tick++)
        {
            intent = model.Tick(Fast, stopped, false, true);
            Assert.Equal(SoccerPlayCommand.Trap, intent.Command);
            Assert.Equal(Fast.DwellTicks - tick, intent.DwellTicksRemaining);
            Assert.Equal(0, model.KickCount);
        }

        intent = model.Tick(Fast, stopped, false, true);
        Assert.Equal(SoccerPlayCommand.Kick, intent.Command);
        Assert.Equal(SoccerPlayPhase.Idle, intent.Phase);
        Assert.Equal(BallId, intent.RuntimeId);
        Assert.Equal(1, model.KickCount);
        Assert.False(model.IsCommitted);
    }

    [Fact]
    public void TheKickIsOneShotAndTheBeatReturnsToIdle()
    {
        SoccerPlayModel model = Model();
        RunToKick(model);

        // The kicked ball is now leaving, so the very next tick must do nothing.
        SoccerPlayIntent after = model.Tick(Fast, Rolling(closingSpeed: -400.0f), false, true);

        Assert.Equal(SoccerPlayCommand.None, after.Command);
        Assert.Equal(1, model.KickCount);
    }

    // ---- Where the kick goes ----------------------------------------------

    [Fact]
    public void TheKickSendsTheBallBackTheWayItCame()
    {
        // Ball on the buddy's right: it must leave to the right.
        SoccerPlayModel right = Model();
        SoccerPlayIntent rightKick = RunToKick(right, direction: 1.0f);
        Assert.True(rightKick.KickVelocity.X > 0.0f);

        // And mirrored on the left.
        SoccerPlayModel left = Model();
        SoccerPlayIntent leftKick = RunToKick(left, direction: -1.0f);
        Assert.True(leftKick.KickVelocity.X < 0.0f);
    }

    [Fact]
    public void TheKickLeavesAtTheAuthoredSpeedWhateverTheLoft()
    {
        for (int choice = 0; choice < Fast.KickLoftChoices; choice++)
        {
            SoccerPlayModel model = Model(new FixedRandom(choice));
            SoccerPlayIntent kick = RunToKick(model);
            Assert.Equal(Fast.KickSpeed, kick.KickVelocity.Length(), 3);
        }
    }

    [Fact]
    public void TheFirstOptionIsDeadStraightAndTheLastIsTheAuthoredMaximum()
    {
        SoccerPlayIntent straight = RunToKick(Model(new FixedRandom(0)));
        Assert.Equal(0.0f, straight.KickLoftDegrees, 3);
        Assert.Equal(0.0f, straight.KickVelocity.Y, 3);

        SoccerPlayIntent steepest = RunToKick(Model(new FixedRandom(Fast.KickLoftChoices - 1)));
        Assert.Equal(Fast.MaximumKickLoftDegrees, steepest.KickLoftDegrees, 3);
        // Screen space: a lofted kick rises, so Y is negative.
        Assert.True(steepest.KickVelocity.Y < 0.0f);
    }

    [Fact]
    public void EveryLoftOptionIsReachableAndNoneExceedTheAuthoredSpread()
    {
        var seen = new HashSet<float>();
        for (int choice = 0; choice < Fast.KickLoftChoices; choice++)
        {
            SoccerPlayIntent kick = RunToKick(Model(new FixedRandom(choice)));
            Assert.InRange(kick.KickLoftDegrees, 0.0f, Fast.MaximumKickLoftDegrees);
            seen.Add(kick.KickLoftDegrees);
        }

        Assert.Equal(Fast.KickLoftChoices, seen.Count);
    }

    [Fact]
    public void ASingleChoiceTuningAlwaysKicksStraight()
    {
        var straightOnly = Fast with { KickLoftChoices = 1 };
        SoccerPlayModel model = Model(new FixedRandom(0));
        SoccerPlayIntent kick = RunToKick(model, straightOnly);

        Assert.Equal(0.0f, kick.KickLoftDegrees, 3);
        Assert.Equal(0.0f, kick.KickVelocity.Y, 3);
    }

    [Fact]
    public void SuccessiveKicksFollowTheInjectedStream()
    {
        var model = new SoccerPlayModel(new QueueRandom(2, 0, 1));

        Assert.Equal(Fast.MaximumKickLoftDegrees, RunToKick(model).KickLoftDegrees, 3);
        Assert.Equal(0.0f, RunToKick(model).KickLoftDegrees, 3);
        Assert.Equal(Fast.MaximumKickLoftDegrees / 2.0f, RunToKick(model).KickLoftDegrees, 3);
        Assert.Equal(3, model.KickCount);
    }

    // ---- Giving the ball up ------------------------------------------------

    [Fact]
    public void LosingTheBallMidDwellAbortsWithoutKicking()
    {
        SoccerPlayModel model = Model();
        model.Tick(Fast, Rolling(), false, true);

        SoccerPlayIntent intent = model.Tick(Fast, SoccerBallReading.None, false, true);

        Assert.Equal(SoccerPlayCommand.None, intent.Command);
        Assert.Equal(SoccerPlayAbort.BallLost, intent.Abort);
        Assert.Equal(0, model.KickCount);
        Assert.False(model.IsCommitted);
    }

    [Fact]
    public void ThePlayerPickingTheBallUpEndsTheBeat()
    {
        SoccerPlayModel model = Model();
        model.Tick(Fast, Rolling(), false, true);

        SoccerPlayIntent intent = model.Tick(Fast, Rolling(available: false), false, true);

        Assert.Equal(SoccerPlayAbort.BallLost, intent.Abort);
        Assert.Equal(0, model.KickCount);
    }

    [Fact]
    public void ADifferentBallDoesNotSatisfyTheTrap()
    {
        SoccerPlayModel model = Model();
        model.Tick(Fast, Rolling(), false, true);

        SoccerPlayIntent intent = model.Tick(
            Fast, Rolling() with { RuntimeId = BallId + 1 }, false, true);

        Assert.Equal(SoccerPlayAbort.BallLost, intent.Abort);
    }

    [Fact]
    public void AHigherPriorityTakesTheBallAway()
    {
        SoccerPlayModel model = Model();
        model.Tick(Fast, Rolling(), false, true);

        SoccerPlayIntent intent = model.Tick(Fast, Rolling(), suppressed: true, conscious: true);

        Assert.Equal(SoccerPlayAbort.HigherPriority, intent.Abort);
        Assert.False(model.IsCommitted);
        Assert.Equal(0, model.KickCount);
    }

    [Fact]
    public void AnUnconsciousBuddyPlaysNoFootball()
    {
        SoccerPlayModel model = Model();
        model.Tick(Fast, Rolling(), false, true);

        SoccerPlayIntent intent = model.Tick(Fast, Rolling(), suppressed: false, conscious: false);

        Assert.Equal(SoccerPlayAbort.Unconscious, intent.Abort);
        Assert.False(model.IsCommitted);
    }

    [Fact]
    public void SuppressionWhileIdleReportsNoAbort()
    {
        SoccerPlayModel model = Model();

        SoccerPlayIntent intent = model.Tick(Fast, Rolling(), suppressed: true, conscious: true);

        Assert.Equal(SoccerPlayAbort.None, intent.Abort);
        Assert.Equal(SoccerPlayCommand.None, intent.Command);
    }

    [Fact]
    public void ResetDropsACommittedTrap()
    {
        SoccerPlayModel model = Model();
        model.Tick(Fast, Rolling(), false, true);
        Assert.True(model.IsCommitted);

        model.Reset();

        Assert.False(model.IsCommitted);
        Assert.Equal(0, model.TrappedRuntimeId);
        Assert.Equal(0, model.DwellTicksRemaining);
    }

    [Fact]
    public void TheBeatCanRunAgainAfterAKick()
    {
        SoccerPlayModel model = Model();
        RunToKick(model);
        RunToKick(model);

        Assert.Equal(2, model.TrapCount);
        Assert.Equal(2, model.KickCount);
    }

    private static SoccerPlayIntent RunToKick(
        SoccerPlayModel model,
        SoccerPlayTuning? tuning = null,
        float direction = 1.0f)
    {
        SoccerPlayTuning use = tuning ?? Fast;
        model.Tick(use, Rolling(direction: direction), false, true);
        SoccerBallReading stopped = Rolling(closingSpeed: 0.0f, direction: direction);
        SoccerPlayIntent intent = default;
        for (int tick = 1; tick <= use.DwellTicks; tick++)
            intent = model.Tick(use, stopped, false, true);

        Assert.Equal(SoccerPlayCommand.Kick, intent.Command);
        return intent;
    }
}
