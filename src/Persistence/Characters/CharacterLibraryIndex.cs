using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DesktopBuddy.Domain.Characters;
using DesktopBuddy.Persistence;

namespace DesktopBuddy.Persistence.Characters;

/// <summary>
/// Text-only lazy character library index. Enumeration reads a bounded JSON prefix and
/// never deserializes, migrates, compiles, renders, or creates thumbnails.
/// </summary>
public sealed class CharacterLibraryIndex
{
    public const int MaximumMetadataBytes = 16 * 1024;

    private readonly ICharacterFileSystem _fileSystem;
    private readonly CharacterPaths _paths;

    public CharacterLibraryIndex(ICharacterFileSystem fileSystem, string resolvedRoot)
    {
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        _paths = new CharacterPaths(resolvedRoot);
    }

    public long DirectoryEnumerationCount { get; private set; }
    public long MetadataReadCount { get; private set; }
    public long MetadataBytesRead { get; private set; }
    public long ThumbnailReadCount => 0;
    public long FullDocumentLoadCount => 0;

    public Task<IReadOnlyList<CharacterIndexEntry>> ReadPageAsync(
        int offset,
        int count,
        CancellationToken token)
    {
        if (offset < 0)
            throw new ArgumentOutOfRangeException(nameof(offset));
        if (count < 1 || count > 200)
            throw new ArgumentOutOfRangeException(nameof(count));

        // Native builds keep this bounded filesystem work off the render thread. The
        // experimental itch Web build is single-threaded, so Task.Run has no worker to make
        // progress and can freeze the Paint Buddy library the first time it is opened.
        return PersistenceWork.Run<IReadOnlyList<CharacterIndexEntry>>(
            () => ReadPageCore(offset, count, token), token);
    }

    private IReadOnlyList<CharacterIndexEntry> ReadPageCore(
        int offset,
        int count,
        CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!_fileSystem.DirectoryExists(_paths.Root))
            return Array.Empty<CharacterIndexEntry>();

        IReadOnlyList<string> directories = _fileSystem.EnumerateDirectories(_paths.Root);
        DirectoryEnumerationCount++;
        var entries = new List<CharacterIndexEntry>(directories.Count);
        foreach (string directory in directories)
        {
            token.ThrowIfCancellationRequested();
            if (!_paths.TryParseDirectory(directory, out Guid id))
                continue;
            if (_fileSystem.IsReparsePoint(directory))
            {
                entries.Add(new CharacterIndexEntry(
                    id,
                    CharacterPaths.CanonicalDirectoryName(id),
                    CharacterPaths.CanonicalDirectoryName(id),
                    null,
                    CharacterIndexStatus.RejectedPath,
                    "Character directory is a link/reparse point."));
                continue;
            }

            entries.Add(ReadEntry(id, token));
        }

        entries.Sort(CharacterIndexEntryComparer.Instance);
        if (offset >= entries.Count)
            return Array.Empty<CharacterIndexEntry>();
        int take = Math.Min(count, entries.Count - offset);
        return entries.GetRange(offset, take);
    }

    private CharacterIndexEntry ReadEntry(Guid id, CancellationToken token)
    {
        string directoryName = CharacterPaths.CanonicalDirectoryName(id);
        string primary = _paths.Primary(id);
        if (!_fileSystem.FileExists(primary))
        {
            return new CharacterIndexEntry(
                id,
                directoryName,
                directoryName,
                null,
                CharacterIndexStatus.InvalidMetadata,
                "Primary character document is missing.");
        }
        if (_fileSystem.IsReparsePoint(primary))
        {
            return new CharacterIndexEntry(
                id,
                directoryName,
                directoryName,
                null,
                CharacterIndexStatus.RejectedPath,
                "Character document is a link/reparse point.");
        }

        token.ThrowIfCancellationRequested();
        byte[] prefix = _fileSystem.ReadPrefix(primary, MaximumMetadataBytes);
        MetadataReadCount++;
        MetadataBytesRead += prefix.Length;
        MetadataReadResult metadata = ReadMetadata(prefix);
        if (!metadata.Success)
        {
            return new CharacterIndexEntry(
                id,
                directoryName,
                metadata.DisplayName ?? directoryName,
                metadata.SchemaVersion,
                metadata.Future
                    ? CharacterIndexStatus.UnsupportedFutureVersion
                    : CharacterIndexStatus.InvalidMetadata,
                metadata.Detail);
        }
        if (metadata.Id != id)
        {
            return new CharacterIndexEntry(
                id,
                directoryName,
                metadata.DisplayName!,
                metadata.SchemaVersion,
                CharacterIndexStatus.InvalidMetadata,
                "Character document ID does not match its directory.");
        }

        return new CharacterIndexEntry(
            id,
            directoryName,
            metadata.DisplayName!,
            metadata.SchemaVersion,
            CharacterIndexStatus.Available);
    }

    private static MetadataReadResult ReadMetadata(ReadOnlySpan<byte> utf8)
    {
        try
        {
            var reader = new Utf8JsonReader(utf8, isFinalBlock: false, state: default);
            int? schema = null;
            Guid? id = null;
            string? displayName = null;
            while (reader.Read())
            {
                if (reader.TokenType != JsonTokenType.PropertyName)
                    continue;

                if (reader.ValueTextEquals("schemaVersion"u8))
                {
                    if (!reader.Read() || reader.TokenType != JsonTokenType.Number ||
                        !reader.TryGetInt32(out int value))
                    {
                        return MetadataReadResult.Invalid("Invalid schemaVersion metadata.", schema, id, displayName);
                    }
                    schema = value;
                }
                else if (reader.ValueTextEquals("id"u8))
                {
                    if (!reader.Read() || reader.TokenType != JsonTokenType.String ||
                        !Guid.TryParseExact(reader.GetString(), "D", out Guid value))
                    {
                        return MetadataReadResult.Invalid("Invalid character ID metadata.", schema, id, displayName);
                    }
                    id = value;
                }
                else if (reader.ValueTextEquals("displayName"u8))
                {
                    if (!reader.Read() || reader.TokenType != JsonTokenType.String)
                        return MetadataReadResult.Invalid("Invalid displayName metadata.", schema, id, displayName);
                    displayName = reader.GetString();
                }
                else
                {
                    if (!reader.Read())
                        break;
                    reader.Skip();
                }

                if (schema.HasValue && id.HasValue && displayName is not null)
                    break;
            }

            if (!schema.HasValue)
                return MetadataReadResult.Invalid("schemaVersion metadata was not found.", schema, id, displayName);
            if (schema.Value > CharacterDocumentPolicy.CurrentSchemaVersion)
                return MetadataReadResult.FutureVersion(schema.Value, id, displayName);
            if (!id.HasValue)
                return MetadataReadResult.Invalid("Character ID metadata was not found.", schema, id, displayName);
            if (string.IsNullOrWhiteSpace(displayName))
                return MetadataReadResult.Invalid("Display name metadata was not found.", schema, id, displayName);
            return MetadataReadResult.Valid(schema.Value, id.Value, displayName);
        }
        catch (JsonException exception)
        {
            return MetadataReadResult.Invalid(exception.Message, null, null, null);
        }
    }

    private sealed class CharacterIndexEntryComparer : IComparer<CharacterIndexEntry>
    {
        public static CharacterIndexEntryComparer Instance { get; } = new();

        public int Compare(CharacterIndexEntry? x, CharacterIndexEntry? y)
        {
            if (ReferenceEquals(x, y))
                return 0;
            if (x is null)
                return -1;
            if (y is null)
                return 1;
            int name = StringComparer.OrdinalIgnoreCase.Compare(x.DisplayName, y.DisplayName);
            if (name != 0)
                return name;
            return x.CharacterId.CompareTo(y.CharacterId);
        }
    }

    private readonly record struct MetadataReadResult(
        bool Success,
        bool Future,
        int? SchemaVersion,
        Guid? Id,
        string? DisplayName,
        string? Detail)
    {
        public static MetadataReadResult Valid(int schema, Guid id, string displayName) =>
            new(true, false, schema, id, displayName, null);
        public static MetadataReadResult Invalid(
            string detail, int? schema, Guid? id, string? displayName) =>
            new(false, false, schema, id, displayName, detail);
        public static MetadataReadResult FutureVersion(
            int schema, Guid? id, string? displayName) =>
            new(false, true, schema, id, displayName,
                "Character document uses a newer schema and was left untouched.");
    }
}
