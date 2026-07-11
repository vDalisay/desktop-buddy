using System.Collections.Generic;
using System.Threading.Tasks;
using DesktopBuddy.App;
using DesktopBuddy.Buddy.Physics;
using DesktopBuddy.Domain.Physics;
using Godot;

namespace DesktopBuddy.Testing;

/// <summary>Live standing, timed assistance, and fail-safe recovery regression.</summary>
public sealed class StandingRecoveryScenario : IScenario
{
    private const int InitialSettleTimeoutTicks = 720;
    private const int AssistedRecoveryTimeoutTicks = 900;

    public string Id => "standing_recovery";

    public async Task<ScenarioResult> RunAsync(SceneTree tree, ulong seed)
    {
        var checks = new List<StartupCheck>();
        var messages = new List<string> { $"seed={seed}" };
        var packed = GD.Load<PackedScene>("res://scenes/buddy_lab.tscn");
        if (packed is null)
        {
            checks.Add(new StartupCheck("standing_scene_loadable", false, "res://scenes/buddy_lab.tscn"));
            return new ScenarioResult(false, checks, messages);
        }

        BuddyLab lab = packed.Instantiate<BuddyLab>();
        tree.Root.AddChild(lab);
        await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);

        bool initiallyStanding = await WaitForStanding(tree, lab, InitialSettleTimeoutTicks);
        StandingSnapshot initial = lab.Buddy.Standing.Snapshot;
        checks.Add(new StartupCheck("standing_uses_support_contacts",
            initial.SupportContactCount > 0,
            $"supports={initial.SupportContactCount}"));
        checks.Add(new StartupCheck("spawn_settles_to_standing", initiallyStanding,
            $"tilt={initial.TorsoTilt:F3} speed={initial.MaximumBodySpeed:F3} stable_ticks={initial.StableTicks}"));

        Vector2 tippedOrigin = new(240, 240);
        foreach (PuppetPartBody body in lab.Buddy.Rig.Parts)
        {
            body.Freeze = true;
            PuppetPartDefinition definition = lab.Buddy.Rig.Profile.FindPart(body.PartId)!;
            body.GlobalPosition = tippedOrigin + definition.RestPosition.Rotated(Mathf.Pi * 0.5f);
            body.GlobalRotation = body.PartId == BuddyPartId.Torso ? Mathf.Pi * 0.5f : 0.0f;
            body.LinearVelocity = Vector2.Zero;
            body.AngularVelocity = 0.0f;
        }
        for (int tick = 0; tick < RecoveryClock.AssistanceDelayTicks - 1; tick++)
        {
            await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
        }

        bool assistanceWasEarly = lab.Buddy.Recovery.State.AssistanceActive;
        await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
        bool assistanceStartedOnTime = lab.Buddy.Recovery.State.AssistanceActive;
        bool noEarlyHardRecovery = lab.Buddy.Recovery.HardRecoveryCount == 0;
        checks.Add(new StartupCheck("assistance_not_early", !assistanceWasEarly,
            $"unable_ticks={RecoveryClock.AssistanceDelayTicks - 1}"));
        checks.Add(new StartupCheck("assistance_starts_at_two_seconds", assistanceStartedOnTime,
            $"unable_ticks={lab.Buddy.Recovery.State.UnableTicks}"));
        checks.Add(new StartupCheck("hard_recovery_not_early", noEarlyHardRecovery,
            $"hard_count={lab.Buddy.Recovery.HardRecoveryCount}"));

        foreach (PuppetPartBody body in lab.Buddy.Rig.Parts)
        {
            body.Freeze = false;
        }

        AssistedStandingResult assisted = await WaitForAssistedStanding(tree, lab, AssistedRecoveryTimeoutTicks);
        bool recoveredWithoutTeleport = assisted.Standing && assisted.MaximumRamp > 0.0f &&
                                         lab.Buddy.Recovery.HardRecoveryCount == 0;
        checks.Add(new StartupCheck("assisted_self_righting_recovers", recoveredWithoutTeleport,
            $"max_ramp={assisted.MaximumRamp:F3} hard_count={lab.Buddy.Recovery.HardRecoveryCount}"));

        int priorHardRecoveries = lab.Buddy.Recovery.HardRecoveryCount;
        lab.Buddy.Rig.Head.GlobalPosition = new Vector2(-1_000, -1_000);
        lab.Buddy.Rig.Head.LinearVelocity = new Vector2(50_000, -50_000);
        await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);

        bool hardRecovered = lab.Buddy.Recovery.HardRecoveryCount == priorHardRecoveries + 1;
        bool safeAfterHardRecovery = lab.Buddy.Rig.AllBodiesFinite() &&
                                     lab.Buddy.Recovery.AllBodiesInsideSafeBounds();
        checks.Add(new StartupCheck("escaped_body_triggers_immediate_recovery", hardRecovered,
            $"reason={lab.Buddy.Recovery.LastHardRecoveryReason}"));
        checks.Add(new StartupCheck("hard_recovery_restores_safe_pose", safeAfterHardRecovery,
            $"inside={lab.Buddy.Recovery.AllBodiesInsideSafeBounds()} finite={lab.Buddy.Rig.AllBodiesFinite()}"));

        int beforeInvalidRecovery = lab.Buddy.Recovery.HardRecoveryCount;
        lab.Buddy.Rig.RightHand.LinearVelocity = new Vector2(float.NaN, 0.0f);
        lab.Buddy.Recovery.PhysicsTick(conscious: true);
        bool invalidRecovered = lab.Buddy.Recovery.HardRecoveryCount == beforeInvalidRecovery + 1 &&
                                lab.Buddy.Recovery.LastHardRecoveryReason == HardRecoveryReason.InvalidState &&
                                lab.Buddy.Rig.AllBodiesFinite();
        checks.Add(new StartupCheck("invalid_state_triggers_immediate_recovery", invalidRecovered,
            $"reason={lab.Buddy.Recovery.LastHardRecoveryReason} finite={lab.Buddy.Rig.AllBodiesFinite()}"));

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

    private static async Task<AssistedStandingResult> WaitForAssistedStanding(
        SceneTree tree,
        BuddyLab lab,
        int timeoutTicks)
    {
        float maximumRamp = 0.0f;
        for (int tick = 0; tick < timeoutTicks; tick++)
        {
            await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
            maximumRamp = Mathf.Max(maximumRamp, lab.Buddy.Recovery.State.AssistanceRamp);
            if (lab.Buddy.Standing.Snapshot.IsStable)
            {
                return new AssistedStandingResult(true, maximumRamp);
            }
        }

        return new AssistedStandingResult(false, maximumRamp);
    }

    private readonly record struct AssistedStandingResult(bool Standing, float MaximumRamp);
}
