using System.Collections.Generic;
using System.Threading.Tasks;
using DesktopBuddy.App;
using DesktopBuddy.Objects;
using Godot;

namespace DesktopBuddy.Testing;

/// <summary>
/// The FR-014 budget through the <b>real</b> registry, not the pure policy: thirty
/// independently spawned balls against a cap of twenty-four, with one of them in the buddy's
/// hands. The count never exceeds the cap, the held ball is never the victim, and eviction
/// order is oldest-first.
///
/// <para>The unit tests own the decision table; this owns the wiring — that every spawn path
/// actually goes through the one registry and that protection flags reach it from real
/// runtime state.</para>
/// </summary>
public sealed class ObjectBudgetScenario : IScenario
{
    private const int SpawnAttempts = 30;

    public string Id => "object_budget";

    public async Task<ScenarioResult> RunAsync(SceneTree tree, ulong seed)
    {
        var checks = new List<StartupCheck>();
        var messages = new List<string> { $"seed={seed}" };
        BuddyLab? lab = await M4ObjectScenarioSupport.LoadLab(tree, seed);
        if (lab is null)
        {
            checks.Add(new StartupCheck("object_budget_lab_loadable", false, "buddy_lab"));
            return new ScenarioResult(false, checks, messages);
        }

        // A ball in the hands is the protected case that matters most: the buddy is holding it
        // while the room fills up around it.
        lab.Progress.ApplyCareMood(30.0f);
        LooseObjectBody? carried = M4ObjectScenarioSupport.SpawnCatchCandidate(lab);
        bool held = carried is not null && await M4ObjectScenarioSupport.WaitFor(
            tree,
            () => lab.Buddy.ObjectInteraction.IsHolding && lab.Buddy.ObjectInteraction.IsAttached,
            900);
        int carriedId = carried?.RuntimeId ?? 0;

        Rect2 room = lab.Boundaries.InnerBounds;
        float floorY = room.End.Y - lab.SafeObjectProfile.Radius - 1.0f;
        var spawnedIds = new List<int>(SpawnAttempts);
        int peakCount = lab.Objects.Count;
        int refusals = 0;

        for (int index = 0; index < SpawnAttempts; index++)
        {
            // Spread along the floor so nothing stacks into a tower that could sleep the
            // physics space; the budget is about admission, not about where they land.
            float x = Mathf.Lerp(
                room.Position.X + 20.0f,
                room.End.X - 20.0f,
                index / (float)(SpawnAttempts - 1));
            LooseObjectBody? ball = lab.SpawnLooseObject(lab.SafeObjectProfile, new Vector2(x, floorY));
            if (ball is null)
                refusals++;
            else
                spawnedIds.Add(ball.RuntimeId);

            peakCount = Mathf.Max(peakCount, lab.Objects.Count);
            await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
        }

        checks.Add(new StartupCheck(
            "count_never_exceeds_the_cap",
            peakCount <= LooseObjectRegistry.Capacity &&
            lab.Objects.Count <= LooseObjectRegistry.Capacity,
            $"peak={peakCount} final={lab.Objects.Count} cap={LooseObjectRegistry.Capacity} " +
            $"refused={refusals}"));

        bool carriedSurvived = carriedId != 0 &&
            lab.Objects.TryGetSnapshot(carriedId, out LooseObjectSnapshot carriedSnapshot) &&
            carriedSnapshot.BuddyHeld &&
            GodotObject.IsInstanceValid(carried);
        checks.Add(new StartupCheck(
            "the_held_ball_is_never_evicted",
            held && carriedSurvived,
            $"held={held} survived={carriedSurvived} runtime={carriedId} " +
            $"evictions={lab.Objects.EvictionCount}"));

        // Oldest-first: the earliest unprotected spawns are gone and the latest are all live.
        int survivingEarly = 0;
        int overflow = spawnedIds.Count - (LooseObjectRegistry.Capacity - 1);
        for (int index = 0; index < overflow && index < spawnedIds.Count; index++)
        {
            if (lab.Objects.FindBody(spawnedIds[index]) is not null)
                survivingEarly++;
        }

        int survivingLate = 0;
        for (int index = Mathf.Max(0, spawnedIds.Count - 5); index < spawnedIds.Count; index++)
        {
            if (lab.Objects.FindBody(spawnedIds[index]) is not null)
                survivingLate++;
        }

        checks.Add(new StartupCheck(
            "eviction_takes_the_oldest_first",
            overflow > 0 && survivingEarly == 0 && survivingLate == 5,
            $"overflow={overflow} surviving_oldest={survivingEarly} surviving_newest={survivingLate} " +
            $"evictions={lab.Objects.EvictionCount}"));

        // Projectiles are explicitly out of this budget (RAGDOLL §10); until the guns land in
        // Task 5 there is nothing pooled to assert, so the count is simply the toy count.
        messages.Add(
            $"spawned={spawnedIds.Count} refused={refusals} peak={peakCount} " +
            $"final={lab.Objects.Count} evictions={lab.Objects.EvictionCount} " +
            $"rejected_admissions={lab.Objects.RejectedAdmissionCount}");
        await M4ObjectScenarioSupport.Cleanup(tree, lab);

        bool passed = true;
        foreach (StartupCheck check in checks) passed &= check.Passed;
        return new ScenarioResult(passed, checks, messages);
    }
}
