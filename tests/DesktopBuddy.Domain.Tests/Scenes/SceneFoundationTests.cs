using System;
using DesktopBuddy.Domain.Environment;
using DesktopBuddy.Domain.Persistence;
using DesktopBuddy.Domain.Platform;
using DesktopBuddy.Domain.Scenes;
using Xunit;

namespace DesktopBuddy.Domain.Tests.Scenes;

public sealed class SceneFoundationTests
{
    [Fact]
    public void Scene_product_surface_starts_at_next_fest_with_owner_locked_ten_scene_cap()
    {
        BuildScopePolicy initialDemo = BuildScopePolicy.Resolve(false, true, false, false);
        BuildScopePolicy nextFest = BuildScopePolicy.Resolve(false, true, true, false);
        BuildScopePolicy full = BuildScopePolicy.Resolve(false, false, false, true);
        BuildScopePolicy itch = BuildScopePolicy.Resolve(true, false, false, false);

        Assert.False(initialDemo.IncludesScenes);
        Assert.False(initialDemo.CanCreateScene(0));
        Assert.Equal(0, initialDemo.MaximumSceneCount);
        Assert.False(itch.IncludesScenes);

        Assert.True(nextFest.IncludesScenes);
        Assert.Equal(BuildScopePolicy.NextFestMaximumSceneCount, nextFest.MaximumSceneCount);
        Assert.True(nextFest.CanCreateScene(9));
        Assert.False(nextFest.CanCreateScene(10));

        Assert.True(full.IncludesScenes);
        Assert.Null(full.MaximumSceneCount);
        Assert.True(full.CanCreateScene(10_000));
    }

    [Fact]
    public void Scene_document_allows_multiple_distinct_buddies_but_never_duplicate_identity()
    {
        BuddyIdentityId first = BuddyIdentityId.From(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"));
        BuddyIdentityId second = BuddyIdentityId.From(Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"));
        var environment = new EnvironmentLayout();

        var document = new SceneDocument(
            SceneId.From(Guid.Parse("10000000-0000-0000-0000-000000000001")),
            "Home",
            environment,
            [
                new BuddyPlacement(
                    BuddyPlacementId.From(Guid.Parse("20000000-0000-0000-0000-000000000001")),
                    first,
                    new CanonicalRoomPosition(0.25f, 0.75f)),
                new BuddyPlacement(
                    BuddyPlacementId.From(Guid.Parse("20000000-0000-0000-0000-000000000002")),
                    second,
                    new CanonicalRoomPosition(0.75f, 0.75f)),
            ]);

        Assert.Equal(2, document.BuddyPlacements.Count);
        Assert.Equal(first, document.BuddyPlacements[0].BuddyIdentityId);
        Assert.Equal(second, document.BuddyPlacements[1].BuddyIdentityId);

        Assert.Throws<ArgumentException>(() => new SceneDocument(
            SceneId.New(),
            "Bad duplicate",
            environment,
            [
                new BuddyPlacement(BuddyPlacementId.New(), first, new CanonicalRoomPosition(0.2f, 0.5f)),
                new BuddyPlacement(BuddyPlacementId.New(), first, new CanonicalRoomPosition(0.8f, 0.5f)),
            ]));
    }

    [Fact]
    public void Scene_document_rejects_duplicate_placement_ids_independently_of_buddy_identity()
    {
        BuddyPlacementId placementId = BuddyPlacementId.New();

        Assert.Throws<ArgumentException>(() => new SceneDocument(
            SceneId.New(),
            "Lab",
            new EnvironmentLayout(),
            [
                new BuddyPlacement(
                    placementId,
                    BuddyIdentityId.From(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa")),
                    new CanonicalRoomPosition(0.2f, 0.5f)),
                new BuddyPlacement(
                    placementId,
                    BuddyIdentityId.From(Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb")),
                    new CanonicalRoomPosition(0.8f, 0.5f)),
            ]));
    }

    [Fact]
    public void Legacy_scene_migration_is_retry_deterministic_and_uses_semantic_anchor_only()
    {
        BuddyIdentityId buddy = BuddyIdentityId.LegacyPrimary;
        var environment = new EnvironmentLayout();
        var anchor = new CanonicalRoomPosition(0.5f, 0.65f);

        SceneDocument first = LegacySceneMigrationPolicy.CreateDefaultScene(buddy, environment, anchor);
        SceneDocument retry = LegacySceneMigrationPolicy.CreateDefaultScene(buddy, environment, anchor);

        Assert.Equal(SceneId.LegacyHome, first.SceneId);
        Assert.Equal(first.SceneId, retry.SceneId);
        Assert.Equal(LegacySceneMigrationPolicy.DefaultSceneName, first.Name);
        Assert.Single(first.BuddyPlacements);
        Assert.Equal(BuddyPlacementId.LegacyPrimary, first.BuddyPlacements[0].PlacementId);
        Assert.Equal(buddy, first.BuddyPlacements[0].BuddyIdentityId);
        Assert.Equal(anchor, first.BuddyPlacements[0].Position);
        Assert.Equal(first.BuddyPlacements[0], retry.BuddyPlacements[0]);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(" Home")]
    [InlineData("Home ")]
    [InlineData("Home\nLab")]
    public void Scene_names_are_display_labels_not_unvalidated_storage_keys(string name)
    {
        Assert.Throws<ArgumentException>(() => new SceneDocument(
            SceneId.New(),
            name,
            new EnvironmentLayout()));
    }
}
