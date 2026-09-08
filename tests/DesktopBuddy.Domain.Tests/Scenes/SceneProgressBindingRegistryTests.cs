using System;
using DesktopBuddy.Domain.Autonomy;
using DesktopBuddy.Domain.Content;
using DesktopBuddy.Domain.Environment;
using DesktopBuddy.Domain.Persistence;
using DesktopBuddy.Domain.Scenes;
using Xunit;

namespace DesktopBuddy.Domain.Tests.Scenes;

public sealed class SceneProgressBindingRegistryTests
{
    [Fact]
    public void Two_buddies_share_one_player_ledger_but_keep_emotional_state_isolated()
    {
        BuddyIdentityId firstId = BuddyIdentityId.From(Guid.Parse("11111111-1111-1111-1111-111111111111"));
        BuddyIdentityId secondId = BuddyIdentityId.From(Guid.Parse("22222222-2222-2222-2222-222222222222"));
        BuddyPlacementId firstPlacement = BuddyPlacementId.From(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"));
        BuddyPlacementId secondPlacement = BuddyPlacementId.From(Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"));
        SceneDocument scene = new(
            SceneId.From(Guid.Parse("99999999-9999-9999-9999-999999999999")),
            "Lab",
            new EnvironmentLayout([]),
            [
                new BuddyPlacement(firstPlacement, firstId, new CanonicalRoomPosition(0.25f, 0.5f)),
                new BuddyPlacement(secondPlacement, secondId, new CanonicalRoomPosition(0.75f, 0.5f)),
            ]);
        PlayerProgressState player = CreatePlayer();
        BuddyIdentityState first = CreateBuddy(firstId, mood: 20.0f);
        BuddyIdentityState second = CreateBuddy(secondId, mood: -30.0f);

        var registry = new SceneProgressBindingRegistry(player, scene, [second, first]);
        SceneBuddyProgressBinding firstBinding = registry.ForPlacement(firstPlacement);
        SceneBuddyProgressBinding secondBinding = registry.ForBuddy(secondId);

        Assert.Equal(2, registry.Count);
        Assert.Same(player, firstBinding.Coordinator.Player);
        Assert.Same(player, secondBinding.Coordinator.Player);
        Assert.Same(first, firstBinding.Coordinator.Buddy);
        Assert.Same(second, secondBinding.Coordinator.Buddy);
        Assert.Same(player, firstBinding.Progress.PlayerProgress);
        Assert.Same(first, firstBinding.Progress.BuddyProgress);

        firstBinding.Progress.ApplyCareMood(5.0f);

        Assert.Equal(25.0f, first.Mood);
        Assert.Equal(-30.0f, second.Mood);
        Assert.Equal(1, player.Statistics.CareAwards);
    }

    [Fact]
    public void Ordered_bindings_follow_scene_placement_order_not_identity_input_order()
    {
        BuddyIdentityId firstId = BuddyIdentityId.From(Guid.NewGuid());
        BuddyIdentityId secondId = BuddyIdentityId.From(Guid.NewGuid());
        BuddyPlacementId firstPlacement = BuddyPlacementId.New();
        BuddyPlacementId secondPlacement = BuddyPlacementId.New();
        SceneDocument scene = new(
            SceneId.New(),
            "Order Test",
            new EnvironmentLayout([]),
            [
                new BuddyPlacement(firstPlacement, firstId, new CanonicalRoomPosition(0.2f, 0.5f)),
                new BuddyPlacement(secondPlacement, secondId, new CanonicalRoomPosition(0.8f, 0.5f)),
            ]);

        var registry = new SceneProgressBindingRegistry(
            CreatePlayer(),
            scene,
            [CreateBuddy(secondId, 0.0f), CreateBuddy(firstId, 0.0f)]);

        Assert.Equal(firstPlacement, registry.OrderedBindings[0].Placement.PlacementId);
        Assert.Equal(secondPlacement, registry.OrderedBindings[1].Placement.PlacementId);
        Assert.Equal(firstId, registry.OrderedBindings[0].Coordinator.Buddy.BuddyIdentityId);
        Assert.Equal(secondId, registry.OrderedBindings[1].Coordinator.Buddy.BuddyIdentityId);
    }

    [Fact]
    public void Missing_extra_or_duplicate_identity_fails_scene_composition()
    {
        BuddyIdentityId placedId = BuddyIdentityId.From(Guid.NewGuid());
        BuddyIdentityId extraId = BuddyIdentityId.From(Guid.NewGuid());
        SceneDocument scene = new(
            SceneId.New(),
            "Validation",
            new EnvironmentLayout([]),
            [new BuddyPlacement(BuddyPlacementId.New(), placedId, new CanonicalRoomPosition(0.5f, 0.5f))]);
        PlayerProgressState player = CreatePlayer();
        BuddyIdentityState placed = CreateBuddy(placedId, 0.0f);
        BuddyIdentityState extra = CreateBuddy(extraId, 0.0f);

        Assert.Throws<ArgumentException>(() =>
            new SceneProgressBindingRegistry(player, scene, []));
        Assert.Throws<ArgumentException>(() =>
            new SceneProgressBindingRegistry(player, scene, [placed, extra]));
        Assert.Throws<ArgumentException>(() =>
            new SceneProgressBindingRegistry(player, scene, [placed, placed]));
    }

    [Fact]
    public void Unknown_placement_or_buddy_never_falls_back_to_another_actor()
    {
        BuddyIdentityId id = BuddyIdentityId.From(Guid.NewGuid());
        SceneDocument scene = new(
            SceneId.New(),
            "No Fallback",
            new EnvironmentLayout([]),
            [new BuddyPlacement(BuddyPlacementId.New(), id, new CanonicalRoomPosition(0.5f, 0.5f))]);
        var registry = new SceneProgressBindingRegistry(CreatePlayer(), scene, [CreateBuddy(id, 0.0f)]);

        Assert.False(registry.TryForPlacement(BuddyPlacementId.New(), out _));
        Assert.Throws<KeyNotFoundException>(() => registry.ForPlacement(BuddyPlacementId.New()));
        Assert.Throws<KeyNotFoundException>(() => registry.ForBuddy(BuddyIdentityId.From(Guid.NewGuid())));
    }

    private static PlayerProgressState CreatePlayer()
    {
        var snapshot = new PlayerProgressSnapshot(
            Revision: 0,
            BalanceMilliCredits: 0,
            SelectedToolId: ContentIds.ToolGrab,
            UnlockedContentIds: [ContentIds.ToolGrab],
            Statistics: default,
            Times: default);
        return new PlayerProgressState(cashPerPain: 0.01, snapshot);
    }

    private static BuddyIdentityState CreateBuddy(BuddyIdentityId id, float mood) => new(
        new BuddyIdentitySnapshot(
            id,
            Revision: 0,
            CharacterId: null,
            Mood: mood,
            Fullness: 50.0f,
            HarmfulContentIds: [],
            Traits: BuddyTraits.Default));
}
