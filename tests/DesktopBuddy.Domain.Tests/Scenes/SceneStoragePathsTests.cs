using System;
using DesktopBuddy.Domain.Persistence;
using DesktopBuddy.Domain.Scenes;
using Xunit;

namespace DesktopBuddy.Domain.Tests.Scenes;

public sealed class SceneStoragePathsTests
{
    [Fact]
    public void Scene_paths_use_only_stable_identity_and_match_master_storage_layout()
    {
        SceneId scene = SceneId.From(Guid.Parse("11111111-2222-3333-4444-555555555555"));
        string key = "11111111222233334444555555555555";

        Assert.Equal($"scenes/{key}", SceneStoragePaths.SceneRoot(scene));
        Assert.Equal($"scenes/{key}/scene.json", SceneStoragePaths.SceneDocument(scene));
        Assert.Equal($"scenes/{key}/environment", SceneStoragePaths.EnvironmentRoot(scene));
        Assert.Equal($"scenes/{key}/environment/background.png", SceneStoragePaths.BackgroundPaint(scene));
        Assert.Equal($"scenes/{key}/sandbox.json", SceneStoragePaths.SandboxDocument(scene));
        Assert.Equal("scenes/index.json", SceneStoragePaths.SceneIndex);
    }

    [Fact]
    public void Buddy_identity_path_uses_stable_identity_only()
    {
        BuddyIdentityId buddy = BuddyIdentityId.From(
            Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"));

        Assert.Equal(
            "buddy-identities/aaaaaaaabbbbccccddddeeeeeeeeeeee.json",
            SceneStoragePaths.BuddyIdentity(buddy));
    }

    [Fact]
    public void Invalid_identity_never_produces_a_storage_path()
    {
        Assert.Throws<ArgumentException>(() => SceneStoragePaths.SceneRoot(default));
        Assert.Throws<ArgumentException>(() => SceneStoragePaths.SceneDocument(default));
        Assert.Throws<ArgumentException>(() => SceneStoragePaths.BackgroundPaint(default));
        Assert.Throws<ArgumentException>(() => SceneStoragePaths.BuddyIdentity(default));
    }
}
