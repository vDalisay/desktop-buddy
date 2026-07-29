using System.Collections.Generic;
using System.Threading.Tasks;
using DesktopBuddy.App;
using DesktopBuddy.Domain.Content;
using DesktopBuddy.Domain.Mood;
using DesktopBuddy.Domain.Presentation;
using Godot;

namespace DesktopBuddy.Testing;

/// <summary>
/// M4 Task 2 gate, on the M5 appetite rules: only bite five applies lab-food care and fills
/// the hunger bar; cancellation applies neither. The per-item reuse cooldown it originally
/// asserted was replaced by the hunger bar (owner decision 2026-07-29), so the reuse checks
/// now cover the rule that actually gates a second helping.
/// </summary>
public sealed class ConsumeCareCooldownScenario : IScenario
{
    public string Id => "consume_care_cooldown";

    public async Task<ScenarioResult> RunAsync(SceneTree tree, ulong seed)
    {
        var checks = new List<StartupCheck>();
        var messages = new List<string> { $"seed={seed}" };
        BuddyLab? lab = await M4ObjectScenarioSupport.LoadLab(tree, seed);
        if (lab is null)
        {
            checks.Add(new StartupCheck("consume_lab_loadable", false, "buddy_lab"));
            return new ScenarioResult(false, checks, messages);
        }

        float initialMood = lab.Progress.Mood;
        await M4ObjectScenarioSupport.SendKey(tree, Key.E);
        bool firstStarted = lab.Buddy.Activity.Current == ActivityId.Eat &&
            lab.Buddy.ObjectInteraction.IsHolding &&
            lab.Controls.LastControlKey == Key.E;
        bool reachedTwoBites = await M4ObjectScenarioSupport.WaitFor(
            tree, () => lab.Buddy.Activity.EatBitesCompleted >= 2, 480);
        await M4ObjectScenarioSupport.SendKey(tree, Key.E);
        await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
        bool cancelClean = reachedTwoBites &&
            Mathf.Abs(lab.Progress.Mood - initialMood) < 0.01f &&
            lab.Buddy.ObjectInteraction.CooldownTicksRemaining(ContentIds.CareLabFood) == 0 &&
            lab.Buddy.ObjectInteraction.ConsumeCancelCount == 1 &&
            lab.Buddy.Activity.Current == ActivityId.None;
        checks.Add(new StartupCheck(
            "failed_consume_no_care_or_cooldown",
            firstStarted && cancelClean,
            $"started={firstStarted} bites={lab.Buddy.Activity.EatBitesCompleted} " +
            $"mood={lab.Progress.Mood:F1} cooldown=" +
            $"{lab.Buddy.ObjectInteraction.CooldownTicksRemaining(ContentIds.CareLabFood)} " +
            $"cancels={lab.Buddy.ObjectInteraction.ConsumeCancelCount}"));

        await M4ObjectScenarioSupport.SendKey(tree, Key.E);
        bool fourBites = await M4ObjectScenarioSupport.WaitFor(
            tree, () => lab.Buddy.Activity.EatBitesCompleted == 4, 900);
        float beforeFinalMood = lab.Progress.Mood;
        float fullnessBeforeFinal = lab.Progress.Fullness;
        int beforeFinalCooldown =
            lab.Buddy.ObjectInteraction.CooldownTicksRemaining(ContentIds.CareLabFood);
        bool success = await M4ObjectScenarioSupport.WaitFor(
            tree, () => lab.Buddy.ObjectInteraction.ConsumeSuccessCount == 1, 180);
        int cooldownAtSuccess =
            lab.Buddy.ObjectInteraction.CooldownTicksRemaining(ContentIds.CareLabFood);
        float fullnessAtSuccess = lab.Progress.Fullness;
        bool finalLoweringContinues = success &&
            lab.Buddy.Activity.Current == ActivityId.Eat &&
            lab.Buddy.Activity.RemainingTicks > 0;
        bool fifthOnly = fourBites &&
            Mathf.Abs(beforeFinalMood - initialMood) < 0.01f &&
            beforeFinalCooldown == 0 &&
            success &&
            Mathf.Abs(lab.Progress.Mood - (initialMood + 10.0f)) < 0.01f &&
            // Food carries no reuse cooldown since the appetite bar replaced it (owner
            // decision 2026-07-29); the bar is what moved instead.
            cooldownAtSuccess == 0 &&
            fullnessAtSuccess >= fullnessBeforeFinal + 49.5f &&
            !lab.Buddy.ObjectInteraction.IsHolding &&
            finalLoweringContinues;
        checks.Add(new StartupCheck(
            "fifth_bite_applies_care_once",
            fifthOnly,
            $"four={fourBites} pre_mood={beforeFinalMood:F1} pre_cd={beforeFinalCooldown} " +
            $"success={success} mood={lab.Progress.Mood:F1} cd={cooldownAtSuccess} " +
            $"fullness={fullnessAtSuccess:F1} was={fullnessBeforeFinal:F1} " +
            $"holding={lab.Buddy.ObjectInteraction.IsHolding} lowering={finalLoweringContinues}"));

        await M4ObjectScenarioSupport.WaitFor(
            tree, () => lab.Buddy.Activity.Current == ActivityId.None, 240);

        // With room left in the bar, a second helping is simply accepted: the wait between
        // meals is gone, and appetite is now the only gate.
        await M4ObjectScenarioSupport.SendKey(tree, Key.E);
        await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
        bool secondAccepted =
            lab.Buddy.ObjectInteraction.LastConsumeRejection == ConsumeRejection.None &&
            lab.Buddy.Activity.Current == ActivityId.Eat;
        lab.Buddy.ObjectInteraction.CancelActiveInteraction();
        lab.Buddy.SetBehaviorActivity(ActivityId.None);
        await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
        checks.Add(new StartupCheck(
            "with_room_left_a_second_helping_is_accepted",
            secondAccepted,
            $"rejection={lab.Buddy.ObjectInteraction.LastConsumeRejection} " +
            $"activity={lab.Buddy.Activity.Current} fullness={lab.Progress.Fullness:F1}"));

        // Full to the brim, the same key is refused — for appetite, not for a timer.
        lab.Progress.FillHunger(lab.Progress.Appetite);
        float moodWhenFull = lab.Progress.Mood;
        await M4ObjectScenarioSupport.SendKey(tree, Key.E);
        await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
        bool fullRejects =
            lab.Buddy.ObjectInteraction.LastConsumeRejection == ConsumeRejection.TooFull &&
            lab.Buddy.Activity.Current != ActivityId.Eat &&
            lab.Buddy.ObjectInteraction.ConsumeSuccessCount == 1 &&
            Mathf.Abs(lab.Progress.Mood - moodWhenFull) < 0.01f;
        checks.Add(new StartupCheck(
            "a_full_buddy_refuses_food",
            fullRejects,
            $"rejection={lab.Buddy.ObjectInteraction.LastConsumeRejection} " +
            $"activity={lab.Buddy.Activity.Current} successes=" +
            $"{lab.Buddy.ObjectInteraction.ConsumeSuccessCount} " +
            $"fullness={lab.Progress.Fullness:F1} mood={lab.Progress.Mood:F1}"));

        messages.Add(
            $"consume cancel={lab.Buddy.ObjectInteraction.ConsumeCancelCount} " +
            $"success={lab.Buddy.ObjectInteraction.ConsumeSuccessCount} " +
            $"fullness={lab.Progress.Fullness:F1}");
        await M4ObjectScenarioSupport.Cleanup(tree, lab);
        bool passed = true;
        foreach (StartupCheck check in checks) passed &= check.Passed;
        return new ScenarioResult(passed, checks, messages);
    }
}
