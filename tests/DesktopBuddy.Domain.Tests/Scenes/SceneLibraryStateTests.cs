using System;
using System.Collections.Generic;
using DesktopBuddy.Domain.Environment;
using DesktopBuddy.Domain.Persistence;
using DesktopBuddy.Domain.Platform;
using DesktopBuddy.Domain.Scenes;
using Xunit;

namespace DesktopBuddy.Domain.Tests.Scenes;

public sealed class SceneLibraryStateTests
{
    [Fact]
    public void Next_Fest_create_and_duplicate_stop_at_exactly_ten_scenes()
    {
        BuildScopePolicy scope = NextFest();
        var ids = new Queue<SceneId>();
        for (int i = 1; i <= 10; i++)
            ids.Enqueue(SceneId.From(GuidFromInt(i)));

        var library = new SceneLibraryState(
            scope,
            sceneIdFactory: () => ids.Dequeue());

        for (int i = 1; i <= BuildScopePolicy.NextFestMaximumSceneCount; i++)
        {
            SceneLibraryResult created = library.Create($"Scene {i}");
            Assert.True(created.Succeeded);
        }

        Assert.Equal(10, library.Count);
        Assert.False(library.CanCreate);
        Assert.Equal(SceneLibraryStatus.LimitReached, library.Create("Overflow").Status);
        Assert.Equal(
            SceneLibraryStatus.LimitReached,
            library.Duplicate(library.Scenes[0].SceneId).Status);
    }

    [Fact]
    public void Full_release_has_no_artificial_ten_scene_cap()
    {
        BuildScopePolicy scope = FullRelease();
        int next = 1;
        var library = new SceneLibraryState(
            scope,
            sceneIdFactory: () => SceneId.From(GuidFromInt(next++)));

        for (int i = 0; i < 14; i++)
            Assert.True(library.Create($"Scene {i + 1}").Succeeded);

        Assert.Equal(14, library.Count);
        Assert.True(library.CanCreate);
    }

    [Fact]
    public void Initial_demo_cannot_expose_scene_library_operations()
    {
        var library = new SceneLibraryState(InitialDemo());
        SceneId missing = SceneId.From(GuidFromInt(90));
        BuddyIdentityId buddy = BuddyIdentityId.From(GuidFromInt(91));

        Assert.Equal(SceneLibraryStatus.ScenesUnavailable, library.Create("Home").Status);
        Assert.Equal(SceneLibraryStatus.ScenesUnavailable, library.Switch(missing).Status);
        Assert.Equal(SceneLibraryStatus.ScenesUnavailable, library.Rename(missing, "Renamed").Status);
        Assert.Equal(SceneLibraryStatus.ScenesUnavailable, library.Duplicate(missing).Status);
        Assert.Equal(SceneLibraryStatus.ScenesUnavailable, library.Delete(missing).Status);
        Assert.Equal(
            SceneLibraryStatus.ScenesUnavailable,
            library.AddBuddy(missing, buddy, new CanonicalRoomPosition(0.5f, 0.7f)).Status);
    }

    [Fact]
    public void Switch_rename_duplicate_delete_preserve_stable_scene_semantics()
    {
        SceneId homeId = SceneId.From(GuidFromInt(1));
        SceneId labId = SceneId.From(GuidFromInt(2));
        SceneId copyId = SceneId.From(GuidFromInt(3));
        BuddyIdentityId buddyId = BuddyIdentityId.From(GuidFromInt(11));
        BuddyPlacementId placementId = BuddyPlacementId.From(GuidFromInt(21));
        BuddyPlacementId copiedPlacementId = BuddyPlacementId.From(GuidFromInt(22));
        var home = new SceneDocument(
            homeId,
            "Home",
            new EnvironmentLayout(),
            [new BuddyPlacement(placementId, buddyId, new CanonicalRoomPosition(0.4f, 0.7f))]);
        var lab = new SceneDocument(labId, "Lab", new EnvironmentLayout());
        var sceneIds = new Queue<SceneId>([copyId]);
        var placementIds = new Queue<BuddyPlacementId>([copiedPlacementId]);
        var library = new SceneLibraryState(
            NextFest(),
            [home, lab],
            activeSceneId: homeId,
            sceneIdFactory: () => sceneIds.Dequeue(),
            placementIdFactory: () => placementIds.Dequeue());

        Assert.True(library.Switch(labId).Succeeded);
        Assert.Equal(labId, library.ActiveSceneId);

        SceneLibraryResult renamed = library.Rename(labId, "Workshop");
        Assert.True(renamed.Succeeded);
        Assert.Equal(labId, renamed.Scene!.SceneId);
        Assert.Equal("Workshop", renamed.Scene.Name);

        SceneLibraryResult duplicated = library.Duplicate(homeId, "Home Backup");
        Assert.True(duplicated.Succeeded);
        Assert.Equal(copyId, duplicated.Scene!.SceneId);
        Assert.Single(duplicated.Scene.BuddyPlacements);
        Assert.Equal(buddyId, duplicated.Scene.BuddyPlacements[0].BuddyIdentityId);
        Assert.Equal(copiedPlacementId, duplicated.Scene.BuddyPlacements[0].PlacementId);
        Assert.NotEqual(placementId, duplicated.Scene.BuddyPlacements[0].PlacementId);
        Assert.Equal(homeId, library.Scenes[0].SceneId);
        Assert.Equal(copyId, library.Scenes[1].SceneId);

        Assert.True(library.Switch(copyId).Succeeded);
        SceneLibraryResult deleted = library.Delete(copyId);
        Assert.True(deleted.Succeeded);
        Assert.Equal(labId, library.ActiveSceneId);
        Assert.DoesNotContain(library.Scenes, scene => scene.SceneId == copyId);
    }

    [Fact]
    public void Deleting_the_last_scene_is_rejected()
    {
        SceneId id = SceneId.From(GuidFromInt(1));
        var scene = new SceneDocument(id, "Only", new EnvironmentLayout());
        var library = new SceneLibraryState(NextFest(), [scene], id);

        SceneLibraryResult result = library.Delete(id);

        Assert.Equal(SceneLibraryStatus.CannotDeleteLastScene, result.Status);
        Assert.Single(library.Scenes);
        Assert.Equal(id, library.ActiveSceneId);
    }

    [Fact]
    public void Buddy_roster_add_move_remove_is_scene_owned_and_prevents_duplicate_identity()
    {
        SceneId id = SceneId.From(GuidFromInt(1));
        BuddyIdentityId buddy = BuddyIdentityId.From(GuidFromInt(11));
        BuddyPlacementId placement = BuddyPlacementId.From(GuidFromInt(21));
        var scene = new SceneDocument(id, "Home", new EnvironmentLayout());
        var placements = new Queue<BuddyPlacementId>([placement]);
        var library = new SceneLibraryState(
            NextFest(),
            [scene],
            id,
            placementIdFactory: () => placements.Dequeue());

        SceneLibraryResult added = library.AddBuddy(
            id,
            buddy,
            new CanonicalRoomPosition(0.25f, 0.7f));
        Assert.True(added.Succeeded);
        Assert.Single(library.ActiveScene!.BuddyPlacements);
        Assert.Equal(placement, library.ActiveScene.BuddyPlacements[0].PlacementId);

        Assert.Equal(
            SceneLibraryStatus.BuddyAlreadyPresent,
            library.AddBuddy(id, buddy, new CanonicalRoomPosition(0.75f, 0.7f)).Status);

        SceneLibraryResult moved = library.MoveBuddy(
            id,
            placement,
            new CanonicalRoomPosition(0.8f, 0.6f));
        Assert.True(moved.Succeeded);
        Assert.Equal(placement, library.ActiveScene.BuddyPlacements[0].PlacementId);
        Assert.Equal(0.8f, library.ActiveScene.BuddyPlacements[0].Position.X);
        Assert.Equal(0.6f, library.ActiveScene.BuddyPlacements[0].Position.Y);

        SceneLibraryResult removed = library.RemoveBuddy(id, buddy);
        Assert.True(removed.Succeeded);
        Assert.Empty(library.ActiveScene.BuddyPlacements);
        Assert.Equal(
            SceneLibraryStatus.BuddyNotFound,
            library.RemoveBuddy(id, buddy).Status);
    }

    [Fact]
    public void Duplicate_names_are_generated_without_exceeding_name_limit()
    {
        SceneId first = SceneId.From(GuidFromInt(1));
        int nextScene = 2;
        var scene = new SceneDocument(
            first,
            new string('A', SceneDocument.MaximumNameLength),
            new EnvironmentLayout());
        var library = new SceneLibraryState(
            NextFest(),
            [scene],
            first,
            sceneIdFactory: () => SceneId.From(GuidFromInt(nextScene++)));

        SceneLibraryResult firstCopy = library.Duplicate(first);
        SceneLibraryResult secondCopy = library.Duplicate(first);

        Assert.True(firstCopy.Succeeded);
        Assert.True(secondCopy.Succeeded);
        Assert.True(firstCopy.Scene!.Name.Length <= SceneDocument.MaximumNameLength);
        Assert.True(secondCopy.Scene!.Name.Length <= SceneDocument.MaximumNameLength);
        Assert.NotEqual(firstCopy.Scene.Name, secondCopy.Scene.Name);
    }

    private static BuildScopePolicy NextFest() => BuildScopePolicy.Resolve(
        itchIo: false,
        steamDemo: true,
        nextFestDemo: true,
        fullRelease: false);

    private static BuildScopePolicy FullRelease() => BuildScopePolicy.Resolve(
        itchIo: false,
        steamDemo: false,
        nextFestDemo: false,
        fullRelease: true);

    private static BuildScopePolicy InitialDemo() => BuildScopePolicy.Resolve(
        itchIo: false,
        steamDemo: true,
        nextFestDemo: false,
        fullRelease: false);

    private static Guid GuidFromInt(int value) =>
        Guid.Parse($"00000000-0000-0000-0000-{value:D12}");
}
