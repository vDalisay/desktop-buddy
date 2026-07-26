using System;
using DesktopBuddy.Domain.Autonomy;
using DesktopBuddy.Domain.Buddy;
using DesktopBuddy.Domain.Mood;
using Xunit;

namespace DesktopBuddy.Domain.Tests.Autonomy;

public sealed class BehaviorArbiterModelTests
{
    private static readonly BuddyTraits EagerHopper = new(100);
    private static readonly BuddyTraits NeverHops = new(0);

    /// <summary>An ambient-only baseline snapshot; each test turns on the layers it needs.</summary>
    private static BehaviorSnapshot Ambient(int tick = 0) => new(
        Tick: tick,
        Consciousness: Consciousness.Conscious,
        RequiresFailsafeReposition: false,
        SelfRightingEligible: false,
        HazardPresent: false,
        HazardFleeDirection: 0.0f,
        Grabbed: false,
        AfraidOfGrab: false,
        GrabFleeDirection: 0.0f,
        HasStableSupport: true,
        WallBlockedLeft: false,
        WallBlockedRight: false,
        MoodBand: MoodBand.Neutral,
        ObjectActionCommitted: false,
        ObjectApproachDirection: 0.0f,
        SocialTargetValid: false,
        SocialTargetDirection: 0.0f,
        SocialTargetDistance: 0.0f,
        AmbientDriveActive: true,
        AmbientWalkDirection: 1.0f,
        AmbientLocomotionScale: 0.6f,
        ObstacleInCommittedPath: false);

    /// <summary>Turns on exactly the layer under test, on top of the ambient baseline.</summary>
    private static BehaviorSnapshot With(BehaviorPriority priority, int tick = 0)
    {
        BehaviorSnapshot snapshot = Ambient(tick);
        return priority switch
        {
            BehaviorPriority.Failsafe => snapshot with { RequiresFailsafeReposition = true },
            BehaviorPriority.Unconscious => snapshot with { Consciousness = Consciousness.Unconscious },
            BehaviorPriority.SelfRighting => snapshot with { SelfRightingEligible = true },
            BehaviorPriority.Hazard => snapshot with { HazardPresent = true, HazardFleeDirection = -1.0f },
            BehaviorPriority.GrabResistance => snapshot with
            {
                Grabbed = true,
                AfraidOfGrab = true,
                GrabFleeDirection = -1.0f,
            },
            BehaviorPriority.ObjectAction => snapshot with
            {
                ObjectActionCommitted = true,
                ObjectApproachDirection = 1.0f,
            },
            BehaviorPriority.Social => snapshot with
            {
                MoodBand = MoodBand.Fearful,
                SocialTargetValid = true,
                SocialTargetDirection = 1.0f,
                SocialTargetDistance = 40.0f,
            },
            _ => snapshot,
        };
    }

    [Theory]
    [InlineData(BehaviorPriority.Failsafe, BehaviorPriority.Unconscious)]
    [InlineData(BehaviorPriority.Unconscious, BehaviorPriority.SelfRighting)]
    [InlineData(BehaviorPriority.SelfRighting, BehaviorPriority.Hazard)]
    [InlineData(BehaviorPriority.Hazard, BehaviorPriority.GrabResistance)]
    [InlineData(BehaviorPriority.GrabResistance, BehaviorPriority.ObjectAction)]
    [InlineData(BehaviorPriority.ObjectAction, BehaviorPriority.Social)]
    [InlineData(BehaviorPriority.Social, BehaviorPriority.Ambient)]
    public void AdjacentPair_HigherPriorityWins(BehaviorPriority higher, BehaviorPriority lower)
    {
        var arbiter = new BehaviorArbiterModel();
        BehaviorSnapshot snapshot = Merge(With(higher), With(lower));

        ActuationIntent intent = arbiter.Resolve(snapshot, NeverHops);

        Assert.Equal(higher, intent.Owner);
        Assert.Equal(higher, arbiter.Diagnostics.HighestEligible);
    }

    [Theory]
    [InlineData(BehaviorPriority.Failsafe)]
    [InlineData(BehaviorPriority.Unconscious)]
    [InlineData(BehaviorPriority.SelfRighting)]
    [InlineData(BehaviorPriority.Hazard)]
    [InlineData(BehaviorPriority.GrabResistance)]
    [InlineData(BehaviorPriority.ObjectAction)]
    [InlineData(BehaviorPriority.Social)]
    public void HigherPriority_PreemptsImmediatelyInsideACommitmentWindow(BehaviorPriority higher)
    {
        // Commitment must never delay a safety or hazard response by even one tick.
        var arbiter = new BehaviorArbiterModel(new BehaviorArbiterTuning(CommitTicks: 600, HopPropensityThreshold: 35));
        arbiter.Resolve(Ambient(), NeverHops);
        Assert.Equal(BehaviorPriority.Ambient, arbiter.Owner);

        ActuationIntent intent = arbiter.Resolve(With(higher, tick: 1), NeverHops);

        Assert.Equal(higher, intent.Owner);
        Assert.True(arbiter.Diagnostics.PreemptedThisTick);
    }

    [Fact]
    public void Commitment_KeepsALowerPriorityFromTakingOverMidGoal()
    {
        var arbiter = new BehaviorArbiterModel(new BehaviorArbiterTuning(CommitTicks: 30, HopPropensityThreshold: 35));
        BehaviorSnapshot committed = With(BehaviorPriority.ObjectAction);

        arbiter.Resolve(committed, NeverHops);
        Assert.Equal(BehaviorPriority.ObjectAction, arbiter.Owner);

        // Social becomes eligible but is a *lower* priority: it must wait.
        BehaviorSnapshot contested = Merge(committed, With(BehaviorPriority.Social));
        ActuationIntent intent = arbiter.Resolve(contested with { Tick = 1 }, NeverHops);

        Assert.Equal(BehaviorPriority.ObjectAction, intent.Owner);
        Assert.False(arbiter.Diagnostics.PreemptedThisTick);
    }

    [Fact]
    public void Commitment_IsInvalidatedWhenTheOwningLayerStopsBeingEligible()
    {
        var arbiter = new BehaviorArbiterModel(new BehaviorArbiterTuning(CommitTicks: 600, HopPropensityThreshold: 35));
        arbiter.Resolve(With(BehaviorPriority.ObjectAction), NeverHops);
        Assert.Equal(BehaviorPriority.ObjectAction, arbiter.Owner);

        // The object action ended; the window must not hold actuation hostage.
        ActuationIntent intent = arbiter.Resolve(Ambient(tick: 1), NeverHops);

        Assert.Equal(BehaviorPriority.Ambient, intent.Owner);
    }

    [Fact]
    public void Commitment_ExpiresAndReleasesToTheHighestEligibleLayer()
    {
        var arbiter = new BehaviorArbiterModel(new BehaviorArbiterTuning(CommitTicks: 3, HopPropensityThreshold: 35));
        BehaviorSnapshot objectAction = With(BehaviorPriority.ObjectAction);
        BehaviorSnapshot both = Merge(objectAction, With(BehaviorPriority.Social));

        arbiter.Resolve(objectAction, NeverHops);
        for (int tick = 1; tick <= 3; tick++)
        {
            arbiter.Resolve(both with { Tick = tick }, NeverHops);
        }

        // Once the window drains, the object action still outranks social while committed.
        Assert.Equal(BehaviorPriority.ObjectAction, arbiter.Owner);

        // With the object action gone, social takes over.
        ActuationIntent intent = arbiter.Resolve(
            With(BehaviorPriority.Social, tick: 9), NeverHops);
        Assert.Equal(BehaviorPriority.Social, intent.Owner);
    }

    [Theory]
    [InlineData(BehaviorPriority.Failsafe)]
    [InlineData(BehaviorPriority.Unconscious)]
    [InlineData(BehaviorPriority.SelfRighting)]
    public void SafetyLayers_ProduceNoVoluntaryDrive(BehaviorPriority priority)
    {
        var arbiter = new BehaviorArbiterModel();

        ActuationIntent intent = arbiter.Resolve(With(priority), EagerHopper);

        Assert.False(intent.DriveActive);
        Assert.Equal(0.0f, intent.WalkDirection);
        Assert.False(intent.JumpRequested);
        Assert.False(intent.ResistGrab);
    }

    [Fact]
    public void Unconscious_DisablesObjectAndSocialDecisions()
    {
        var arbiter = new BehaviorArbiterModel();
        BehaviorSnapshot snapshot = Merge(
            With(BehaviorPriority.Unconscious),
            Merge(With(BehaviorPriority.ObjectAction), With(BehaviorPriority.Social)));

        ActuationIntent intent = arbiter.Resolve(snapshot, EagerHopper);

        Assert.Equal(BehaviorPriority.Unconscious, intent.Owner);
        Assert.False(intent.DriveActive);
    }

    [Fact]
    public void GrabResistance_RequiresConsciousAndAfraid()
    {
        var arbiter = new BehaviorArbiterModel();

        BehaviorSnapshot calm = Ambient() with
        {
            Grabbed = true,
            AfraidOfGrab = false,
            GrabFleeDirection = -1.0f,
        };
        Assert.Equal(BehaviorPriority.Ambient, arbiter.Resolve(calm, NeverHops).Owner);

        arbiter.Reset();
        ActuationIntent afraid = arbiter.Resolve(
            Ambient() with { Grabbed = true, AfraidOfGrab = true, GrabFleeDirection = -1.0f },
            NeverHops);
        Assert.Equal(BehaviorPriority.GrabResistance, afraid.Owner);
        Assert.True(afraid.ResistGrab);
        // Owner feel note 2026-07-25: resisting means WALKING away, not sliding. The gait
        // must be driven, or the feet read as dead while the body slides sideways.
        Assert.True(afraid.DriveActive);
        Assert.Equal(-1.0f, afraid.WalkDirection);
    }

    [Fact]
    public void GrabResistance_RequiresSupportContact()
    {
        var arbiter = new BehaviorArbiterModel();
        BehaviorSnapshot dangled = Ambient() with
        {
            Grabbed = true,
            AfraidOfGrab = true,
            GrabFleeDirection = -1.0f,
            HasSupportContact = false,
        };

        ActuationIntent intent = arbiter.Resolve(dangled, NeverHops);

        Assert.Equal(BehaviorPriority.Ambient, intent.Owner);
        Assert.False(intent.ResistGrab);
    }

    [Fact]
    public void Hazard_FleesAndGuards()
    {
        var arbiter = new BehaviorArbiterModel();

        ActuationIntent intent = arbiter.Resolve(With(BehaviorPriority.Hazard), NeverHops);

        Assert.Equal(BehaviorPriority.Hazard, intent.Owner);
        Assert.True(intent.DriveActive);
        Assert.Equal(-1.0f, intent.WalkDirection);
        Assert.True(intent.GuardRequested);
        Assert.Equal(SocialStance.Flee, intent.Stance);
    }

    [Theory]
    [InlineData(MoodBand.Fearful, 40.0f, SocialStance.Flee)]
    [InlineData(MoodBand.Wary, 40.0f, SocialStance.KeepDistance)]
    [InlineData(MoodBand.Neutral, 40.0f, SocialStance.None)]
    [InlineData(MoodBand.Content, 400.0f, SocialStance.Approach)]
    [InlineData(MoodBand.Delighted, 400.0f, SocialStance.Approach)]
    public void MoodBands_DifferentiateSocialStance(MoodBand band, float distance, SocialStance expected)
    {
        var arbiter = new BehaviorArbiterModel();
        BehaviorSnapshot snapshot = Ambient() with
        {
            MoodBand = band,
            SocialTargetValid = true,
            SocialTargetDirection = 1.0f,
            SocialTargetDistance = distance,
        };

        ActuationIntent intent = arbiter.Resolve(snapshot, NeverHops);

        Assert.Equal(expected, intent.Stance);
        if (expected == SocialStance.None)
        {
            // Neutral is "current ambient behavior": the social layer stands down entirely.
            Assert.Equal(BehaviorPriority.Ambient, intent.Owner);
        }
        else
        {
            Assert.Equal(BehaviorPriority.Social, intent.Owner);
        }
    }

    [Fact]
    public void FearfulAndWary_MoveAwayFromTheTargetNotToward()
    {
        var arbiter = new BehaviorArbiterModel();
        BehaviorSnapshot near = Ambient() with
        {
            MoodBand = MoodBand.Fearful,
            SocialTargetValid = true,
            SocialTargetDirection = 1.0f,
            SocialTargetDistance = 30.0f,
        };

        ActuationIntent intent = arbiter.Resolve(near, NeverHops);

        Assert.Equal(-1.0f, intent.WalkDirection);
    }

    [Fact]
    public void ContentAndDelighted_ApproachTheTarget()
    {
        var arbiter = new BehaviorArbiterModel();
        BehaviorSnapshot far = Ambient() with
        {
            MoodBand = MoodBand.Delighted,
            SocialTargetValid = true,
            SocialTargetDirection = -1.0f,
            SocialTargetDistance = 400.0f,
        };

        ActuationIntent intent = arbiter.Resolve(far, NeverHops);

        Assert.Equal(-1.0f, intent.WalkDirection);
        Assert.Equal(SocialStance.Approach, intent.Stance);
    }

    [Fact]
    public void StandoffEnvelope_HasHysteresisSoItCannotFlipFlop()
    {
        SocialBandTuning wary = SocialBandTuning.Wary;
        var arbiter = new BehaviorArbiterModel(new BehaviorArbiterTuning(CommitTicks: 0, HopPropensityThreshold: 35));

        BehaviorSnapshot At(float distance) => Ambient() with
        {
            MoodBand = MoodBand.Wary,
            SocialTargetValid = true,
            SocialTargetDirection = 1.0f,
            SocialTargetDistance = distance,
        };

        // Inside the standoff distance: retreat.
        Assert.Equal(SocialStance.KeepDistance, arbiter.Resolve(At(wary.StandoffDistance - 1.0f), NeverHops).Stance);
        // Just outside it, still inside the dead band: keep retreating rather than snapping off.
        Assert.Equal(
            SocialStance.KeepDistance,
            arbiter.Resolve(At(wary.StandoffDistance + wary.Hysteresis - 1.0f), NeverHops).Stance);
        // Clear of the dead band: stand down.
        Assert.Equal(
            SocialStance.None,
            arbiter.Resolve(At(wary.StandoffDistance + wary.Hysteresis + 1.0f), NeverHops).Stance);
    }

    [Fact]
    public void ApproachEnvelope_HasHysteresisSoItCannotFlipFlop()
    {
        SocialBandTuning content = SocialBandTuning.Content;
        var arbiter = new BehaviorArbiterModel(
            new BehaviorArbiterTuning(CommitTicks: 0, HopPropensityThreshold: 35));

        BehaviorSnapshot At(float distance) => Ambient() with
        {
            MoodBand = MoodBand.Content,
            SocialTargetValid = true,
            SocialTargetDirection = 1.0f,
            SocialTargetDistance = distance,
        };

        Assert.Equal(
            SocialStance.Approach,
            arbiter.Resolve(
                At(content.ApproachDistance + content.Hysteresis + 1.0f),
                NeverHops).Stance);
        Assert.Equal(
            SocialStance.Approach,
            arbiter.Resolve(At(content.ApproachDistance + 1.0f), NeverHops).Stance);
        Assert.Equal(
            SocialStance.Greet,
            arbiter.Resolve(At(content.ApproachDistance - 1.0f), NeverHops).Stance);
    }

    [Fact]
    public void InjectedSocialSet_IsTheSingleBandSource()
    {
        SocialTuningSet defaults = SocialTuningSet.Default;
        SocialBandTuning customFearful = defaults.Fearful with
        {
            StandoffDistance = 50.0f,
            Hysteresis = 5.0f,
        };
        var custom = defaults with { Fearful = customFearful };
        var arbiter = new BehaviorArbiterModel(
            new BehaviorArbiterTuning(0, 35),
            custom);
        BehaviorSnapshot target = Ambient() with
        {
            MoodBand = MoodBand.Fearful,
            SocialTargetValid = true,
            SocialTargetDirection = 1.0f,
            SocialTargetDistance = 80.0f,
        };

        Assert.Equal(BehaviorPriority.Ambient, arbiter.Resolve(target, NeverHops).Owner);
    }

    [Fact]
    public void TransientToolEmotion_ClaimsSocialLayerWithoutDistanceMovement()
    {
        var arbiter = new BehaviorArbiterModel();

        ActuationIntent intent = arbiter.Resolve(
            Ambient() with { SocialReactionPresent = true },
            NeverHops);

        Assert.Equal(BehaviorPriority.Social, intent.Owner);
        Assert.Equal(SocialStance.None, intent.Stance);
    }

    [Fact]
    public void Greet_RespectsItsBandCadence()
    {
        var arbiter = new BehaviorArbiterModel(new BehaviorArbiterTuning(CommitTicks: 0, HopPropensityThreshold: 35));
        int interval = SocialBandTuning.Delighted.GreetIntervalTicks;

        BehaviorSnapshot Close(int tick) => Ambient(tick) with
        {
            MoodBand = MoodBand.Delighted,
            SocialTargetValid = true,
            SocialTargetDirection = 1.0f,
            SocialTargetDistance = SocialBandTuning.Delighted.ApproachDistance - 10.0f,
        };

        Assert.True(arbiter.Resolve(Close(0), NeverHops).GreetRequested);
        Assert.False(arbiter.Resolve(Close(interval - 1), NeverHops).GreetRequested);
        Assert.True(arbiter.Resolve(Close(interval), NeverHops).GreetRequested);
    }

    [Fact]
    public void AmbientHop_RequiresPropensityObstacleSupportAndACommittedPath()
    {
        var arbiter = new BehaviorArbiterModel(new BehaviorArbiterTuning(CommitTicks: 0, HopPropensityThreshold: 35));
        BehaviorSnapshot obstacle = Ambient() with { ObstacleInCommittedPath = true };

        Assert.True(arbiter.Resolve(obstacle, EagerHopper).JumpRequested);

        // Each missing precondition alone suppresses the hop.
        Assert.False(arbiter.Resolve(obstacle, NeverHops).JumpRequested);
        Assert.False(arbiter.Resolve(obstacle with { HasStableSupport = false }, EagerHopper).JumpRequested);
        Assert.False(arbiter.Resolve(obstacle with { AmbientWalkDirection = 0.0f }, EagerHopper).JumpRequested);
    }

    [Fact]
    public void AmbientHop_NeverFiresWithoutObstacleEvidence()
    {
        // DECISIONS 2026-07-20: pure-timer ambient jumping stays OFF. No amount of
        // propensity may produce a hop on flat ground.
        var arbiter = new BehaviorArbiterModel(new BehaviorArbiterTuning(CommitTicks: 0, HopPropensityThreshold: 0));

        for (int tick = 0; tick < 2000; tick++)
        {
            Assert.False(arbiter.Resolve(Ambient(tick), EagerHopper).JumpRequested);
        }
    }

    [Fact]
    public void WallBlock_ZeroesDriveIntoTheWallForEveryLayer()
    {
        var arbiter = new BehaviorArbiterModel(new BehaviorArbiterTuning(CommitTicks: 0, HopPropensityThreshold: 35));

        BehaviorSnapshot ambientIntoWall = Ambient() with
        {
            AmbientWalkDirection = 1.0f,
            WallBlockedRight = true,
        };
        Assert.Equal(0.0f, arbiter.Resolve(ambientIntoWall, NeverHops).WalkDirection);

        BehaviorSnapshot hazardIntoWall = With(BehaviorPriority.Hazard) with { WallBlockedLeft = true };
        Assert.Equal(0.0f, arbiter.Resolve(hazardIntoWall, NeverHops).WalkDirection);

        BehaviorSnapshot objectIntoWall = With(BehaviorPriority.ObjectAction) with { WallBlockedRight = true };
        Assert.Equal(0.0f, arbiter.Resolve(objectIntoWall, NeverHops).WalkDirection);
    }

    [Fact]
    public void AmbientSuppression_IsReportedSoTheRuntimeCanPauseItsRngStream()
    {
        var arbiter = new BehaviorArbiterModel();

        arbiter.Resolve(Ambient(), NeverHops);
        Assert.False(arbiter.Diagnostics.AmbientSuppressed);

        arbiter.Resolve(With(BehaviorPriority.Hazard, tick: 1), NeverHops);
        Assert.True(arbiter.Diagnostics.AmbientSuppressed);
    }

    [Fact]
    public void Reset_DropsCommitmentAndGreetCadence()
    {
        var arbiter = new BehaviorArbiterModel(new BehaviorArbiterTuning(CommitTicks: 600, HopPropensityThreshold: 35));
        arbiter.Resolve(With(BehaviorPriority.ObjectAction), NeverHops);

        arbiter.Reset();

        Assert.Equal(BehaviorPriority.Ambient, arbiter.Owner);
        Assert.Equal(0, arbiter.Diagnostics.CommitTicksRemaining);
    }

    [Fact]
    public void InactiveAmbient_YieldsAnIdleIntentRatherThanDrive()
    {
        var arbiter = new BehaviorArbiterModel();

        ActuationIntent intent = arbiter.Resolve(Ambient() with { AmbientDriveActive = false }, EagerHopper);

        Assert.Equal(BehaviorPriority.Ambient, intent.Owner);
        Assert.False(intent.DriveActive);
    }

    [Fact]
    public void Resolve_AllocatesNothingOnTheFixedTickPath()
    {
        var arbiter = new BehaviorArbiterModel();
        BehaviorSnapshot snapshot = Ambient();
        _ = arbiter.Resolve(snapshot, EagerHopper);

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int tick = 0; tick < 10_000; tick++)
        {
            snapshot = snapshot with { Tick = tick };
            _ = arbiter.Resolve(snapshot, EagerHopper);
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(0, allocated);
    }

    /// <summary>Turns on every layer that is active in either snapshot.</summary>
    private static BehaviorSnapshot Merge(BehaviorSnapshot a, BehaviorSnapshot b) => a with
    {
        RequiresFailsafeReposition = a.RequiresFailsafeReposition || b.RequiresFailsafeReposition,
        Consciousness = a.Consciousness == Consciousness.Unconscious ||
                        b.Consciousness == Consciousness.Unconscious
            ? Consciousness.Unconscious
            : Consciousness.Conscious,
        SelfRightingEligible = a.SelfRightingEligible || b.SelfRightingEligible,
        HazardPresent = a.HazardPresent || b.HazardPresent,
        HazardFleeDirection = a.HazardPresent ? a.HazardFleeDirection : b.HazardFleeDirection,
        Grabbed = a.Grabbed || b.Grabbed,
        AfraidOfGrab = a.AfraidOfGrab || b.AfraidOfGrab,
        GrabFleeDirection = a.Grabbed ? a.GrabFleeDirection : b.GrabFleeDirection,
        ObjectActionCommitted = a.ObjectActionCommitted || b.ObjectActionCommitted,
        ObjectApproachDirection = a.ObjectActionCommitted
            ? a.ObjectApproachDirection
            : b.ObjectApproachDirection,
        SocialTargetValid = a.SocialTargetValid || b.SocialTargetValid,
        MoodBand = a.SocialTargetValid ? a.MoodBand : b.MoodBand,
        SocialTargetDirection = a.SocialTargetValid ? a.SocialTargetDirection : b.SocialTargetDirection,
        SocialTargetDistance = a.SocialTargetValid ? a.SocialTargetDistance : b.SocialTargetDistance,
        AmbientDriveActive = a.AmbientDriveActive || b.AmbientDriveActive,
    };
}
