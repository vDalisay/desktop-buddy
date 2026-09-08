using System;
using DesktopBuddy.Domain.Persistence;

namespace DesktopBuddy.Domain.Scenes;

/// <summary>
/// Engine-free relative storage keys for the Scene architecture. Callers resolve these beneath the
/// trusted save root (for Godot, <c>user://</c>). User-visible names never participate in a path.
/// </summary>
public static class SceneStoragePaths
{
    public const string ScenesRoot = "scenes";
    public const string SceneIndex = "scenes/index.json";
    public const string BuddyIdentitiesRoot = "buddy-identities";

    public static string SceneRoot(SceneId sceneId)
    {
        Require(sceneId);
        return $"{ScenesRoot}/{sceneId}";
    }

    public static string SceneDocument(SceneId sceneId) => $"{SceneRoot(sceneId)}/scene.json";

    public static string EnvironmentRoot(SceneId sceneId) => $"{SceneRoot(sceneId)}/environment";

    public static string BackgroundPaint(SceneId sceneId) => $"{EnvironmentRoot(sceneId)}/background.png";

    public static string SandboxDocument(SceneId sceneId) => $"{SceneRoot(sceneId)}/sandbox.json";

    public static string BuddyIdentity(BuddyIdentityId buddyIdentityId)
    {
        if (!buddyIdentityId.IsValid)
            throw new ArgumentException("Buddy identity storage requires a stable ID.", nameof(buddyIdentityId));
        return $"{BuddyIdentitiesRoot}/{buddyIdentityId}.json";
    }

    private static void Require(SceneId sceneId)
    {
        if (!sceneId.IsValid)
            throw new ArgumentException("Scene storage requires a stable Scene ID.", nameof(sceneId));
    }
}
