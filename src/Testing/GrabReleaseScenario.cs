using System.Collections.Generic;
using System.Threading.Tasks;
using DesktopBuddy.App;
using DesktopBuddy.Buddy.Physics;
using DesktopBuddy.Objects;
using Godot;

namespace DesktopBuddy.Testing;

/// <summary>
/// Grab tether acquisition/release regression (TEST_PLAN.md Section 3): every
/// one of the six buddy parts and a loose object are acquired through the same
/// tether contract, stretch under a scripted pull without the tether breaking,
/// and release with a capped throw velocity (FR-006).
/// </summary>
public sealed class GrabReleaseScenario : IScenario
{
    private const int SettleTimeoutTicks = 720;
    private const int PullTicks = 24;
    private const float MinimumStretch = 5.0f;
    private const float FlingSpeedFloor = 300.0f; // calibrated from measured swipe peak
    private const float LooseObjectLinearDamp = 1.5f;
    private const float LooseObjectAngularDamp = 2.0f;

    public string Id => "grab_release";

    public async Task<ScenarioResult> RunAsync(SceneTree tree, ulong seed)
    {
        var checks = new List<StartupCheck>();
        var messages = new List<string> { $"seed={seed}" };
        var packed = GD.Load<PackedScene>("res://scenes/buddy_lab.tscn");
        if (packed is null)
        {
            checks.Add(new StartupCheck("grab_scene_loadable", false, "res://scenes/buddy_lab.tscn"));
            return new ScenarioResult(false, checks, messages);
        }

        BuddyLab lab = packed.Instantiate<BuddyLab>();
        tree.Root.AddChild(lab);
        await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);

        bool standing = await WaitForStanding(tree, lab, SettleTimeoutTicks);
        checks.Add(new StartupCheck("grab_starts_from_standing", standing,
            $"stable_ticks={lab.Buddy.Standing.Snapshot.StableTicks}"));

        // Six buddy parts plus a loose object, acquired through the same contract.
        var targets = new List<RigidBody2D>();
        foreach (PuppetPartBody part in lab.Buddy.Rig.Parts)
        {
            targets.Add(part);
        }

        var loose = new LooseObjectBody();
        loose.Configure(
            12.0f,
            1.0f,
            LooseObjectLinearDamp,
            LooseObjectAngularDamp);
        lab.AddChild(loose);
        loose.GlobalPosition = new Vector2(120.0f, 300.0f);
        await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
        targets.Add(loose);

        int acquired = 0;
        int stretched = 0;
        bool everBroke = false;
        float worstReleaseSpeed = 0.0f;

        foreach (RigidBody2D target in targets)
        {
            Vector2 grabPoint = target.GlobalPosition;
            if (!lab.Grab.TryGrab(target, grabPoint))
            {
                continue;
            }

            acquired++;
            lab.Grab.MoveCursor(grabPoint + new Vector2(60.0f, -20.0f));

            float maxExtension = 0.0f;
            for (int tick = 0; tick < PullTicks; tick++)
            {
                await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
                maxExtension = Mathf.Max(maxExtension, lab.Grab.Telemetry.Extension);
                if (!lab.Grab.IsGrabbing)
                {
                    everBroke = true;
                }
            }

            if (maxExtension >= MinimumStretch)
            {
                stretched++;
            }

            lab.Grab.Release();
            worstReleaseSpeed = Mathf.Max(worstReleaseSpeed, lab.Grab.LastReleaseSpeed);
            await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
        }

        float cap = lab.Grab.Profile.ThrowSpeedCap;
        checks.Add(new StartupCheck("grab_acquires_all_parts_and_loose_object",
            acquired == targets.Count, $"{acquired}/{targets.Count}"));
        checks.Add(new StartupCheck("tether_stretches_under_pull",
            stretched == targets.Count, $"stretched={stretched}/{targets.Count}"));
        checks.Add(new StartupCheck("tether_never_breaks_from_pull", !everBroke, $"broke={everBroke}"));
        checks.Add(new StartupCheck("pull_release_speed_within_cap",
            worstReleaseSpeed <= cap + 0.5f, $"worst={worstReleaseSpeed:F1} cap={cap:F1}"));

        // The controller clamps an over-cap release to exactly the cap, preserving direction.
        lab.Grab.TryGrab(loose, loose.GlobalPosition);
        loose.LinearVelocity = new Vector2(cap * 3.0f, 0.0f);
        lab.Grab.Release();
        float clampedRelease = lab.Grab.LastReleaseSpeed;
        checks.Add(new StartupCheck("release_velocity_clamped_to_cap",
            Mathf.Abs(clampedRelease - cap) < 1.0f, $"released={clampedRelease:F1} cap={cap:F1}"));

        // Fling feel (M1_FEEL_AND_GAIT_PLAN target 2): a fast cursor swipe on the buddy
        // must actually whip it fast, not sag heavily. Grab the torso and sweep the
        // cursor quickly, then release; the buddy should carry a fast throw.
        PuppetPartBody torso = lab.Buddy.Rig.Torso;
        Vector2 grabAt = torso.GlobalPosition;
        lab.Grab.TryGrab(torso, grabAt);
        // Lift clear of the floor first (planted feet otherwise anchor the buddy
        // through the leg links), then whip sideways: the owner's "hold him up, then
        // fling" motion. Peak torso speed during the swipe is the fling metric.
        var lifted = new Vector2(grabAt.X, 140.0f);
        for (int i = 0; i < 40; i++)
        {
            lab.Grab.MoveCursor(lifted);
            await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
        }

        float peakFlingSpeed = 0.0f;
        for (int i = 0; i < 12; i++)
        {
            lab.Grab.MoveCursor(lifted + new Vector2((i + 1) * 52.0f, 0.0f));
            await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
            peakFlingSpeed = Mathf.Max(peakFlingSpeed, torso.LinearVelocity.Length());
        }

        lab.Grab.Release();
        // Provisional bound; calibrate from the measured peak, then pinch-test.
        checks.Add(new StartupCheck("grab_fling_carries_fast_throw",
            peakFlingSpeed >= FlingSpeedFloor,
            $"peak={peakFlingSpeed:F1} cap={cap:F1} bound={FlingSpeedFloor:F1}"));

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
