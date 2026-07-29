using System.Collections.Generic;
using System.Threading.Tasks;
using DesktopBuddy.App;
using DesktopBuddy.Buddy.Physics;
using DesktopBuddy.Buddy.Presentation3D;
using DesktopBuddy.Domain.Autonomy;
using DesktopBuddy.Domain.Content;
using DesktopBuddy.Domain.Mood;
using DesktopBuddy.Domain.Presentation;
using DesktopBuddy.Objects;
using Godot;

namespace DesktopBuddy.Testing;

/// <summary>
/// M5 Task 3 gate for the catalogue Meal, on the owner's appetite rules (2026-07-29): the
/// buddy eats what fits in its hunger bar and refuses what would overfill it. Placement goes
/// through the real launcher; the buddy fetches and eats through the ordinary arbiter path.
///
/// <para>Five phases in one room: an abandoned meal feeds nobody, a finished one pays its
/// mood and fills the bar, meals stop being accepted once the bar is full, a refused meal is
/// performed as the owner asked — carried in one hand, shaken off at the player, put down
/// below the buddy, and then left alone instead of being fetched again forever — and appetite
/// returning puts it back on the menu.</para>
/// </summary>
public sealed class MealConsumeScenario : IScenario
{
    private const float MealMoodGain = 10.0f;
    private const float MealHungerFill = 50.0f;
    private const int FetchTimeoutTicks = 2400;
    private const int ConsumeTimeoutTicks = 3000;

    public string Id => "meal_consume";

    public async Task<ScenarioResult> RunAsync(SceneTree tree, ulong seed)
    {
        var checks = new List<StartupCheck>();
        var messages = new List<string> { $"seed={seed}" };
        BuddyLab? lab = await M4ObjectScenarioSupport.LoadLab(tree, seed);
        if (lab is null)
        {
            checks.Add(new StartupCheck("meal_lab_loadable", false, "buddy_lab"));
            return new ScenarioResult(false, checks, messages);
        }

        // Phase 1 — a meal abandoned mid-bite feeds nobody (FR-008.10).
        float moodBeforeCancel = lab.Progress.Mood;
        float fullnessBeforeCancel = lab.Progress.Fullness;
        bool placedFirst = await PlaceMeal(tree, lab);
        bool eating = placedFirst && await M4ObjectScenarioSupport.WaitFor(
            tree,
            () => lab.Buddy.Activity.Current == ActivityId.Eat,
            FetchTimeoutTicks);
        bool bitTwice = eating && await M4ObjectScenarioSupport.WaitFor(
            tree, () => lab.Buddy.Activity.EatBitesCompleted >= 2, 600);

        // The owner's screenshot bug: the food rode one hand while both hands lifted. Eating
        // is a two-handed gesture, so the item belongs between them.
        LooseObjectBody? eaten = lab.Objects.FindBody(
            lab.Buddy.ObjectInteraction.TrackedRuntimeId);
        float leftGap = float.MaxValue;
        float rightGap = float.MaxValue;
        float sidewaysOffset = float.MaxValue;
        float heightAboveHands = float.MaxValue;
        float restingLimit = 0.0f;
        if (GodotObject.IsInstanceValid(eaten))
        {
            Vector2 left = lab.Buddy.Rig.LeftHand.GlobalPosition;
            Vector2 right = lab.Buddy.Rig.RightHand.GlobalPosition;
            Vector2 between = (left + right) * 0.5f;
            leftGap = left.DistanceTo(eaten!.GlobalPosition);
            rightGap = right.DistanceTo(eaten.GlobalPosition);
            sidewaysOffset = Mathf.Abs(eaten.GlobalPosition.X - between.X);
            // Positive means above the hand line, which is where a carried item rests.
            heightAboveHands = between.Y - eaten.GlobalPosition.Y;
            restingLimit = lab.Buddy.Rig.LeftHand.Radius + eaten.Radius + 6.0f;
        }

        checks.Add(new StartupCheck(
            "the_meal_is_held_between_both_hands",
            bitTwice &&
            sidewaysOffset <= 6.0f &&
            Mathf.Abs(leftGap - rightGap) <= 8.0f &&
            heightAboveHands >= 0.0f && heightAboveHands <= restingLimit,
            $"sideways={sidewaysOffset:F1} left_gap={leftGap:F1} right_gap={rightGap:F1} " +
            $"height_above_hands={heightAboveHands:F1} limit={restingLimit:F1}"));

        lab.Buddy.ObjectInteraction.CancelActiveInteraction();
        await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);

        checks.Add(new StartupCheck(
            "an_abandoned_meal_feeds_nobody",
            placedFirst && eating && bitTwice &&
            lab.Buddy.ObjectInteraction.ConsumeSuccessCount == 0 &&
            Mathf.Abs(lab.Progress.Mood - moodBeforeCancel) < 0.01f &&
            Mathf.Abs(lab.Progress.Fullness - fullnessBeforeCancel) < 0.01f,
            $"placed={placedFirst} eating={eating} bites={lab.Buddy.Activity.EatBitesCompleted} " +
            $"mood={lab.Progress.Mood:F1} fullness={lab.Progress.Fullness:F1}"));

        // Phase 2 — a finished meal pays its mood once and fills the bar by its own amount.
        float moodBeforeMeal = lab.Progress.Mood;
        float fullnessBeforeMeal = lab.Progress.Fullness;
        float treatInterestBefore = lab.Progress.InterestIn(FunActivityId.Treat);
        bool consumed = await FeedOne(tree, lab);
        float treatInterestAfter = lab.Progress.InterestIn(FunActivityId.Treat);

        checks.Add(new StartupCheck(
            "a_finished_meal_pays_mood_and_fills_the_bar",
            consumed &&
            Mathf.Abs(lab.Progress.Mood - (moodBeforeMeal + MealMoodGain)) < 0.01f &&
            Mathf.Abs(lab.Progress.Fullness - (fullnessBeforeMeal + MealHungerFill)) < 0.01f,
            $"consumed={consumed} mood={lab.Progress.Mood:F1} was={moodBeforeMeal:F1} " +
            $"fullness={lab.Progress.Fullness:F1} was={fullnessBeforeMeal:F1}"));

        checks.Add(new StartupCheck(
            "eating_a_meal_is_still_a_treat",
            consumed && treatInterestAfter < treatInterestBefore,
            $"interest_before={treatInterestBefore:F1} after={treatInterestAfter:F1}"));

        // Phase 3 — keep feeding until the bar cannot take another meal.
        int meals = 1;
        while (lab.Progress.WouldEat(MealHungerFill) && meals < 8)
        {
            if (!await FeedOne(tree, lab))
                break;
            meals++;
        }

        checks.Add(new StartupCheck(
            "the_bar_fills_by_the_portion_size",
            !lab.Progress.WouldEat(MealHungerFill) &&
            lab.Progress.Fullness >= lab.Progress.Fullness - 0.01f &&
            meals == 4,
            $"meals={meals} fullness={lab.Progress.Fullness:F1} appetite={lab.Progress.Appetite:F1} " +
            $"successes={lab.Buddy.ObjectInteraction.ConsumeSuccessCount}"));

        // Phase 4 — the refusal, as the owner asked for it: picked up in ONE hand, aimed at the
        // player, shaken off twice, put down below itself, then left alone.
        int successesBeforeRefusal = lab.Buddy.ObjectInteraction.ConsumeSuccessCount;
        int discardsBeforeRefusal = lab.Buddy.ObjectInteraction.DiscardCount;
        float moodBeforeRefusal = lab.Progress.Mood;
        bool placedRefused = await PlaceMeal(tree, lab);
        LooseObjectBody? refused = lab.Launcher.CurrentLaunchable;
        int refusedId = refused?.RuntimeId ?? 0;
        bool shookItsHead = placedRefused && await M4ObjectScenarioSupport.WaitFor(
            tree,
            () => lab.Buddy.Activity.Current == ActivityId.Refuse,
            FetchTimeoutTicks);

        // Sampled through the performance, not once: the reported defect was a pose, so it has
        // to hold for the whole shake. The pose itself is read over the back half of the
        // window, because the hands start where the pickup left them and spring to the carry
        // pose over the first few ticks. The clip and the frontal turn are read on process
        // frames, where the expressive layer runs in either presentation mode.
        bool heldInOneHand = true;
        bool poseSampled = false;
        bool shakeClipPlayed = false;
        bool frontalThroughout = true;
        bool yawBounded = true;
        bool pitchAndRollStable = true;
        bool headTranslationStable = true;
        float shakeLeft = 0.0f;
        float shakeRight = 0.0f;
        var yawLobePeaks = new float[4];
        var yawLobeSigns = new int[4];
        int yawLobeCount = 0;
        int activeYawSign = 0;
        int neutralFrames = 0;
        int maximumMiddleNeutralFrames = 0;
        float activeYawPeak = 0.0f;
        bool tooManyYawLobes = false;
        float carrySideways = 0.0f;
        while (shookItsHead && lab.Buddy.ObjectInteraction.IsRefusing)
        {
            await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            LooseObjectBody? inHand = lab.Objects.FindBody(refusedId);
            if (lab.Buddy.Activity.RefuseProgress >= 0.5f &&
                lab.Buddy.ObjectInteraction.IsHolding &&
                GodotObject.IsInstanceValid(inHand))
            {
                Vector2 left = lab.Buddy.Rig.LeftHand.GlobalPosition;
                Vector2 right = lab.Buddy.Rig.RightHand.GlobalPosition;
                Vector2 between = (left + right) * 0.5f;
                float nearer = Mathf.Min(
                    left.DistanceTo(inHand!.GlobalPosition),
                    right.DistanceTo(inHand.GlobalPosition));
                float clearance = lab.Buddy.Rig.LeftHand.Radius + inHand.Radius + 8.0f;
                carrySideways = Mathf.Abs(inHand.GlobalPosition.X - between.X);
                // One hand: resting on a hand, and NOT between them the way an eaten meal is.
                heldInOneHand &= nearer <= clearance && carrySideways >= 12.0f;
                poseSampled = true;
            }

            if (lab.Buddy.Activity.Current != ActivityId.Refuse)
                continue;

            shakeClipPlayed |= lab.Activities.CurrentClipName ==
                ActivityAnimator.ClipNameFor(ActivityId.Refuse);
            float headYaw = lab.VisualPresenter.AppliedActivityHeadYawDegrees;
            shakeLeft = Mathf.Min(shakeLeft, headYaw);
            shakeRight = Mathf.Max(shakeRight, headYaw);
            frontalThroughout &= Mathf.Abs(lab.VisualPresenter.AppliedYawDegrees) < 0.5f;
            yawBounded &= Mathf.Abs(headYaw) <= 30.5f;
            pitchAndRollStable &=
                Mathf.Abs(lab.VisualPresenter.AppliedHeadPitchDegrees) < 0.5f &&
                Mathf.Abs(lab.Activities.RotationFor((int)BuddyPartId.Head).X) < 0.001f &&
                Mathf.Abs(lab.Activities.RotationFor((int)BuddyPartId.Head).Z) < 0.001f;
            headTranslationStable &=
                lab.Activities.OffsetFor((int)BuddyPartId.Head).Length() < 0.05f;

            int yawSign = headYaw < -2.0f ? -1 : headYaw > 2.0f ? 1 : 0;
            if (yawSign == 0)
            {
                if (activeYawSign != 0)
                    neutralFrames++;
                continue;
            }

            if (activeYawSign == 0)
            {
                activeYawSign = yawSign;
                activeYawPeak = Mathf.Abs(headYaw);
                neutralFrames = 0;
            }
            else if (yawSign == activeYawSign)
            {
                activeYawPeak = Mathf.Max(activeYawPeak, Mathf.Abs(headYaw));
                neutralFrames = 0;
            }
            else
            {
                maximumMiddleNeutralFrames = Mathf.Max(
                    maximumMiddleNeutralFrames,
                    neutralFrames);
                if (yawLobeCount < yawLobePeaks.Length)
                {
                    yawLobePeaks[yawLobeCount] = activeYawPeak;
                    yawLobeSigns[yawLobeCount] = activeYawSign;
                }
                else
                {
                    tooManyYawLobes = true;
                }
                yawLobeCount++;
                activeYawSign = yawSign;
                activeYawPeak = Mathf.Abs(headYaw);
                neutralFrames = 0;
            }
        }

        if (activeYawSign != 0)
        {
            if (yawLobeCount < yawLobePeaks.Length)
            {
                yawLobePeaks[yawLobeCount] = activeYawPeak;
                yawLobeSigns[yawLobeCount] = activeYawSign;
            }
            else
            {
                tooManyYawLobes = true;
            }
            yawLobeCount++;
        }

        Vector2 torsoAtRelease = lab.Buddy.Rig.Torso.GlobalPosition;
        bool releasedIt = shookItsHead && await M4ObjectScenarioSupport.WaitFor(
            tree,
            () => !lab.Buddy.ObjectInteraction.IsHolding &&
                  !lab.Buddy.ObjectInteraction.IsRefusing,
            600);

        checks.Add(new StartupCheck(
            "the_refused_meal_is_held_in_one_hand",
            shookItsHead && poseSampled && heldInOneHand,
            $"shook={shookItsHead} sampled={poseSampled} sideways={carrySideways:F1}"));

        checks.Add(new StartupCheck(
            "the_refusal_smoothly_rotates_the_head_left_and_right_at_the_player",
            shakeClipPlayed &&
            frontalThroughout &&
            yawBounded &&
            pitchAndRollStable &&
            headTranslationStable &&
            !tooManyYawLobes &&
            yawLobeCount == 4 &&
            yawLobeSigns[0] == -1 &&
            yawLobeSigns[1] == 1 &&
            yawLobeSigns[2] == -1 &&
            yawLobeSigns[3] == 1 &&
            yawLobePeaks[0] >= 20.0f &&
            yawLobePeaks[0] <= 30.5f &&
            yawLobePeaks[1] < yawLobePeaks[0] &&
            yawLobePeaks[2] < yawLobePeaks[1] &&
            yawLobePeaks[3] < yawLobePeaks[2] &&
            maximumMiddleNeutralFrames <= 2 &&
            Mathf.Abs(lab.VisualPresenter.AppliedActivityHeadYawDegrees) < 0.5f,
            $"clip={shakeClipPlayed} left={shakeLeft:F1} right={shakeRight:F1} " +
            $"frontal={frontalThroughout} bounded={yawBounded} pitch_roll={pitchAndRollStable} " +
            $"translated={!headTranslationStable} lobes={yawLobeCount} " +
            $"peaks={yawLobePeaks[0]:F1}/{yawLobePeaks[1]:F1}/" +
            $"{yawLobePeaks[2]:F1}/{yawLobePeaks[3]:F1} " +
            $"middle_neutral_frames={maximumMiddleNeutralFrames}"));

        // The second reported defect: the food used to be flung aside on a discard impulse,
        // which read as it glitching away. It is put down instead — it falls from the hand that
        // held it and comes to rest below where the buddy was standing.
        for (int tick = 0; tick < 180 && GodotObject.IsInstanceValid(refused); tick++)
        {
            await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
            if (refused!.LinearVelocity.Length() < 1.0f && tick > 30)
                break;
        }

        float droppedSideways = GodotObject.IsInstanceValid(refused)
            ? Mathf.Abs(refused!.GlobalPosition.X - torsoAtRelease.X)
            : float.MaxValue;
        float droppedBelow = GodotObject.IsInstanceValid(refused)
            ? refused!.GlobalPosition.Y - torsoAtRelease.Y
            : float.MinValue;

        checks.Add(new StartupCheck(
            "the_refused_meal_is_put_down_below_the_buddy",
            releasedIt &&
            lab.Buddy.ObjectInteraction.DiscardCount == discardsBeforeRefusal &&
            lab.Buddy.ObjectInteraction.LastReleaseImpulse == Vector2.Zero &&
            droppedSideways <= 60.0f && droppedBelow > 0.0f,
            $"sideways={droppedSideways:F1} below={droppedBelow:F1} " +
            $"impulse={lab.Buddy.ObjectInteraction.LastReleaseImpulse.Length():F1} " +
            $"discards={lab.Buddy.ObjectInteraction.DiscardCount}"));

        checks.Add(new StartupCheck(
            "a_meal_it_has_no_room_for_is_refused_not_eaten",
            shookItsHead && releasedIt &&
            lab.Buddy.ObjectInteraction.RefusalCount == 1 &&
            lab.Buddy.ObjectInteraction.LastConsumeRejection == ConsumeRejection.TooFull &&
            lab.Buddy.ObjectInteraction.ConsumeSuccessCount == successesBeforeRefusal &&
            Mathf.Abs(lab.Progress.Mood - moodBeforeRefusal) < 0.01f,
            $"shook={shookItsHead} released={releasedIt} " +
            $"refusals={lab.Buddy.ObjectInteraction.RefusalCount} " +
            $"rejection={lab.Buddy.ObjectInteraction.LastConsumeRejection} " +
            $"successes={lab.Buddy.ObjectInteraction.ConsumeSuccessCount} " +
            $"mood={lab.Progress.Mood:F1}"));

        // The reported bug: the buddy used to walk back, pick the same meal up, drop it, and
        // repeat forever. It must now leave that meal where it landed.
        int pickupsAfterRefusal = 0;
        for (int tick = 0; tick < 1800; tick++)
        {
            await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
            if (lab.Buddy.ObjectInteraction.IsHolding &&
                lab.Buddy.ObjectInteraction.TrackedRuntimeId == refusedId)
            {
                pickupsAfterRefusal++;
                break;
            }
        }

        checks.Add(new StartupCheck(
            "a_refused_meal_is_left_alone",
            pickupsAfterRefusal == 0 &&
            lab.Buddy.ObjectInteraction.RefusalCount == 1 &&
            GodotObject.IsInstanceValid(refused),
            $"pickups={pickupsAfterRefusal} refusals={lab.Buddy.ObjectInteraction.RefusalCount} " +
            $"still_there={GodotObject.IsInstanceValid(refused)} " +
            $"fullness={lab.Progress.Fullness:F1}"));

        // Phase 5 — appetite returns. Drained through the same domain call the lifecycle
        // drives every accepted span; the scenario only supplies the elapsed time, because
        // the real clock is wall time and a headless run outpaces it.
        lab.Progress.DrainHunger(600.0, HungerActivity.Playing);
        bool eatsAgain = lab.Progress.WouldEat(MealHungerFill) &&
            await M4ObjectScenarioSupport.WaitFor(
                tree,
                () => lab.Buddy.ObjectInteraction.ConsumeSuccessCount > successesBeforeRefusal,
                ConsumeTimeoutTicks);

        checks.Add(new StartupCheck(
            "the_same_meal_is_wanted_again_once_there_is_room",
            eatsAgain,
            $"fullness={lab.Progress.Fullness:F1} appetite={lab.Progress.Appetite:F1} " +
            $"successes={lab.Buddy.ObjectInteraction.ConsumeSuccessCount} " +
            $"was={successesBeforeRefusal}"));

        messages.Add(
            $"meals={meals} successes={lab.Buddy.ObjectInteraction.ConsumeSuccessCount} " +
            $"refusals={lab.Buddy.ObjectInteraction.RefusalCount} " +
            $"cancels={lab.Buddy.ObjectInteraction.ConsumeCancelCount} " +
            $"fullness={lab.Progress.Fullness:F1} mood={lab.Progress.Mood:F1}");
        await M4ObjectScenarioSupport.Cleanup(tree, lab);

        bool passed = true;
        foreach (StartupCheck check in checks) passed &= check.Passed;
        return new ScenarioResult(passed, checks, messages);
    }

    /// <summary>Places one Meal and waits for the buddy to finish eating it.</summary>
    private static async Task<bool> FeedOne(SceneTree tree, BuddyLab lab)
    {
        int before = lab.Buddy.ObjectInteraction.ConsumeSuccessCount;
        if (!await PlaceMeal(tree, lab))
            return false;

        return await M4ObjectScenarioSupport.WaitFor(
            tree,
            () => lab.Buddy.ObjectInteraction.ConsumeSuccessCount == before + 1,
            ConsumeTimeoutTicks);
    }

    /// <summary>
    /// Places one Meal on the floor beside the buddy through the real launcher, which also
    /// clears whatever was in the room — so each phase starts from one object and the previous
    /// phase's leftovers cannot be re-fetched mid-measurement.
    /// </summary>
    private static async Task<bool> PlaceMeal(SceneTree tree, BuddyLab lab)
    {
        Rect2 room = lab.Boundaries.InnerBounds;
        float torsoX = lab.Buddy.Rig.Torso.GlobalPosition.X;
        float side = room.End.X - torsoX > 110.0f ? 1.0f : -1.0f;
        float spawnX = Mathf.Clamp(
            torsoX + (side * 80.0f),
            room.Position.X + 20.0f,
            room.End.X - 20.0f);
        lab.Launcher.RequestSpawn(
            ContentIds.ToolMeal,
            new Vector2(spawnX, room.End.Y - 24.0f));

        // The launcher consumes queued intent on the root's routed tick, never inline.
        for (int tick = 0; tick < 8; tick++)
            await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);

        LooseObjectBody? meal = lab.Launcher.CurrentLaunchable;
        return GodotObject.IsInstanceValid(meal) &&
            meal!.SemanticContentId == ContentIds.ToolMeal;
    }
}
