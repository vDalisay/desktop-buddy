using System.Collections.Generic;
using System.Threading.Tasks;
using DesktopBuddy.App;
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
/// performed and thrown aside rather than silently dropped — and, crucially, is then left
/// alone instead of being fetched again forever — and appetite returning puts it back on the
/// menu.</para>
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

        // Phase 4 — the refusal: picked up once, performed, thrown aside, then left alone.
        int successesBeforeRefusal = lab.Buddy.ObjectInteraction.ConsumeSuccessCount;
        float moodBeforeRefusal = lab.Progress.Mood;
        bool placedRefused = await PlaceMeal(tree, lab);
        LooseObjectBody? refused = lab.Launcher.CurrentLaunchable;
        int refusedId = refused?.RuntimeId ?? 0;
        bool shookItsHead = placedRefused && await M4ObjectScenarioSupport.WaitFor(
            tree,
            () => lab.Buddy.Activity.Current == ActivityId.Refuse,
            FetchTimeoutTicks);
        bool releasedIt = shookItsHead && await M4ObjectScenarioSupport.WaitFor(
            tree,
            () => !lab.Buddy.ObjectInteraction.IsHolding &&
                  !lab.Buddy.ObjectInteraction.IsRefusing,
            600);

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
