using System.Collections.Generic;
using System.Threading.Tasks;
using DesktopBuddy.App;
using DesktopBuddy.Buddy.Physics;
using DesktopBuddy.Domain.Autonomy;
using DesktopBuddy.Domain.Buddy;
using DesktopBuddy.Domain.Content;
using DesktopBuddy.Domain.Presentation;
using DesktopBuddy.Domain.Tools;
using DesktopBuddy.Interaction;
using DesktopBuddy.Objects;
using DesktopBuddy.Tools;
using Godot;

namespace DesktopBuddy.Testing;

/// <summary>
/// The M5 Fire Sprayer and Burning gate (Task 7 plan Tasks B–E), asserted against the real
/// composition rather than the models alone.
///
/// <para>The claims worth stating up front, because the checks are written to catch them if
/// they stop being true:</para>
/// <list type="bullet">
///   <item>The stream runs while primary is held and stops on the tick it is released.
///   There is no press edge, no magazine, and no reload.</item>
///   <item>Droplets live in their own bounded pool and never consume one of the 24 FR-014
///   loose-object slots.</item>
///   <item><b>Burning is the only harm lane.</b> A droplet never scores an impact; every
///   accepted <c>tool.fire_sprayer</c> event is a burn tick, carrying the burn's own
///   interaction id, so one stream can never double-dip.</item>
///   <item>Contact grants 4 s and sustained contact pins the remaining duration at the 8 s
///   cap (FR-010.7, FR-010.8).</item>
///   <item>Burn ticks pay and hurt on the shared formula, with the shared
///   <c>min(10, pain x 0.1)</c> mood loss, and never knock the buddy out by themselves
///   (owner default 1).</item>
///   <item>Burning raises the priority-3 hazard through the real ladder, which is what
///   drops a held object and makes the buddy run.</item>
///   <item>Burning survives a knockout and is cleared by a hard reposition.</item>
///   <item>The four accessibility settings change what a run looks like and cannot change
///   one tick of what it simulates (FR-017.3).</item>
/// </list>
/// </summary>
public sealed class BurningStatusScenario : IScenario
{
    private const int SettleTicks = 20;

    /// <summary>Well inside the droplet's 260 px reach, and clear of the drawn nozzle.</summary>
    private const float SprayStandOffPx = 120.0f;

    /// <summary>
    /// Routed ticks each accessibility probe measures, counted from ignition. Five whole
    /// pain intervals, so the expected event count is exact rather than a range.
    /// </summary>
    private const int MeasuredWindowTicks = 300;

    public string Id => "burning_status";

    public async Task<ScenarioResult> RunAsync(SceneTree tree, ulong seed)
    {
        var checks = new List<StartupCheck>();
        var messages = new List<string> { $"seed={seed}" };
        BuddyLab? lab = await M4ObjectScenarioSupport.LoadLab(tree, seed);
        if (lab is null)
        {
            checks.Add(new StartupCheck("burning_lab_loadable", false, "buddy_lab"));
            return new ScenarioResult(false, checks, messages);
        }

        FireSprayerComponent sprayer = lab.FireSprayer;
        FireSprayerProfile profile = sprayer.Profile;
        lab.Pipeline.SelectTool(ToolId.FireSprayer);

        // --- Emission: held is on, released is off, on the same tick ---
        // Aimed along the room rather than at the buddy, so the emission cadence is
        // measured on a stream that ignites nothing.
        Rect2 room = lab.Boundaries.InnerBounds;
        Vector2 bench = room.GetCenter() + new Vector2(0.0f, -60.0f);
        await AimAt(tree, lab, sprayer, bench, Vector2.Up);

        checks.Add(new StartupCheck(
            "selecting_the_sprayer_arms_a_stream_with_no_magazine",
            lab.Progress.IsToolUnlocked(ContentIds.ToolFireSprayer) &&
            sprayer.IsActive && !sprayer.IsSpraying && !sprayer.IsBurning,
            $"owned={lab.Progress.IsToolUnlocked(ContentIds.ToolFireSprayer)} " +
            $"active={sprayer.IsActive} spraying={sprayer.IsSpraying} " +
            $"aim={sprayer.AimForward}"));

        const int holdTicks = 60;
        int launchedBefore = sprayer.DropletsLaunched;
        sprayer.SetPrimaryHeld(true);
        for (int tick = 0; tick < holdTicks; tick++)
            await Tick(tree);
        int emittedWhileHeld = sprayer.DropletsLaunched - launchedBefore;
        bool sprayingWhileHeld = sprayer.IsSpraying;

        sprayer.SetPrimaryHeld(false);
        await Tick(tree);
        bool stoppedSameTick = !sprayer.IsSpraying;
        int launchedAtRelease = sprayer.DropletsLaunched;
        for (int tick = 0; tick < 30; tick++)
            await Tick(tree);
        int emittedAfterRelease = sprayer.DropletsLaunched - launchedAtRelease;

        int expected = holdTicks / profile.EmitIntervalTicks;
        checks.Add(new StartupCheck(
            "spray_streams_only_while_primary_held",
            sprayingWhileHeld &&
            emittedWhileHeld >= expected && emittedWhileHeld <= expected + 1 &&
            stoppedSameTick &&
            emittedAfterRelease == 0 &&
            sprayer.PoolExhaustedCount == 0,
            $"held_ticks={holdTicks} emitted={emittedWhileHeld} expected~{expected} " +
            $"stopped_same_tick={stoppedSameTick} after_release={emittedAfterRelease} " +
            $"exhausted={sprayer.PoolExhaustedCount}"));

        // --- FR-014: the stream is bounded by its own pool, not by the registry ---
        await Idle(tree, sprayer, profile.DropletLifetimeTicks + 4);
        int registryBefore = lab.Objects.Count;
        int registryPeak = registryBefore;
        int dropletPeak = 0;
        sprayer.SetPrimaryHeld(true);
        for (int tick = 0; tick < 600; tick++)
        {
            await Tick(tree);
            registryPeak = Mathf.Max(registryPeak, lab.Objects.Count);
            dropletPeak = Mathf.Max(dropletPeak, sprayer.ActiveDropletCount);
        }

        sprayer.SetPrimaryHeld(false);
        await Idle(tree, sprayer, profile.DropletLifetimeTicks + 4);
        checks.Add(new StartupCheck(
            "droplets_never_register_as_loose_objects",
            registryPeak == registryBefore &&
            dropletPeak > 0 && dropletPeak <= profile.PoolCapacity &&
            sprayer.PoolExhaustedCount == 0 &&
            sprayer.ActiveDropletCount == 0,
            $"registry_before={registryBefore} registry_peak={registryPeak} " +
            $"droplet_peak={dropletPeak} pool={profile.PoolCapacity} " +
            $"exhausted={sprayer.PoolExhaustedCount} settled={sprayer.ActiveDropletCount}"));

        // --- Ignition, refresh, and the 8 s cap ---
        var impacts = new List<AcceptedImpact>();
        void OnImpact(AcceptedImpact impact)
        {
            if (impact.ContentId == ContentIds.ToolFireSprayer)
                impacts.Add(impact);
        }

        lab.Pipeline.ImpactAccepted += OnImpact;

        float moodBeforeBurn = lab.Progress.Mood;
        long balanceBeforeBurn = lab.Progress.BalanceMilliCredits;
        int ignitionsBefore = sprayer.IgnitionCount;
        int freshRemaining = await SprayUntilBurning(tree, lab, sprayer, 240);
        bool ignitedFresh = sprayer.IsBurning && sprayer.IgnitionCount == ignitionsBefore + 1;
        // Every accepted event so far, at the instant the fire caught: a droplet contact is
        // not an impact, so this must be empty.
        int impactsAtIgnition = impacts.Count;

        // Sustained contact from here: the remaining duration must pin at the cap and
        // never climb past it.
        int peakRemaining = freshRemaining;
        sprayer.SetPrimaryHeld(true);
        for (int tick = 0; tick < 900; tick++)
        {
            await Tick(tree);
            peakRemaining = Mathf.Max(peakRemaining, sprayer.BurnTicksRemaining);
        }

        sprayer.SetPrimaryHeld(false);
        checks.Add(new StartupCheck(
            "spray_contact_ignites_and_refreshes_to_cap",
            ignitedFresh &&
            freshRemaining > profile.BurnApplyTicks - 20 &&
            freshRemaining <= profile.BurnApplyTicks &&
            peakRemaining > profile.BurnCapTicks - 20 &&
            peakRemaining <= profile.BurnCapTicks,
            $"ignited={ignitedFresh} fresh_remaining={freshRemaining} " +
            $"applied={profile.BurnApplyTicks} peak_remaining={peakRemaining} " +
            $"cap={profile.BurnCapTicks}"));

        int burnEvents = sprayer.BurnPainEventCount;
        checks.Add(new StartupCheck(
            "droplets_score_zero_impacts",
            impactsAtIgnition == 0 &&
            impacts.Count == burnEvents &&
            AllCarryBurnId(impacts, sprayer.BurnInteractionId),
            $"impacts_at_ignition={impactsAtIgnition} accepted={impacts.Count} " +
            $"burn_events={burnEvents} burn_id={sprayer.BurnInteractionId}"));

        // --- Burn ticks pay and hurt on the shared formula ---
        float perEvent = burnEvents == 0 ? 0.0f : sprayer.TotalBurnPain / burnEvents;
        // Mood is measured across the single routed tick one burn event lands on, not across
        // the whole burn: persistent mood also carries the shared passive drift, and over a
        // window this long that drift is larger than the thing being checked.
        (float moodDelta, float expectedMoodLoss) =
            await MeasureOneEventMoodLoss(tree, lab, sprayer);
        float burnMoodDelta = moodBeforeBurn - lab.Progress.Mood;
        long payout = lab.Progress.BalanceMilliCredits - balanceBeforeBurn;
        long toolPainMilli = CountFor(lab.Progress.Statistics.ToolPainMilli, ContentIds.ToolFireSprayer);

        checks.Add(new StartupCheck(
            "burn_ticks_pay_and_hurt_on_the_shared_formula",
            burnEvents > 0 &&
            perEvent >= 3.0f && perEvent <= 6.0f &&
            expectedMoodLoss > 0.0f &&
            Mathf.IsEqualApprox(moodDelta, expectedMoodLoss, 0.02f) &&
            burnMoodDelta > 0.0f &&
            payout > 0L &&
            toolPainMilli > 0L &&
            lab.Progress.IsContentHarmful(ContentIds.ToolFireSprayer),
            $"events={burnEvents} pain_per_event={perEvent:F2} total_pain={sprayer.TotalBurnPain:F1} " +
            $"one_event_mood_delta={moodDelta:F4} expected={expectedMoodLoss:F4} " +
            $"whole_burn_mood_delta={burnMoodDelta:F3} " +
            $"payout_milli={payout} tool_pain_milli={toolPainMilli} " +
            $"harmful={lab.Progress.IsContentHarmful(ContentIds.ToolFireSprayer)}"));

        // --- A full cap burn never knocks out by itself (owner default 1) ---
        // The burn above ran at the cap for a full 900 ticks with no other damage in play.
        checks.Add(new StartupCheck(
            "a_full_cap_burn_never_knocks_out",
            lab.Buddy.CurrentConsciousness == Consciousness.Conscious &&
            lab.Progress.Statistics.Knockouts == 0L,
            $"consciousness={lab.Buddy.CurrentConsciousness} " +
            $"knockouts={lab.Progress.Statistics.Knockouts} " +
            $"burn_events={burnEvents} total_pain={sprayer.TotalBurnPain:F1}"));

        lab.Pipeline.ImpactAccepted -= OnImpact;

        // --- Panic and the dropped ball, through the real ladder ---
        await WaitForBurnOut(tree, sprayer, profile.BurnCapTicks + 60);
        lab.Pipeline.SelectTool(ToolId.Grab);
        await Tick(tree);
        bool holding = await GiveBuddyABall(tree, lab);
        BehaviorPriority ownerWhileHolding = lab.Buddy.Arbiter.Diagnostics.Owner;

        lab.Pipeline.SelectTool(ToolId.FireSprayer);
        await AimAtBuddy(tree, lab, sprayer);
        await SprayUntilBurning(tree, lab, sprayer, 240);
        sprayer.SetPrimaryHeld(false);
        bool hazardOwned = false;
        bool released = false;
        for (int tick = 0; tick < 180; tick++)
        {
            await Tick(tree);
            hazardOwned |= lab.Buddy.Arbiter.Diagnostics.Owner == BehaviorPriority.Hazard;
            released |= !lab.Buddy.ObjectInteraction.IsHolding;
        }

        checks.Add(new StartupCheck(
            "a_burning_buddy_drops_its_ball_and_panics",
            holding && sprayer.IsBurning && hazardOwned && released,
            $"was_holding={holding} owner_before={ownerWhileHolding} burning={sprayer.IsBurning} " +
            $"hazard_owned={hazardOwned} released={released} " +
            $"owner_now={lab.Buddy.Arbiter.Diagnostics.Owner} " +
            $"flee={sprayer.HazardFleeDirection}"));

        // --- Burning survives a knockout, and a hard reposition puts it out ---
        int eventsBeforeKnockout = sprayer.BurnPainEventCount;
        lab.Buddy.SetConsciousness(Consciousness.Unconscious);
        for (int tick = 0; tick < profile.BurnPainIntervalTicks + 4; tick++)
            await Tick(tree);
        bool burnedWhileOut = sprayer.IsBurning &&
                              sprayer.BurnPainEventCount > eventsBeforeKnockout;
        lab.Buddy.SetConsciousness(Consciousness.Conscious);
        await Tick(tree);

        int hardRecoveriesBefore = lab.Buddy.Recovery.HardRecoveryCount;
        lab.Buddy.Rig.Head.GlobalPosition = new Vector2(-1_000.0f, -1_000.0f);
        lab.Buddy.Rig.Head.LinearVelocity = new Vector2(40_000.0f, -40_000.0f);
        await Tick(tree);
        bool hardRecovered = lab.Buddy.Recovery.HardRecoveryCount > hardRecoveriesBefore;

        checks.Add(new StartupCheck(
            "burning_survives_knockout_but_not_hard_reposition",
            burnedWhileOut && hardRecovered && !sprayer.IsBurning &&
            sprayer.BurnTicksRemaining == 0,
            $"burned_while_unconscious={burnedWhileOut} " +
            $"events_during_knockout={sprayer.BurnPainEventCount - eventsBeforeKnockout} " +
            $"hard_recovered={hardRecovered} burning_after={sprayer.IsBurning} " +
            $"remaining={sprayer.BurnTicksRemaining}"));

        // --- FR-017.3: the settings change the look, never the simulation ---
        // Both probes start from the same known safe standing pose, put there by the same
        // centralized hard reposition, and are driven by identical scripted input. Any
        // difference between them is therefore the settings and nothing else.
        await WaitForBurnOut(tree, sprayer, profile.BurnCapTicks + 120);
        await ScenarioSteps.WaitForStanding(tree, lab, 900);
        Transform2D[] benchPose = CapturePose(lab);
        SettingsProbe permissive = await MeasureUnderSettings(
            tree, lab, sprayer, EffectsSettings.Default, benchPose);
        SettingsProbe restrictive = await MeasureUnderSettings(
            tree, lab, sprayer, EffectsSettings.MostRestrictive, benchPose);

        checks.Add(new StartupCheck(
            "settings_change_visuals_and_never_gameplay",
            permissive.BurnEvents == MeasuredWindowTicks / profile.BurnPainIntervalTicks &&
            permissive.BurnEvents == restrictive.BurnEvents &&
            Mathf.IsEqualApprox(permissive.Pain, restrictive.Pain, 0.001f) &&
            // Mood carries the shared passive drift as well as the burn's own loss, so it is
            // compared to the burn's contribution rather than bit for bit.
            Mathf.IsEqualApprox(permissive.MoodDelta, restrictive.MoodDelta, 0.05f) &&
            permissive.DropletsLaunched == restrictive.DropletsLaunched &&
            // Both windows end with the burn pinned at the 8 s cap. The last few ticks of
            // slack are which droplet of the eight-wide fan last connected, which is a
            // property of the stream and not of the settings.
            permissive.BurnTicks > profile.BurnCapTicks - 10 &&
            restrictive.BurnTicks > profile.BurnCapTicks - 10 &&
            restrictive.DrawEnabledDroplets < permissive.DrawEnabledDroplets,
            $"events={permissive.BurnEvents}/{restrictive.BurnEvents} " +
            $"pain={permissive.Pain:F3}/{restrictive.Pain:F3} " +
            $"mood={permissive.MoodDelta:F3}/{restrictive.MoodDelta:F3} " +
            $"droplets={permissive.DropletsLaunched}/{restrictive.DropletsLaunched} " +
            $"burn_ticks={permissive.BurnTicks}/{restrictive.BurnTicks} " +
            $"drawable_droplets={permissive.DrawEnabledDroplets}/{restrictive.DrawEnabledDroplets} " +
            $"embers={permissive.VisibleEmbers}/{restrictive.VisibleEmbers} " +
            $"[{permissive.Geometry}] [{restrictive.Geometry}]"));

        lab.ApplyEffectsSettings(EffectsSettings.Default);
        float safeHz = lab.FireVisualLegacy.FlickerHz;
        float safe3DHz = lab.FireVisual.FlickerHz;
        lab.ApplyEffectsSettings(EffectsSettings.Default with { PhotosensitivitySafe = false });
        float unsafeHz = lab.FireVisualLegacy.FlickerHz;
        lab.ApplyEffectsSettings(EffectsSettings.Default);
        checks.Add(new StartupCheck(
            "flicker_respects_the_photosensitivity_cap",
            safeHz <= 3.0f && safe3DHz <= 3.0f &&
            Mathf.IsEqualApprox(safeHz, profile.SafeFlickerHz) &&
            unsafeHz > safeHz,
            $"safe_2d={safeHz:F2}Hz safe_3d={safe3DHz:F2}Hz opted_out={unsafeHz:F2}Hz " +
            $"authored_safe={profile.SafeFlickerHz:F2}Hz"));

        // The seam was built here, so the one existing shake setting stops being dead.
        lab.CameraKick.ResetPeak();
        lab.ApplyEffectsSettings(EffectsSettings.Default with { ScreenShake = false });
        int kicksBefore = lab.CameraKick.KickCount;
        lab.CameraKick.Kick(4.0f, 8);
        await Tick(tree);
        bool silenced = lab.CameraKick.KickCount == kicksBefore &&
                        !lab.CameraKick.IsKicking &&
                        lab.CameraKick.PeakOffsetPx <= 0.0f;
        lab.ApplyEffectsSettings(EffectsSettings.Default);
        lab.CameraKick.Kick(4.0f, 8);
        await Tick(tree);
        bool restored = lab.CameraKick.IsKicking && lab.CameraKick.PeakOffsetPx > 0.0f;
        checks.Add(new StartupCheck(
            "screen_shake_setting_silences_the_kick_lane",
            silenced && restored,
            $"silenced={silenced} restored={restored} " +
            $"peak={lab.CameraKick.PeakOffsetPx:F2}px kicks={lab.CameraKick.KickCount}"));

        // --- Scorch marks: darken while burning, hold, fade, and wipe (owner 2026-08-01) ---
        // Measured against the pinned pose for the same reason the settings probe is: the
        // hold and the fade are exact tick counts, and a buddy wandering out of the stream
        // half-way through would be measuring the walk instead.
        Transform2D[] scorchPose = CapturePose(lab);
        await RestorePose(tree, lab, sprayer, scorchPose);
        Rect2 scorchRoom = lab.Boundaries.InnerBounds;
        Vector2 scorchTorso = lab.Buddy.Rig.Torso.GlobalPosition;
        (Vector2 scorchCursor, Vector2 scorchForward) =
            M4ObjectScenarioSupport.StandOffFrom(scorchRoom, scorchTorso, SprayStandOffPx);
        await AimAt(tree, lab, sprayer, scorchCursor, scorchForward);

        bool cleanBefore = Mathf.IsZeroApprox(sprayer.PeakScorch);
        sprayer.SetPrimaryHeld(true);
        for (int tick = 0; tick < 240 && !sprayer.IsBurning; tick++)
        {
            await Tick(tree);
            sprayer.MoveCursor(scorchCursor);
        }

        BuddyPartId litPart = sprayer.IgnitionPart;
        var darkeningSamples = new List<float>();
        bool monotonic = true;
        float previousDarkness = sprayer.ScorchOf(litPart);
        for (int tick = 0; tick < profile.ScorchTicksToFull; tick++)
        {
            await Tick(tree);
            sprayer.MoveCursor(scorchCursor);
            float now = sprayer.ScorchOf(litPart);
            monotonic &= now >= previousDarkness - 0.0001f;
            previousDarkness = now;
            if (tick % 180 == 0)
                darkeningSamples.Add(now);
        }

        float darkestWhileBurning = sprayer.ScorchOf(litPart);
        int markedWhileBurning = sprayer.ScorchedPartCount;
        bool grewOverTime = darkeningSamples.Count >= 3 &&
                            darkeningSamples[^1] > darkeningSamples[0];
        checks.Add(new StartupCheck(
            "scorch_darkens_progressively_only_on_the_part_that_is_burning",
            cleanBefore && sprayer.IsBurning && monotonic && grewOverTime &&
            darkestWhileBurning > 0.0f &&
            darkestWhileBurning <= profile.MaxScorchDarkness + 0.0001f &&
            markedWhileBurning == 1,
            $"clean_before={cleanBefore} part={litPart} samples=[{string.Join(", ", darkeningSamples.ConvertAll(v => v.ToString("F3")))}] " +
            $"darkest={darkestWhileBurning:F3} ceiling={profile.MaxScorchDarkness:F3} " +
            $"monotonic={monotonic} marked_parts={markedWhileBurning}"));

        // The tint really reaches both presentations, and it is the part's own material.
        PuppetPartBody? litBody = FindRigPart(lab, litPart);
        Color authoredAlbedo = lab.VisualPresenter.AuthoredPartAlbedo(litPart);
        Color scorchedAlbedo = lab.VisualPresenter.PartAlbedo(litPart);
        BuddyPartId cleanPart = litPart == BuddyPartId.Head ? BuddyPartId.Torso : BuddyPartId.Head;
        checks.Add(new StartupCheck(
            "the_scorch_tint_reaches_both_presentations_and_no_other_part",
            litBody is not null && litBody.Scorch > 0.0f &&
            litBody.DrawnFillColor != litBody.FillColor &&
            scorchedAlbedo != authoredAlbedo &&
            lab.VisualPresenter.PartAlbedo(cleanPart) ==
                lab.VisualPresenter.AuthoredPartAlbedo(cleanPart) &&
            Mathf.IsZeroApprox(FindRigPart(lab, cleanPart)?.Scorch ?? 1.0f) &&
            lab.Scorch.MarkedPartCount == 1,
            $"legacy_scorch={litBody?.Scorch:F3} legacy_fill={litBody?.DrawnFillColor} " +
            $"authored_fill={litBody?.FillColor} albedo={scorchedAlbedo} " +
            $"authored_albedo={authoredAlbedo} clean_part={cleanPart} " +
            $"presenter_marked={lab.Scorch.MarkedPartCount} peak={lab.Scorch.PeakDarkness:F3}"));

        // Let the fire go out on its own — a cleared burn would take the mark with it, and
        // the hold is a fact about what happens *after* a natural burn ends.
        sprayer.SetPrimaryHeld(false);
        for (int tick = 0; tick < profile.BurnCapTicks + 120 && sprayer.IsBurning; tick++)
            await Tick(tree);

        float atBurnOut = sprayer.ScorchOf(litPart);
        bool heldFull = true;
        for (int tick = 0; tick < profile.ScorchHoldTicks - 2; tick++)
        {
            await Tick(tree);
            heldFull &= Mathf.IsEqualApprox(sprayer.ScorchOf(litPart), atBurnOut, 0.0005f);
        }

        bool stillHolding = sprayer.ScorchIsHolding(litPart);
        // Over the hold's last couple of ticks the fade is armed.
        for (int tick = 0; tick < 4; tick++)
            await Tick(tree);
        bool fading = sprayer.ScorchIsFading(litPart);

        int fadeTicks = 0;
        for (int tick = 0; tick < profile.ScorchFadeTicks + 60; tick++)
        {
            await Tick(tree);
            fadeTicks++;
            if (Mathf.IsZeroApprox(sprayer.ScorchOf(litPart)))
                break;
        }

        checks.Add(new StartupCheck(
            "scorch_holds_for_the_authored_hold_then_fades_to_clean_skin",
            atBurnOut > 0.0f && heldFull && stillHolding && fading &&
            Mathf.IsZeroApprox(sprayer.ScorchOf(litPart)) &&
            fadeTicks <= profile.ScorchFadeTicks + 8 &&
            lab.VisualPresenter.PartAlbedo(litPart) == authoredAlbedo &&
            Mathf.IsZeroApprox(FindRigPart(lab, litPart)?.Scorch ?? 1.0f),
            $"at_burn_out={atBurnOut:F3} held_full={heldFull} hold_ticks={profile.ScorchHoldTicks} " +
            $"still_holding={stillHolding} fading={fading} fade_ticks={fadeTicks} " +
            $"authored_fade={profile.ScorchFadeTicks} " +
            $"albedo_restored={lab.VisualPresenter.PartAlbedo(litPart) == authoredAlbedo}"));

        // --- And the fail-safe wipes a mark that has not finished fading ---
        await RestorePose(tree, lab, sprayer, scorchPose);
        await AimAt(tree, lab, sprayer, scorchCursor, scorchForward);
        sprayer.SetPrimaryHeld(true);
        for (int tick = 0; tick < 240 && !sprayer.IsBurning; tick++)
        {
            await Tick(tree);
            sprayer.MoveCursor(scorchCursor);
        }

        for (int tick = 0; tick < 240; tick++)
        {
            await Tick(tree);
            sprayer.MoveCursor(scorchCursor);
        }

        sprayer.SetPrimaryHeld(false);
        float markedBeforeReposition = sprayer.PeakScorch;
        UnpinBuddy(lab);
        int recoveriesBefore = lab.Buddy.Recovery.HardRecoveryCount;
        lab.Buddy.Rig.Head.GlobalPosition = new Vector2(-1_000.0f, -1_000.0f);
        lab.Buddy.Rig.Head.LinearVelocity = new Vector2(40_000.0f, -40_000.0f);
        await Tick(tree);
        await Tick(tree);
        bool wiped = lab.Buddy.Recovery.HardRecoveryCount > recoveriesBefore &&
                     Mathf.IsZeroApprox(sprayer.PeakScorch) &&
                     !sprayer.IsBurning &&
                     lab.Scorch.MarkedPartCount == 0;
        checks.Add(new StartupCheck(
            "a_hard_reposition_wipes_the_scorch_with_the_burn",
            markedBeforeReposition > 0.0f && wiped &&
            lab.VisualPresenter.PartAlbedo(litPart) == authoredAlbedo,
            $"marked_before={markedBeforeReposition:F3} burning_after={sprayer.IsBurning} " +
            $"peak_after={sprayer.PeakScorch:F3} presenter_marked={lab.Scorch.MarkedPartCount} " +
            $"albedo_restored={lab.VisualPresenter.PartAlbedo(litPart) == authoredAlbedo}"));

        checks.Add(new StartupCheck(
            "spray_and_ignition_cues_fire_with_counters",
            lab.FireAudio.GeneratedStreamCount == 2 &&
            lab.FireAudio.HissStartCount > 0 &&
            lab.FireAudio.HissStopCount > 0 &&
            lab.FireAudio.IgnitionCueCount > 0 &&
            lab.FireAudio.IgnitionCueCount == sprayer.IgnitionCount &&
            !lab.FireAudio.IsHissing,
            $"streams={lab.FireAudio.GeneratedStreamCount} hiss_start={lab.FireAudio.HissStartCount} " +
            $"hiss_stop={lab.FireAudio.HissStopCount} ignition_cues={lab.FireAudio.IgnitionCueCount} " +
            $"ignitions={sprayer.IgnitionCount} hissing={lab.FireAudio.IsHissing} " +
            $"bus={lab.FireAudio.RoutedBus}"));

        UnpinBuddy(lab);
        messages.Add(
            $"burn_impulse={profile.BurnEquivalentImpulse:F0} pain_per_event={perEvent:F2} " +
            $"apply_ticks={profile.BurnApplyTicks} cap_ticks={profile.BurnCapTicks} " +
            $"interval_ticks={profile.BurnPainIntervalTicks}");

        lab.QueueFree();
        bool passed = true;
        foreach (StartupCheck check in checks)
            passed &= check.Passed;

        return new ScenarioResult(passed, checks, messages);
    }

    /// <summary>
    /// Holds the burn until one attributed pain event lands and reports the mood the buddy
    /// actually lost on that tick against the shared <c>min(10, pain x 0.1)</c> rule.
    /// </summary>
    private static async Task<(float Measured, float Expected)> MeasureOneEventMoodLoss(
        SceneTree tree,
        BuddyLab lab,
        FireSprayerComponent sprayer)
    {
        for (int tick = 0; tick < 400; tick++)
        {
            float moodBefore = lab.Progress.Mood;
            int eventsBefore = sprayer.BurnPainEventCount;
            await Tick(tree);
            if (sprayer.BurnPainEventCount == eventsBefore)
                continue;

            return (moodBefore - lab.Progress.Mood,
                Mathf.Min(10.0f, sprayer.LastBurnPain * 0.1f));
        }

        return (0.0f, 0.0f);
    }

    private readonly record struct SettingsProbe(
        int BurnEvents,
        float Pain,
        float MoodDelta,
        int DropletsLaunched,
        int BurnTicks,
        int DrawEnabledDroplets,
        int VisibleEmbers,
        string Geometry);

    /// <summary>
    /// Runs one identical burn under one settings set and reports both the simulation
    /// outcome and one presentation counter. The two probes must agree on every simulation
    /// number and disagree on the presentation one.
    /// </summary>
    private static async Task<SettingsProbe> MeasureUnderSettings(
        SceneTree tree,
        BuddyLab lab,
        FireSprayerComponent sprayer,
        EffectsSettings settings,
        Transform2D[] pose)
    {
        await RestorePose(tree, lab, sprayer, pose);
        lab.ApplyEffectsSettings(settings);
        lab.Pipeline.SelectTool(ToolId.FireSprayer);

        // The cursor is parked at a stand-off computed from the pose the reposition just
        // restored and then held still, so both probes drive identical input.
        Rect2 room = lab.Boundaries.InnerBounds;
        Vector2 torso = lab.Buddy.Rig.Torso.GlobalPosition;
        (Vector2 cursor, Vector2 forward) =
            M4ObjectScenarioSupport.StandOffFrom(room, torso, SprayStandOffPx);
        await AimAt(tree, lab, sprayer, cursor, forward);

        // The measured window opens on the tick the fire catches rather than on the tick the
        // stream starts. How long the first droplet takes to connect depends on where in its
        // eight-droplet fan the stream happens to be, which is a property of the stream and
        // not of the settings; anchoring on ignition takes that out of the comparison and
        // leaves exactly the thing under test.
        sprayer.SetPrimaryHeld(true);
        for (int tick = 0; tick < 240 && !sprayer.IsBurning; tick++)
        {
            await Tick(tree);
            sprayer.MoveCursor(cursor);
        }

        float moodBefore = lab.Progress.Mood;
        int eventsBefore = sprayer.BurnPainEventCount;
        float painBefore = sprayer.TotalBurnPain;
        int dropletsBefore = sprayer.DropletsLaunched;
        int embers = 0;

        for (int tick = 0; tick < MeasuredWindowTicks; tick++)
        {
            await Tick(tree);
            sprayer.MoveCursor(cursor);
            embers = Mathf.Max(embers, lab.FireVisual.VisibleEmberCount);
        }

        sprayer.SetPrimaryHeld(false);
        int burnTicks = sprayer.BurnTicksRemaining;
        return new SettingsProbe(
            sprayer.BurnPainEventCount - eventsBefore,
            sprayer.TotalBurnPain - painBefore,
            moodBefore - lab.Progress.Mood,
            sprayer.DropletsLaunched - dropletsBefore,
            burnTicks,
            sprayer.DrawEnabledDropletCount,
            embers,
            $"torso={torso} cursor={cursor} aim={sprayer.AimForward} " +
            $"nozzle={sprayer.VisualMuzzle2D} active={sprayer.IsActive} " +
            $"sprayed_ticks={sprayer.SprayTicks}");
    }

    /// <summary>One settled standing pose, so both settings probes start from the same one.</summary>
    private static Transform2D[] CapturePose(BuddyLab lab)
    {
        System.Collections.Generic.IReadOnlyList<PuppetPartBody> parts = lab.Buddy.Rig.Parts;
        var pose = new Transform2D[parts.Count];
        for (int index = 0; index < parts.Count; index++)
            pose[index] = parts[index].GlobalTransform;
        return pose;
    }

    /// <summary>
    /// Restores the captured pose and pins the buddy in it, with any burn already out.
    ///
    /// <para>The pin is what makes the FR-017.3 comparison a real one. Without it the buddy
    /// wanders under ambient autonomy between the two probes, the stream lands somewhere
    /// else the second time, and the check would be measuring the walk rather than the
    /// settings. Pinned in one captured pose, both probes see the identical geometry, the
    /// identical fixed cursor, and the identical droplet stream, so any difference in the
    /// numbers is the settings and nothing else.</para>
    /// </summary>
    private static async Task RestorePose(
        SceneTree tree,
        BuddyLab lab,
        FireSprayerComponent sprayer,
        Transform2D[] pose)
    {
        sprayer.SetPrimaryHeld(false);
        sprayer.ClearBurning();
        // Holster first. Drawing the sprayer again resets the shared aim to Initial, so the
        // scripted pointer walk below establishes the same forward both times instead of
        // slewing out of whatever direction the previous section left the weapon pointing.
        lab.Pipeline.SelectTool(ToolId.Grab);
        await Tick(tree);
        System.Collections.Generic.IReadOnlyList<PuppetPartBody> parts = lab.Buddy.Rig.Parts;
        for (int index = 0; index < parts.Count && index < pose.Length; index++)
        {
            PuppetPartBody part = parts[index];
            part.LinearVelocity = Vector2.Zero;
            part.AngularVelocity = 0.0f;
            part.GlobalTransform = pose[index];
            part.FreezeMode = RigidBody2D.FreezeModeEnum.Kinematic;
            part.Freeze = true;
            part.ResetPhysicsInterpolation();
        }

        await Tick(tree);
    }

    private static PuppetPartBody? FindRigPart(BuddyLab lab, BuddyPartId partId)
    {
        System.Collections.Generic.IReadOnlyList<PuppetPartBody> parts = lab.Buddy.Rig.Parts;
        for (int index = 0; index < parts.Count; index++)
        {
            if (parts[index].PartId == partId)
                return parts[index];
        }

        return null;
    }

    private static void UnpinBuddy(BuddyLab lab)
    {
        foreach (PuppetPartBody part in lab.Buddy.Rig.Parts)
            part.Freeze = false;
    }

    /// <summary>
    /// Puts a ball in the buddy's hands the way the object scenarios do, so the drop is a
    /// real committed object action being outranked rather than a flag being cleared.
    /// </summary>
    private static async Task<bool> GiveBuddyABall(SceneTree tree, BuddyLab lab)
    {
        Vector2 hands =
            (lab.Buddy.Rig.LeftHand.GlobalPosition + lab.Buddy.Rig.RightHand.GlobalPosition) * 0.5f;
        LooseObjectBody? ball = lab.SpawnLooseObject(lab.SafeObjectProfile, hands);
        if (ball is null)
            return false;

        return await M4ObjectScenarioSupport.WaitFor(
            tree, () => lab.Buddy.ObjectInteraction.IsHolding, 600);
    }

    /// <summary>Holds the stream on the buddy until it catches; returns the fresh duration.</summary>
    private static async Task<int> SprayUntilBurning(
        SceneTree tree,
        BuddyLab lab,
        FireSprayerComponent sprayer,
        int timeoutTicks)
    {
        await AimAtBuddy(tree, lab, sprayer);
        sprayer.SetPrimaryHeld(true);
        for (int tick = 0; tick < timeoutTicks; tick++)
        {
            await Tick(tree);
            if (sprayer.IsBurning)
                return sprayer.BurnTicksRemaining;
        }

        return 0;
    }

    private static async Task WaitForBurnOut(
        SceneTree tree,
        FireSprayerComponent sprayer,
        int timeoutTicks)
    {
        sprayer.SetPrimaryHeld(false);
        for (int tick = 0; tick < timeoutTicks && sprayer.IsBurning; tick++)
            await Tick(tree);
    }

    private static async Task AimAtBuddy(
        SceneTree tree,
        BuddyLab lab,
        FireSprayerComponent sprayer)
    {
        Rect2 room = lab.Boundaries.InnerBounds;
        Vector2 torso = lab.Buddy.Rig.Torso.GlobalPosition;
        (Vector2 cursor, Vector2 forward) =
            M4ObjectScenarioSupport.StandOffFrom(room, torso, SprayStandOffPx);
        await AimAt(tree, lab, sprayer, cursor, forward);
    }

    /// <summary>
    /// Walks the cursor into <paramref name="cursor"/> along <paramref name="forward"/>, the
    /// way a hand does, because the shared aim model has no direction until the pointer has
    /// really travelled.
    ///
    /// <para>The weapon is holstered first and the pointer is parked at the start of the walk
    /// while it is away. That is not ceremony: drawing the sprayer resets the shared aim, and
    /// without the park the very first frame of the walk would be one big jump from wherever
    /// the pointer happened to be, which establishes the aim in the <b>opposite</b> direction
    /// and then has to slew 180 degrees back at the authored six degrees a tick. A player
    /// never does that; a scripted scenario does it every time.</para>
    /// </summary>
    private static async Task AimAt(
        SceneTree tree,
        BuddyLab lab,
        FireSprayerComponent sprayer,
        Vector2 cursor,
        Vector2 forward)
    {
        const int steps = 30;
        const float stepPx = 3.0f;
        Vector2 start = cursor - (forward * (steps * stepPx));

        lab.Pipeline.SelectTool(ToolId.Grab);
        sprayer.MoveCursor(start);
        await Tick(tree);
        await Tick(tree);
        lab.Pipeline.SelectTool(ToolId.FireSprayer);

        for (int step = 0; step <= steps; step++)
        {
            sprayer.MoveCursor(start + (forward * (step * stepPx)));
            await Tick(tree);
        }

        for (int tick = 0; tick < SettleTicks; tick++)
            await Tick(tree);
    }

    private static async Task Idle(SceneTree tree, FireSprayerComponent sprayer, int ticks)
    {
        sprayer.SetPrimaryHeld(false);
        for (int tick = 0; tick < ticks; tick++)
            await Tick(tree);
    }

    private static async Task Tick(SceneTree tree) =>
        await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);

    private static bool AllCarryBurnId(IReadOnlyList<AcceptedImpact> impacts, int burnId)
    {
        for (int index = 0; index < impacts.Count; index++)
        {
            if (impacts[index].InteractionId != burnId)
                return false;
        }

        return true;
    }

    private static long CountFor(IReadOnlyDictionary<string, long>? source, string key) =>
        source is not null && source.TryGetValue(key, out long value) ? value : 0L;
}
