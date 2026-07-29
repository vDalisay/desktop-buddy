using System.Collections.Generic;
using System.Threading.Tasks;
using DesktopBuddy.App;
using DesktopBuddy.Buddy.Physics;
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
        // Wait on the attachment, not on catching a single frame of the Hold phase: the ball
        // is thrown from across the room now, so the buddy may have to close on it first.
        bool held = await M4ObjectScenarioSupport.WaitFor(
            tree,
            () => lab.Buddy.ObjectInteraction.IsHolding && lab.Buddy.ObjectInteraction.IsAttached,
            900);

        bool registered = runtimeId != 0 &&
            lab.Objects.TryGetSnapshot(runtimeId, out LooseObjectSnapshot heldSnapshot) &&
            heldSnapshot.BuddyHeld;
        bool physical = lab.Buddy.ActiveDrive.LastLeftObjectHandForce.Length() > 0.0f ||
            lab.Buddy.ActiveDrive.LastRightObjectHandForce.Length() > 0.0f;
        bool careOnce = lab.Buddy.ObjectInteraction.CatchCareCount == 1 &&
            Mathf.Abs(lab.Progress.Mood - (moodBefore + 1.0f)) < 0.01f;
        bool exceptions = lab.Buddy.ObjectInteraction.CollisionExceptionsActive;

        // The arms must never stretch past a minimal extension, and a caught object must be
        // stuck to the hand rather than sprung toward the buddy (owner correction 2026-07-26).
        float reachLimit = lab.Buddy.ObjectInteraction.Profile.MaximumReach;
        float commandedReach = lab.Buddy.ObjectInteraction.MaximumCommandedReach;
        bool attached = lab.Buddy.ObjectInteraction.IsAttached &&
            GodotObject.IsInstanceValid(ball) && ball!.Freeze;
        float handGap = attached
            ? Mathf.Min(
                lab.Buddy.Rig.LeftHand.GlobalPosition.DistanceTo(ball!.GlobalPosition),
                lab.Buddy.Rig.RightHand.GlobalPosition.DistanceTo(ball.GlobalPosition))
            : float.MaxValue;
        checks.Add(new StartupCheck(
            "hands_stay_inside_the_reach_envelope",
            commandedReach > 0.0f && commandedReach <= reachLimit + 0.01f,
            $"commanded={commandedReach:F2} limit={reachLimit:F2}"));
        checks.Add(new StartupCheck(
            "caught_object_sticks_to_a_hand",
            attached && handGap <= reachLimit,
            $"attached={attached} hand_gap={handGap:F2}"));

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

        // Strictly after the first lab is gone: every lab instance shares one 2D physics
        // space, so two live labs let one buddy shove the other's test objects around.
        (bool scooped, string scoopDetail) = await RunGroundPickup(tree, seed);
        checks.Add(new StartupCheck("resting_ball_is_scooped_off_the_floor", scooped, scoopDetail));
        messages.Add(scoopDetail);

        bool passed = true;
        foreach (StartupCheck check in checks) passed &= check.Passed;
        return new ScenarioResult(passed, checks, messages);
    }

    /// <summary>
    /// End-to-end ground pickup in a fresh lab: drop a ball on the floor beside the buddy,
    /// let it settle, and require the buddy to walk over, scoop it, and end up holding it
    /// with the object attached between its hands.
    /// </summary>
    private static async Task<(bool, string)> RunGroundPickup(SceneTree tree, ulong seed)
    {
        BuddyLab? lab = await M4ObjectScenarioSupport.LoadLab(tree, seed);
        if (lab is null)
            return (false, "ground pickup lab failed to load");

        Rect2 room = lab.Boundaries.InnerBounds;
        float floorY = room.End.Y - lab.SafeObjectProfile.Radius - 1.0f;
        float torsoX = lab.Buddy.Rig.Torso.GlobalPosition.X;
        // Drop it on whichever side has room, clamped inside the walls: this check owns the
        // open-floor pickup. The wall case is `corner_scoop`, which used to be unreachable and
        // is now its own gate (owner report 2026-07-29).
        float side = room.End.X - torsoX > 110.0f ? 1.0f : -1.0f;
        float spawnX = Mathf.Clamp(
            torsoX + (side * 90.0f),
            room.Position.X + lab.SafeObjectProfile.Radius + 4.0f,
            room.End.X - lab.SafeObjectProfile.Radius - 4.0f);
        LooseObjectBody? ball = lab.SpawnLooseObject(
            lab.SafeObjectProfile,
            new Vector2(spawnX, floorY));
        bool rested = ball is not null && await M4ObjectScenarioSupport.WaitFor(
            tree,
            () => lab.Objects.TryGetSnapshot(ball!.RuntimeId, out LooseObjectSnapshot s) && s.AtRest,
            600);

        float closest = float.MaxValue;
        int trackedId = 0;
        ObjectPhase deepest = ObjectPhase.Idle;
        bool held = false;
        for (int tick = 0; tick < 1800 && !held; tick++)
        {
            await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
            held = lab.Buddy.ObjectInteraction.IsHolding && lab.Buddy.ObjectInteraction.IsAttached;
            if (GodotObject.IsInstanceValid(ball))
            {
                closest = Mathf.Min(
                    closest,
                    Mathf.Abs(ball!.GlobalPosition.X - lab.Buddy.Rig.Torso.GlobalPosition.X));
            }
            if (lab.Buddy.ObjectInteraction.TrackedRuntimeId != 0)
                trackedId = lab.Buddy.ObjectInteraction.TrackedRuntimeId;
            if (lab.Buddy.ObjectInteraction.Phase > deepest)
                deepest = lab.Buddy.ObjectInteraction.Phase;
        }

        // Carried resting on top of one hand, in a natural pose out to the side — not clutched
        // to the chest and not inside the head (owner correction 2026-07-27).
        PuppetPartBody near = lab.Buddy.Rig.LeftHand;
        if (GodotObject.IsInstanceValid(ball) &&
            lab.Buddy.Rig.RightHand.GlobalPosition.DistanceTo(ball!.GlobalPosition) <
            near.GlobalPosition.DistanceTo(ball.GlobalPosition))
        {
            near = lab.Buddy.Rig.RightHand;
        }

        float seat = GodotObject.IsInstanceValid(ball)
            ? near.GlobalPosition.DistanceTo(ball!.GlobalPosition)
            : float.MaxValue;
        bool aboveHand = GodotObject.IsInstanceValid(ball) &&
            ball!.GlobalPosition.Y < near.GlobalPosition.Y;
        bool clearOfHead = GodotObject.IsInstanceValid(ball) &&
            lab.Buddy.Rig.Head.GlobalPosition.DistanceTo(ball!.GlobalPosition) >
                lab.Buddy.Rig.Head.Radius;
        bool onHand = held && aboveHand && clearOfHead &&
            seat <= near.Radius + lab.SafeObjectProfile.Radius + 6.0f;
        bool passed = rested && held && onHand;
        string detail = $"rested={rested} held={held} on_hand={onHand} " +
            $"seat={seat:F1} above={aboveHand} clear_of_head={clearOfHead} " +
            $"phase={lab.Buddy.ObjectInteraction.Phase} " +
            $"closest_dx={closest:F1} tracked={trackedId} ball={ball?.RuntimeId ?? 0} " +
            $"deepest={deepest} scoop_gate={lab.Buddy.ObjectInteraction.Profile.ScoopDistance:F0}";
        await M4ObjectScenarioSupport.Cleanup(tree, lab);
        return (passed, detail);
    }
}
