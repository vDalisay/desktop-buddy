using System.Collections.Generic;
using System.Threading.Tasks;
using DesktopBuddy.App;
using DesktopBuddy.Buddy.Physics;
using DesktopBuddy.Domain.Buddy;
using DesktopBuddy.Domain.Content;
using DesktopBuddy.Domain.Physics;
using DesktopBuddy.Domain.Presentation;
using DesktopBuddy.Domain.Tools;
using DesktopBuddy.Interaction;
using DesktopBuddy.Presentation3D;
using DesktopBuddy.Tools;
using Godot;

namespace DesktopBuddy.Testing;

/// <summary>
/// The Home-Run Bat handling gate (`docs/M5_TASK4_HOME_RUN_BAT_FEEL_PLAN.md`).
/// This is the single host scenario for the whole slice: Task B's grip, weak
/// free swing, and impact admission land here, and the charge, swing, and
/// hit-lag checks join them in the same file as those tasks land.
///
/// The weak free swing is a real attack and must keep scoring — it is the
/// owner-confirmed secondary attack, and `bat_swing`/`m5_baseball_bat` still
/// depend on it. What must not happen is a flicked pointer manufacturing a
/// home-run-grade impulse out of one frame's travel, or the act of picking the
/// bat up hurting anyone.
/// </summary>
public sealed class HomeRunBatFeelScenario : IScenario
{
    private const float BenchmarkSwingSpeed = 2400.0f;

    /// <summary>Four times the authored anchor cap: what a high-DPI flick looks like.</summary>
    private const float FlickSwingSpeed = 9600.0f;

    private const int SettleTicks = 240;

    /// <summary>
    /// Deliberately far wider than the other controlled-impact labs. A curve
    /// that saturates at the free swing's own impulse would make "positive but
    /// bounded" unprovable — every hit would read as maximum pain — and would
    /// leave the charged swing no room to measure stronger than it.
    /// </summary>
    private const float CurveMaximumImpulse = 6000.0f;

    private const float CurveMaximumPain = 10.0f;

    // Laboratory-set after the production collider/servo probe was measured.
    // They are deliberately ratios with margin, not fragile strict ordering.
    // After the owner-requested full-speed increase, the 0/300/600-tick
    // reference run measures 1.43× then 3.27× impulse and 1.12× then 1.40×
    // post-hit COM travel. Mid remains materially above the tap while the
    // larger margin is deliberately concentrated at full charge.
    private const float MinimumMidToLowImpulseRatio = 1.20f;
    private const float MinimumFullToMidImpulseRatio = 2.50f;
    private const float MinimumMidToLowTravelRatio = 1.10f;
    private const float MinimumFullToMidTravelRatio = 1.05f;

    // Distal barrel sweet spot (70 px from the handle on the 83 px lever),
    // where a tangential swing meets the side of the barrel. The geometric
    // outermost end cap has a longitudinal normal and only produces a glancing
    // contact under tangential motion, so it is not the "tip hit" players mean.
    private const float TipContactRadiusFraction = 70.0f / 83.0f;

    public string Id => "homerun_bat_feel";

    public async Task<ScenarioResult> RunAsync(SceneTree tree, ulong seed)
    {
        var checks = new List<StartupCheck>();
        var messages = new List<string> { $"seed={seed}" };
        BuddyLab? lab = await ScenarioSteps.CreateControlledImpactLab(
            tree, CurveMaximumPain, CurveMaximumImpulse);
        if (lab is null)
        {
            checks.Add(new StartupCheck("homerun_lab_loadable", false, "buddy_lab"));
            return new ScenarioResult(false, checks, messages);
        }

        lab.Pipeline.SelectTool(ToolId.BaseballBat);
        Vector2 torso = lab.Buddy.Rig.Torso.GlobalPosition;
        Vector2 openSpace = torso + new Vector2(0.0f, -240.0f);
        lab.CursorTools.MoveCursor(openSpace + new Vector2(-320.0f, 0.0f));
        await Ticks(tree, 30);

        CursorToolBody? bat = lab.CursorTools.Body;
        checks.Add(new StartupCheck(
            "the_bat_authors_grip_charge_and_swing_handling",
            bat is not null &&
            lab.CursorTools.IsSwingCapable &&
            lab.CursorTools.ActiveProfile?.Swing is not null &&
            lab.CursorTools.SwingState == ChargedSwingState.Follow,
            $"capable={lab.CursorTools.IsSwingCapable} state={lab.CursorTools.SwingState} " +
            $"content={bat?.ContentId}"));
        if (bat is null)
        {
            return Finish(checks, messages, lab);
        }

        // ---- the anchor cap, measured in open air so no contact perturbs it ----
        // Both sweeps are identical except for how fast the pointer moves. The
        // second is four times the authored cap, which is what a high-DPI mouse
        // or a teleporting pointer really looks like.
        float benchmarkPeak = await SweepAndMeasurePeakSpeed(
            tree, lab, bat, openSpace, BenchmarkSwingSpeed);
        float flickPeak = await SweepAndMeasurePeakSpeed(
            tree, lab, bat, openSpace, FlickSwingSpeed);

        checks.Add(new StartupCheck(
            "free_swing_speed_cap_bounds_a_flicked_pointer",
            benchmarkPeak > 0.0f &&
            flickPeak <= benchmarkPeak * 1.25f,
            $"benchmark_peak={benchmarkPeak:F0}px/s flick_peak={flickPeak:F0}px/s " +
            $"ratio={(benchmarkPeak > 0.0f ? flickPeak / benchmarkPeak : float.NaN):F2} " +
            $"(pointer drove {BenchmarkSwingSpeed:F0} then {FlickSwingSpeed:F0})"));

        // ---- the weak free swing still lands, and still scores ----
        AcceptedImpact? freeSwing = null;
        void OnFreeSwing(AcceptedImpact impact)
        {
            if (freeSwing is null && impact.ContentId == ContentIds.ToolBaseballBat)
                freeSwing = impact;
        }

        lab.Pipeline.ImpactAccepted += OnFreeSwing;
        Vector2 windUp = torso + new Vector2(-300.0f, 0.0f);
        lab.CursorTools.MoveCursor(windUp);
        await Ticks(tree, 30);
        await DragCursor(tree, lab, windUp, Vector2.Right, BenchmarkSwingSpeed, 60,
            () => freeSwing is not null);
        await Ticks(tree, 30);
        lab.Pipeline.ImpactAccepted -= OnFreeSwing;

        checks.Add(new StartupCheck(
            "weak_free_swing_scores_positive_but_bounded_pain",
            freeSwing is { Pain: > 0.0f } hit &&
            hit.MilliCredits > 0L &&
            // Not attributed to a charged swing: the free swing opens no epoch,
            // so it can never be mistaken for the home run in the ledger.
            hit.SwingEpoch == 0 &&
            hit.SwingCharge == 0.0f &&
            hit.SwingReleasedTick == 0L &&
            // Below maximum pain, with room left for the charged swing to be
            // measurably stronger than this on the same curve.
            hit.Pain < CurveMaximumPain * 0.8f,
            $"pain={freeSwing?.Pain:F2} impulse={freeSwing?.Impulse:F1} " +
            $"milli={freeSwing?.MilliCredits} epoch={freeSwing?.SwingEpoch} " +
            $"charge={freeSwing?.SwingCharge:F2} " +
            $"(curve tops out at {CurveMaximumPain:F2} / {CurveMaximumImpulse:F0} impulse)"));

        // ---- picking the bat up is not an attack ----
        // Creep into contact while following, then grip and drag hard. In Follow
        // the same motion scores; gripped it must score nothing at all, even
        // though the collider is still fully physical and still in contact.
        Vector2 contactAnchor = torso + new Vector2(-40.0f, 0.0f);
        await CreepCursor(tree, lab, contactAnchor, 120);
        lab.CursorTools.SetGrip(true);
        await Ticks(tree, 10);

        bool grippedInTime = lab.CursorTools.SwingState == ChargedSwingState.Gripped;
        long grippedScored = 0;
        long grippedEpisodes = 0;
        void OnGrippedImpact(AcceptedImpact impact)
        {
            if (impact.ContentId == ContentIds.ToolBaseballBat)
                grippedScored++;
        }

        void OnGrippedEpisode(AcceptedContactEpisode episode)
        {
            if (episode.ContentId == ContentIds.ToolBaseballBat)
                grippedEpisodes++;
        }

        lab.Pipeline.ImpactAccepted += OnGrippedImpact;
        lab.Pipeline.EpisodeAccepted += OnGrippedEpisode;
        for (int pass = 0; pass < 3; pass++)
        {
            Vector2 across = lab.Buddy.Rig.Torso.GlobalPosition + new Vector2(-40.0f, 0.0f);
            await DragCursor(tree, lab, across, Vector2.Right, BenchmarkSwingSpeed, 6, null);
            await DragCursor(
                tree, lab, lab.CursorTools.Cursor, Vector2.Left, BenchmarkSwingSpeed, 6, null);
        }

        // Release immediately after the last solver contact. The pipeline sees
        // that sample on the following routed tick, after the controller has
        // already returned to Follow. It must still use the None context captured
        // with the gripped contact rather than reclassifying it from the live
        // Follow state one tick late.
        lab.CursorTools.SetGrip(false);
        await Ticks(tree, 2);
        bool releasedAcrossObservationBoundary =
            lab.CursorTools.SwingState == ChargedSwingState.Follow;

        // Reacquire for the independent upright/handle check below.
        lab.CursorTools.SetGrip(true);
        await Ticks(tree, 10);
        lab.Pipeline.ImpactAccepted -= OnGrippedImpact;
        lab.Pipeline.EpisodeAccepted -= OnGrippedEpisode;

        checks.Add(new StartupCheck(
            "gripping_in_contact_scores_no_pain",
            grippedInTime &&
            releasedAcrossObservationBoundary &&
            grippedScored == 0L &&
            lab.CursorTools.SwingState == ChargedSwingState.Gripped,
            $"gripped={grippedInTime} state={lab.CursorTools.SwingState} " +
            $"release_boundary={releasedAcrossObservationBoundary} " +
            $"scored={grippedScored} episodes={grippedEpisodes} " +
            "(episodes may be non-zero; scoring may not)"));

        // ---- held by the handle, barrel up ----
        Vector2 hold = openSpace + new Vector2(-120.0f, 0.0f);
        lab.CursorTools.MoveCursor(hold);
        await Ticks(tree, SettleTicks);

        float barrelDegrees = Mathf.RadToDeg(Mathf.Abs(Mathf.Wrap(
            bat.GlobalRotation, -Mathf.Pi, Mathf.Pi)));
        Vector2 handlePoint = HandlePoint(lab, bat);
        float handleError = handlePoint.DistanceTo(lab.CursorTools.Cursor);
        float centreError = bat.GlobalPosition.DistanceTo(lab.CursorTools.Cursor);

        checks.Add(new StartupCheck(
            "gripping_the_bat_holds_it_upright_by_the_handle",
            barrelDegrees <= 8.0f &&
            handleError <= 16.0f &&
            // Proving it hangs from the handle rather than its middle: a centre
            // tether would put the origin on the cursor, not a lever arm away.
            centreError >= 20.0f,
            $"barrel_from_up={barrelDegrees:F2}deg handle_to_cursor={handleError:F1}px " +
            $"centre_to_cursor={centreError:F1}px state={lab.CursorTools.SwingState}"));

        // ---- the charged handle may be lowered to the floor ----
        // The cursor is the player's requested pivot. The room, not an
        // invisible arc-sized inset, should decide whether the physical bat can
        // actually follow it at the bottom edge.
        CursorToolProfile authoredProfile = lab.CursorTools.ActiveProfile!;
        Rect2 playable = lab.Boundaries.InnerBounds;
        lab.CursorTools.SetChargeHeld(true);
        await Ticks(tree, 1);
        lab.CursorTools.MoveCursor(new Vector2(
            hold.X,
            playable.End.Y + authoredProfile.Length));
        await Ticks(tree, 30);
        float chargedFloorCursorY = lab.CursorTools.Cursor.Y;
        bool bodyStayedFiniteAtFloor =
            bat.GlobalPosition.IsFinite() &&
            bat.LinearVelocity.IsFinite() &&
            float.IsFinite(bat.GlobalRotation) &&
            float.IsFinite(bat.AngularVelocity);
        checks.Add(new StartupCheck(
            "charging_cursor_can_reach_the_floor_and_physics_blocks_the_bat",
            lab.CursorTools.SwingState == ChargedSwingState.Charging &&
            chargedFloorCursorY >=
                playable.End.Y - authoredProfile.WallClearance - 0.1f &&
            chargedFloorCursorY <= playable.End.Y + 0.1f &&
            bodyStayedFiniteAtFloor,
            $"cursor_y={chargedFloorCursorY:F1} floor={playable.End.Y:F1} " +
            $"clearance={authoredProfile.WallClearance:F1} finite={bodyStayedFiniteAtFloor}"));

        // Cancel this independent obstruction probe, then reacquire a clean
        // zero-charge grip for the exact milestone checks below.
        lab.CursorTools.SetGrip(false);
        lab.CursorTools.SetChargeHeld(false);
        await Ticks(tree, 1);
        lab.CursorTools.MoveCursor(hold);
        lab.CursorTools.SetGrip(true);
        await WaitForState(tree, lab.CursorTools, ChargedSwingState.Gripped, 120);
        await Ticks(tree, 30);

        // ---- charge is exactly five routed seconds, with eased visual strain ----
        int chargeCompletedEdges = 0;
        int swingReleaseEdges = 0;
        int firstReleasedDirection = 0;
        int secondReleasedDirection = 0;
        void OnChargeCompleted() => chargeCompletedEdges++;
        void OnSwingReleased(float charge, int epoch)
        {
            _ = charge;
            _ = epoch;
            swingReleaseEdges++;
            if (swingReleaseEdges == 1)
                firstReleasedDirection = lab.CursorTools.SwingDirectionSign;
            else if (swingReleaseEdges == 2)
                secondReleasedDirection = lab.CursorTools.SwingDirectionSign;
        }

        lab.CursorTools.ChargeCompleted += OnChargeCompleted;
        lab.CursorTools.SwingReleased += OnSwingReleased;
        lab.CursorTools.SetChargeHeld(true);
        await Ticks(tree, 1); // GRIPPED -> CHARGING, charge remains zero on the entry edge.

        await Ticks(tree, 120);
        int chargeAt120 = lab.CursorTools.SwingChargeTicks;
        await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        int glintStartsAt120 = bat.ChargeGlintStarts;
        float glintSizeAt120 = bat.VisualGlintSizePx;
        bool sourceGlintAt120 = bat.IsChargeGlintActive;
        bool counterpartGlintAt120 = lab.CursorToolVisual.IsGlintVisible;

        await Ticks(tree, 180);
        int chargeAt300 = lab.CursorTools.SwingChargeTicks;
        float shakeAt300 = bat.ChargeShakeAmplitude;
        await Ticks(tree, 60);
        int chargeAt360 = lab.CursorTools.SwingChargeTicks;
        await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        int glintStartsAt360 = bat.ChargeGlintStarts;
        float glintSizeAt360 = bat.VisualGlintSizePx;
        bool sourceGlintAt360 = bat.IsChargeGlintActive;
        bool counterpartGlintAt360 = lab.CursorToolVisual.IsGlintVisible;

        await Ticks(tree, 239);
        int chargeAt599 = lab.CursorTools.SwingChargeTicks;
        float shakeAt599 = bat.ChargeShakeAmplitude;
        await Ticks(tree, 1);
        int chargeAt600 = lab.CursorTools.SwingChargeTicks;
        float shakeAt600 = bat.ChargeShakeAmplitude;
        await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        bool sourceGlintWasVisible = bat.IsChargeGlintActive;
        bool counterpartGlintWasVisible = lab.CursorToolVisual.IsGlintVisible;
        bool glintWasVisible =
            sourceGlintWasVisible &&
            (lab.Mode != PresentationMode.Mii3D || counterpartGlintWasVisible);
        await Ticks(tree, 1);
        int chargeAt601 = lab.CursorTools.SwingChargeTicks;
        float shakeAt601 = bat.ChargeShakeAmplitude;

        checks.Add(new StartupCheck(
            "charge_caps_on_tick_600_not_300",
            chargeAt300 == 300 &&
            chargeAt599 == 599 &&
            chargeAt600 == 600 &&
            chargeAt601 == 600 &&
            lab.CursorTools.SwingState == ChargedSwingState.Charging,
            $"t300={chargeAt300} t599={chargeAt599} t600={chargeAt600} " +
            $"t601={chargeAt601} state={lab.CursorTools.SwingState}"));

        float authoredShake = lab.CursorTools.ActiveProfile!.Swing!.ShakeMaxAmplitudePx;
        checks.Add(new StartupCheck(
            "charge_shake_amplitude_ramps_and_caps_at_five_seconds",
            shakeAt300 > 0.0f &&
            shakeAt300 < shakeAt599 &&
            shakeAt599 < shakeAt600 &&
            Mathf.IsEqualApprox(shakeAt600, authoredShake) &&
            Mathf.IsEqualApprox(shakeAt601, authoredShake) &&
            bat.VisualOffset2D.IsFinite(),
            $"a300={shakeAt300:F3} a599={shakeAt599:F3} " +
            $"a600={shakeAt600:F3} a601={shakeAt601:F3} max={authoredShake:F3} " +
            $"offset={bat.VisualOffset2D}"));

        SwingToolProfile authoredSwing = authoredProfile.Swing!;
        bool earlyGlintVisible =
            sourceGlintAt120 &&
            (lab.Mode != PresentationMode.Mii3D || counterpartGlintAt120);
        bool middleGlintVisible =
            sourceGlintAt360 &&
            (lab.Mode != PresentationMode.Mii3D || counterpartGlintAt360);
        checks.Add(new StartupCheck(
            "charge_shows_small_medium_and_large_tip_glimmers",
            chargeCompletedEdges == 1 &&
            chargeAt120 == 120 &&
            chargeAt360 == 360 &&
            glintStartsAt120 == 1 &&
            glintStartsAt360 == 2 &&
            bat.ChargeGlintStarts == 3 &&
            earlyGlintVisible &&
            middleGlintVisible &&
            glintWasVisible &&
            Mathf.IsEqualApprox(glintSizeAt120, authoredSwing.OneSecondGlintSizePx) &&
            Mathf.IsEqualApprox(glintSizeAt360, authoredSwing.ThreeSecondGlintSizePx) &&
            Mathf.IsEqualApprox(bat.VisualGlintSizePx, authoredSwing.FiveSecondGlintSizePx) &&
            glintSizeAt120 < glintSizeAt360 &&
            glintSizeAt360 < bat.VisualGlintSizePx &&
            bat.VisualGlintLocalPosition.IsEqualApprox(new Vector2(0.0f, bat.Length * -0.5f)),
            $"edges={chargeCompletedEdges} starts=({glintStartsAt120}," +
            $"{glintStartsAt360},{bat.ChargeGlintStarts}) sizes=(" +
            $"{glintSizeAt120:F1},{glintSizeAt360:F1},{bat.VisualGlintSizePx:F1}) " +
            $"visible=({earlyGlintVisible},{middleGlintVisible},{glintWasVisible}) " +
            $"mode={lab.Mode} " +
            $"tip={bat.VisualGlintLocalPosition}"));

        // ---- cursor travel alone picks the side, through the release edge ----
        // A significant right drag followed by a significant left drag must use
        // the latter. Sub-threshold hand jitter after it must not change the aim.
        lab.CursorTools.MoveCursor(lab.CursorTools.Cursor + new Vector2(12.0f, 0.0f));
        await Ticks(tree, 1);
        bool sawRight = lab.CursorTools.SwingDirectionSign == 1;
        lab.CursorTools.MoveCursor(lab.CursorTools.Cursor + new Vector2(-24.0f, 0.0f));
        await Ticks(tree, 1);
        bool sawLeft = lab.CursorTools.SwingDirectionSign == -1;
        for (int jitter = 0; jitter < 4; jitter++)
        {
            lab.CursorTools.MoveCursor(lab.CursorTools.Cursor + new Vector2(5.0f, 0.0f));
            await Ticks(tree, 1);
            lab.CursorTools.MoveCursor(lab.CursorTools.Cursor + new Vector2(-5.0f, 0.0f));
            await Ticks(tree, 1);
        }
        bool jitterHeldLeft = lab.CursorTools.SwingDirectionSign == -1;
        await Ticks(tree, 90);
        float leftLeanDegrees = Mathf.RadToDeg(Mathf.Wrap(
            bat.GlobalRotation, -Mathf.Pi, Mathf.Pi));

        lab.CursorTools.SetChargeHeld(false);

        await Ticks(tree, 1);
        int firstEpoch = lab.CursorTools.SwingEpoch;
        bool firstSwingReleased = lab.CursorTools.SwingState == ChargedSwingState.Swinging &&
                                  firstReleasedDirection == -1;
        Vector2 firstPivot = lab.CursorTools.LatchedSwingPivot;
        float firstReleasedCharge = lab.CursorTools.ReleasedSwingCharge;
        lab.CursorTools.MoveCursor(lab.CursorTools.Cursor + new Vector2(30.0f, 0.0f));
        await Ticks(tree, 1);
        bool firstDirectionLocked = lab.CursorTools.SwingDirectionSign == -1 &&
                                    lab.CursorTools.SwingEpoch == firstEpoch &&
                                    lab.CursorTools.LatchedSwingPivot.DistanceTo(firstPivot) <= 0.001f &&
                                    Mathf.IsEqualApprox(
                                        lab.CursorTools.ReleasedSwingCharge,
                                        firstReleasedCharge);
        SwingMeasurement fullSwing = await MeasureSwing(tree, lab, bat);

        checks.Add(new StartupCheck(
            "dragging_right_then_left_swings_left",
            sawRight && sawLeft && firstSwingReleased,
            $"saw_right={sawRight} saw_left={sawLeft} released={firstSwingReleased} " +
            $"direction={firstReleasedDirection} epoch={firstEpoch}"));
        checks.Add(new StartupCheck(
            "sub_threshold_jitter_does_not_flip_the_direction",
            jitterHeldLeft,
            $"threshold={lab.CursorTools.ActiveProfile!.Swing!.DirectionTravelThreshold:F1} " +
            $"direction_before_release={firstReleasedDirection}"));
        checks.Add(new StartupCheck(
            "pointer_motion_after_release_cannot_change_pivot_direction_or_charge",
            firstDirectionLocked,
            $"direction={lab.CursorTools.SwingDirectionSign} epoch={lab.CursorTools.SwingEpoch} " +
            $"pivot={firstPivot}->{lab.CursorTools.LatchedSwingPivot} " +
            $"charge={firstReleasedCharge:F3}->{lab.CursorTools.ReleasedSwingCharge:F3}"));

        bool recoveredFromFirst = await WaitForState(
            tree, lab.CursorTools, ChargedSwingState.Gripped, 120);

        // Mirror the input. The charge pose must lean the other way and the
        // release must commit +1, proving the first result was not hard-coded.
        lab.CursorTools.SetChargeHeld(true);
        await Ticks(tree, 1);
        lab.CursorTools.MoveCursor(lab.CursorTools.Cursor + new Vector2(-12.0f, 0.0f));
        await Ticks(tree, 1);
        lab.CursorTools.MoveCursor(lab.CursorTools.Cursor + new Vector2(24.0f, 0.0f));
        await Ticks(tree, 1);
        await Ticks(tree, 300);
        float rightLeanDegrees = Mathf.RadToDeg(Mathf.Wrap(
            bat.GlobalRotation, -Mathf.Pi, Mathf.Pi));
        lab.CursorTools.SetChargeHeld(false);
        await Ticks(tree, 1);
        bool secondSwingReleased = lab.CursorTools.SwingState == ChargedSwingState.Swinging &&
                                   secondReleasedDirection == 1;
        int secondEpoch = lab.CursorTools.SwingEpoch;
        lab.CursorTools.MoveCursor(lab.CursorTools.Cursor + new Vector2(-30.0f, 0.0f));
        await Ticks(tree, 1);
        bool secondDirectionLocked = lab.CursorTools.SwingDirectionSign == 1 &&
                                     lab.CursorTools.SwingEpoch == secondEpoch;
        SwingMeasurement midSwing = await MeasureSwing(tree, lab, bat);

        checks.Add(new StartupCheck(
            "mirrored_drags_produce_mirrored_swings",
            recoveredFromFirst &&
            firstSwingReleased &&
            secondSwingReleased &&
            leftLeanDegrees > 10.0f &&
            rightLeanDegrees < -10.0f &&
            Mathf.Abs(leftLeanDegrees + rightLeanDegrees) <= 12.0f &&
            secondDirectionLocked,
            $"first={firstReleasedDirection} second={secondReleasedDirection} " +
            $"left_lean={leftLeanDegrees:F1}deg right_lean={rightLeanDegrees:F1}deg " +
            $"locked={secondDirectionLocked}"));

        bool recoveredFromSecond = await WaitForState(
            tree, lab.CursorTools, ChargedSwingState.Gripped, 120);

        // The RMB-tap swing is a real minimum-charge arc. Measure it separately
        // so charge bands cannot all collapse to one saturated speed.
        bool settledBeforeLow = await WaitForBatSettled(tree, lab, bat, 600);
        lab.CursorTools.SetChargeHeld(true);
        await Ticks(tree, 1);
        lab.CursorTools.SetChargeHeld(false);
        await Ticks(tree, 1);
        SwingMeasurement lowSwing = await MeasureSwing(tree, lab, bat);
        bool recoveredFromLow = await WaitForState(
            tree, lab.CursorTools, ChargedSwingState.Gripped, 120);

        checks.Add(new StartupCheck(
            "charged_swing_tip_speed_tracks_non_overlapping_targets",
            fullSwing.SawSwing &&
            midSwing.SawSwing &&
            lowSwing.SawSwing &&
            settledBeforeLow &&
            lowSwing.PeakTipSpeed < midSwing.PeakTipSpeed &&
            midSwing.PeakTipSpeed < fullSwing.PeakTipSpeed &&
            WithinFraction(lowSwing.PeakTipSpeed, lowSwing.TargetTipSpeed, 0.20f) &&
            WithinFraction(midSwing.PeakTipSpeed, midSwing.TargetTipSpeed, 0.20f) &&
            WithinFraction(fullSwing.PeakTipSpeed, fullSwing.TargetTipSpeed, 0.20f),
            $"settled={settledBeforeLow} " +
            $"low={lowSwing.PeakTipSpeed:F0}/{lowSwing.TargetTipSpeed:F0} " +
            $"mid={midSwing.PeakTipSpeed:F0}/{midSwing.TargetTipSpeed:F0} " +
            $"full={fullSwing.PeakTipSpeed:F0}/{fullSwing.TargetTipSpeed:F0}"));

        checks.Add(new StartupCheck(
            "full_charge_uses_the_owner_boosted_physical_speed",
            Mathf.IsEqualApprox(fullSwing.TargetTipSpeed, 6000.0f) &&
            fullSwing.PeakTipSpeed > midSwing.PeakTipSpeed,
            $"full={fullSwing.PeakTipSpeed:F0}/{fullSwing.TargetTipSpeed:F0} " +
            $"mid={midSwing.PeakTipSpeed:F0}/{midSwing.TargetTipSpeed:F0}"));

        checks.Add(new StartupCheck(
            "the_handle_pivot_holds_through_a_full_charge_swing",
            fullSwing.SawSwing &&
            fullSwing.SawCastShapeCcd &&
            fullSwing.MaximumPivotDrift <= 18.0f,
            $"drift={fullSwing.MaximumPivotDrift:F2}px ccd={fullSwing.SawCastShapeCcd} " +
            $"peak_omega={fullSwing.PeakAngularSpeed:F1}rad/s"));

        // ---- releasing the grip is the safe charge cancel ----
        int epochBeforeCancel = lab.CursorTools.SwingEpoch;
        int releasesBeforeCancel = swingReleaseEdges;
        int cancelPainEvents = 0;
        void OnCancelImpact(AcceptedImpact impact)
        {
            if (impact.ContentId == ContentIds.ToolBaseballBat && impact.Pain > 0.0f)
                cancelPainEvents++;
        }

        lab.Pipeline.ImpactAccepted += OnCancelImpact;
        lab.CursorTools.SetChargeHeld(true);
        await Ticks(tree, 1);
        await Ticks(tree, 60);
        bool cancelWasCharging = lab.CursorTools.SwingState == ChargedSwingState.Charging &&
                                 lab.CursorTools.SwingChargeTicks == 60;
        lab.CursorTools.SetGrip(false);
        await Ticks(tree, 2);
        lab.CursorTools.SetChargeHeld(false);
        lab.Pipeline.ImpactAccepted -= OnCancelImpact;
        bool returnedToFollow = lab.CursorTools.SwingState == ChargedSwingState.Follow;

        checks.Add(new StartupCheck(
            "releasing_the_grip_cancels_without_a_swing_or_pain",
            recoveredFromSecond &&
            recoveredFromLow &&
            cancelWasCharging &&
            returnedToFollow &&
            lab.CursorTools.SwingEpoch == epochBeforeCancel &&
            swingReleaseEdges == releasesBeforeCancel &&
            cancelPainEvents == 0,
            $"charging={cancelWasCharging} state={lab.CursorTools.SwingState} " +
            $"epoch={epochBeforeCancel}->{lab.CursorTools.SwingEpoch} " +
            $"releases={releasesBeforeCancel}->{swingReleaseEdges} pain={cancelPainEvents}"));

        lab.CursorTools.ChargeCompleted -= OnChargeCompleted;
        lab.CursorTools.SwingReleased -= OnSwingReleased;

        // ---- letting go hands the bat back to the weak free swing ----

        Vector2 followTarget = openSpace + new Vector2(120.0f, 0.0f);
        lab.CursorTools.MoveCursor(followTarget);
        await Ticks(tree, SettleTicks);
        float followError = bat.GlobalPosition.DistanceTo(lab.CursorTools.Cursor);

        checks.Add(new StartupCheck(
            "letting_go_returns_to_weak_follow",
            returnedToFollow &&
            lab.CursorTools.SwingState == ChargedSwingState.Follow &&
            // Back to a centre tether: the origin sits on the cursor again.
            followError <= 16.0f,
            $"state={lab.CursorTools.SwingState} centre_to_cursor={followError:F1}px"));

        // ---- the glove is untouched by any of this ----
        lab.Pipeline.SelectTool(ToolId.BoxingGlove);
        lab.CursorTools.MoveCursor(openSpace + new Vector2(-260.0f, 0.0f));
        await Ticks(tree, 20);
        CursorToolBody? glove = lab.CursorTools.Body;

        // Grip and charge are pressed deliberately: a tool that authors no swing
        // must ignore them outright rather than half-entering a state.
        lab.CursorTools.SetGrip(true);
        lab.CursorTools.SetChargeHeld(true);
        await Ticks(tree, 30);
        bool gloveIgnoredGrip = lab.CursorTools.SwingState == ChargedSwingState.Follow &&
                                !lab.CursorTools.IsSwingCapable;

        AcceptedImpact? gloveImpact = null;
        void OnGlove(AcceptedImpact impact)
        {
            if (gloveImpact is null && impact.ContentId == ContentIds.ToolBoxingGlove)
                gloveImpact = impact;
        }

        lab.Pipeline.ImpactAccepted += OnGlove;
        Vector2 gloveWindUp = lab.Buddy.Rig.Torso.GlobalPosition + new Vector2(-300.0f, 0.0f);
        lab.CursorTools.MoveCursor(gloveWindUp);
        await Ticks(tree, 30);
        await DragCursor(tree, lab, gloveWindUp, Vector2.Right, BenchmarkSwingSpeed, 60,
            () => gloveImpact is not null);
        await Ticks(tree, 30);
        lab.Pipeline.ImpactAccepted -= OnGlove;
        lab.CursorTools.SetGrip(false);
        lab.CursorTools.SetChargeHeld(false);

        checks.Add(new StartupCheck(
            "glove_response_is_unchanged_by_the_swing_mechanism",
            gloveIgnoredGrip &&
            glove is not null &&
            !glove.IsElongated &&
            gloveImpact is { Pain: > 0.0f } gloveHit &&
            gloveHit.SwingEpoch == 0 &&
            gloveHit.SwingReleasedTick == 0L,
            $"ignored_grip={gloveIgnoredGrip} elongated={glove?.IsElongated} " +
            $"pain={gloveImpact?.Pain:F2} impulse={gloveImpact?.Impulse:F1} " +
            $"epoch={gloveImpact?.SwingEpoch}"));

        messages.Add(
            $"free_swing_pain={freeSwing?.Pain:F2} benchmark_peak={benchmarkPeak:F0} " +
            $"flick_peak={flickPeak:F0} barrel_from_up={barrelDegrees:F2}deg " +
            $"charge=({chargeAt599},{chargeAt600},{chargeAt601}) " +
            $"directions=({firstReleasedDirection},{secondReleasedDirection}) " +
            $"tip_speed=({lowSwing.PeakTipSpeed:F0},{midSwing.PeakTipSpeed:F0}," +
            $"{fullSwing.PeakTipSpeed:F0})");

        // Contact envelopes use fresh isolated labs. Keeping the open-air/glove
        // host alive would put two complete rigs in one World2D and let them
        // become accidental collision partners.
        float weakFreeSwingImpulse = freeSwing?.RawImpulse ?? 0.0f;
        lab.QueueFree();
        await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);

        BuddySwingProbe lowContact = await RunBuddySwingProbe(tree, 0, 0.65f);
        BuddySwingProbe midContact = await RunBuddySwingProbe(tree, 300, 0.65f);
        BuddySwingProbe fullContact = await RunBuddySwingProbe(tree, 600, 0.65f);
        BuddySwingProbe offsetFullContact =
            await RunBuddySwingProbe(tree, 600, 0.65f, radialOffsetRadii: 1.0f);
        BuddySwingProbe canceledFullContact =
            await RunBuddySwingProbe(tree, 600, 0.65f, cancelHitLag: true);
        LooseObjectSwingProbe lowObject = await RunLooseObjectSwingProbe(
            tree, 0, TipContactRadiusFraction);
        LooseObjectSwingProbe midObject = await RunLooseObjectSwingProbe(
            tree, 300, TipContactRadiusFraction);
        LooseObjectSwingProbe fullTipObject = await RunLooseObjectSwingProbe(
            tree, 600, TipContactRadiusFraction);
        LooseObjectSwingProbe underCapObject = await RunLooseObjectSwingProbe(
            tree, 599, TipContactRadiusFraction);
        LooseObjectSwingProbe fullBarrelObject =
            await RunLooseObjectSwingProbe(tree, 600, contactRadiusFraction: 0.66f);
        LooseObjectSwingProbe fullHandleObject =
            await RunLooseObjectSwingProbe(tree, 600, contactRadiusFraction: 0.24f);
        GrazeThenHitProbe grazeThenHit = await RunGrazeThenHitProbe(tree);
        WhiffRecoveryProbe whiffRecovery = await RunWhiffRecoveryProbe(tree);

        checks.Add(new StartupCheck(
            "charged_swing_contact_probe_reaches_the_buddy",
            lowContact.PositiveImpactCount == 1 &&
            midContact.PositiveImpactCount == 1 &&
            fullContact.PositiveImpactCount == 1,
            $"positive=({lowContact.PositiveImpactCount}," +
            $"{midContact.PositiveImpactCount},{fullContact.PositiveImpactCount}) " +
            $"impulse=({lowContact.MaximumImpulse:F1},{midContact.MaximumImpulse:F1}," +
            $"{fullContact.MaximumImpulse:F1})"));

        checks.Add(new StartupCheck(
            "charge_scales_measured_impulse_by_laboratory_ratios",
            midContact.MaximumImpulse >=
                lowContact.MaximumImpulse * MinimumMidToLowImpulseRatio &&
            fullContact.MaximumImpulse >=
                midContact.MaximumImpulse * MinimumFullToMidImpulseRatio,
            $"impulse=({lowContact.MaximumImpulse:F1},{midContact.MaximumImpulse:F1}," +
            $"{fullContact.MaximumImpulse:F1}) ratios=(" +
            $"{Ratio(midContact.MaximumImpulse, lowContact.MaximumImpulse):F2}," +
            $"{Ratio(fullContact.MaximumImpulse, midContact.MaximumImpulse):F2}) " +
            $"minimum=({MinimumMidToLowImpulseRatio:F2}," +
            $"{MinimumFullToMidImpulseRatio:F2})"));

        checks.Add(new StartupCheck(
            "charge_scales_post_hit_whole_buddy_travel_by_laboratory_ratios",
            midContact.MaximumTravel >=
                lowContact.MaximumTravel * MinimumMidToLowTravelRatio &&
            fullContact.MaximumTravel >=
                midContact.MaximumTravel * MinimumFullToMidTravelRatio,
            $"travel=({lowContact.MaximumTravel:F2},{midContact.MaximumTravel:F2}," +
            $"{fullContact.MaximumTravel:F2}) ratios=(" +
            $"{Ratio(midContact.MaximumTravel, lowContact.MaximumTravel):F2}," +
            $"{Ratio(fullContact.MaximumTravel, midContact.MaximumTravel):F2}) " +
            $"minimum=({MinimumMidToLowTravelRatio:F2}," +
            $"{MinimumFullToMidTravelRatio:F2})"));

        checks.Add(new StartupCheck(
            "weak_free_swing_cannot_match_full_charge_impulse",
            weakFreeSwingImpulse > 0.0f &&
            fullContact.MaximumImpulse >= weakFreeSwingImpulse * 2.5f,
            $"weak={weakFreeSwingImpulse:F1} full={fullContact.MaximumImpulse:F1} " +
            $"ratio={Ratio(fullContact.MaximumImpulse, weakFreeSwingImpulse):F2}"));

        float launchAngle = Mathf.RadToDeg(Mathf.Atan2(
            -fullContact.PeakWholeBuddyVelocity.Y,
            fullContact.PeakWholeBuddyVelocity.X));
        checks.Add(new StartupCheck(
            "full_charge_launches_the_buddy_up_and_away",
            fullContact.PeakWholeBuddyVelocity.X > 0.0f &&
            fullContact.PeakWholeBuddyVelocity.Y < 0.0f &&
            launchAngle >= 20.0f &&
            launchAngle <= 55.0f,
            $"velocity={fullContact.PeakWholeBuddyVelocity} angle={launchAngle:F1}deg " +
            "envelope=[20,55]deg"));

        checks.Add(new StartupCheck(
            "one_home_run_epoch_scores_once_across_multiple_buddy_parts",
            fullContact.SwingEpoch > 0 &&
            fullContact.PositiveImpactCount == 1 &&
            fullContact.BatEpisodeCount >= 2 &&
            fullContact.DistinctEpisodeParts >= 2,
            $"epoch={fullContact.SwingEpoch} positive={fullContact.PositiveImpactCount} " +
            $"episodes={fullContact.BatEpisodeCount} " +
            $"parts={fullContact.DistinctEpisodeParts}"));

        checks.Add(new StartupCheck(
            "home_run_contact_emits_one_small_impact_burst",
            fullContact.HomeRunBurstCount == 1 &&
            fullContact.HomeRunBurstMatchedContact &&
            fullContact.HomeRunBurstSizePx > 0.0f &&
            fullContact.HomeRunBurstSizePx <= 24.0f,
            $"bursts={fullContact.HomeRunBurstCount} " +
            $"matched={fullContact.HomeRunBurstMatchedContact} " +
            $"size={fullContact.HomeRunBurstSizePx:F1}px"));

        checks.Add(new StartupCheck(
            "uncharged_rmb_tap_stays_modest",
            lowContact.MaximumImpulse > 0.0f &&
            lowContact.MaximumImpulse <= fullContact.MaximumImpulse * 0.30f,
            $"tap={lowContact.MaximumImpulse:F1} full={fullContact.MaximumImpulse:F1} " +
            $"fraction={Ratio(lowContact.MaximumImpulse, fullContact.MaximumImpulse):F2}"));

        checks.Add(new StartupCheck(
            "point_blank_and_one_radius_offset_full_charge_do_not_tunnel",
            fullContact.PositiveImpactCount == 1 &&
            offsetFullContact.PositiveImpactCount == 1 &&
            fullContact.SawCastShapeCcd &&
            offsetFullContact.SawCastShapeCcd,
            $"point_blank_positive={fullContact.PositiveImpactCount} " +
            $"offset_positive={offsetFullContact.PositiveImpactCount} " +
            $"ccd=({fullContact.SawCastShapeCcd},{offsetFullContact.SawCastShapeCcd}) " +
            $"offset_impulse={offsetFullContact.MaximumImpulse:F1}"));

        checks.Add(new StartupCheck(
            "higher_charge_sends_a_controlled_loose_object_farther",
            lowObject.SawContact &&
            midObject.SawContact &&
            fullTipObject.SawContact &&
            midObject.MaximumTravel >= lowObject.MaximumTravel * 1.15f &&
            fullTipObject.MaximumTravel >= midObject.MaximumTravel * 1.15f,
            $"travel=({lowObject.MaximumTravel:F1},{midObject.MaximumTravel:F1}," +
            $"{fullTipObject.MaximumTravel:F1}) ratios=(" +
            $"{Ratio(midObject.MaximumTravel, lowObject.MaximumTravel):F2}," +
            $"{Ratio(fullTipObject.MaximumTravel, midObject.MaximumTravel):F2})"));

        checks.Add(new StartupCheck(
            "the_real_solver_makes_the_bat_tip_the_strongest_contact",
            fullTipObject.SawContact &&
            fullTipObject.MaximumTravel > fullBarrelObject.MaximumTravel &&
            fullTipObject.MaximumTravel > fullHandleObject.MaximumTravel,
            $"handle={fullHandleObject.MaximumTravel:F1}px " +
            $"barrel={fullBarrelObject.MaximumTravel:F1}px " +
            $"tip={fullTipObject.MaximumTravel:F1}px " +
            $"peak_speed=({fullHandleObject.PeakSpeed:F1}," +
            $"{fullBarrelObject.PeakSpeed:F1},{fullTipObject.PeakSpeed:F1})"));

        checks.Add(new StartupCheck(
            "a_zero_pain_graze_does_not_consume_the_home_run_epoch",
            grazeThenHit.EpisodeCount >= 2 &&
            grazeThenHit.PositiveImpactCount == 1 &&
            grazeThenHit.FirstEpisodeImpulse >= 10.0f &&
            grazeThenHit.FirstEpisodeImpulse < grazeThenHit.CurveFloorImpulse &&
            grazeThenHit.AcceptedEpoch == grazeThenHit.SwingEpoch,
            $"episodes={grazeThenHit.EpisodeCount} positive=" +
            $"{grazeThenHit.PositiveImpactCount} first_impulse=" +
            $"{grazeThenHit.FirstEpisodeImpulse:F1} floor=" +
            $"{grazeThenHit.CurveFloorImpulse:F1} accepted_epoch=" +
            $"{grazeThenHit.AcceptedEpoch}/{grazeThenHit.SwingEpoch}"));

        checks.Add(new StartupCheck(
            "a_charged_whiff_cannot_reuse_stale_charge_on_recovery_contact",
            whiffRecovery.SawSwing &&
            whiffRecovery.SawRecovery &&
            whiffRecovery.SwingEpoch > 0 &&
            whiffRecovery.WhiffPositiveImpacts == 0 &&
            whiffRecovery.RestingContactEpisodes > 0 &&
            whiffRecovery.RestingPositiveImpacts == 0,
            $"swing={whiffRecovery.SawSwing} recovery={whiffRecovery.SawRecovery} " +
            $"epoch={whiffRecovery.SwingEpoch} whiff_positive=" +
            $"{whiffRecovery.WhiffPositiveImpacts} resting_episodes=" +
            $"{whiffRecovery.RestingContactEpisodes} resting_positive=" +
            $"{whiffRecovery.RestingPositiveImpacts}"));

        checks.Add(new StartupCheck(
            "charge_scales_hit_lag_ticks",
            lowContact.HitLagDurationTicks == 6 &&
            fullContact.HitLagDurationTicks == 60 &&
            lowContact.FrozenFrames == 6 &&
            fullContact.FrozenFrames == 60 &&
            lowContact.HitLagTriggerCount == 1 &&
            fullContact.HitLagTriggerCount == 1,
            $"duration=({lowContact.HitLagDurationTicks}," +
            $"{fullContact.HitLagDurationTicks}) frozen=(" +
            $"{lowContact.FrozenFrames},{fullContact.FrozenFrames}) " +
            $"triggers=({lowContact.HitLagTriggerCount}," +
            $"{fullContact.HitLagTriggerCount})"));

        checks.Add(new StartupCheck(
            "launch_velocity_resumes_after_hit_lag",
            fullContact.VelocityHeldDuringHitLag &&
            fullContact.LaunchResumedAfterHitLag,
            $"held={fullContact.VelocityHeldDuringHitLag} " +
            $"resumed={fullContact.LaunchResumedAfterHitLag} " +
            $"travel={fullContact.MaximumTravel:F2}"));

        checks.Add(new StartupCheck(
            "every_loose_object_stops_during_hit_lag",
            fullTipObject.AllLooseObjectsHeldDuringHitLag &&
            fullTipObject.UnrelatedObjectResumedAfterHitLag,
            $"held={fullTipObject.AllLooseObjectsHeldDuringHitLag} " +
            $"unrelated_resumed={fullTipObject.UnrelatedObjectResumedAfterHitLag}"));

        checks.Add(new StartupCheck(
            "knockout_and_recovery_timers_do_not_burn_during_the_freeze",
            fullContact.GameplayTimersHeldDuringHitLag,
            $"timers_held={fullContact.GameplayTimersHeldDuringHitLag} " +
            $"duration={fullContact.HitLagDurationTicks}"));

        checks.Add(new StartupCheck(
            "full_charge_object_hit_freezes_but_partial_charge_does_not",
            fullTipObject.HitLagDurationTicks == 60 &&
            fullTipObject.HitLagTriggerCount == 1 &&
            underCapObject.SawContact &&
            underCapObject.HitLagTriggerCount == 0,
            $"full=({fullTipObject.HitLagTriggerCount}," +
            $"{fullTipObject.HitLagDurationTicks}) under_cap=(" +
            $"{underCapObject.HitLagTriggerCount}," +
            $"{underCapObject.HitLagDurationTicks})"));

        checks.Add(new StartupCheck(
            "home_run_freeze_suppresses_the_global_slow_time",
            fullContact.GlobalSlowTimeSuppressed,
            $"suppressed={fullContact.GlobalSlowTimeSuppressed} " +
            $"hit_stop={fullContact.SawImpactHitStop}"));

        checks.Add(new StartupCheck(
            "cancel_resumes_the_tick_exactly_once",
            canceledFullContact.HitLagCancelCount == 1 &&
            canceledFullContact.CancelResumedRouting,
            $"cancel_count={canceledFullContact.HitLagCancelCount} " +
            $"resumed={canceledFullContact.CancelResumedRouting}"));

        checks.Add(new StartupCheck(
            "struck_part_shakes_during_freeze_only",
            fullContact.MaximumVictimShake > 0.05f &&
            fullContact.MaximumOtherPartShake <= 0.001f &&
            fullContact.ShakeAfterHitLag <= 0.001f &&
            fullContact.PoseStayedTrackingDuringHitLag,
            $"victim={fullContact.MaximumVictimShake:F3}px " +
            $"other={fullContact.MaximumOtherPartShake:F3}px " +
            $"after={fullContact.ShakeAfterHitLag:F3}px " +
            $"tracking={fullContact.PoseStayedTrackingDuringHitLag}"));

        checks.Add(new StartupCheck(
            "placeholder_sounds_follow_semantic_swing_edges",
            fullContact.AudioPlayCount == 4 &&
            fullContact.AudioChargeStartedCount == 1 &&
            fullContact.AudioChargeCompletedCount == 1 &&
            fullContact.AudioSwingReleasedCount == 1 &&
            fullContact.AudioHomeRunImpactCount == 1 &&
            lowContact.AudioPlayCount == 3 &&
            lowContact.AudioChargeStartedCount == 1 &&
            lowContact.AudioChargeCompletedCount == 0 &&
            lowContact.AudioSwingReleasedCount == 1 &&
            lowContact.AudioHomeRunImpactCount == 1,
            $"full=({fullContact.AudioPlayCount}," +
            $"{fullContact.AudioChargeStartedCount}," +
            $"{fullContact.AudioChargeCompletedCount}," +
            $"{fullContact.AudioSwingReleasedCount}," +
            $"{fullContact.AudioHomeRunImpactCount}) low=(" +
            $"{lowContact.AudioPlayCount},{lowContact.AudioChargeStartedCount}," +
            $"{lowContact.AudioChargeCompletedCount}," +
            $"{lowContact.AudioSwingReleasedCount}," +
            $"{lowContact.AudioHomeRunImpactCount})"));

        checks.Add(new StartupCheck(
            "charged_whiff_has_no_home_run_impact_sound",
            whiffRecovery.AudioPlayCount == 3 &&
            whiffRecovery.AudioChargeStartedCount == 1 &&
            whiffRecovery.AudioChargeCompletedCount == 1 &&
            whiffRecovery.AudioSwingReleasedCount == 1 &&
            whiffRecovery.AudioHomeRunImpactCount == 0,
            $"plays={whiffRecovery.AudioPlayCount} cues=(" +
            $"{whiffRecovery.AudioChargeStartedCount}," +
            $"{whiffRecovery.AudioChargeCompletedCount}," +
            $"{whiffRecovery.AudioSwingReleasedCount}," +
            $"{whiffRecovery.AudioHomeRunImpactCount})"));

        checks.Add(new StartupCheck(
            "placeholder_audio_is_procedural_and_bus_scoped",
            fullContact.AudioGeneratedStreamCount == 4 &&
            fullContact.AudioStreamIsGeneratedPcm &&
            fullContact.AudioOwnsExactlyOnePlayer &&
            fullContact.AudioBusExists &&
            fullContact.AudioMasterVolumeUnchanged &&
            fullContact.AudioVolumeMatchesProfile,
            $"streams={fullContact.AudioGeneratedStreamCount} " +
            $"pcm={fullContact.AudioStreamIsGeneratedPcm} " +
            $"one_player={fullContact.AudioOwnsExactlyOnePlayer} " +
            $"bus_exists={fullContact.AudioBusExists} " +
            $"master_unchanged={fullContact.AudioMasterVolumeUnchanged} " +
            $"profile_volume={fullContact.AudioVolumeMatchesProfile}"));

        messages.Add(
            $"task_d_contact impulse=({lowContact.MaximumImpulse:F1}," +
            $"{midContact.MaximumImpulse:F1},{fullContact.MaximumImpulse:F1}) " +
            $"travel=({lowContact.MaximumTravel:F2},{midContact.MaximumTravel:F2}," +
            $"{fullContact.MaximumTravel:F2}) weak={weakFreeSwingImpulse:F1} " +
            $"speed=({lowContact.PeakWholeBuddyVelocity.Length():F1}," +
            $"{midContact.PeakWholeBuddyVelocity.Length():F1}," +
            $"{fullContact.PeakWholeBuddyVelocity.Length():F1}) " +
            $"full_velocity=({fullContact.PeakWholeBuddyVelocity.X:F1}," +
            $"{fullContact.PeakWholeBuddyVelocity.Y:F1}) " +
            $"episodes={fullContact.BatEpisodeCount} parts={fullContact.DistinctEpisodeParts} " +
            $"saw=({lowContact.SawSwing},{midContact.SawSwing},{fullContact.SawSwing}) " +
            $"direction=({lowContact.DirectionSign},{midContact.DirectionSign}," +
            $"{fullContact.DirectionSign}) tip_distance=({lowContact.MinimumTipDistance:F1}," +
            $"{midContact.MinimumTipDistance:F1},{fullContact.MinimumTipDistance:F1}) " +
            $"full_episode_impulse=({fullContact.FirstEpisodeImpulse:F1}," +
            $"{fullContact.MaximumEpisodeImpulse:F1})");
        messages.Add(
            $"task_d_object travel=({lowObject.MaximumTravel:F1}," +
            $"{midObject.MaximumTravel:F1},{fullTipObject.MaximumTravel:F1}) " +
            $"tip_barrel_handle=({fullTipObject.MaximumTravel:F1}," +
            $"{fullBarrelObject.MaximumTravel:F1},{fullHandleObject.MaximumTravel:F1})");
        messages.Add(
            $"task_e2_audio full=({fullContact.AudioChargeStartedCount}," +
            $"{fullContact.AudioChargeCompletedCount}," +
            $"{fullContact.AudioSwingReleasedCount}," +
            $"{fullContact.AudioHomeRunImpactCount}) whiff_impact=" +
            $"{whiffRecovery.AudioHomeRunImpactCount} streams=" +
            $"{fullContact.AudioGeneratedStreamCount}");
        return Finish(checks, messages);
    }

    /// <summary>
    /// Drive the cursor across open air at a speed and report the fastest the
    /// bat's own body actually travelled. This is the direct measurement of the
    /// anchor rate limiter: the pointer's speed is an input, the body's speed is
    /// the consequence, and only the second one can turn into an impulse.
    /// </summary>
    private static async Task<float> SweepAndMeasurePeakSpeed(
        SceneTree tree,
        BuddyLab lab,
        CursorToolBody bat,
        Vector2 openSpace,
        float speed)
    {
        Vector2 start = openSpace + new Vector2(-320.0f, 0.0f);
        lab.CursorTools.MoveCursor(start);
        await Ticks(tree, 60);

        float peak = 0.0f;
        float step = speed / Engine.PhysicsTicksPerSecond;
        Vector2 point = start;
        for (int tick = 0; tick < 40; tick++)
        {
            point += new Vector2(step, 0.0f);
            lab.CursorTools.MoveCursor(point);
            await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
            if (GodotObject.IsInstanceValid(bat))
            {
                peak = Mathf.Max(peak, bat.LinearVelocity.Length());
            }
        }

        return peak;
    }

    /// <summary>Move the cursor at a fixed speed for a number of ticks, or until a stop condition.</summary>
    private static async Task DragCursor(
        SceneTree tree,
        BuddyLab lab,
        Vector2 from,
        Vector2 direction,
        float speed,
        int ticks,
        System.Func<bool>? stop)
    {
        float step = speed / Engine.PhysicsTicksPerSecond;
        Vector2 point = from;
        for (int tick = 0; tick < ticks; tick++)
        {
            if (stop is not null && stop())
            {
                return;
            }

            point += direction * step;
            lab.CursorTools.MoveCursor(point);
            await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
        }
    }

    /// <summary>Walk the cursor slowly to a point, so arriving is not itself a swing.</summary>
    private static async Task CreepCursor(SceneTree tree, BuddyLab lab, Vector2 target, int ticks)
    {
        Vector2 point = lab.CursorTools.Cursor;
        for (int tick = 0; tick < ticks; tick++)
        {
            point = point.MoveToward(target, 3.0f);
            lab.CursorTools.MoveCursor(point);
            await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
        }
    }

    /// <summary>
    /// The grip point in world space, derived from the collider exactly the way
    /// the controller derives it — local <c>(0, +Length/2 - Radius)</c>, the
    /// centre of the capsule's handle-end hemisphere.
    /// </summary>
    private static Vector2 HandlePoint(BuddyLab lab, CursorToolBody bat)
    {
        CursorToolProfile profile = lab.CursorTools.ActiveProfile!;
        return bat.GlobalPosition + profile.HandleLocalOffset.Rotated(bat.GlobalRotation);
    }

    private static async Task Ticks(SceneTree tree, int count)
    {
        for (int tick = 0; tick < count; tick++)
        {
            await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
        }
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
            {
                return true;
            }

            await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
        }

        return controller.SwingState == state;
    }

    private readonly record struct SwingMeasurement(
        bool SawSwing,
        bool SawCastShapeCcd,
        float PeakTipSpeed,
        float TargetTipSpeed,
        float PeakAngularSpeed,
        float MaximumPivotDrift);

    private static async Task<SwingMeasurement> MeasureSwing(
        SceneTree tree,
        BuddyLab lab,
        CursorToolBody bat)
    {
        bool saw = false;
        bool ccd = false;
        float peakTip = 0.0f;
        float peakAngular = 0.0f;
        float drift = 0.0f;
        float target = lab.CursorTools.CurrentSwingPlan.TargetTipSpeed;
        SwingPlan plan = lab.CursorTools.CurrentSwingPlan;
        Vector2 pivot = lab.CursorTools.LatchedSwingPivot;
        CursorToolProfile profile = lab.CursorTools.ActiveProfile!;

        for (int timeout = 0; timeout < 90; timeout++)
        {
            if (lab.CursorTools.SwingState != ChargedSwingState.Swinging)
            {
                break;
            }

            saw = true;
            ccd |= bat.ContinuousCd == RigidBody2D.CcdMode.CastShape;
            // The authored target is the tip speed *about the handle pivot*,
            // not absolute world translation. Pivot drift is asserted
            // independently below.
            int swingTick = lab.CursorTools.SwingTicksInState;
            if (swingTick >= plan.WindupTicks &&
                swingTick < plan.WindupTicks + plan.SweepTicks)
            {
                peakTip = Mathf.Max(
                    peakTip,
                    Mathf.Abs(bat.AngularVelocity) * profile.HandleToTipRadius);
            }
            peakAngular = Mathf.Max(peakAngular, Mathf.Abs(bat.AngularVelocity));
            Vector2 handle = bat.GlobalPosition +
                             profile.HandleLocalOffset.Rotated(bat.GlobalRotation);
            drift = Mathf.Max(drift, handle.DistanceTo(pivot));
            await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
        }

        return new SwingMeasurement(saw, ccd, peakTip, target, peakAngular, drift);
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
                             lab.CursorTools.ActiveProfile!.HandleLocalOffset.Rotated(
                                 bat.GlobalRotation);
            if (lab.CursorTools.SwingState == ChargedSwingState.Gripped &&
                Mathf.Abs(bat.AngularVelocity) <= 0.5f &&
                angle <= Mathf.DegToRad(3.0f) &&
                handle.DistanceTo(lab.CursorTools.Cursor) <= 8.0f)
            {
                return true;
            }

            await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
        }

        return false;
    }

    private readonly record struct BuddySwingProbe(
        int PositiveImpactCount,
        int BatEpisodeCount,
        int DistinctEpisodeParts,
        float MaximumImpulse,
        float MaximumTravel,
        Vector2 PeakWholeBuddyVelocity,
        int SwingEpoch,
        bool SawCastShapeCcd,
        bool SawSwing,
        int DirectionSign,
        float MinimumTipDistance,
        float FirstEpisodeImpulse,
        float MaximumEpisodeImpulse,
        int HitLagDurationTicks,
        int HitLagTriggerCount,
        int FrozenFrames,
        bool VelocityHeldDuringHitLag,
        bool LaunchResumedAfterHitLag,
        bool GameplayTimersHeldDuringHitLag,
        bool GlobalSlowTimeSuppressed,
        bool SawImpactHitStop,
        int HitLagCancelCount,
        bool CancelResumedRouting,
        float MaximumVictimShake,
        float MaximumOtherPartShake,
        float ShakeAfterHitLag,
        bool PoseStayedTrackingDuringHitLag,
        int AudioGeneratedStreamCount,
        int AudioPlayCount,
        int AudioChargeStartedCount,
        int AudioChargeCompletedCount,
        int AudioSwingReleasedCount,
        int AudioHomeRunImpactCount,
        bool AudioStreamIsGeneratedPcm,
        bool AudioOwnsExactlyOnePlayer,
        bool AudioBusExists,
        bool AudioMasterVolumeUnchanged,
        bool AudioVolumeMatchesProfile,
        int HomeRunBurstCount,
        bool HomeRunBurstMatchedContact,
        float HomeRunBurstSizePx);

    /// <summary>
    /// Release one real charged bat through the torso in an otherwise isolated
    /// production rig. The pivot is derived from the authored trajectory at the
    /// same authored late-sweep contact zone, so every charge band crosses the
    /// same point on the same part instead of being hand-placed on three
    /// different arcs.
    /// </summary>
    private static async Task<BuddySwingProbe> RunBuddySwingProbe(
        SceneTree tree,
        int chargeTicks,
        float sweepFraction = 0.5f,
        float radialOffsetRadii = 0.0f,
        bool cancelHitLag = false)
    {
        BuddyLab? lab = await ScenarioSteps.CreateControlledImpactLab(
            tree, CurveMaximumPain, CurveMaximumImpulse);
        if (lab is null)
        {
            return default;
        }

        int masterBus = AudioServer.GetBusIndex("Master");
        float masterVolumeBefore = masterBus >= 0
            ? AudioServer.GetBusVolumeDb(masterBus)
            : 0.0f;
        lab.Pipeline.SelectTool(ToolId.BaseballBat);
        Vector2 torso = lab.Buddy.Rig.Torso.GlobalPosition;
        lab.CursorTools.MoveCursor(torso + new Vector2(-140.0f, -100.0f));
        await Ticks(tree, 2);
        CursorToolBody? bat = lab.CursorTools.Body;
        CursorToolProfile? profile = lab.CursorTools.ActiveProfile;
        if (bat is null || profile?.Swing is null)
        {
            lab.QueueFree();
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            return default;
        }

        SwingToolProfile swing = profile.Swing;
        SwingPlan plan = ChargedSwing.SwingPlanFor(
            ChargedSwing.ChargeProgress(chargeTicks, swing.MaxChargeTicks),
            profile.HandleToTipRadius,
            swing.ToConstants());
        SwingTrajectoryPoint contactPoint = ChargedSwing.SwingTrajectoryAt(
            plan.WindupTicks +
            Mathf.Clamp(
                Mathf.RoundToInt(plan.SweepTicks * sweepFraction),
                0,
                plan.SweepTicks - 1),
            plan,
            directionSign: 1,
            swing.ToConstants());

        Vector2 tipFromPivot =
            new Vector2(0.0f, -profile.HandleToTipRadius).Rotated(contactPoint.BarrelAngle);
        Vector2 pivot = torso - tipFromPivot +
                        tipFromPivot.Normalized() * profile.Radius * radialOffsetRadii;

        // The last significant cursor travel is rightward and ends exactly at
        // the release pivot. Once released, the helper never moves the pointer.
        lab.CursorTools.MoveCursor(pivot + new Vector2(-12.0f, 0.0f));
        await Ticks(tree, 120);
        lab.CursorTools.SetGrip(true);
        await WaitForState(tree, lab.CursorTools, ChargedSwingState.Gripped, 120);
        lab.CursorTools.MoveCursor(pivot);
        await Ticks(tree, 60);
        await WaitForBatSettled(tree, lab, bat, 360);

        int positiveImpacts = 0;
        int episodes = 0;
        var episodeParts = new HashSet<BuddyPart>();
        float firstEpisodeImpulse = 0.0f;
        float maximumEpisodeImpulse = 0.0f;
        float maximumImpulse = 0.0f;
        int epoch = 0;
        bool hitObserved = false;
        Vector2 hitCenter = Vector2.Zero;
        int ticksAfterHit = 0;
        int hitLagDuration = 0;
        Vector2 frozenCenter = Vector2.Zero;
        Vector2 frozenVelocity = Vector2.Zero;
        double frozenPipelineTime = 0.0;
        long frozenBuddyTicks = 0;
        RecoveryClockState frozenRecovery = default;
        Vector2 firstImpactPoint = Vector2.Zero;
        void OnImpact(AcceptedImpact impact)
        {
            if (impact.ContentId != ContentIds.ToolBaseballBat ||
                impact.SwingEpoch <= 0 ||
                impact.Pain <= 0.0f)
            {
                return;
            }

            positiveImpacts++;
            maximumImpulse = Mathf.Max(maximumImpulse, impact.RawImpulse);
            epoch = impact.SwingEpoch;
            if (!hitObserved)
            {
                hitObserved = true;
                firstImpactPoint = impact.Point;
                hitCenter = WholeBuddyCenter(lab);
                ticksAfterHit = 0;
                hitLagDuration = lab.SwingHitLag.TotalTicks;
                frozenCenter = hitCenter;
                frozenVelocity = WholeBuddyVelocity(lab);
                frozenPipelineTime = lab.Pipeline.NowSeconds;
                frozenBuddyTicks = lab.Buddy.RoutedTicks;
                frozenRecovery = lab.Buddy.Recovery.State;
            }
        }

        void OnEpisode(AcceptedContactEpisode episode)
        {
            if (episode.ContentId != ContentIds.ToolBaseballBat)
            {
                return;
            }

            if (episodes == 0)
            {
                firstEpisodeImpulse = episode.Impulse;
            }

            episodes++;
            maximumEpisodeImpulse = Mathf.Max(maximumEpisodeImpulse, episode.Impulse);
            episodeParts.Add(episode.Part);
        }

        lab.Pipeline.ImpactAccepted += OnImpact;
        lab.Pipeline.EpisodeAccepted += OnEpisode;
        float maximumTravel = 0.0f;
        Vector2 peakVelocity = Vector2.Zero;
        bool sawCcd = false;
        bool sawSwing = false;
        float minimumTipDistance = float.PositiveInfinity;
        bool sawHitLag = false;
        bool velocityHeld = true;
        bool timersHeld = true;
        bool slowTimeSuppressed = true;
        bool sawImpactHitStop = false;
        bool launchResumed = false;
        bool canceled = false;
        long routedTicksBeforeCancel = 0;
        bool cancelResumedRouting = false;
        float maximumVictimShake = 0.0f;
        float maximumOtherPartShake = 0.0f;
        float shakeAfterHitLag = 0.0f;
        bool poseStayedTracking = true;

        lab.CursorTools.SetChargeHeld(true);
        await WaitForState(tree, lab.CursorTools, ChargedSwingState.Charging, 3);
        await Ticks(tree, chargeTicks);
        lab.CursorTools.SetChargeHeld(false);
        int releasedEpoch = lab.CursorTools.SwingEpoch;
        int directionSign = lab.CursorTools.SwingDirectionSign;

        for (int tick = 0; tick < 220; tick++)
        {
            await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
            sawSwing |= lab.CursorTools.SwingState == ChargedSwingState.Swinging;
            if (GodotObject.IsInstanceValid(bat))
            {
                sawCcd |= bat.ContinuousCd == RigidBody2D.CcdMode.CastShape;
                Vector2 tip = bat.GlobalPosition +
                              new Vector2(0.0f, -profile.Length * 0.5f).Rotated(
                                  bat.GlobalRotation);
                minimumTipDistance = Mathf.Min(
                    minimumTipDistance,
                    tip.DistanceTo(lab.Buddy.Rig.Torso.GlobalPosition));
            }
            if (lab.SwingHitLag.IsActive)
            {
                sawHitLag = true;
                velocityHeld &= WholeBuddyVelocity(lab).DistanceTo(frozenVelocity) <= 0.01f;
                velocityHeld &= WholeBuddyCenter(lab).DistanceTo(frozenCenter) <= 0.01f;
                timersHeld &= Mathf.IsEqualApprox(
                    (float)lab.Pipeline.NowSeconds,
                    (float)frozenPipelineTime);
                timersHeld &= lab.Buddy.RoutedTicks == frozenBuddyTicks;
                timersHeld &= lab.Buddy.Recovery.State == frozenRecovery;
                sawImpactHitStop |= lab.ImpactFeedback.IsHitStopActive;
                slowTimeSuppressed &= !lab.ImpactFeedback.IsHitStopActive &&
                                      Mathf.IsEqualApprox((float)Engine.TimeScale, 1.0f);
                poseStayedTracking &=
                    lab.PosePipeline.Mode == PresentationPoseMode.Tracking;

                if (lab.SwingHitLag.Current.StruckPart is BuddyPart struckPart)
                {
                    BuddyPartId victim = (BuddyPartId)(int)struckPart;
                    maximumVictimShake = Mathf.Max(
                        maximumVictimShake,
                        lab.ImpactVisualOffset.OffsetFor(victim).Length());
                    BuddyPartId other = victim == BuddyPartId.Head
                        ? BuddyPartId.Torso
                        : BuddyPartId.Head;
                    maximumOtherPartShake = Mathf.Max(
                        maximumOtherPartShake,
                        lab.ImpactVisualOffset.OffsetFor(other).Length());
                }

                if (cancelHitLag && !canceled)
                {
                    canceled = true;
                    routedTicksBeforeCancel = lab.Buddy.RoutedTicks;
                    lab.CursorTools.ClearCursor();
                    // Prove fail-safe cleanup is idempotent at the public seam.
                    lab.CursorTools.ClearCursor();
                }
            }
            else if (sawHitLag)
            {
                if (lab.SwingHitLag.Current.StruckPart is BuddyPart struckPart)
                {
                    shakeAfterHitLag = Mathf.Max(
                        shakeAfterHitLag,
                        lab.ImpactVisualOffset.OffsetFor(
                            (BuddyPartId)(int)struckPart).Length());
                }

                cancelResumedRouting |= canceled &&
                    lab.Buddy.RoutedTicks > routedTicksBeforeCancel;
                launchResumed |= !canceled &&
                    WholeBuddyCenter(lab).DistanceTo(frozenCenter) > 0.1f;
            }
            // Task D's travel envelope is measured over routed post-hit ticks.
            // Task E deliberately inserts charge-scaled engine frames where the
            // entire simulation is frozen; those frames must not consume this
            // older probe's observation window.
            if (hitObserved && !lab.SwingHitLag.IsActive && ticksAfterHit <= 24)
            {
                Vector2 center = WholeBuddyCenter(lab);
                maximumTravel = Mathf.Max(maximumTravel, center.DistanceTo(hitCenter));
                Vector2 velocity = WholeBuddyVelocity(lab);
                if (velocity.LengthSquared() > peakVelocity.LengthSquared())
                {
                    peakVelocity = velocity;
                }

                ticksAfterHit++;
            }
        }

        lab.Pipeline.ImpactAccepted -= OnImpact;
        lab.Pipeline.EpisodeAccepted -= OnEpisode;
        int hitLagTriggerCount = lab.SwingHitLag.TriggerCount;
        int frozenFrames = lab.SwingHitLag.FrozenFrameCount;
        int hitLagCancelCount = lab.SwingHitLag.CancelCount;
        int audioGeneratedStreamCount = lab.SwingAudio.GeneratedStreamCount;
        int audioPlayCount = lab.SwingAudio.PlayCount;
        int audioChargeStartedCount = lab.SwingAudio.ChargeStartedCount;
        int audioChargeCompletedCount = lab.SwingAudio.ChargeCompletedCount;
        int audioSwingReleasedCount = lab.SwingAudio.SwingReleasedCount;
        int audioHomeRunImpactCount = lab.SwingAudio.HomeRunImpactCount;
        bool audioStreamIsGeneratedPcm =
            lab.SwingAudio.Player.Stream is AudioStreamWav;
        bool audioOwnsExactlyOnePlayer =
            lab.SwingAudio.GetChildCount() == 1 &&
            lab.SwingAudio.Player.GetParent() == lab.SwingAudio;
        bool audioBusExists =
            AudioServer.GetBusIndex(lab.SwingAudio.RoutedBus) >= 0;
        bool audioMasterVolumeUnchanged =
            masterBus < 0 ||
            Mathf.IsEqualApprox(
                AudioServer.GetBusVolumeDb(masterBus),
                masterVolumeBefore);
        bool audioVolumeMatchesProfile =
            Mathf.IsEqualApprox(
                lab.SwingAudio.Player.VolumeDb,
                swing.AudioVolumeDb);
        int homeRunBurstCount = lab.ImpactFeedback.HomeRunBurstCount;
        bool homeRunBurstMatchedContact =
            hitObserved &&
            lab.ImpactFeedback.LastHomeRunBurstWorldPoint.DistanceTo(firstImpactPoint) <= 0.01f;
        float homeRunBurstSizePx = lab.ImpactFeedback.Profile.HomeRunBurstSizePx;
        lab.QueueFree();
        await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        return new BuddySwingProbe(
            positiveImpacts,
            episodes,
            episodeParts.Count,
            maximumImpulse,
            maximumTravel,
            peakVelocity,
            epoch == 0 ? releasedEpoch : epoch,
            sawCcd,
            sawSwing,
            directionSign,
            minimumTipDistance,
            firstEpisodeImpulse,
            maximumEpisodeImpulse,
            hitLagDuration,
            hitLagTriggerCount,
            frozenFrames,
            sawHitLag && velocityHeld,
            launchResumed,
            sawHitLag && timersHeld,
            sawHitLag && slowTimeSuppressed,
            sawImpactHitStop,
            hitLagCancelCount,
            cancelResumedRouting,
            maximumVictimShake,
            maximumOtherPartShake,
            shakeAfterHitLag,
            sawHitLag && poseStayedTracking,
            audioGeneratedStreamCount,
            audioPlayCount,
            audioChargeStartedCount,
            audioChargeCompletedCount,
            audioSwingReleasedCount,
            audioHomeRunImpactCount,
            audioStreamIsGeneratedPcm,
            audioOwnsExactlyOnePlayer,
            audioBusExists,
            audioMasterVolumeUnchanged,
            audioVolumeMatchesProfile,
            homeRunBurstCount,
            homeRunBurstMatchedContact,
            homeRunBurstSizePx);
    }

    private static Vector2 WholeBuddyCenter(BuddyLab lab)
    {
        Vector2 weighted = Vector2.Zero;
        float totalMass = 0.0f;
        foreach (var part in lab.Buddy.Rig.Parts)
        {
            weighted += part.GlobalPosition * part.Mass;
            totalMass += part.Mass;
        }

        return totalMass > 0.0f ? weighted / totalMass : Vector2.Zero;
    }

    private static Vector2 WholeBuddyVelocity(BuddyLab lab)
    {
        Vector2 weighted = Vector2.Zero;
        float totalMass = 0.0f;
        foreach (var part in lab.Buddy.Rig.Parts)
        {
            weighted += part.LinearVelocity * part.Mass;
            totalMass += part.Mass;
        }

        return totalMass > 0.0f ? weighted / totalMass : Vector2.Zero;
    }

    private readonly record struct LooseObjectSwingProbe(
        bool SawContact,
        float MaximumTravel,
        float PeakSpeed,
        bool SawCastShapeCcd,
        int HitLagDurationTicks,
        int HitLagTriggerCount,
        bool AllLooseObjectsHeldDuringHitLag,
        bool UnrelatedObjectResumedAfterHitLag);

    /// <summary>
    /// Swing the production bat into a passive one-kilogram loose-object probe.
    /// Varying <paramref name="contactRadiusFraction"/> changes only where the
    /// real capsule meets it; there is no tip or charge outcome multiplier.
    /// </summary>
    private static async Task<LooseObjectSwingProbe> RunLooseObjectSwingProbe(
        SceneTree tree,
        int chargeTicks,
        float contactRadiusFraction = 1.0f)
    {
        BuddyLab? lab = await ScenarioSteps.CreateControlledImpactLab(
            tree, CurveMaximumPain, CurveMaximumImpulse);
        if (lab is null)
        {
            return default;
        }

        // The buddy remains composed and ticking, but is not a collision target
        // in this object-only measurement.
        foreach (var part in lab.Buddy.Rig.Parts)
        {
            part.CollisionLayer = 0;
            part.CollisionMask = 0;
        }

        Vector2 target = lab.Buddy.Rig.Torso.GlobalPosition;
        lab.Pipeline.SelectTool(ToolId.BaseballBat);
        lab.CursorTools.MoveCursor(target + new Vector2(-140.0f, -100.0f));
        await Ticks(tree, 2);
        CursorToolBody? bat = lab.CursorTools.Body;
        CursorToolProfile? profile = lab.CursorTools.ActiveProfile;
        if (bat is null || profile?.Swing is null)
        {
            lab.QueueFree();
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            return default;
        }

        SwingToolProfile swing = profile.Swing;
        SwingPlan plan = ChargedSwing.SwingPlanFor(
            ChargedSwing.ChargeProgress(chargeTicks, swing.MaxChargeTicks),
            profile.HandleToTipRadius,
            swing.ToConstants());
        SwingTrajectoryPoint contactPoint = ChargedSwing.SwingTrajectoryAt(
            plan.WindupTicks +
            Mathf.Clamp(
                Mathf.RoundToInt(plan.SweepTicks * 0.65f),
                0,
                plan.SweepTicks - 1),
            plan,
            directionSign: 1,
            swing.ToConstants());

        float contactRadius = profile.HandleToTipRadius *
                              Mathf.Clamp(contactRadiusFraction, 0.20f, 1.0f);
        Vector2 contactFromPivot =
            new Vector2(0.0f, -contactRadius).Rotated(contactPoint.BarrelAngle);
        Vector2 pivot = target - contactFromPivot;

        lab.CursorTools.MoveCursor(pivot + new Vector2(-12.0f, 0.0f));
        await Ticks(tree, 120);
        lab.CursorTools.SetGrip(true);
        await WaitForState(tree, lab.CursorTools, ChargedSwingState.Gripped, 120);
        lab.CursorTools.MoveCursor(pivot);
        await Ticks(tree, 60);
        await WaitForBatSettled(tree, lab, bat, 360);

        lab.CursorTools.SetChargeHeld(true);
        await WaitForState(tree, lab.CursorTools, ChargedSwingState.Charging, 3);
        await Ticks(tree, chargeTicks);
        lab.CursorTools.SetChargeHeld(false);

        var unrelatedObject = new ScenarioImpactBody();
        unrelatedObject.ConfigureLooseObject(radius: 6.0f);
        unrelatedObject.CollisionMask = 0;
        lab.AddChild(unrelatedObject);
        unrelatedObject.GlobalPosition = new Vector2(45.0f, 55.0f);
        unrelatedObject.LinearVelocity = new Vector2(90.0f, 25.0f);

        int sampleTick = plan.WindupTicks +
                         Mathf.Clamp(
                             Mathf.RoundToInt(plan.SweepTicks * 0.65f),
                             0,
                             plan.SweepTicks - 1);
        ScenarioImpactBody? targetBody = null;
        Vector2 start = Vector2.Zero;
        float maximumTravel = 0.0f;
        float peakSpeed = 0.0f;
        bool sawContact = false;
        bool sawCcd = false;
        int hitLagDuration = 0;
        bool sawHitLag = false;
        bool allLooseObjectsHeld = true;
        bool unrelatedResumed = false;
        Vector2 frozenTargetPosition = Vector2.Zero;
        Vector2 frozenTargetVelocity = Vector2.Zero;
        Vector2 frozenUnrelatedPosition = Vector2.Zero;
        Vector2 frozenUnrelatedVelocity = Vector2.Zero;
        // The full-charge object case now includes exactly 60 frozen engine
        // frames. Add those frames only to that case so every charge band keeps
        // Task D's original 150 routed-frame observation window.
        int observationFrames = chargeTicks == profile.Swing!.MaxChargeTicks ? 210 : 150;
        for (int tick = 0; tick < observationFrames; tick++)
        {
            await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
            sawCcd |= bat.ContinuousCd == RigidBody2D.CcdMode.CastShape;

            if (targetBody is null &&
                lab.CursorTools.SwingState == ChargedSwingState.Swinging &&
                lab.CursorTools.SwingTicksInState >= sampleTick)
            {
                // Place the mass immediately ahead of the selected point on the
                // *realized* moving capsule. Rotational CastShape CCD does not
                // expose a swept-contact query, so a theoretical world target
                // can sit between two 46 px full-charge tip samples. This
                // placement uses measured point velocity only to choose the
                // next physical contact location; the resulting travel still
                // comes entirely from the solver.
                Vector2 pointOffset = new Vector2(
                    0.0f,
                    profile.HandleLocalOffset.Y - contactRadius).Rotated(
                        bat.GlobalRotation);
                Vector2 point = bat.GlobalPosition + pointOffset;
                Vector2 pointVelocity = bat.LinearVelocity + new Vector2(
                    -bat.AngularVelocity * pointOffset.Y,
                    bat.AngularVelocity * pointOffset.X);
                Vector2 direction = pointVelocity.Normalized();
                if (direction.IsZeroApprox())
                {
                    direction = Vector2.Right;
                }

                const float objectRadius = 8.0f;
                // The tip sample names the capsule's outermost surface; the
                // barrel/handle samples name points on its centre line. Start
                // the passive circle one pixel inside the corresponding
                // contact shell so the next solver step observes the real
                // point velocity even though CastShape does not sweep rotation.
                bool outerTip =
                    contactRadius >= profile.HandleToTipRadius - 0.1f;
                float ahead = outerTip
                    ? objectRadius - 1.0f
                    : profile.Radius + objectRadius - 1.0f;
                targetBody = new ScenarioImpactBody();
                targetBody.ConfigureLooseObject(radius: objectRadius);
                lab.AddChild(targetBody);
                targetBody.GlobalPosition = point + direction * ahead;
                start = targetBody.GlobalPosition;
            }

            if (targetBody is null)
            {
                continue;
            }

            if (lab.SwingHitLag.IsActive)
            {
                if (!sawHitLag)
                {
                    sawHitLag = true;
                    hitLagDuration = lab.SwingHitLag.TotalTicks;
                    frozenTargetPosition = targetBody.GlobalPosition;
                    frozenTargetVelocity = targetBody.LinearVelocity;
                    frozenUnrelatedPosition = unrelatedObject.GlobalPosition;
                    frozenUnrelatedVelocity = unrelatedObject.LinearVelocity;
                }
                else
                {
                    allLooseObjectsHeld &=
                        targetBody.GlobalPosition.DistanceTo(frozenTargetPosition) <= 0.01f &&
                        targetBody.LinearVelocity.DistanceTo(frozenTargetVelocity) <= 0.01f &&
                        unrelatedObject.GlobalPosition.DistanceTo(frozenUnrelatedPosition) <= 0.01f &&
                        unrelatedObject.LinearVelocity.DistanceTo(frozenUnrelatedVelocity) <= 0.01f;
                }
            }
            else if (sawHitLag)
            {
                unrelatedResumed |=
                    unrelatedObject.GlobalPosition.DistanceTo(frozenUnrelatedPosition) > 0.1f;
            }

            float speed = targetBody.LinearVelocity.Length();
            peakSpeed = Mathf.Max(peakSpeed, speed);
            maximumTravel = Mathf.Max(
                maximumTravel,
                targetBody.GlobalPosition.DistanceTo(start));
            if (!sawContact && speed >= 1.0f)
            {
                sawContact = true;
                // Compare the one solver launch, not several ticks of the
                // anchored bat continuing to push a mass parked near its
                // handle. The body then coasts with the velocity that contact
                // actually transferred.
                targetBody.CollisionLayer = 0;
                targetBody.CollisionMask = 0;
            }
        }

        int hitLagTriggerCount = lab.SwingHitLag.TriggerCount;
        lab.QueueFree();
        await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        return new LooseObjectSwingProbe(
            sawContact,
            maximumTravel,
            peakSpeed,
            sawCcd,
            hitLagDuration,
            hitLagTriggerCount,
            sawHitLag && allLooseObjectsHeld,
            unrelatedResumed);
    }

    private readonly record struct GrazeThenHitProbe(
        int EpisodeCount,
        int PositiveImpactCount,
        float FirstEpisodeImpulse,
        float CurveFloorImpulse,
        int AcceptedEpoch,
        int SwingEpoch);

    /// <summary>
    /// Exercise the production curve/admission order with two real contacts
    /// carrying one immutable swing identity. The first is routed above the
    /// episode threshold but below the pain-curve floor; after re-arm, the
    /// second is harmful. If the graze claimed the epoch, the second event
    /// would disappear.
    /// </summary>
    private static async Task<GrazeThenHitProbe> RunGrazeThenHitProbe(SceneTree tree)
    {
        const float curveFloor = 100.0f;
        const int swingEpoch = 77;
        BuddyLab? lab = await ScenarioSteps.CreateControlledImpactLab(
            tree,
            CurveMaximumPain,
            CurveMaximumImpulse,
            curveFloor);
        if (lab is null)
        {
            return default;
        }

        int interactionId = InteractionIds.Next();
        int episodes = 0;
        int positives = 0;
        float firstEpisodeImpulse = 0.0f;
        int acceptedEpoch = 0;
        void OnEpisode(AcceptedContactEpisode episode)
        {
            if (episode.InteractionId != interactionId)
            {
                return;
            }

            if (episodes == 0)
            {
                firstEpisodeImpulse = episode.Impulse;
            }

            episodes++;
        }

        void OnImpact(AcceptedImpact impact)
        {
            if (impact.InteractionId != interactionId)
            {
                return;
            }

            positives++;
            acceptedEpoch = impact.SwingEpoch;
        }

        lab.Pipeline.EpisodeAccepted += OnEpisode;
        lab.Pipeline.ImpactAccepted += OnImpact;
        var context = new SwingImpactContext(
            SwingImpactMode.HomeRun,
            swingEpoch,
            ReleasedCharge: 1.0f,
            ReleasedTick: 1234L);

        await StrikeWithSwingContext(
            tree, lab, interactionId, context, speed: 100.0f);
        // The episode router rearms after 0.2 s of separation. This wait is
        // routed physics time, not wall time.
        await Ticks(tree, 30);
        await StrikeWithSwingContext(
            tree, lab, interactionId, context, speed: 2000.0f);
        await Ticks(tree, 4);

        lab.Pipeline.EpisodeAccepted -= OnEpisode;
        lab.Pipeline.ImpactAccepted -= OnImpact;
        lab.QueueFree();
        await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        return new GrazeThenHitProbe(
            episodes,
            positives,
            firstEpisodeImpulse,
            curveFloor,
            acceptedEpoch,
            swingEpoch);
    }

    private static async Task StrikeWithSwingContext(
        SceneTree tree,
        BuddyLab lab,
        int interactionId,
        SwingImpactContext context,
        float speed)
    {
        var source = new ScenarioImpactBody();
        source.Configure(
            ContentIds.ToolBaseballBat,
            radius: 8.0f,
            mass: 0.25f,
            interactionId: interactionId);
        source.SetSwingContext(context);
        var target = lab.Buddy.Rig.Torso;
        source.GlobalPosition =
            target.GlobalPosition - Vector2.Right * (target.Radius + 10.0f);
        source.LinearVelocity = Vector2.Right * speed;
        lab.AddChild(source);
        await Ticks(tree, 60);
        source.QueueFree();
        await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
    }

    private readonly record struct WhiffRecoveryProbe(
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

    private static async Task<WhiffRecoveryProbe> RunWhiffRecoveryProbe(SceneTree tree)
    {
        BuddyLab? lab = await ScenarioSteps.CreateControlledImpactLab(
            tree, CurveMaximumPain, CurveMaximumImpulse);
        if (lab is null)
        {
            return default;
        }

        Vector2 torso = lab.Buddy.Rig.Torso.GlobalPosition;
        Vector2 openPivot = torso + new Vector2(0.0f, -220.0f);
        lab.Pipeline.SelectTool(ToolId.BaseballBat);
        lab.CursorTools.MoveCursor(openPivot + new Vector2(-12.0f, 0.0f));
        await Ticks(tree, 120);
        CursorToolBody? bat = lab.CursorTools.Body;
        if (bat is null)
        {
            lab.QueueFree();
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            return default;
        }

        lab.CursorTools.SetGrip(true);
        await WaitForState(tree, lab.CursorTools, ChargedSwingState.Gripped, 120);
        lab.CursorTools.MoveCursor(openPivot);
        await Ticks(tree, 60);
        await WaitForBatSettled(tree, lab, bat, 360);
        lab.CursorTools.SetChargeHeld(true);
        await WaitForState(tree, lab.CursorTools, ChargedSwingState.Charging, 3);
        await Ticks(tree, 600);

        bool restingContactPhase = false;
        int whiffPositive = 0;
        int restingPositive = 0;
        int restingEpisodes = 0;
        void OnImpact(AcceptedImpact impact)
        {
            if (impact.ContentId != ContentIds.ToolBaseballBat)
            {
                return;
            }

            if (restingContactPhase)
                restingPositive++;
            else
                whiffPositive++;
        }

        void OnEpisode(AcceptedContactEpisode episode)
        {
            if (restingContactPhase &&
                episode.ContentId == ContentIds.ToolBaseballBat)
            {
                restingEpisodes++;
            }
        }

        lab.Pipeline.ImpactAccepted += OnImpact;
        lab.Pipeline.EpisodeAccepted += OnEpisode;
        lab.CursorTools.SetChargeHeld(false);
        bool sawSwing = await WaitForState(
            tree, lab.CursorTools, ChargedSwingState.Swinging, 3);
        int epoch = lab.CursorTools.SwingEpoch;
        bool sawRecovery = await WaitForState(
            tree, lab.CursorTools, ChargedSwingState.Recovery, 120);
        await WaitForState(tree, lab.CursorTools, ChargedSwingState.Gripped, 120);

        restingContactPhase = true;
        lab.CursorTools.MoveCursor(torso);
        await Ticks(tree, 180);

        lab.Pipeline.ImpactAccepted -= OnImpact;
        lab.Pipeline.EpisodeAccepted -= OnEpisode;
        int audioPlayCount = lab.SwingAudio.PlayCount;
        int audioChargeStartedCount = lab.SwingAudio.ChargeStartedCount;
        int audioChargeCompletedCount = lab.SwingAudio.ChargeCompletedCount;
        int audioSwingReleasedCount = lab.SwingAudio.SwingReleasedCount;
        int audioHomeRunImpactCount = lab.SwingAudio.HomeRunImpactCount;
        lab.QueueFree();
        await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        return new WhiffRecoveryProbe(
            sawSwing,
            sawRecovery,
            epoch,
            whiffPositive,
            restingEpisodes,
            restingPositive,
            audioPlayCount,
            audioChargeStartedCount,
            audioChargeCompletedCount,
            audioSwingReleasedCount,
            audioHomeRunImpactCount);
    }

    private static bool WithinFraction(float actual, float target, float fraction) =>
        actual >= target * (1.0f - fraction) &&
        actual <= target * (1.0f + fraction);

    private static float Ratio(float numerator, float denominator) =>
        denominator > 0.0f ? numerator / denominator : 0.0f;

    private static ScenarioResult Finish(
        List<StartupCheck> checks,
        List<string> messages,
        BuddyLab lab)
    {
        lab.QueueFree();
        bool passed = true;
        foreach (StartupCheck check in checks)
            passed &= check.Passed;
        return new ScenarioResult(passed, checks, messages);
    }

    private static ScenarioResult Finish(
        List<StartupCheck> checks,
        List<string> messages)
    {
        bool passed = true;
        foreach (StartupCheck check in checks)
            passed &= check.Passed;
        return new ScenarioResult(passed, checks, messages);
    }
}
