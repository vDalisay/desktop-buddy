using System.Collections.Generic;
using System.Threading.Tasks;
using DesktopBuddy.App;
using DesktopBuddy.Domain.Content;
using DesktopBuddy.Domain.Tools;
using DesktopBuddy.Interaction;
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

        await Ticks(tree, 300);
        int chargeAt300 = lab.CursorTools.SwingChargeTicks;
        float shakeAt300 = bat.ChargeShakeAmplitude;
        await Ticks(tree, 299);
        int chargeAt599 = lab.CursorTools.SwingChargeTicks;
        float shakeAt599 = bat.ChargeShakeAmplitude;
        await Ticks(tree, 1);
        int chargeAt600 = lab.CursorTools.SwingChargeTicks;
        float shakeAt600 = bat.ChargeShakeAmplitude;
        await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        bool glintWasVisible = bat.IsChargeGlintActive || lab.CursorToolVisual.IsGlintVisible;
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

        checks.Add(new StartupCheck(
            "full_charge_shows_the_tip_glimmer_once",
            chargeCompletedEdges == 1 &&
            bat.ChargeGlintStarts == 1 &&
            glintWasVisible &&
            bat.VisualGlintLocalPosition.IsEqualApprox(new Vector2(0.0f, bat.Length * -0.5f)),
            $"edges={chargeCompletedEdges} starts={bat.ChargeGlintStarts} " +
            $"visible={glintWasVisible} tip={bat.VisualGlintLocalPosition}"));

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
        lab.CursorTools.MoveCursor(lab.CursorTools.Cursor + new Vector2(30.0f, 0.0f));
        await Ticks(tree, 1);
        bool firstDirectionLocked = lab.CursorTools.SwingDirectionSign == -1 &&
                                    lab.CursorTools.SwingEpoch == firstEpoch;

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
            "pointer_motion_after_release_cannot_change_direction",
            firstDirectionLocked,
            $"direction={lab.CursorTools.SwingDirectionSign} epoch={lab.CursorTools.SwingEpoch}"));

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
        await Ticks(tree, 90);
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
            $"directions=({firstReleasedDirection},{secondReleasedDirection})");
        return Finish(checks, messages, lab);
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
}
