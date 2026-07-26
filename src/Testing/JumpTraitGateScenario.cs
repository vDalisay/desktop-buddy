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
/// </summary>
public sealed class JumpTraitGateScenario : IScenario
{
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

        bool committedWalk = await M4ObjectScenarioSupport.WaitFor(
            tree,
            () => lab.Buddy.Standing.Snapshot.IsStable &&
                  !Mathf.IsZeroApprox(lab.Buddy.AutonomousMotion.Intent.WalkDirection),
            1800);
        float direction = Mathf.Sign(lab.Buddy.AutonomousMotion.Intent.WalkDirection);
        Vector2 obstaclePosition = lab.Buddy.Rig.Torso.GlobalPosition +
            new Vector2(direction * 50.0f, 0.0f);
        LooseObjectBody? obstacle = lab.SpawnLooseObject(
            lab.SafeObjectProfile,
            obstaclePosition);
        if (obstacle is not null)
            obstacle.Freeze = true;

        lab.Progress.SeedTraits(new BuddyTraits(0));
        bool probeSeen = false;
        bool lowTraitJumped = false;
        for (int tick = 0; tick < 12; tick++)
        {
            await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
            probeSeen |= lab.Buddy.AutonomousMotion.ObstacleInCommittedPath(direction);
            lowTraitJumped |= lab.Buddy.Arbiter.Intent.JumpRequested;
        }

        lab.Progress.SeedTraits(new BuddyTraits(100));
        bool highTraitJumped = false;
        for (int tick = 0; tick < 24; tick++)
        {
            await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
            highTraitJumped |= lab.Buddy.Arbiter.Intent.JumpRequested;
            if (highTraitJumped)
                break;
        }

        lab.Buddy.SetConsciousness(DesktopBuddy.Domain.Buddy.Consciousness.Unconscious);
        await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
        bool higherPriorityBlocked =
            lab.Buddy.Arbiter.Diagnostics.Owner == BehaviorPriority.Unconscious &&
            !lab.Buddy.Arbiter.Intent.JumpRequested;

        checks.Add(new StartupCheck(
            "jump_requires_committed_path_and_physical_obstacle",
            committedWalk && obstacle is not null && probeSeen,
            $"walk={committedWalk} direction={direction:F0} obstacle={obstacle is not null} probe={probeSeen}"));
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
