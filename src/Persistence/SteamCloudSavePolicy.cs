using System;
using System.Collections.Generic;
using System.IO;
using DesktopBuddy.Domain.Painting;

namespace DesktopBuddy.Persistence;

/// <summary>
/// Canonical save boundary mirrored by the Steam Auto-Cloud configuration.
///
/// Desktop Buddy deliberately keeps machine preferences out of Steam Cloud while carrying the
/// player's semantic progress and authored visual content between machines and, once Valve allows
/// shared storage for the released base app, from the Demo into the full game.
/// </summary>
public static class SteamCloudSavePolicy
{
    public const uint FullGameAppId = 5114950;
    public const uint DemoAppId = 5228990;

    // project.godot uses application/config/use_custom_user_dir=true with this exact name. Keeping
    // both Steam builds on one local user:// root makes a same-PC Demo -> full-game upgrade work
    // even before Steam's shared-cloud handoff is available.
    public const string UserDataDirectoryName = "DesktopBuddy";

    public const string ProgressFileName = "progress.json";
    public const string WorkProgressFileName = "work-progress.json";
    public const string SceneProgressManifestFileName = "scene-progress.commit.json";
    public const string SettingsFileName = "settings.json";
    public const string CharactersDirectoryName = "characters";
    public const string CharacterDocumentFileName = "character.json";
    public const string EnvironmentDirectoryName = "environment";
    public const string EnvironmentBackgroundFileName = "background.png";
    public const string BuddyIdentitiesDirectoryName = "buddy-identities";
    public const string ScenesDirectoryName = "scenes";
    public const string SceneIndexFileName = "index.json";
    public const string SceneDocumentFileName = "scene.json";
    public const string SceneRecoverySuffix = ".next";

    public const string SharedRoomsDirectoryName = "shared_rooms";
    public const string WorkshopStagingDirectory = "sharing/workshop";

    /// <summary>
    /// The Windows Auto-Cloud rows to enter in Steamworks. Root is WinAppDataRoaming for every row;
    /// these values are intentionally data-only so the runbook and tests can stay aligned.
    ///
    /// Scene transaction <c>.next</c> files are intentionally included. Once the manifest commits,
    /// those exact staged bytes may be the only copy matching its hashes if canonical promotion was
    /// interrupted. They are therefore committed recovery data, not disposable temporaries. Generic
    /// <c>.tmp</c>, <c>.bak</c> and quarantined files remain excluded.
    /// </summary>
    public static IReadOnlyList<SteamAutoCloudRoot> WindowsAutoCloudRoots { get; } =
    [
        new(UserDataDirectoryName, ProgressFileName, Recursive: false),
        new(UserDataDirectoryName, ProgressFileName + SceneRecoverySuffix, Recursive: false),
        new(UserDataDirectoryName, WorkProgressFileName, Recursive: false),
        new(UserDataDirectoryName, WorkProgressFileName + SceneRecoverySuffix, Recursive: false),
        new(UserDataDirectoryName, SceneProgressManifestFileName, Recursive: false),
        new($"{UserDataDirectoryName}/{BuddyIdentitiesDirectoryName}", "*.json", Recursive: false),
        new($"{UserDataDirectoryName}/{BuddyIdentitiesDirectoryName}", "*.json.next", Recursive: false),
        new($"{UserDataDirectoryName}/{ScenesDirectoryName}", SceneIndexFileName, Recursive: false),
        new($"{UserDataDirectoryName}/{ScenesDirectoryName}", SceneIndexFileName + SceneRecoverySuffix, Recursive: false),
        new($"{UserDataDirectoryName}/{ScenesDirectoryName}", SceneDocumentFileName, Recursive: true),
        new($"{UserDataDirectoryName}/{ScenesDirectoryName}", SceneDocumentFileName + SceneRecoverySuffix, Recursive: true),
        new($"{UserDataDirectoryName}/{ScenesDirectoryName}", EnvironmentBackgroundFileName, Recursive: true),
        new($"{UserDataDirectoryName}/{CharactersDirectoryName}", CharacterDocumentFileName, Recursive: true),
        new($"{UserDataDirectoryName}/{CharactersDirectoryName}", "*.png", Recursive: true),
        // Legacy one-room Demo save. Keep it eligible during the staged upgrade so a user can move
        // between machines before the deterministic Scene migration has happened there.
        new($"{UserDataDirectoryName}/{EnvironmentDirectoryName}", EnvironmentBackgroundFileName, Recursive: false),
    ];

    /// <summary>
    /// Whether a path relative to user:// is player-authored state that should be synchronized.
    /// Transaction .next files are eligible because a committed manifest may require them; generic
    /// backups, temporaries/quarantine, settings and Workshop-derived caches are rejected.
    /// </summary>
    public static bool IsCloudEligibleRelativePath(string relativePath)
    {
        if (!TryNormalize(relativePath, out string normalized))
            return false;

        if (string.Equals(normalized, ProgressFileName, StringComparison.Ordinal) ||
            string.Equals(normalized, ProgressFileName + SceneRecoverySuffix, StringComparison.Ordinal) ||
            string.Equals(normalized, WorkProgressFileName, StringComparison.Ordinal) ||
            string.Equals(normalized, WorkProgressFileName + SceneRecoverySuffix, StringComparison.Ordinal) ||
            string.Equals(normalized, SceneProgressManifestFileName, StringComparison.Ordinal))
        {
            return true;
        }

        if (string.Equals(
                normalized,
                $"{EnvironmentDirectoryName}/{EnvironmentBackgroundFileName}",
                StringComparison.Ordinal))
        {
            return true;
        }

        if (IsBuddyIdentityPath(normalized) || IsScenePath(normalized))
            return true;

        string prefix = CharactersDirectoryName + "/";
        if (!normalized.StartsWith(prefix, StringComparison.Ordinal))
            return false;

        string[] segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 3 || !TryStableGuid(segments[1]))
            return false;

        if (segments.Length == 3 && string.Equals(segments[2], CharacterDocumentFileName, StringComparison.Ordinal))
            return true;

        if (segments.Length != 4 || !string.Equals(segments[2], "paint", StringComparison.Ordinal))
            return false;

        string candidate = $"paint/{segments[3]}";
        foreach (string allowed in PaintPolicy.WhitelistedPaths.Values)
        {
            if (string.Equals(candidate, allowed, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    private static bool IsBuddyIdentityPath(string normalized)
    {
        string[] segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length != 2 ||
            !string.Equals(segments[0], BuddyIdentitiesDirectoryName, StringComparison.Ordinal))
        {
            return false;
        }

        string file = segments[1];
        string suffix = file.EndsWith(".json.next", StringComparison.Ordinal)
            ? ".json.next"
            : file.EndsWith(".json", StringComparison.Ordinal) ? ".json" : string.Empty;
        if (suffix.Length == 0)
            return false;

        string id = file[..^suffix.Length];
        return TryStableGuid(id);
    }

    private static bool IsScenePath(string normalized)
    {
        string[] segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 2 &&
            string.Equals(segments[0], ScenesDirectoryName, StringComparison.Ordinal) &&
            (string.Equals(segments[1], SceneIndexFileName, StringComparison.Ordinal) ||
             string.Equals(segments[1], SceneIndexFileName + SceneRecoverySuffix, StringComparison.Ordinal)))
        {
            return true;
        }

        if (segments.Length < 3 ||
            !string.Equals(segments[0], ScenesDirectoryName, StringComparison.Ordinal) ||
            !TryStableGuid(segments[1]))
        {
            return false;
        }

        if (segments.Length == 3 &&
            (string.Equals(segments[2], SceneDocumentFileName, StringComparison.Ordinal) ||
             string.Equals(segments[2], SceneDocumentFileName + SceneRecoverySuffix, StringComparison.Ordinal)))
        {
            return true;
        }

        return segments.Length == 4 &&
            string.Equals(segments[2], EnvironmentDirectoryName, StringComparison.Ordinal) &&
            string.Equals(segments[3], EnvironmentBackgroundFileName, StringComparison.Ordinal);
    }

    private static bool TryStableGuid(string value) =>
        Guid.TryParseExact(value, "N", out Guid id) && id != Guid.Empty;

    private static bool TryNormalize(string relativePath, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
            return false;

        string candidate = relativePath.Replace('\\', '/').Trim('/');
        if (candidate.Length == 0)
            return false;

        string[] segments = candidate.Split('/', StringSplitOptions.RemoveEmptyEntries);
        foreach (string segment in segments)
        {
            if (segment is "." or "..")
                return false;
        }

        normalized = string.Join('/', segments);
        return true;
    }
}

/// <summary>One Steamworks Auto-Cloud root row beneath WinAppDataRoaming.</summary>
public readonly record struct SteamAutoCloudRoot(string Subdirectory, string Pattern, bool Recursive);
