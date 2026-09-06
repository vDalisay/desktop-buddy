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
    public const string SettingsFileName = "settings.json";
    public const string CharactersDirectoryName = "characters";
    public const string CharacterDocumentFileName = "character.json";
    public const string EnvironmentDirectoryName = "environment";
    public const string EnvironmentBackgroundFileName = "background.png";

    public const string SharedRoomsDirectoryName = "shared_rooms";
    public const string WorkshopStagingDirectory = "sharing/workshop";

    /// <summary>
    /// The Windows Auto-Cloud rows to enter in Steamworks. Root is WinAppDataRoaming for every row;
    /// these values are intentionally data-only so the runbook and tests can stay aligned.
    /// </summary>
    public static IReadOnlyList<SteamAutoCloudRoot> WindowsAutoCloudRoots { get; } =
    [
        new(UserDataDirectoryName, ProgressFileName, Recursive: false),
        new($"{UserDataDirectoryName}/{CharactersDirectoryName}", CharacterDocumentFileName, Recursive: true),
        new($"{UserDataDirectoryName}/{CharactersDirectoryName}", "*.png", Recursive: true),
        new($"{UserDataDirectoryName}/{EnvironmentDirectoryName}", EnvironmentBackgroundFileName, Recursive: false),
    ];

    /// <summary>
    /// Whether a path relative to user:// is player-authored state that should be synchronized.
    /// Backups, temporary/quarantined files, settings and Workshop-derived caches are rejected.
    /// </summary>
    public static bool IsCloudEligibleRelativePath(string relativePath)
    {
        if (!TryNormalize(relativePath, out string normalized))
            return false;

        if (string.Equals(normalized, ProgressFileName, StringComparison.Ordinal))
            return true;

        if (string.Equals(
                normalized,
                $"{EnvironmentDirectoryName}/{EnvironmentBackgroundFileName}",
                StringComparison.Ordinal))
            return true;

        string prefix = CharactersDirectoryName + "/";
        if (!normalized.StartsWith(prefix, StringComparison.Ordinal))
            return false;

        string[] segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 3 || !Guid.TryParseExact(segments[1], "N", out Guid characterId) || characterId == Guid.Empty)
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
