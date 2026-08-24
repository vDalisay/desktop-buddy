using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using DesktopBuddy.Domain.Sharing;

namespace DesktopBuddy.Persistence.Sharing;

public sealed record ShareFolderReadResult(
    ShareManifest? Manifest,
    IReadOnlyDictionary<string, byte[]> Files,
    ShareValidationResult Validation)
{
    public bool IsSuccess => Manifest is not null && Validation.IsValid;
}

/// <summary>
/// Copies no authority from Steam: a Workshop install directory is hostile input. This reader
/// validates its exact data-only shape, paths, links, lengths and SHA-256 values before any JSON
/// or PNG payload is handed to a domain/import service.
/// </summary>
public sealed class ShareFolderReader
{
    public ShareFolderReadResult Read(string sourceRoot, ShareContentType expectedType)
    {
        var issues = new List<ShareValidationIssue>();
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        try
        {
            string root = CanonicalRoot(sourceRoot);
            if (!Directory.Exists(root))
                return Failure(ShareValidationCode.MissingFile, root, "Share content folder does not exist.");
            if (IsLinked(root))
                return Failure(ShareValidationCode.LinkedPath, root, "Share content root is a link/reparse point.");

            string manifestPath = ResolveUnder(root, ShareManifestPolicy.ManifestFileName);
            if (!File.Exists(manifestPath) || IsLinked(manifestPath))
                return Failure(ShareValidationCode.MissingFile, ShareManifestPolicy.ManifestFileName, "Share manifest is missing or linked.");
            byte[] manifestBytes = ReadBounded(manifestPath, ShareManifestPolicy.MaximumManifestBytes);
            ShareManifestDecodeResult decoded = ShareManifestPolicy.Decode(manifestBytes, expectedType);
            if (!decoded.IsSuccess || decoded.Manifest is null)
                return new ShareFolderReadResult(null, files, decoded.Validation);
            ShareManifest manifest = decoded.Manifest;

            var declared = new HashSet<string>(manifest.Files.Select(entry => entry.Path), StringComparer.Ordinal)
            {
                ShareManifestPolicy.ManifestFileName,
            };

            foreach (string directory in EnumerateDirectoriesSafe(root))
            {
                if (IsLinked(directory))
                    issues.Add(new ShareValidationIssue(ShareValidationCode.LinkedPath, Relative(root, directory), "Linked directories are not allowed."));
            }

            foreach (string actual in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            {
                string relative = Relative(root, actual);
                if (IsLinked(actual))
                {
                    issues.Add(new ShareValidationIssue(ShareValidationCode.LinkedPath, relative, "Linked files are not allowed."));
                    continue;
                }
                if (!declared.Contains(relative))
                    issues.Add(new ShareValidationIssue(ShareValidationCode.UnexpectedFile, relative, "Undeclared files are not allowed in Workshop content."));
            }

            foreach (Sha256FileEntry entry in manifest.Files)
            {
                if (!ShareManifestPolicy.IsCanonicalRelativePath(entry.Path))
                    continue;
                string path = ResolveUnder(root, entry.Path);
                if (!File.Exists(path))
                {
                    issues.Add(new ShareValidationIssue(ShareValidationCode.MissingFile, entry.Path, "Declared file is missing."));
                    continue;
                }
                if (IsLinked(path))
                {
                    issues.Add(new ShareValidationIssue(ShareValidationCode.LinkedPath, entry.Path, "Declared file is linked."));
                    continue;
                }

                int cap = entry.Path == ShareManifestPolicy.CharacterFileName
                    ? ShareManifestPolicy.MaximumCharacterJsonBytes
                    : DesktopBuddy.Domain.Painting.PaintPolicy.MaximumEncodedPngBytes;
                byte[] payload;
                try
                {
                    payload = ReadBounded(path, cap);
                }
                catch (InvalidDataException exception)
                {
                    issues.Add(new ShareValidationIssue(ShareValidationCode.InvalidEncodedSize, entry.Path, exception.Message));
                    continue;
                }
                if (payload.LongLength != entry.EncodedBytes)
                {
                    issues.Add(new ShareValidationIssue(ShareValidationCode.InvalidEncodedSize, entry.Path, "Actual encoded size does not match the manifest."));
                    continue;
                }
                string hash = Convert.ToHexString(SHA256.HashData(payload));
                if (!string.Equals(hash, entry.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    issues.Add(new ShareValidationIssue(ShareValidationCode.HashMismatch, entry.Path, "SHA-256 does not match the manifest."));
                    continue;
                }
                files[entry.Path] = payload;
            }

            if (issues.Count > 0)
                return new ShareFolderReadResult(null, files, new ShareValidationResult(issues));
            return new ShareFolderReadResult(manifest, files, ShareValidationResult.Valid);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return Failure(ShareValidationCode.IoFailure, sourceRoot, exception.Message);
        }
    }

    public static string ResolveUnder(string root, string relative)
    {
        if (!ShareManifestPolicy.IsCanonicalRelativePath(relative) && relative != ShareManifestPolicy.ManifestFileName)
            throw new InvalidDataException("Share path is not canonical.");
        string canonicalRoot = CanonicalRoot(root);
        string full = Path.GetFullPath(Path.Combine(canonicalRoot, relative.Replace('/', Path.DirectorySeparatorChar)));
        StringComparison comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!full.StartsWith(canonicalRoot + Path.DirectorySeparatorChar, comparison))
            throw new InvalidDataException("Share path escaped its root directory.");
        return full;
    }

    public static string CanonicalRoot(string root)
    {
        if (string.IsNullOrWhiteSpace(root))
            throw new ArgumentException("A share root is required.", nameof(root));
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
    }

    private static IEnumerable<string> EnumerateDirectoriesSafe(string root) =>
        Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories);

    private static byte[] ReadBounded(string path, int maximumBytes)
    {
        var info = new FileInfo(path);
        if (info.Length <= 0 || info.Length > maximumBytes)
            throw new InvalidDataException($"File exceeds its {maximumBytes}-byte limit or is empty.");
        byte[] bytes = File.ReadAllBytes(path);
        if (bytes.Length <= 0 || bytes.Length > maximumBytes)
            throw new InvalidDataException($"File exceeds its {maximumBytes}-byte limit or is empty.");
        return bytes;
    }

    private static bool IsLinked(string path)
    {
        FileSystemInfo info = Directory.Exists(path) ? new DirectoryInfo(path) : new FileInfo(path);
        return (info.Attributes & FileAttributes.ReparsePoint) != 0 || info.LinkTarget is not null;
    }

    private static string Relative(string root, string fullPath) =>
        Path.GetRelativePath(root, fullPath).Replace(Path.DirectorySeparatorChar, '/');

    private static ShareFolderReadResult Failure(ShareValidationCode code, string path, string message) =>
        new(null, new Dictionary<string, byte[]>(), new ShareValidationResult([new ShareValidationIssue(code, path, message)]));
}
