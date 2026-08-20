using DesktopBuddy.Domain.Autonomy;
using DesktopBuddy.Domain.Buddy;
using DesktopBuddy.Domain.Mood;
using Xunit;

namespace DesktopBuddy.Domain.Tests.Autonomy;

/// <summary>
/// Hopping something in the path is trait-gated so ambient jumping does not read as random
/// (DECISIONS 2026-07-20). The threshold is 35 against a uniform 0-100 roll, so about a third of
/// buddies fail it — and those buddies walked into a dropped bat forever, because nothing else
/// ever got them past it (owner report 2026-08-20). A blocking obstacle is therefore not
/// discretionary and ignores the trait. Balls stay on the discretionary path: a buddy who hopped
/// them instead of walking into them would never kick or catch anything.
/// </summary>
public sealed class BlockingObstacleHopTests
{
    private static readonly BuddyTraits EagerHopper = new(100);
    private static readonly BuddyTraits NeverHops = new(0);

    private static BehaviorArbiterModel Arbiter() =>
        new(new BehaviorArbiterTuning(CommitTicks: 600, HopPropensityThreshold: 35));

    private static BehaviorSnapshot Walking() => new(
        Tick: 0,
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

    [Fact]
    public void BlockingObstacle_IsHoppedEvenByABuddyWhoNeverHops()
    {
        BehaviorSnapshot snapshot = Walking() with
        {
            ObstacleInCommittedPath = true,
            BlockingObstacleInPath = true,
        };

        Assert.True(Arbiter().Resolve(snapshot, NeverHops).JumpRequested);
    }

    [Fact]
    public void PlainObstacle_StaysTraitGated()
    {
        BehaviorSnapshot snapshot = Walking() with { ObstacleInCommittedPath = true };

        // A ball reaches here and only here: the buddy walks into it unless he is a hopper.
        Assert.False(Arbiter().Resolve(snapshot, NeverHops).JumpRequested);
        Assert.True(Arbiter().Resolve(snapshot, EagerHopper).JumpRequested);
    }

    [Fact]
    public void BlockingObstacle_StillNeedsFootingAndACommittedWalk()
    {
        BehaviorSnapshot blocked = Walking() with { BlockingObstacleInPath = true };

        Assert.False(Arbiter().Resolve(blocked with { HasStableSupport = false }, NeverHops).JumpRequested);
        Assert.False(Arbiter().Resolve(blocked with { AmbientWalkDirection = 0.0f }, NeverHops).JumpRequested);
        Assert.True(Arbiter().Resolve(blocked, NeverHops).JumpRequested);
    }
}
