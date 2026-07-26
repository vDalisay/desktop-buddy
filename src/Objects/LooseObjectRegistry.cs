using System;
using DesktopBuddy.Domain.Content;
using DesktopBuddy.Grab;
using Godot;

namespace DesktopBuddy.Objects;

/// <summary>
/// Fixed-capacity owner of loose-object runtime identity and lifecycle metadata
/// (FR-014, RAGDOLL §10). It has no engine process callback; the composition
/// root advances rest/attribution tracking from its single routed physics tick.
/// </summary>
[GlobalClass]
public partial class LooseObjectRegistry : Node
{
    public const int Capacity = 24;

    private readonly Entry[] _entries = new Entry[Capacity];
    private int _nextRuntimeId = 1;
    private int _nextThrowToken = 1;
    private ulong _nextSpawnSequence = 1;

    public bool IsInitialized { get; private set; }
    public int Count { get; private set; }
    public int EvictionCount { get; private set; }
    public int RejectedAdmissionCount { get; private set; }

    public void Initialize()
    {
        Array.Clear(_entries);
        _nextRuntimeId = 1;
        _nextThrowToken = 1;
        _nextSpawnSequence = 1;
        Count = 0;
        EvictionCount = 0;
        RejectedAdmissionCount = 0;
        IsInitialized = true;
    }

    /// <summary>
    /// Registers a configured object. At capacity, evicts the oldest eligible
    /// safe/unheld/unprotected object; rejects cleanly if no such object exists.
    /// </summary>
    public bool TryRegister(LooseObjectBody body, LooseObjectProfile profile, out int runtimeId)
    {
        RequireInitialized();
        runtimeId = 0;
        bool profileValid = GodotObject.IsInstanceValid(profile) && profile.IsRuntimeValid;
        if (!GodotObject.IsInstanceValid(body) || !profileValid || body.RuntimeId != 0)
        {
            return false;
        }

        int slot = FindFreeSlot();
        if (slot < 0)
        {
            slot = FindOldestEvictableSlot();
            if (slot < 0)
            {
                RejectedAdmissionCount++;
                return false;
            }

            LooseObjectBody evicted = _entries[slot].Body!;
            ClearSlot(slot);
            EvictionCount++;
            if (GodotObject.IsInstanceValid(evicted))
                evicted.QueueFree();
        }

        runtimeId = NextNonZero(ref _nextRuntimeId);
        _entries[slot] = new Entry
        {
            Body = body,
            Profile = profile,
            RuntimeId = runtimeId,
            SpawnSequence = _nextSpawnSequence++,
            AtRest = true,
        };
        Count++;
        body.AttachRegistration(this, profile, runtimeId);
        return true;
    }

    public bool Unregister(LooseObjectBody body)
    {
        if (!IsInitialized || body is null)
            return false;

        int slot = FindSlot(body.RuntimeId);
        if (slot < 0 || _entries[slot].Body != body)
            return false;

        ClearSlot(slot);
        body.DetachRegistration();
        return true;
    }

    public LooseObjectBody? FindBody(int runtimeId)
    {
        int slot = FindSlot(runtimeId);
        return slot < 0 ? null : _entries[slot].Body;
    }

    public LooseObjectProfile? FindProfile(int runtimeId)
    {
        int slot = FindSlot(runtimeId);
        return slot < 0 ? null : _entries[slot].Profile;
    }

    public bool TryGetSnapshot(int runtimeId, out LooseObjectSnapshot snapshot)
    {
        int slot = FindSlot(runtimeId);
        if (slot < 0)
        {
            snapshot = default;
            return false;
        }

        ref Entry entry = ref _entries[slot];
        snapshot = new LooseObjectSnapshot(
            entry.RuntimeId,
            entry.Profile!.ContentId,
            entry.ThrowToken,
            entry.Profile.Consumable,
            entry.Profile.Hazardous,
            entry.AtRest,
            entry.PlayerHeld,
            entry.BuddyHeld,
            entry.ExplicitlyProtected,
            entry.SpawnSequence,
            entry.IgnoreTicks > 0);
        return true;
    }

    /// <summary>
    /// Starts a player-originating throw/catch event. The token survives bounces
    /// and is cleared only on rest or buddy release.
    /// </summary>
    public int MarkPlayerThrown(LooseObjectBody body, string attributionContentId = ContentIds.LooseObject)
    {
        int slot = SlotFor(body);
        if (slot < 0)
            return 0;

        ref Entry entry = ref _entries[slot];
        entry.ThrowToken = NextNonZero(ref _nextThrowToken);
        entry.AtRest = false;
        entry.RestTicks = 0;
        entry.PlayerHeld = false;
        body.SetImpactAttribution(attributionContentId);
        return entry.ThrowToken;
    }

    /// <summary>
    /// Buddy toss/discard is not a player throw and cannot earn catch care. It also starts an
    /// ignore window: without one the buddy re-commits to the object it just put down, which
    /// starves the priority 7 obstacle hop and reads as an obsessive pickup loop.
    /// </summary>
    public void MarkBuddyReleased(LooseObjectBody body, int ignoreTicks = 0)
    {
        int slot = SlotFor(body);
        if (slot < 0)
            return;

        ref Entry entry = ref _entries[slot];
        entry.ThrowToken = 0;
        entry.AtRest = false;
        entry.RestTicks = 0;
        entry.BuddyHeld = false;
        entry.IgnoreTicks = Math.Max(0, ignoreTicks);
        body.SetImpactAttribution(ContentIds.LooseObject);
    }

    public void SetBuddyHeld(LooseObjectBody body, bool held)
    {
        int slot = SlotFor(body);
        if (slot < 0)
            return;

        ref Entry entry = ref _entries[slot];
        entry.BuddyHeld = held;
        if (held)
        {
            entry.AtRest = false;
            entry.RestTicks = 0;
        }
    }

    public void SetProtected(LooseObjectBody body, bool isProtected)
    {
        int slot = SlotFor(body);
        if (slot >= 0)
            _entries[slot].ExplicitlyProtected = isProtected;
    }

    /// <summary>Updates player-held and rest state from the root-owned fixed tick.</summary>
    public void PhysicsTick(in GrabState grab)
    {
        RequireInitialized();
        LooseObjectBody? playerHeld = grab.Active ? grab.Target as LooseObjectBody : null;

        for (int index = 0; index < Capacity; index++)
        {
            ref Entry entry = ref _entries[index];
            LooseObjectBody? body = entry.Body;
            if (body is null || !GodotObject.IsInstanceValid(body))
                continue;

            if (entry.IgnoreTicks > 0)
                entry.IgnoreTicks--;

            entry.PlayerHeld = body == playerHeld;
            if (entry.PlayerHeld)
            {
                // Picking it back up is an invitation, so stop ignoring it immediately.
                entry.IgnoreTicks = 0;
            }
            if (entry.PlayerHeld || entry.BuddyHeld)
            {
                entry.AtRest = false;
                entry.RestTicks = 0;
                continue;
            }

            float speed = body.LinearVelocity.Length();
            float edgeSpeed = speed + (Mathf.Abs(body.AngularVelocity) * body.Radius);
            if (edgeSpeed <= entry.Profile!.RestSpeedThreshold || body.Sleeping)
            {
                entry.RestTicks++;
                if (entry.RestTicks >= entry.Profile.RestTicksRequired)
                {
                    entry.AtRest = true;
                    entry.ThrowToken = 0;
                    body.SetImpactAttribution(ContentIds.LooseObject);
                }
            }
            else
            {
                entry.RestTicks = 0;
                entry.AtRest = false;
            }
        }
    }

    /// <summary>
    /// Re-anchors interpolation for every live object. Called when the render loop restarts
    /// after hidden mode so no object visually snaps from its pre-hide transform (FR-015.10).
    /// </summary>
    public void ResetInterpolation()
    {
        for (int index = 0; index < Capacity; index++)
        {
            LooseObjectBody? body = _entries[index].Body;
            if (body is not null && GodotObject.IsInstanceValid(body))
                body.ResetPhysicsInterpolation();
        }
    }

    private int FindFreeSlot()
    {
        for (int index = 0; index < Capacity; index++)
        {
            if (_entries[index].Body is null)
                return index;
        }
        return -1;
    }

    private int FindOldestEvictableSlot()
    {
        int best = -1;
        ulong oldest = ulong.MaxValue;
        for (int index = 0; index < Capacity; index++)
        {
            ref Entry entry = ref _entries[index];
            if (entry.Body is null || entry.Profile is null ||
                !entry.Profile.SafeToEvict || entry.Profile.Hazardous ||
                entry.PlayerHeld || entry.BuddyHeld || entry.ExplicitlyProtected ||
                entry.SpawnSequence >= oldest)
            {
                continue;
            }

            best = index;
            oldest = entry.SpawnSequence;
        }
        return best;
    }

    private int FindSlot(int runtimeId)
    {
        if (runtimeId == 0)
            return -1;
        for (int index = 0; index < Capacity; index++)
        {
            if (_entries[index].RuntimeId == runtimeId)
                return index;
        }
        return -1;
    }

    private int SlotFor(LooseObjectBody body) =>
        body is null ? -1 : FindSlot(body.RuntimeId);

    private void ClearSlot(int slot)
    {
        if (_entries[slot].Body is null)
            return;
        _entries[slot] = default;
        Count--;
    }

    private static int NextNonZero(ref int value)
    {
        int result = value++;
        if (result == 0)
            result = value++;
        if (value == 0)
            value = 1;
        return result;
    }

    private void RequireInitialized()
    {
        if (!IsInitialized)
            throw new InvalidOperationException("LooseObjectRegistry used before initialization.");
    }

    private struct Entry
    {
        public LooseObjectBody? Body;
        public LooseObjectProfile? Profile;
        public int RuntimeId;
        public int ThrowToken;
        public ulong SpawnSequence;
        public int RestTicks;
        public int IgnoreTicks;
        public bool AtRest;
        public bool PlayerHeld;
        public bool BuddyHeld;
        public bool ExplicitlyProtected;
    }
}

public readonly record struct LooseObjectSnapshot(
    int RuntimeId,
    string ContentId,
    int ThrowToken,
    bool Consumable,
    bool Hazardous,
    bool AtRest,
    bool PlayerHeld,
    bool BuddyHeld,
    bool Protected,
    ulong SpawnSequence,
    /// <summary>True while a post-release ignore window is still counting down.</summary>
    bool Ignored = false);
