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
/// M5 Task 3 gate for the catalogue Meal: the M4 consume machinery driven by an authored
/// catalogue item rather than the laboratory food. Placement goes through the real launcher
/// (so the ownership check and the one-object spawn policy are exercised); the buddy then
/// fetches and eats it through the ordinary arbiter path.
///
/// <para>Four phases in one room: a cancelled meal charges nothing, a finished one pays
/// <c>+10</c> mood exactly once and starts the <c>60 s</c> cooldown, a second meal inside that
/// window is refused, and once it elapses the next meal is eaten normally.</para>
/// </summary>
public sealed class MealConsumeScenario : IScenario
{
    private const int CooldownTicks = 7200;
    private const float MealMoodGain = 10.0f;
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

        // Phase 1 — a meal abandoned mid-bite costs nothing and starts no wait (FR-008.10).
        float moodBeforeCancel = lab.Progress.Mood;
        bool placedFirst = await PlaceMeal(tree, lab);
        bool eating = placedFirst && await M4ObjectScenarioSupport.WaitFor(
            tree,
            () => lab.Buddy.Activity.Current == ActivityId.Eat,
            FetchTimeoutTicks);
        bool bitTwice = eating && await M4ObjectScenarioSupport.WaitFor(
            tree, () => lab.Buddy.Activity.EatBitesCompleted >= 2, 600);
        lab.Buddy.ObjectInteraction.CancelActiveInteraction();
        await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);

        int cooldownAfterCancel =
            lab.Buddy.ObjectInteraction.CooldownTicksRemaining(ContentIds.ToolMeal);
        checks.Add(new StartupCheck(
            "an_abandoned_meal_starts_no_cooldown",
            placedFirst && eating && bitTwice &&
            cooldownAfterCancel == 0 &&
            lab.Buddy.ObjectInteraction.ConsumeSuccessCount == 0 &&
            Mathf.Abs(lab.Progress.Mood - moodBeforeCancel) < 0.01f,
            $"placed={placedFirst} eating={eating} bites={lab.Buddy.Activity.EatBitesCompleted} " +
            $"cooldown={cooldownAfterCancel} successes={lab.Buddy.ObjectInteraction.ConsumeSuccessCount} " +
            $"mood={lab.Progress.Mood:F1} was={moodBeforeCancel:F1}"));

        // Phase 2 — a finished meal pays once and starts the wait.
        float moodBeforeMeal = lab.Progress.Mood;
        float treatInterestBefore = lab.Progress.InterestIn(FunActivityId.Treat);
        bool placedSecond = await PlaceMeal(tree, lab);
        bool consumed = placedSecond && await M4ObjectScenarioSupport.WaitFor(
            tree,
            () => lab.Buddy.ObjectInteraction.ConsumeSuccessCount == 1,
            ConsumeTimeoutTicks);
        int cooldownAtSuccess =
            lab.Buddy.ObjectInteraction.CooldownTicksRemaining(ContentIds.ToolMeal);
        float treatInterestAfter = lab.Progress.InterestIn(FunActivityId.Treat);

        checks.Add(new StartupCheck(
            "a_finished_meal_pays_ten_mood_once",
            consumed &&
            Mathf.Abs(lab.Progress.Mood - (moodBeforeMeal + MealMoodGain)) < 0.01f &&
            cooldownAtSuccess == CooldownTicks,
            $"consumed={consumed} mood={lab.Progress.Mood:F1} was={moodBeforeMeal:F1} " +
            $"cooldown={cooldownAtSuccess} expected={CooldownTicks}"));

        checks.Add(new StartupCheck(
            "eating_a_meal_is_still_a_treat",
            consumed && treatInterestAfter < treatInterestBefore,
            $"interest_before={treatInterestBefore:F1} after={treatInterestAfter:F1}"));

        // Phase 3 — another meal inside the window is refused, and refusal costs nothing.
        float moodAfterMeal = lab.Progress.Mood;
        bool placedThird = await PlaceMeal(tree, lab);
        bool refused = placedThird && await M4ObjectScenarioSupport.WaitFor(
            tree,
            () => lab.Buddy.ObjectInteraction.LastConsumeRejection == ConsumeRejection.OnCooldown,
            FetchTimeoutTicks);
        checks.Add(new StartupCheck(
            "a_second_meal_inside_the_window_is_refused",
            refused &&
            lab.Buddy.ObjectInteraction.ConsumeSuccessCount == 1 &&
            Mathf.Abs(lab.Progress.Mood - moodAfterMeal) < 0.01f,
            $"refused={refused} rejection={lab.Buddy.ObjectInteraction.LastConsumeRejection} " +
            $"successes={lab.Buddy.ObjectInteraction.ConsumeSuccessCount} " +
            $"mood={lab.Progress.Mood:F1} was={moodAfterMeal:F1}"));

        // Phase 4 — the wait is real: once it elapses, the next meal is eaten normally.
        bool cooldownElapsed = await M4ObjectScenarioSupport.WaitFor(
            tree,
            () => lab.Buddy.ObjectInteraction.CooldownTicksRemaining(ContentIds.ToolMeal) == 0,
            CooldownTicks + 600);
        float moodBeforeFourth = lab.Progress.Mood;
        bool placedFourth = cooldownElapsed && await PlaceMeal(tree, lab);
        bool consumedAgain = placedFourth && await M4ObjectScenarioSupport.WaitFor(
            tree,
            () => lab.Buddy.ObjectInteraction.ConsumeSuccessCount == 2,
            ConsumeTimeoutTicks);
        checks.Add(new StartupCheck(
            "after_the_cooldown_a_meal_is_edible_again",
            cooldownElapsed && consumedAgain &&
            Mathf.Abs(lab.Progress.Mood - (moodBeforeFourth + MealMoodGain)) < 0.01f,
            $"elapsed={cooldownElapsed} consumed={consumedAgain} " +
            $"mood={lab.Progress.Mood:F1} was={moodBeforeFourth:F1}"));

        messages.Add(
            $"successes={lab.Buddy.ObjectInteraction.ConsumeSuccessCount} " +
            $"cancels={lab.Buddy.ObjectInteraction.ConsumeCancelCount} " +
            $"launcher_spawns={lab.Launcher.SpawnCount} mood={lab.Progress.Mood:F1}");
        await M4ObjectScenarioSupport.Cleanup(tree, lab);

        bool passed = true;
        foreach (StartupCheck check in checks) passed &= check.Passed;
        return new ScenarioResult(passed, checks, messages);
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
