using System.Collections.Generic;
using System.Threading.Tasks;
using DesktopBuddy.App;
using DesktopBuddy.Buddy.Physics;
using DesktopBuddy.Domain.Buddy;
using Godot;

namespace DesktopBuddy.Testing;

/// <summary>Seeded walk/jump actuation and consciousness-profile regression.</summary>
public sealed class AutonomousMotionScenario : IScenario
{
    private const int SettleTimeoutTicks = 720;
    private const int MotionObservationTicks = 1_800;

    public string Id => "autonomous_motion";

    public async Task<ScenarioResult> RunAsync(SceneTree tree, ulong seed)
    {
        var checks = new List<StartupCheck>();
        var messages = new List<string> { $"seed={seed}" };
        var packed = GD.Load<PackedScene>("res://scenes/buddy_lab.tscn");
        if (packed is null)
        {
            checks.Add(new StartupCheck("autonomy_scene_loadable", false, "res://scenes/buddy_lab.tscn"));
            return new ScenarioResult(false, checks, messages);
        }

        BuddyLab lab = packed.Instantiate<BuddyLab>();
        tree.Root.AddChild(lab);
        await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        lab.Buddy.ReseedAutonomy(seed);

        bool initiallyStanding = await WaitForStanding(tree, lab, SettleTimeoutTicks);
        checks.Add(new StartupCheck(
            "autonomy_starts_from_standing",
            initiallyStanding,
            $"stable_ticks={lab.Buddy.Standing.Snapshot.StableTicks}"));

        Vector2 start = lab.Buddy.Rig.Torso.GlobalPosition;
        float minimumTorsoY = start.Y;
        float maximumHorizontalDelta = 0.0f;
        bool sawLeftForce = false;
        bool sawRightForce = false;
        bool sawJumpImpulse = false;
        for (int tick = 0; tick < MotionObservationTicks; tick++)
        {
            await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
            float locomotionX = lab.Buddy.ActiveDrive.LastLocomotionForce.X;
            sawLeftForce |= locomotionX < 0.0f;
            sawRightForce |= locomotionX > 0.0f;
            sawJumpImpulse |= lab.Buddy.ActiveDrive.LastJumpImpulse > 0.0f;
            Vector2 position = lab.Buddy.Rig.Torso.GlobalPosition;
            minimumTorsoY = Mathf.Min(minimumTorsoY, position.Y);
            maximumHorizontalDelta = Mathf.Max(maximumHorizontalDelta, Mathf.Abs(position.X - start.X));

            if (sawLeftForce && sawRightForce && sawJumpImpulse && maximumHorizontalDelta >= 8.0f)
            {
                break;
            }
        }

        checks.Add(new StartupCheck(
            "seeded_autonomy_walks_both_directions",
            sawLeftForce && sawRightForce,
            $"left={sawLeftForce} right={sawRightForce} max_dx={maximumHorizontalDelta:F2}"));
        checks.Add(new StartupCheck(
            "seeded_autonomy_applies_whole_body_jump",
            sawJumpImpulse && lab.Buddy.ActiveDrive.JumpImpulseCount > 0 && minimumTorsoY < start.Y - 8.0f,
            $"jump_count={lab.Buddy.ActiveDrive.JumpImpulseCount} rise={start.Y - minimumTorsoY:F2}"));
        checks.Add(new StartupCheck(
            "seeded_autonomy_moves_physical_rig",
            maximumHorizontalDelta >= 8.0f,
            $"max_dx={maximumHorizontalDelta:F2}"));

        lab.Buddy.SetConsciousness(Consciousness.Unconscious);
        await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
        bool activeOutputsDisabled = !lab.Buddy.ActiveDrive.ActiveOutputsEnabled &&
                                     Mathf.IsZeroApprox(lab.Buddy.ActiveDrive.LastUprightTorque) &&
                                     lab.Buddy.ActiveDrive.LastBalanceForce.IsZeroApprox() &&
                                     lab.Buddy.ActiveDrive.LastLocomotionForce.IsZeroApprox() &&
                                     Mathf.IsZeroApprox(lab.Buddy.ActiveDrive.LastJumpImpulse) &&
                                     lab.Buddy.AutonomousMotion.Intent.IsSuppressed;
        checks.Add(new StartupCheck(
            "unconscious_profile_disables_active_outputs",
            activeOutputsDisabled,
            $"enabled={lab.Buddy.ActiveDrive.ActiveOutputsEnabled} suppressed={lab.Buddy.AutonomousMotion.Intent.IsSuppressed}"));

        Vector2 beforeImpulse = lab.Buddy.Rig.Torso.GlobalPosition;
        lab.Buddy.Rig.Torso.ApplyCentralImpulse(new Vector2(220.0f, -120.0f));
        for (int tick = 0; tick < 30; tick++)
        {
            await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
        }

        Vector2 afterImpulse = lab.Buddy.Rig.Torso.GlobalPosition;
        bool passivePhysicsContinued = afterImpulse.DistanceTo(beforeImpulse) > 1.0f &&
                                       lab.Buddy.Constraints.Telemetry.Count > 0;
        checks.Add(new StartupCheck(
            "unconscious_profile_preserves_passive_physics",
            passivePhysicsContinued,
            $"travel={afterImpulse.DistanceTo(beforeImpulse):F2} links={lab.Buddy.Constraints.Telemetry.Count}"));

        lab.Buddy.SetConsciousness(Consciousness.Conscious);
        await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
        checks.Add(new StartupCheck(
            "conscious_profile_restores_active_drive",
            lab.Buddy.ActiveDrive.ActiveOutputsEnabled && !lab.Buddy.AutonomousMotion.Intent.IsSuppressed,
            $"enabled={lab.Buddy.ActiveDrive.ActiveOutputsEnabled} suppressed={lab.Buddy.AutonomousMotion.Intent.IsSuppressed}"));

        lab.QueueFree();
        bool passed = true;
        foreach (StartupCheck check in checks)
        {
            passed &= check.Passed;
        }

        return new ScenarioResult(passed, checks, messages);
    }

    private static async Task<bool> WaitForStanding(SceneTree tree, BuddyLab lab, int timeoutTicks)
    {
        for (int tick = 0; tick < timeoutTicks; tick++)
        {
            await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
            if (lab.Buddy.Standing.Snapshot.IsStable)
            {
                return true;
            }
        }

        return false;
    }
}
