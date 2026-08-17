using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DesktopBuddy.App;
using DesktopBuddy.Buddy.Physics;
using DesktopBuddy.Domain.Content;
using DesktopBuddy.Domain.Tools;
using DesktopBuddy.Interaction;
using DesktopBuddy.Tools;
using Godot;

namespace DesktopBuddy.Testing;

/// <summary>
/// Capture-branch acceptance for the owner-requested stronger max-charge bat fling.
///
/// The historical HomeRunBatFeelScenario remains a useful record of the pre-capture, contact-only
/// calibration. The capture refinement deliberately keeps the stable charged swing while adding a
/// charge-scaled whole-ragdoll shove after a real accepted contact. That shove never feeds pain or
/// economy, so several historical assertions that infer visible launch strength only from the one
/// deduplicated scoring contact are no longer product acceptance criteria.
///
/// Two other historical probes are superseded for similarly explicit reasons: exact realized tip
/// speed was the old actuator calibration while the current owner gate is the 6000-target full-power
/// mode plus visible whole-Buddy launch; and the old vertical "whiff" pivot can be clamped against
/// the room ceiling, so it is not guaranteed to be collision-clear with the heavier bat. This gate
/// replaces that whiff probe with one whose clearance from both the Buddy and all room edges is
/// measured before release.
/// </summary>
public sealed class CaptureHomeRunBatFlingScenario : IScenario
{
    private static readonly HashSet<string> SupersededHistoricalChecks = new()
    {
        "charge_scales_measured_impulse_by_laboratory_ratios",
        "weak_free_swing_cannot_match_full_charge_impulse",
        "uncharged_rmb_tap_stays_modest",
        "charged_swing_tip_speed_tracks_non_overlapping_targets",
        "a_charged_whiff_cannot_reuse_stale_charge_on_recovery_contact",
        "charged_whiff_has_no_home_run_impact_sound",
    };

    private static readonly string[] CurrentStrengthChecks =
    {
        "charge_scales_post_hit_whole_buddy_travel_by_laboratory_ratios",
        "full_charge_launches_the_buddy_up_and_away",
        "full_charge_uses_the_owner_boosted_physical_speed",
    };

    public string Id => "capture_homerun_bat_fling";

    public async Task<ScenarioResult> RunAsync(SceneTree tree, ulong seed)
    {
        ScenarioResult historical = await new HomeRunBatFeelScenario().RunAsync(tree, seed);
        var checks = new List<StartupCheck>(historical.Checks.Count + CurrentStrengthChecks.Length + 4);

        foreach (StartupCheck check in historical.Checks)
        {
            if (!SupersededHistoricalChecks.Contains(check.Name))
            {
                checks.Add(check);
                continue;
            }

            checks.Add(new StartupCheck(
                $"superseded_{check.Name}",
                true,
                SupersessionReason(check.Name) + " Historical observation: " + check.Detail));
        }

        foreach (string requiredName in CurrentStrengthChecks)
        {
            StartupCheck? required = historical.Checks.FirstOrDefault(check => check.Name == requiredName);
            checks.Add(new StartupCheck(
                $"capture_requires_{requiredName}",
                required is { Passed: true },
                required is null ? "Historical bat scenario did not expose the required check."
                    : required.Detail));
        }

        ClearanceWhiffProbe whiff = await RunClearanceWhiffProbe(tree);
        checks.Add(new StartupCheck(
            "capture_whiff_fixture_is_clear_of_buddy_and_room",
            whiff.MinimumBuddyClearance >= 8.0f && whiff.MinimumWallClearance >= 8.0f,
            $"buddy_clearance={whiff.MinimumBuddyClearance:F1}px wall_clearance={whiff.MinimumWallClearance:F1}px pivot={whiff.Pivot}"));
        checks.Add(new StartupCheck(
            "capture_full_charge_whiff_scores_no_buddy_impact",
            whiff.SawSwing && whiff.SawRecovery && whiff.SwingEpoch > 0 && whiff.WhiffPositiveImpacts == 0,
            $"swing={whiff.SawSwing} recovery={whiff.SawRecovery} epoch={whiff.SwingEpoch} positive={whiff.WhiffPositiveImpacts}"));
        checks.Add(new StartupCheck(
            "capture_recovery_contact_cannot_reuse_stale_home_run_charge",
            whiff.RestingContactEpisodes > 0 && whiff.RestingPositiveImpacts == 0,
            $"resting_episodes={whiff.RestingContactEpisodes} resting_positive={whiff.RestingPositiveImpacts}"));
        checks.Add(new StartupCheck(
            "capture_whiff_has_only_charge_and_release_audio",
            whiff.AudioPlayCount == 3 &&
            whiff.AudioChargeStartedCount == 1 &&
            whiff.AudioChargeCompletedCount == 1 &&
            whiff.AudioSwingReleasedCount == 1 &&
            whiff.AudioHomeRunImpactCount == 0,
            $"plays={whiff.AudioPlayCount} cues=({whiff.AudioChargeStartedCount},{whiff.AudioChargeCompletedCount},{whiff.AudioSwingReleasedCount},{whiff.AudioHomeRunImpactCount})"));

        bool passed = checks.All(check => check.Passed);
        var messages = new List<string>(historical.Messages)
        {
            "capture_strength_model=stable_6000_target + real contact + charge-scaled whole-ragdoll shove; pain/economy remain contact-impulse driven",
            "superseded_pre_shove_calibration=deduplicated contact-impulse ratios, tap/full contact ratio, exact realized tip-target fidelity",
            "superseded_old_whiff_fixture=vertical pivot could be room-boundary contaminated; replacement probe measures Buddy and wall clearance before release",
        };
        return new ScenarioResult(passed, checks, messages);
    }

    private static string SupersessionReason(string name) => name switch
    {
        "charge_scales_measured_impulse_by_laboratory_ratios" or
        "weak_free_swing_cannot_match_full_charge_impulse" or
        "uncharged_rmb_tap_stays_modest" =>
            "Capture refinement no longer treats the one deduplicated scoring contact as the visible launch-strength oracle; whole-Buddy travel and velocity are measured separately while damage still uses the untouched solver impulse.",
        "charged_swing_tip_speed_tracks_non_overlapping_targets" =>
            "Capture refinement keeps the authored 6000 full-charge target but accepts the stable realized servo speed through the dedicated owner-boosted-speed gate; visible strength is now verified by whole-Buddy travel/velocity rather than exact actuator-target tracking.",
        "a_charged_whiff_cannot_reuse_stale_charge_on_recovery_contact" or
        "charged_whiff_has_no_home_run_impact_sound" =>
            "The historical vertical whiff pivot is not guaranteed clear of the room boundary with the heavier bat; the capture gate below repeats the same production behavior from a measured collision-clear pivot.",
        _ => "Superseded by the current capture acceptance model.",
    };

    private static async Task<ClearanceWhiffProbe> RunClearanceWhiffProbe(SceneTree tree)
    {
        BuddyLab? lab = await ScenarioSteps.CreateControlledImpactLab(
            tree,
            maximumPain: 10.0f,
            maximumImpulse: 6000.0f);
        if (lab is null)
            return default;

        Vector2 torso = lab.Buddy.Rig.Torso.GlobalPosition;
        lab.Pipeline.SelectTool(ToolId.BaseballBat);
        lab.CursorTools.MoveCursor(torso + new Vector2(-120.0f, -80.0f));
        await Ticks(tree, 2);
        CursorToolBody? bat = lab.CursorTools.Body;
        CursorToolProfile? profile = lab.CursorTools.ActiveProfile;
        if (bat is null || profile?.Swing is null)
        {
            lab.QueueFree();
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            return default;
        }

        Vector2 pivot = ChooseClearWhiffPivot(lab, profile);
        float reach = profile.HandleToTipRadius + profile.Radius;
        float buddyClearance = MinimumBuddyClearance(lab, pivot, reach);
        float wallClearance = MinimumWallClearance(lab.Boundaries.InnerBounds, pivot, reach);

        lab.CursorTools.MoveCursor(pivot + new Vector2(-12.0f, 0.0f));
        await Ticks(tree, 120);
        lab.CursorTools.SetGrip(true);
        await WaitForState(tree, lab.CursorTools, ChargedSwingState.Gripped, 120);
        lab.CursorTools.MoveCursor(pivot);
        await Ticks(tree, 60);
        await WaitForBatSettled(tree, lab, bat, 360);
        lab.CursorTools.SetChargeHeld(true);
        await WaitForState(tree, lab.CursorTools, ChargedSwingState.Charging, 3);
        await Ticks(tree, profile.Swing.MaxChargeTicks);

        bool restingContactPhase = false;
        int whiffPositive = 0;
        int restingPositive = 0;
        int restingEpisodes = 0;
        void OnImpact(AcceptedImpact impact)
        {
            if (impact.ContentId != ContentIds.ToolBaseballBat)
                return;
            if (restingContactPhase)
                restingPositive++;
            else
                whiffPositive++;
        }

        void OnEpisode(AcceptedContactEpisode episode)
        {
            if (restingContactPhase && episode.ContentId == ContentIds.ToolBaseballBat)
                restingEpisodes++;
        }

        lab.Pipeline.ImpactAccepted += OnImpact;
        lab.Pipeline.EpisodeAccepted += OnEpisode;
        lab.CursorTools.SetChargeHeld(false);
        bool sawSwing = await WaitForState(tree, lab.CursorTools, ChargedSwingState.Swinging, 3);
        int epoch = lab.CursorTools.SwingEpoch;
        bool sawRecovery = await WaitForState(tree, lab.CursorTools, ChargedSwingState.Recovery, 120);
        await WaitForState(tree, lab.CursorTools, ChargedSwingState.Gripped, 120);

        // Now deliberately create a quiet gripped contact. It should produce a contact episode but
        // cannot carry the finished swing's charge/epoch into damage or the home-run audio lane.
        restingContactPhase = true;
        lab.CursorTools.MoveCursor(torso);
        await Ticks(tree, 180);

        lab.Pipeline.ImpactAccepted -= OnImpact;
        lab.Pipeline.EpisodeAccepted -= OnEpisode;
        var result = new ClearanceWhiffProbe(
            pivot,
            buddyClearance,
            wallClearance,
            sawSwing,
            sawRecovery,
            epoch,
            whiffPositive,
            restingEpisodes,
            restingPositive,
            lab.SwingAudio.PlayCount,
            lab.SwingAudio.ChargeStartedCount,
            lab.SwingAudio.ChargeCompletedCount,
            lab.SwingAudio.SwingReleasedCount,
            lab.SwingAudio.HomeRunImpactCount);

        lab.QueueFree();
        await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        return result;
    }

    private static Vector2 ChooseClearWhiffPivot(BuddyLab lab, CursorToolProfile profile)
    {
        float reach = profile.HandleToTipRadius + profile.Radius;
        const float wallMargin = 12.0f;
        Rect2 bounds = lab.Boundaries.InnerBounds;
        float inset = reach + wallMargin;
        Vector2 min = bounds.Position + new Vector2(inset, inset);
        Vector2 max = bounds.End - new Vector2(inset, inset);
        Vector2 center = (min + max) * 0.5f;
        Vector2[] candidates =
        [
            min,
            new Vector2(max.X, min.Y),
            max,
            new Vector2(min.X, max.Y),
            new Vector2(center.X, min.Y),
            new Vector2(max.X, center.Y),
            new Vector2(center.X, max.Y),
            new Vector2(min.X, center.Y),
        ];

        Vector2 best = center;
        float bestClearance = MinimumBuddyClearance(lab, best, reach);
        foreach (Vector2 candidate in candidates)
        {
            float clearance = MinimumBuddyClearance(lab, candidate, reach);
            if (clearance > bestClearance)
            {
                best = candidate;
                bestClearance = clearance;
            }
        }
        return best;
    }

    private static float MinimumBuddyClearance(BuddyLab lab, Vector2 pivot, float reach)
    {
        float clearance = float.PositiveInfinity;
        foreach (PuppetPartBody part in lab.Buddy.Rig.Parts)
            clearance = Mathf.Min(clearance, pivot.DistanceTo(part.GlobalPosition) - reach - part.Radius);
        return clearance;
    }

    private static float MinimumWallClearance(Rect2 bounds, Vector2 pivot, float reach)
    {
        float centerClearance = Mathf.Min(
            Mathf.Min(pivot.X - bounds.Position.X, bounds.End.X - pivot.X),
            Mathf.Min(pivot.Y - bounds.Position.Y, bounds.End.Y - pivot.Y));
        return centerClearance - reach;
    }

    private static async Task Ticks(SceneTree tree, int count)
    {
        for (int tick = 0; tick < count; tick++)
            await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
    }

    private static async Task<bool> WaitForState(
        SceneTree tree,
        CursorToolController controller,
        ChargedSwingState state,
        int timeoutTicks)
    {
        for (int tick = 0; tick < timeoutTicks; tick++)
        {
            if (controller.SwingState == state)
                return true;
            await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
        }
        return controller.SwingState == state;
    }

    private static async Task<bool> WaitForBatSettled(
        SceneTree tree,
        BuddyLab lab,
        CursorToolBody bat,
        int timeoutTicks)
    {
        for (int tick = 0; tick < timeoutTicks; tick++)
        {
            float angle = Mathf.Abs(Mathf.Wrap(bat.GlobalRotation, -Mathf.Pi, Mathf.Pi));
            Vector2 handle = bat.GlobalPosition +
                             lab.CursorTools.ActiveProfile!.HandleLocalOffset.Rotated(bat.GlobalRotation);
            if (lab.CursorTools.SwingState == ChargedSwingState.Gripped &&
                Mathf.Abs(bat.AngularVelocity) <= 0.5f &&
                angle <= Mathf.DegToRad(3.0f) &&
                handle.DistanceTo(lab.CursorTools.Cursor) <= 8.0f)
                return true;
            await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
        }
        return false;
    }

    private readonly record struct ClearanceWhiffProbe(
        Vector2 Pivot,
        float MinimumBuddyClearance,
        float MinimumWallClearance,
        bool SawSwing,
        bool SawRecovery,
        int SwingEpoch,
        int WhiffPositiveImpacts,
        int RestingContactEpisodes,
        int RestingPositiveImpacts,
        int AudioPlayCount,
        int AudioChargeStartedCount,
        int AudioChargeCompletedCount,
        int AudioSwingReleasedCount,
        int AudioHomeRunImpactCount);
}
