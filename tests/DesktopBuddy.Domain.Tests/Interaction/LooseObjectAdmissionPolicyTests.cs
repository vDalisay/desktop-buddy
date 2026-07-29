using System;
using DesktopBuddy.Domain.Interaction;
using Xunit;

namespace DesktopBuddy.Domain.Tests.Interaction;

public sealed class LooseObjectAdmissionPolicyTests
{
    [Fact]
    public void TheCapIsTheOneDeclaredNumber() =>
        Assert.Equal(24, LooseObjectAdmissionPolicy.Capacity);

    [Fact]
    public void AFreeSlotIsUsedBeforeAnythingIsEvicted()
    {
        Span<LooseObjectSlot> slots = Full();
        slots[7] = default;

        AdmissionDecision decision = LooseObjectAdmissionPolicy.Decide(slots);

        Assert.Equal(AdmissionOutcome.FreeSlot, decision.Outcome);
        Assert.Equal(7, decision.Slot);
    }

    [Fact]
    public void AtCapacityTheOldestEvictableObjectIsChosen()
    {
        // Spawn order, not slot order: the registry reuses slots, so a low index is not a
        // young object and the rule must never be allowed to drift into "first slot wins".
        Span<LooseObjectSlot> slots = Full();
        slots[5] = slots[5] with { SpawnSequence = 3 };
        slots[19] = slots[19] with { SpawnSequence = 1 };
        slots[2] = slots[2] with { SpawnSequence = 2 };

        AdmissionDecision decision = LooseObjectAdmissionPolicy.Decide(slots);

        Assert.Equal(AdmissionOutcome.Evict, decision.Outcome);
        Assert.Equal(19, decision.Slot);
    }

    [Theory]
    [InlineData("player")]
    [InlineData("buddy")]
    [InlineData("explicit")]
    [InlineData("hazardous")]
    [InlineData("unsafe")]
    public void AProtectedObjectIsNeverTheVictimEvenWhenItIsOldest(string protection)
    {
        Span<LooseObjectSlot> slots = Full();
        // The oldest object of all, and untouchable; the next-oldest must go instead.
        slots[4] = Protect(slots[4] with { SpawnSequence = 0 }, protection);
        slots[11] = slots[11] with { SpawnSequence = 1 };

        AdmissionDecision decision = LooseObjectAdmissionPolicy.Decide(slots);

        Assert.Equal(AdmissionOutcome.Evict, decision.Outcome);
        Assert.Equal(11, decision.Slot);
    }

    [Fact]
    public void WhenEverythingIsProtectedTheSpawnIsRefusedNotForced()
    {
        Span<LooseObjectSlot> slots = Full();
        for (int index = 0; index < slots.Length; index++)
            slots[index] = slots[index] with { BuddyHeld = true };

        AdmissionDecision decision = LooseObjectAdmissionPolicy.Decide(slots);

        Assert.Equal(AdmissionOutcome.Refused, decision.Outcome);
        Assert.Equal(-1, decision.Slot);
    }

    [Fact]
    public void RepeatedAdmissionNeverExceedsTheCap()
    {
        // The registry's own loop, in miniature: 30 independent spawns against 24 slots.
        Span<LooseObjectSlot> slots = stackalloc LooseObjectSlot[LooseObjectAdmissionPolicy.Capacity];
        ulong sequence = 1;
        int live = 0;

        for (int spawn = 0; spawn < 30; spawn++)
        {
            AdmissionDecision decision = LooseObjectAdmissionPolicy.Decide(slots);
            Assert.NotEqual(AdmissionOutcome.Refused, decision.Outcome);
            if (decision.Outcome == AdmissionOutcome.Evict)
                live--;

            slots[decision.Slot] = new LooseObjectSlot(
                Occupied: true,
                SafeToEvict: true,
                Hazardous: false,
                PlayerHeld: false,
                BuddyHeld: false,
                ExplicitlyProtected: false,
                SpawnSequence: sequence++);
            live++;
            Assert.True(live <= LooseObjectAdmissionPolicy.Capacity, $"live={live}");
        }

        Assert.Equal(LooseObjectAdmissionPolicy.Capacity, live);
    }

    [Fact]
    public void AHeldObjectSurvivesAFloodOfSpawns()
    {
        Span<LooseObjectSlot> slots = stackalloc LooseObjectSlot[LooseObjectAdmissionPolicy.Capacity];
        slots[0] = new LooseObjectSlot(true, true, false, false, BuddyHeld: true, false, SpawnSequence: 1);
        ulong sequence = 2;

        for (int spawn = 0; spawn < 60; spawn++)
        {
            AdmissionDecision decision = LooseObjectAdmissionPolicy.Decide(slots);
            Assert.NotEqual(0, decision.Slot);
            slots[decision.Slot] = new LooseObjectSlot(
                true, true, false, false, false, false, sequence++);
        }

        Assert.True(slots[0].BuddyHeld);
        Assert.Equal(1UL, slots[0].SpawnSequence);
    }

    private static Span<LooseObjectSlot> Full()
    {
        var slots = new LooseObjectSlot[LooseObjectAdmissionPolicy.Capacity];
        for (int index = 0; index < slots.Length; index++)
        {
            slots[index] = new LooseObjectSlot(
                Occupied: true,
                SafeToEvict: true,
                Hazardous: false,
                PlayerHeld: false,
                BuddyHeld: false,
                ExplicitlyProtected: false,
                SpawnSequence: (ulong)(100 + index));
        }

        return slots;
    }

    private static LooseObjectSlot Protect(LooseObjectSlot slot, string protection) => protection switch
    {
        "player" => slot with { PlayerHeld = true },
        "buddy" => slot with { BuddyHeld = true },
        "explicit" => slot with { ExplicitlyProtected = true },
        "hazardous" => slot with { Hazardous = true },
        "unsafe" => slot with { SafeToEvict = false },
        _ => throw new ArgumentOutOfRangeException(nameof(protection), protection, null),
    };
}
