using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DesktopBuddy.Domain.Sharing;

namespace DesktopBuddy.Persistence.Sharing;

public readonly record struct WorkshopPublishStaging(
    Guid OperationId,
    string OperationRoot,
    string ContentRoot,
    string PreviewPath);

public readonly record struct WorkshopIncomingStaging(
    Guid OperationId,
    string OperationRoot,
    string ContentRoot);

/// <summary>
/// Project-owned transaction space between mutable local saves / untrusted Steam install folders
/// and long-running Workshop operations. Steam never receives a live save directory and import
/// validation never operates directly on Steam's mutable cache.
/// </summary>
public sealed class WorkshopStagingStore
{
    public const long MaximumIncomingBytes = 16L * 1024 * 1024;
    public const int MaximumIncomingFiles = 16;
    private const int MaximumIncomingDirectories = 12;
    private const int MaximumIncomingDepth = 4;

    private readonly string _root;

    public WorkshopStagingStore(string resolvedRoot)
    {
        if (string.IsNullOrWhiteSpace(resolvedRoot))
            throw new ArgumentException("A resolved sharing root is required.", nameof(resolvedRoot));
        _root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(resolvedRoot));
    }

    public string Root => _root;
    public string PublishRoot => Path.Combine(_root, "publish");
    public string IncomingRoot => Path.Combine(_root, "incoming");
    public string QuarantineRoot => Path.Combine(_root, "quarantine");

    public WorkshopPublishStaging CreatePublish(Guid operationId)
    {
        if (operationId == Guid.Empty) throw new ArgumentException("Operation ID cannot be empty.", nameof(operationId));
        string operation = OperationPath(PublishRoot, operationId);
        RecreateOwnedDirectory(operation);
        string content = Path.Combine(operation, "content");
        Directory.CreateDirectory(content);
        return new WorkshopPublishStaging(operationId, operation, content, Path.Combine(operation, "preview.png"));
    }

    public WorkshopIncomingStaging SnapshotIncoming(string sourceRoot, Guid operationId)
    {
        if (operationId == Guid.Empty) throw new ArgumentException("Operation ID cannot be empty.", nameof(operationId));
        string source = ShareFolderReader.CanonicalRoot(sourceRoot);
        if (!Directory.Exists(source)) throw new DirectoryNotFoundException(source);
        RejectLink(source);

        string operation = OperationPath(IncomingRoot, operationId);
        RecreateOwnedDirectory(operation);
        string content = Path.Combine(operation, "content");
        Directory.CreateDirectory(content);

        try
        {
            CopyBoundedTree(source, content);
            return new WorkshopIncomingStaging(operationId, operation, content);
        }
        catch
        {
            SafeDelete(operation);
            throw;
        }
    }

    public string Quarantine(WorkshopIncomingStaging staging, string reasonCode)
    {
        string safeReason = string.Concat((reasonCode ?? "invalid").Where(c => char.IsLetterOrDigit(c) || c is '-' or '_'));
        if (safeReason.Length == 0) safeReason = "invalid";
        Directory.CreateDirectory(QuarantineRoot);
        string target = Path.Combine(QuarantineRoot, $"{staging.OperationId:D}-{safeReason}");
        int suffix = 0;
        while (Directory.Exists(target))
            target = Path.Combine(QuarantineRoot, $"{staging.OperationId:D}-{safeReason}-{++suffix}");
        Directory.Move(staging.OperationRoot, target);
        return target;
    }

    public void Cleanup(Guid operationId)
    {
        SafeDelete(OperationPath(PublishRoot, operationId));
        SafeDelete(OperationPath(IncomingRoot, operationId));
    }

    public int CleanupStale(TimeSpan olderThan, DateTimeOffset utcNow)
    {
        if (olderThan < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(olderThan));
        int removed = 0;
        foreach (string parent in new[] { PublishRoot, IncomingRoot })
        {
            if (!Directory.Exists(parent)) continue;
            foreach (string directory in Directory.EnumerateDirectories(parent))
            {
                try
                {
                    RejectLink(directory);
                    DateTimeOffset lastWrite = Directory.GetLastWriteTimeUtc(directory);
                    if (utcNow - lastWrite < olderThan) continue;
                    Directory.Delete(directory, recursive: true);
                    removed++;
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
                {
                    // Startup cleanup is best-effort and must never block the single-player game.
                }
            }
        }
        return removed;
    }

    public static void WriteOwnedFile(string root, string relative, ReadOnlySpan<byte> bytes)
    {
        string path = ShareFolderReader.ResolveUnder(root, relative);
        string? directory = Path.GetDirectoryName(path);
        if (directory is not null) Directory.CreateDirectory(directory);
        File.WriteAllBytes(path, bytes.ToArray());
    }

    private static void CopyBoundedTree(string sourceRoot, string destinationRoot)
    {
        long aggregate = 0;
        int fileCount = 0;
        int directoryCount = 0;
        var pending = new Stack<(string Source, string Destination, int Depth)>();
        pending.Push((sourceRoot, destinationRoot, 0));

        while (pending.Count > 0)
        {
            (string sourceDirectory, string destinationDirectory, int depth) = pending.Pop();
            foreach (string entry in Directory.EnumerateFileSystemEntries(sourceDirectory))
            {
                FileAttributes attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException($"Linked Workshop path is not allowed: {entry}");

                string relative = Path.GetRelativePath(sourceRoot, entry).Replace(Path.DirectorySeparatorChar, '/');
                if (!ShareManifestPolicy.IsCanonicalRelativePath(relative) && relative != ShareManifestPolicy.ManifestFileName)
                    throw new InvalidDataException($"Unsafe Workshop path '{relative}'.");

                if ((attributes & FileAttributes.Directory) != 0)
                {
                    if (++directoryCount > MaximumIncomingDirectories)
                        throw new InvalidDataException($"Workshop item contains more than {MaximumIncomingDirectories} directories.");
                    if (depth >= MaximumIncomingDepth)
                        throw new InvalidDataException("Workshop item directory nesting is too deep.");
                    string destination = ShareFolderReader.ResolveUnder(destinationRoot, relative);
                    Directory.CreateDirectory(destination);
                    pending.Push((entry, destination, depth + 1));
                    continue;
                }

                if (++fileCount > MaximumIncomingFiles)
                    throw new InvalidDataException($"Workshop item contains more than {MaximumIncomingFiles} files.");
                var before = new FileInfo(entry);
                aggregate = checked(aggregate + before.Length);
                if (aggregate > MaximumIncomingBytes)
                    throw new InvalidDataException("Workshop item exceeds the 16 MiB incoming copy budget.");

                string target = ShareFolderReader.ResolveUnder(destinationRoot, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                using FileStream input = new(entry, FileMode.Open, FileAccess.Read, FileShare.Read);
                using FileStream output = new(target, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                input.CopyTo(output);
                long copied = output.Length;
                if (copied != before.Length)
                    throw new InvalidDataException("Workshop item changed size while being copied.");
            }
        }
    }

    private static void RejectLink(string path)
    {
        FileSystemInfo info = Directory.Exists(path) ? new DirectoryInfo(path) : new FileInfo(path);
        if ((info.Attributes & FileAttributes.ReparsePoint) != 0 || info.LinkTarget is not null)
            throw new InvalidDataException($"Linked Workshop path is not allowed: {path}");
    }

    private static void RecreateOwnedDirectory(string path)
    {
        SafeDelete(path);
        Directory.CreateDirectory(path);
    }

    private static void SafeDelete(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
    }

    private static string OperationPath(string parent, Guid id)
    {
        string canonicalParent = Path.TrimEndingDirectorySeparator(Path.GetFullPath(parent));
        string candidate = Path.GetFullPath(Path.Combine(canonicalParent, id.ToString("D")));
        StringComparison comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!candidate.StartsWith(canonicalParent + Path.DirectorySeparatorChar, comparison))
            throw new InvalidDataException("Workshop staging operation escaped its root.");
        return candidate;
    }
}