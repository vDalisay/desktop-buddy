using System.Collections.Generic;
using System.Threading.Tasks;
using DesktopBuddy.App;
using DesktopBuddy.Domain.Autonomy;
using DesktopBuddy.Objects;
using Godot;

namespace DesktopBuddy.Testing;

/// <summary>M4 Task 2 gate for sensor wiring, physical catch/hold, and registry protection.</summary>
public sealed class ObjectCatchHoldScenario : IScenario
{
    public string Id => "object_catch_hold";

    public async Task<ScenarioResult> RunAsync(SceneTree tree, ulong seed)
    {
        var checks = new List<StartupCheck>();
        var messages = new List<string> { $"seed={seed}" };
        BuddyLab? lab = await M4ObjectScenarioSupport.LoadLab(tree, seed);
        if (lab is null)
        {
            checks.Add(new StartupCheck("object_lab_loadable", false, "buddy_lab"));
            return new ScenarioResult(false, checks, messages);
        }

        lab.Progress.ApplyCareMood(30.0f);
        StartupCheck sensor = StartupValidator.ValidateInteractionSense(lab.Buddy.ObjectInteraction);
        checks.Add(sensor);

        float moodBefore = lab.Progress.Mood;
        LooseObjectBody? ball = M4ObjectScenarioSupport.SpawnCatchCandidate(lab);
        int runtimeId = ball?.RuntimeId ?? 0;
        bool sensed = await M4ObjectScenarioSupport.WaitFor(
            tree, () => lab.Buddy.ObjectInteraction.SensedCount > 0, 120);
        bool held = await M4ObjectScenarioSupport.WaitForPhase(tree, lab, ObjectPhase.Hold, 240);

        bool registered = runtimeId != 0 &&
            lab.Objects.TryGetSnapshot(runtimeId, out LooseObjectSnapshot heldSnapshot) &&
            heldSnapshot.BuddyHeld;
        bool physical = lab.Buddy.ActiveDrive.LastLeftObjectHandForce.Length() > 0.0f ||
            lab.Buddy.ActiveDrive.LastRightObjectHandForce.Length() > 0.0f ||
            lab.Buddy.ActiveDrive.LastObjectForce.Length() > 0.0f;
        bool careOnce = lab.Buddy.ObjectInteraction.CatchCareCount == 1 &&
            Mathf.Abs(lab.Progress.Mood - (moodBefore + 1.0f)) < 0.01f;
        bool exceptions = lab.Buddy.ObjectInteraction.CollisionExceptionsActive;

        LooseObjectBody? firstEvictable = null;
        int firstEvictableId = 0;
        for (int index = 0; index < LooseObjectRegistry.Capacity; index++)
        {
            LooseObjectBody? filler = lab.SpawnLooseObject(
                lab.SafeObjectProfile,
                new Vector2(30.0f + index, 40.0f));
            if (filler is null)
                continue;
            filler.Freeze = true;
            if (firstEvictable is null)
            {
                firstEvictable = filler;
                firstEvictableId = filler.RuntimeId;
            }
        }

        bool heldSurvivedCap = GodotObject.IsInstanceValid(ball) &&
            lab.Objects.FindBody(runtimeId) == ball &&
            lab.Objects.Count == LooseObjectRegistry.Capacity;
        bool oldestEligibleEvicted = firstEvictableId != 0 &&
            lab.Objects.FindBody(firstEvictableId) is null &&
            lab.Objects.EvictionCount >= 1;

        // Let deferred eviction/scene changes settle, then measure the real
        // routed lab tick with arbiter, object sensor/registry, progress, and
        // presentation all live after warm-up.
        for (int tick = 0; tick < 30; tick++)
            await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
        lab.BeginPhysicsAllocationProbe();
        for (int tick = 0; tick < 240; tick++)
            await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
        lab.EndPhysicsAllocationProbe();
        bool allocationFree =
            lab.PhysicsRegistryAllocationSamples == 240 &&
            lab.PhysicsRegistryAllocatedBytes == 0 &&
            lab.Buddy.Arbiter.AllocationSamples == 240 &&
            lab.Buddy.Arbiter.AllocatedBytes == 0;

        checks.Add(new StartupCheck(
            "object_catch_two_hand_hold",
            ball is not null && sensed && held && registered && physical && exceptions,
            $"spawned={ball is not null} sensed={sensed} held={held} registered={registered} " +
            $"physical={physical} exceptions={exceptions}"));
        checks.Add(new StartupCheck(
            "safe_catch_care_once",
            careOnce,
            $"count={lab.Buddy.ObjectInteraction.CatchCareCount} mood={lab.Progress.Mood:F1}"));
        checks.Add(new StartupCheck(
            "held_object_protected_from_eviction",
            heldSurvivedCap && oldestEligibleEvicted,
            $"count={lab.Objects.Count} evictions={lab.Objects.EvictionCount} " +
            $"held_survived={heldSurvivedCap} oldest_safe_evicted={oldestEligibleEvicted}"));
        checks.Add(new StartupCheck(
            "m4_live_tick_zero_managed_allocation",
            allocationFree,
            $"registry_samples={lab.PhysicsRegistryAllocationSamples} " +
            $"registry_bytes={lab.PhysicsRegistryAllocatedBytes} " +
            $"arbiter_samples={lab.Buddy.Arbiter.AllocationSamples} " +
            $"arbiter_bytes={lab.Buddy.Arbiter.AllocatedBytes}"));

        messages.Add(
            $"catch runtime={runtimeId} sensed={sensed} phase={lab.Buddy.ObjectInteraction.Phase} " +
            $"care={lab.Buddy.ObjectInteraction.CatchCareCount} object_count={lab.Objects.Count}");
        await M4ObjectScenarioSupport.Cleanup(tree, lab);
        bool passed = true;
        foreach (StartupCheck check in checks) passed &= check.Passed;
        return new ScenarioResult(passed, checks, messages);
    }
}
