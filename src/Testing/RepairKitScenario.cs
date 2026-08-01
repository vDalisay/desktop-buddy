using System.Collections.Generic;
using System.Threading.Tasks;
using DesktopBuddy.App;
using DesktopBuddy.Domain.Autonomy;
using DesktopBuddy.Domain.Buddy;
using DesktopBuddy.Domain.Content;
using DesktopBuddy.Objects;
using Godot;

namespace DesktopBuddy.Testing;

/// <summary>
/// M5 Task 10 gate for the Repair Kit. The kit is a care item with no appetite gate and no
/// cooldown (owner 2026-07-29, "it is not food, so nothing rations it"), and it is the one
/// consumable that also puts out what is hurting the buddy.
///
/// <para>Task A covers the data and the flag: the buddy eats a kit for its authored twenty
/// mood without starting a cooldown, a buddy with a full stomach still takes one because a
/// medkit is not a meal, and the flag stays off for the Meal and the Drink so food never
/// cures anything.</para>
/// </summary>
public sealed class RepairKitScenario : IScenario
{
    private const float KitMoodGain = 20.0f;
    private const int ConsumeTimeoutTicks = 3000;

    public string Id => "repair_kit";

    public async Task<ScenarioResult> RunAsync(SceneTree tree, ulong seed)
    {
        var checks = new List<StartupCheck>();
        var messages = new List<string> { $"seed={seed}" };
        BuddyLab? lab = await M4ObjectScenarioSupport.LoadLab(tree, seed);
        if (lab is null)
        {
            checks.Add(new StartupCheck("repair_kit_lab_loadable", false, "buddy_lab"));
            return new ScenarioResult(false, checks, messages);
        }

        LooseObjectProfile? kit = FindProfile(lab, ContentIds.ToolRepairKit);
        LooseObjectProfile? meal = FindProfile(lab, ContentIds.ToolMeal);
        LooseObjectProfile? drink = FindProfile(lab, ContentIds.ToolDrink);

        // The flag is the whole seam: one authored bool decides whether taking an item also
        // puts out a fire. Food must never carry it, or a sandwich cures burning.
        checks.Add(new StartupCheck(
            "meal_and_drink_do_not_clear_statuses",
            kit is not null && meal is not null && drink is not null &&
            kit.ClearsHarmfulStatuses &&
            !meal.ClearsHarmfulStatuses &&
            !drink.ClearsHarmfulStatuses,
            $"kit={kit?.ClearsHarmfulStatuses} meal={meal?.ClearsHarmfulStatuses} " +
            $"drink={drink?.ClearsHarmfulStatuses}"));

        // The kit's own authored tuning, read off the shipped Resource rather than restated:
        // zero cooldown and zero hunger fill ARE the "nothing rations it" decision.
        checks.Add(new StartupCheck(
            "the_kit_is_rationed_by_nothing",
            kit is not null &&
            kit.Consumable &&
            kit.Validate().Count == 0 &&
            Mathf.IsEqualApprox(kit.ConsumeMoodGain, KitMoodGain) &&
            kit.ConsumeCooldownTicks == 0 &&
            Mathf.IsZeroApprox(kit.ConsumeHungerFill),
            $"mood={kit?.ConsumeMoodGain:F1} cooldown={kit?.ConsumeCooldownTicks} " +
            $"fill={kit?.ConsumeHungerFill:F1} errors={kit?.Validate().Count}"));

        // The flag only means anything on something that can be taken, so an author who sets
        // it on scenery hears about it instead of wondering why nothing heals.
        var flaggedScenery = new LooseObjectProfile
        {
            ContentId = ContentIds.ToolRepairKit,
            Consumable = false,
            ClearsHarmfulStatuses = true,
        };
        checks.Add(new StartupCheck(
            "clearing_statuses_is_rejected_on_something_that_cannot_be_taken",
            flaggedScenery.Validate().Count == 1 && !flaggedScenery.IsRuntimeValid,
            $"errors={flaggedScenery.Validate().Count} runtime_valid={flaggedScenery.IsRuntimeValid}"));

        // A buddy that has just eaten its fill still accepts a kit: appetite gates food, and
        // this is not food. Pinned full first so the acceptance cannot be luck.
        lab.Progress.FillHunger(200.0f);
        float moodBefore = lab.Progress.Mood;
        float treatInterestBefore = lab.Progress.InterestIn(FunActivityId.Treat);
        int refusalsBefore = lab.Buddy.ObjectInteraction.RefusalCount;
        bool full = !lab.Progress.WouldEat(1.0f);
        bool consumed = await FeedOneKit(tree, lab);
        int cooldownAfter =
            lab.Buddy.ObjectInteraction.CooldownTicksRemaining(ContentIds.ToolRepairKit);

        checks.Add(new StartupCheck(
            "a_full_buddy_still_accepts_one",
            full && consumed &&
            lab.Buddy.ObjectInteraction.RefusalCount == refusalsBefore,
            $"full={full} consumed={consumed} " +
            $"fullness={lab.Progress.Fullness:F1} " +
            $"refusals={lab.Buddy.ObjectInteraction.RefusalCount} was={refusalsBefore}"));

        checks.Add(new StartupCheck(
            "buddy_eats_a_repair_kit_for_twenty_mood",
            consumed &&
            Mathf.Abs(lab.Progress.Mood - (moodBefore + KitMoodGain)) < 0.01f &&
            cooldownAfter == 0 &&
            lab.Progress.InterestIn(FunActivityId.Treat) < treatInterestBefore,
            $"mood={lab.Progress.Mood:F1} was={moodBefore:F1} cooldown={cooldownAfter} " +
            $"treat_before={treatInterestBefore:F1} " +
            $"after={lab.Progress.InterestIn(FunActivityId.Treat):F1}"));

        // --- Task B: the player-thrown route ---
        // A missed throw applies nothing and waits (FR-008.10). Thrown along the floor away
        // from the buddy, so nothing about it touches anybody.
        float moodBeforeMiss = lab.Progress.Mood;
        int contactCareBeforeMiss = lab.Buddy.ObjectInteraction.ContactCareCount;
        LooseObjectBody? missed = ThrowKitWide(lab, kit!);
        bool missedLanded = missed is not null && await M4ObjectScenarioSupport.WaitFor(
            tree,
            () => !GodotObject.IsInstanceValid(missed) ||
                  missed!.LinearVelocity.Length() < 5.0f,
            900);
        bool missedStillThere = GodotObject.IsInstanceValid(missed) &&
            lab.Objects.TryGetSnapshot(missed!.RuntimeId, out _);

        checks.Add(new StartupCheck(
            "a_missed_throw_applies_nothing_and_waits",
            missedLanded && missedStillThere &&
            lab.Buddy.ObjectInteraction.ContactCareCount == contactCareBeforeMiss &&
            Mathf.Abs(lab.Progress.Mood - moodBeforeMiss) < 0.01f,
            $"landed={missedLanded} still_there={missedStillThere} " +
            $"contact_care={lab.Buddy.ObjectInteraction.ContactCareCount} " +
            $"mood={lab.Progress.Mood:F1} was={moodBeforeMiss:F1}"));

        // The missed kit is deliberately left lying there: "and waits" is half the requirement,
        // and an unconscious buddy cannot go and fetch it during the phases that follow.
        // The real throw. Measured on a buddy that cannot reach for it, because the two
        // buddies this route exists for are the two that cannot: the kit has to land on a
        // body, not be caught by it.
        lab.Buddy.SetConsciousness(Consciousness.Unconscious);
        for (int tick = 0; tick < 60; tick++)
            await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);

        float moodBeforeThrow = lab.Progress.Mood;
        long impactsBeforeThrow = lab.Pipeline.ScoredImpactCount;
        int contactCareBefore = lab.Buddy.ObjectInteraction.ContactCareCount;
        LooseObjectBody? thrown = M4ObjectScenarioSupport.SpawnCleanThrow(
            lab, profile: kit);
        int thrownId = thrown?.RuntimeId ?? 0;
        bool applied = thrown is not null && await M4ObjectScenarioSupport.WaitFor(
            tree,
            () => lab.Buddy.ObjectInteraction.ContactCareCount == contactCareBefore + 1,
            900);

        // Freed and unregistered: the slot is back, not leaked.
        bool despawned = !GodotObject.IsInstanceValid(thrown) ||
            !lab.Objects.TryGetSnapshot(thrownId, out _);

        checks.Add(new StartupCheck(
            "a_thrown_kit_applies_on_buddy_contact",
            applied && despawned &&
            Mathf.Abs(lab.Progress.Mood - (moodBeforeThrow + KitMoodGain)) < 0.01f,
            $"applied={applied} despawned={despawned} mood={lab.Progress.Mood:F1} " +
            $"was={moodBeforeThrow:F1} contact_care={lab.Buddy.ObjectInteraction.ContactCareCount}"));

        checks.Add(new StartupCheck(
            "double_contact_cannot_double_apply",
            applied &&
            lab.Buddy.ObjectInteraction.ContactCareCount == contactCareBefore + 1 &&
            (!GodotObject.IsInstanceValid(thrown) ||
             !lab.Buddy.ObjectInteraction.TryApplyThrownCareContact(thrown!)) &&
            lab.Buddy.ObjectInteraction.ContactCareCount == contactCareBefore + 1 &&
            Mathf.Abs(lab.Progress.Mood - (moodBeforeThrow + KitMoodGain)) < 0.01f,
            $"contact_care={lab.Buddy.ObjectInteraction.ContactCareCount} " +
            $"was={contactCareBefore} mood={lab.Progress.Mood:F1}"));

        // A medkit that bruises would enter itself into harmful memory and teach the buddy to
        // run from the thing that heals it.
        checks.Add(new StartupCheck(
            "kit_contact_scores_zero_impacts_and_no_harmful_memory",
            applied &&
            lab.Pipeline.ScoredImpactCount == impactsBeforeThrow &&
            !lab.Progress.IsContentHarmful(ContentIds.ToolRepairKit),
            $"impacts={lab.Pipeline.ScoredImpactCount} was={impactsBeforeThrow} " +
            $"harmful={lab.Progress.IsContentHarmful(ContentIds.ToolRepairKit)}"));

        lab.Buddy.SetConsciousness(Consciousness.Conscious);

        messages.Add(
            $"successes={lab.Buddy.ObjectInteraction.ConsumeSuccessCount} " +
            $"contact_care={lab.Buddy.ObjectInteraction.ContactCareCount} " +
            $"mood={lab.Progress.Mood:F1} fullness={lab.Progress.Fullness:F1}");
        await M4ObjectScenarioSupport.Cleanup(tree, lab);

        bool passed = true;
        foreach (StartupCheck check in checks) passed &= check.Passed;
        return new ScenarioResult(passed, checks, messages);
    }

    /// <summary>
    /// Throws a kit along the floor away from the buddy, through the real grab/release bridge
    /// so it carries a genuine player throw token and only the landing is different.
    /// </summary>
    private static LooseObjectBody? ThrowKitWide(BuddyLab lab, LooseObjectProfile kit)
    {
        Rect2 room = lab.Boundaries.InnerBounds;
        Vector2 chest = lab.Buddy.Rig.Torso.GlobalPosition;
        float side = chest.X - room.Position.X > room.End.X - chest.X ? -1.0f : 1.0f;
        Vector2 spawn = new(
            Mathf.Clamp(chest.X + (side * 60.0f), room.Position.X + 20.0f, room.End.X - 20.0f),
            room.End.Y - 30.0f);

        LooseObjectBody? body = lab.SpawnLooseObject(kit, spawn, Vector2.Zero, playerThrown: false);
        if (body is null || !lab.Grab.TryGrab(body, body.GlobalPosition))
            return null;

        lab.Grab.Release();
        body.LinearVelocity = new Vector2(side * 260.0f, 0.0f);
        return body;
    }

    private static LooseObjectProfile? FindProfile(BuddyLab lab, string contentId)
    {
        foreach (LooseObjectProfile profile in lab.Launcher.LaunchableProfiles)
        {
            if (GodotObject.IsInstanceValid(profile) && profile.ContentId == contentId)
                return profile;
        }

        return null;
    }

    /// <summary>Places one kit beside the buddy and waits for it to be taken.</summary>
    private static async Task<bool> FeedOneKit(SceneTree tree, BuddyLab lab)
    {
        int before = lab.Buddy.ObjectInteraction.ConsumeSuccessCount;
        Rect2 room = lab.Boundaries.InnerBounds;
        float torsoX = lab.Buddy.Rig.Torso.GlobalPosition.X;
        float side = room.End.X - torsoX > 110.0f ? 1.0f : -1.0f;
        float spawnX = Mathf.Clamp(
            torsoX + (side * 80.0f),
            room.Position.X + 20.0f,
            room.End.X - 20.0f);
        lab.Launcher.RequestSpawn(
            ContentIds.ToolRepairKit,
            new Vector2(spawnX, room.End.Y - 24.0f));

        // The launcher consumes queued intent on the root's routed tick, never inline.
        for (int tick = 0; tick < 8; tick++)
            await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);

        LooseObjectBody? placed = lab.Launcher.CurrentLaunchable;
        if (!GodotObject.IsInstanceValid(placed) ||
            placed!.SemanticContentId != ContentIds.ToolRepairKit)
        {
            return false;
        }

        return await M4ObjectScenarioSupport.WaitFor(
            tree,
            () => lab.Buddy.ObjectInteraction.ConsumeSuccessCount == before + 1,
            ConsumeTimeoutTicks);
    }
}
