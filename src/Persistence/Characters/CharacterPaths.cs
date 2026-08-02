using System;
using System.IO;

namespace DesktopBuddy.Persistence.Characters;

/// <summary>Canonical, traversal-safe paths rooted at the already-resolved character directory.</summary>
public sealed class CharacterPaths
{
    public const string PrimaryFileName = "character.json";
    public const string BackupFileName = "character.json.bak";
    public const string TemporaryFileName = "character.json.tmp";

    private readonly StringComparison _comparison = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    public CharacterPaths(string resolvedRoot)
    {
        if (string.IsNullOrWhiteSpace(resolvedRoot))
            throw new ArgumentException("Character root is required.", nameof(resolvedRoot));

        Root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(resolvedRoot));
    }

    public string Root { get; }

    public string Directory(Guid id)
    {
        if (id == Guid.Empty)
            throw new ArgumentOutOfRangeException(nameof(id));
        return EnsureUnderRoot(Path.Combine(Root, CanonicalDirectoryName(id)));
    }

    public string Primary(Guid id) => EnsureUnderRoot(Path.Combine(Directory(id), PrimaryFileName));
    public string Backup(Guid id) => EnsureUnderRoot(Path.Combine(Directory(id), BackupFileName));
    public string Temporary(Guid id) => EnsureUnderRoot(Path.Combine(Directory(id), TemporaryFileName));

    public string Quarantine(string sourcePath, DateTimeOffset utcNow, int suffix = 0)
    {
        string fullSource = EnsureUnderRoot(sourcePath);
        string timestamp = utcNow.UtcDateTime.ToString("yyyyMMdd'T'HHmmssfff'Z'",
            System.Globalization.CultureInfo.InvariantCulture);
        string extension = suffix <= 0 ? string.Empty : $"-{suffix}";
        return EnsureUnderRoot($"{fullSource}.invalid-{timestamp}{extension}");
    }

    public bool TryParseDirectory(string path, out Guid id)
    {
        id = Guid.Empty;
        if (string.IsNullOrWhiteSpace(path))
            return false;

        string full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        string? parent = Path.GetDirectoryName(full);
        if (parent is null ||
            !string.Equals(Path.TrimEndingDirectorySeparator(parent), Root, _comparison))
        {
            return false;
        }

        string name = Path.GetFileName(full);
        return name.Length == 32 &&
            Guid.TryParseExact(name, "N", out id) &&
            string.Equals(name, CanonicalDirectoryName(id), StringComparison.Ordinal);
    }

    public bool IsUnderRoot(string path)
    {
        string full = Path.GetFullPath(path);
        if (string.Equals(Path.TrimEndingDirectorySeparator(full), Root, _comparison))
            return true;
        string prefix = Root + Path.DirectorySeparatorChar;
        return full.StartsWith(prefix, _comparison);
    }

    public static string CanonicalDirectoryName(Guid id) => id.ToString("N");

    private string EnsureUnderRoot(string path)
    {
        string full = Path.GetFullPath(path);
        if (!IsUnderRoot(full))
            throw new InvalidOperationException("Character path escaped the configured root.");
        return full;
    }
}
