using System.Collections.Generic;
using System.Threading.Tasks;
using DesktopBuddy.App;
using DesktopBuddy.Domain.Autonomy;
using DesktopBuddy.Objects;
using Godot;

namespace DesktopBuddy.Testing;

/// <summary>
/// M4 Task 3 gate: obstacle hops require persisted propensity, a committed
/// walking path, physical obstacle evidence, stable support, and no higher layer.
///
/// <para>The obstacles here are <b>real</b>: ordinary loose objects spawned on the floor
/// line and allowed to settle under gravity, one on each side so whichever direction
/// ambient autonomy commits to has something in it. A frozen torso-height prop would
/// prove only that the gate chain fires, never that the shipped probe can see what the
/// buddy actually walks into.</para>
/// </summary>
public sealed class JumpTraitGateScenario : IScenario
{
    /// <summary>Long enough to cover the whole observation, so the balls stay scenery.</summary>
    private const int ScenerySettleTicks = 6000;
    private const int ProbeTimeoutTicks = 2400;
    private const int NoHopObservationTicks = 240;
    private const int HopTimeoutTicks = 600;

    public string Id => "jump_trait_gate";

    public async Task<ScenarioResult> RunAsync(SceneTree tree, ulong seed)
    {
        var checks = new List<StartupCheck>();
        var messages = new List<string> { $"seed={seed}" };
        BuddyLab? lab = await M4ObjectScenarioSupport.LoadLab(tree, seed);
        if (lab is null)
        {
            checks.Add(new StartupCheck("jump_trait_lab_loadable", false, "buddy_lab"));
            return new ScenarioResult(false, checks, messages);
        }

        float floorY = lab.Boundaries.InnerBounds.End.Y - lab.SafeObjectProfile.Radius - 1.0f;
        float torsoX = lab.Buddy.Rig.Torso.GlobalPosition.X;
        LooseObjectBody? left = lab.SpawnLooseObject(
            lab.SafeObjectProfile, new Vector2(torsoX - 60.0f, floorY));
        LooseObjectBody? right = lab.SpawnLooseObject(
            lab.SafeObjectProfile, new Vector2(torsoX + 60.0f, floorY));
        bool spawned = left is not null && right is not null;

        // Mark both as objects the buddy has just put down. That ignore window is the shipped
        // mechanism by which an object becomes scenery rather than a pickup target, and it is
        // the only configuration in which hopping can happen at all: object action is priority
        // 5 and the hop is priority 7, so anything the buddy would pick up it picks up.
        if (spawned)
        {
            lab.Objects.MarkBuddyReleased(left!, ScenerySettleTicks);
            lab.Objects.MarkBuddyReleased(right!, ScenerySettleTicks);
        }

        // Propensity 0 first: the probe must report an obstacle and the buddy must
        // still never hop, which separates "no evidence" from "no personality".
        lab.Progress.SeedTraits(new BuddyTraits(0));
        bool probeSeen = false;
        bool committedWalk = false;
        bool lowTraitJumped = false;
        for (int tick = 0; tick < ProbeTimeoutTicks && !probeSeen; tick++)
        {
            await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
            float direction = lab.Buddy.AutonomousMotion.Intent.WalkDirection;
            bool walking = !Mathf.IsZeroApprox(direction) && lab.Buddy.Standing.Snapshot.IsStable;
            committedWalk |= walking;
            probeSeen = walking && lab.Buddy.AutonomousMotion.ObstacleInCommittedPath(direction);
            lowTraitJumped |= lab.Buddy.Arbiter.Intent.JumpRequested;
        }

        for (int tick = 0; tick < NoHopObservationTicks; tick++)
        {
            await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
            lowTraitJumped |= lab.Buddy.Arbiter.Intent.JumpRequested;
        }

        lab.Progress.SeedTraits(new BuddyTraits(100));
        bool highTraitJumped = false;
        for (int tick = 0; tick < HopTimeoutTicks && !highTraitJumped; tick++)
        {
            await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
            highTraitJumped = lab.Buddy.Arbiter.Intent.JumpRequested;
        }

        lab.Buddy.SetConsciousness(DesktopBuddy.Domain.Buddy.Consciousness.Unconscious);
        await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
        bool higherPriorityBlocked =
            lab.Buddy.Arbiter.Diagnostics.Owner == BehaviorPriority.Unconscious &&
            !lab.Buddy.Arbiter.Intent.JumpRequested;

        checks.Add(new StartupCheck(
            "floor_resting_object_is_real_obstacle_evidence",
            spawned && committedWalk && probeSeen,
            $"spawned={spawned} walk={committedWalk} probe={probeSeen} " +
            $"offset={lab.Buddy.AutonomousMotion.Profile.ObstacleProbeHeightOffset:F0}"));
        checks.Add(new StartupCheck(
            "jump_trait_propensity_gates_obstacle_hop",
            !lowTraitJumped && highTraitJumped,
            $"low={lowTraitJumped} high={highTraitJumped}"));
        checks.Add(new StartupCheck(
            "higher_priority_suppresses_obstacle_hop",
            higherPriorityBlocked,
            $"owner={lab.Buddy.Arbiter.Diagnostics.Owner} jump={lab.Buddy.Arbiter.Intent.JumpRequested}"));
        checks.Add(new StartupCheck(
            "pure_timer_ambient_jumps_remain_disabled",
            !lab.Buddy.AutonomousMotion.Profile.AmbientJumpsEnabled,
            $"enabled={lab.Buddy.AutonomousMotion.Profile.AmbientJumpsEnabled}"));

        await M4ObjectScenarioSupport.Cleanup(tree, lab);
        bool passed = true;
        foreach (StartupCheck check in checks) passed &= check.Passed;
        return new ScenarioResult(passed, checks, messages);
    }
}
