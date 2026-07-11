using System.Collections.Generic;
using System.Threading.Tasks;
using DesktopBuddy.App;
using DesktopBuddy.Buddy.Physics;
using Godot;

namespace DesktopBuddy.Testing;

/// <summary>
/// Fail-safe cleanup regression (DECISIONS.md "Fail-safe cleanup",
/// TEST_PLAN.md Section 3: "Hard recovery releases grabs/held objects"): while the
/// player holds a buddy part on the tether, forcing an out-of-bounds body triggers
/// an immediate hard recovery that releases the active grab and restores a finite,
/// in-bounds safe pose. The real lab composition performs the release, not the
/// scenario.
/// </summary>
public sealed class GrabHardRecoveryScenario : IScenario
{
    private const int SettleTimeoutTicks = 720;
    private const int PullTicks = 12;

    public string Id => "grab_hard_recovery";

    public async Task<ScenarioResult> RunAsync(SceneTree tree, ulong seed)
    {
        var checks = new List<StartupCheck>();
        var messages = new List<string> { $"seed={seed}" };
        var packed = GD.Load<PackedScene>("res://scenes/buddy_lab.tscn");
        if (packed is null)
        {
            checks.Add(new StartupCheck("grab_hard_recovery_scene_loadable", false, "res://scenes/buddy_lab.tscn"));
            return new ScenarioResult(false, checks, messages);
        }

        BuddyLab lab = packed.Instantiate<BuddyLab>();
        tree.Root.AddChild(lab);
        await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);

        bool standing = await WaitForStanding(tree, lab, SettleTimeoutTicks);
        checks.Add(new StartupCheck("grab_hard_recovery_starts_from_standing", standing,
            $"stable_ticks={lab.Buddy.Standing.Snapshot.StableTicks}"));

        // Grab the torso and pull briefly so the tether is unmistakably active.
        PuppetPartBody torso = lab.Buddy.Rig.Torso;
        bool grabbed = lab.Grab.TryGrab(torso, torso.GlobalPosition);
        lab.Grab.MoveCursor(torso.GlobalPosition + new Vector2(50.0f, -20.0f));
        for (int tick = 0; tick < PullTicks; tick++)
        {
            await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
        }

        checks.Add(new StartupCheck("grab_active_before_recovery", grabbed && lab.Grab.IsGrabbing,
            $"grabbing={lab.Grab.IsGrabbing}"));

        // Fling a body out of the sandbox to force an immediate hard recovery.
        int priorHardRecoveries = lab.Buddy.Recovery.HardRecoveryCount;
        lab.Buddy.Rig.Head.GlobalPosition = new Vector2(-1_000.0f, -1_000.0f);
        lab.Buddy.Rig.Head.LinearVelocity = new Vector2(40_000.0f, -40_000.0f);
        await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);

        bool hardRecovered = lab.Buddy.Recovery.HardRecoveryCount == priorHardRecoveries + 1;
        checks.Add(new StartupCheck("hard_recovery_triggered", hardRecovered,
            $"reason={lab.Buddy.Recovery.LastHardRecoveryReason} count={lab.Buddy.Recovery.HardRecoveryCount}"));
        checks.Add(new StartupCheck("hard_recovery_releases_grab", !lab.Grab.IsGrabbing,
            $"grabbing={lab.Grab.IsGrabbing}"));
        checks.Add(new StartupCheck("hard_recovery_restores_safe_pose",
            lab.Buddy.Rig.AllBodiesFinite() && lab.Buddy.Recovery.AllBodiesInsideSafeBounds(),
            $"inside={lab.Buddy.Recovery.AllBodiesInsideSafeBounds()} finite={lab.Buddy.Rig.AllBodiesFinite()}"));

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
