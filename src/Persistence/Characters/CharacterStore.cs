using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using DesktopBuddy.Domain.Characters;

namespace DesktopBuddy.Persistence.Characters;

/// <summary>
/// Failure-safe local character document store. The immutable feature catalogue is injected so
/// Asset Forge-generated trusted IDs survive save/load while tests and legacy callers continue to
/// default to the shipped catalogue.
/// </summary>
public sealed class CharacterStore
{
    private static readonly JsonSerializerOptions SerializeOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly ICharacterFileSystem _fileSystem;
    private readonly CharacterPaths _paths;
    private readonly Func<DateTimeOffset> _utcNow;
    private readonly CharacterFeatureCatalog _featureCatalog;

    public CharacterStore(
        ICharacterFileSystem fileSystem,
        string resolvedRoot,
        Func<DateTimeOffset>? utcNow = null,
        CharacterFeatureCatalog? featureCatalog = null)
    {
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        _paths = new CharacterPaths(resolvedRoot);
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
        _featureCatalog = featureCatalog ?? CharacterFeatureCatalog.Shipped;
    }

    public CharacterPaths Paths => _paths;
    public CharacterFeatureCatalog FeatureCatalog => _featureCatalog;
    public long FullDocumentLoadCount { get; private set; }
    public long SaveCount { get; private set; }
    public long DeleteCount { get; private set; }

    public Task<CharacterLoadResult> LoadAsync(Guid id, CancellationToken token) =>
        Task.Run(() => LoadCore(id, token), CancellationToken.None);

    public Task<CharacterSaveResult> SaveAsync(
        CharacterDocument document,
        CancellationToken token) =>
        Task.Run(() => SaveCore(document, token), CancellationToken.None);

    public Task<CharacterDeleteResult> DeleteAsync(Guid id, CancellationToken token) =>
        Task.Run(() => DeleteCore(id, token), CancellationToken.None);

    /// <summary>
    /// Removes every stored character. Reset Progress means a first run, and a first run has
    /// no buddy the player made earlier (owner instruction 2026-08-21) — leaving the documents
    /// behind made the reset look like it had done nothing at all.
    /// </summary>
    /// <returns>How many character directories were removed.</returns>
    public Task<int> DeleteAllAsync(CancellationToken token) => Task.Run(
        () =>
        {
            if (!_fileSystem.DirectoryExists(_paths.Root))
                return 0;

            int removed = 0;
            foreach (string directory in _fileSystem.EnumerateDirectories(_paths.Root))
            {
                token.ThrowIfCancellationRequested();
                // Anything that is not a character directory is not ours to delete.
                if (!Guid.TryParse(Path.GetFileName(directory), out Guid id) || id == Guid.Empty)
                    continue;
                if (DeleteCore(id, token).Status == CharacterDeleteStatus.Deleted)
                    removed++;
            }

            return removed;
        },
        CancellationToken.None);

    private CharacterLoadResult LoadCore(Guid id, CancellationToken token)
    {
        if (id == Guid.Empty)
            return new CharacterLoadResult(CharacterLoadStatus.RejectedPath, null, "Empty character ID.");

        try
        {
            token.ThrowIfCancellationRequested();
            string directory = _paths.Directory(id);
            if (!_fileSystem.DirectoryExists(directory))
                return new CharacterLoadResult(CharacterLoadStatus.NotFound, null);
            if (_fileSystem.IsReparsePoint(directory))
                return new CharacterLoadResult(CharacterLoadStatus.RejectedPath, null, "Character directory is a link/reparse point.");

            string? quarantinedPrimary = null;
            LoadAttempt primary = TryLoadFile(_paths.Primary(id), id, token);
            if (primary.Status == AttemptStatus.Valid)
            {
                FullDocumentLoadCount++;
                return new CharacterLoadResult(CharacterLoadStatus.Loaded, primary.Document);
            }
            if (primary.Status == AttemptStatus.Future)
            {
                return new CharacterLoadResult(
                    CharacterLoadStatus.UnsupportedFutureVersion,
                    null,
                    primary.Detail);
            }
            LoadAttempt backup = TryLoadFile(_paths.Backup(id), id, token);

            // Never rename away the only copy. A character's first save writes no backup — the
            // backup appears on the second save — so quarantining the primary turned one bad
            // read into permanent loss: the next load found nothing at all and reported
            // NotFound, which reads as "you never had this character" rather than "this file
            // would not parse" (owner report 2026-08-21). With a backup present, quarantine is
            // still right: it clears the way for the copy that can be recovered, or for two
            // junk files to be swept aside together.
            if (primary.Status == AttemptStatus.Invalid && backup.Status != AttemptStatus.Missing)
                quarantinedPrimary = Quarantine(_paths.Primary(id));

            if (backup.Status == AttemptStatus.Valid)
            {
                FullDocumentLoadCount++;
                return new CharacterLoadResult(
                    CharacterLoadStatus.BackupRecovered,
                    backup.Document,
                    primary.Detail,
                    quarantinedPrimary);
            }
            if (backup.Status == AttemptStatus.Future)
            {
                return new CharacterLoadResult(
                    CharacterLoadStatus.UnsupportedFutureVersion,
                    null,
                    backup.Detail,
                    quarantinedPrimary);
            }

            string? quarantinedBackup = backup.Status == AttemptStatus.Invalid
                ? Quarantine(_paths.Backup(id))
                : null;
            if (primary.Status == AttemptStatus.Missing && backup.Status == AttemptStatus.Missing)
                return new CharacterLoadResult(CharacterLoadStatus.NotFound, null);

            return new CharacterLoadResult(
                CharacterLoadStatus.Invalid,
                null,
                backup.Detail ?? primary.Detail ?? "No valid character document was found.",
                quarantinedPrimary,
                quarantinedBackup);
        }
        catch (OperationCanceledException)
        {
            return new CharacterLoadResult(CharacterLoadStatus.Cancelled, null);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new CharacterLoadResult(CharacterLoadStatus.IoFailure, null, exception.Message);
        }
    }

    private CharacterSaveResult SaveCore(CharacterDocument document, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (document.Id == Guid.Empty)
            return new CharacterSaveResult(CharacterSaveStatus.RejectedPath, null, "Empty character ID.");

        string? temporary = null;
        try
        {
            CharacterNormalizationResult normalized = CharacterDocumentNormalizer.Normalize(document);
            CharacterValidationResult validation = CharacterDocumentValidator.Validate(normalized.Document, _featureCatalog);
            if (!validation.IsValid)
            {
                return new CharacterSaveResult(
                    CharacterSaveStatus.Invalid,
                    null,
                    string.Join("; ", validation.Errors));
            }
            CharacterDocumentPolicy.ValidatePaintManifest(normalized.Document.Paint);

            token.ThrowIfCancellationRequested();
            string directory = _paths.Directory(normalized.Document.Id);
            if (_fileSystem.DirectoryExists(directory) && _fileSystem.IsReparsePoint(directory))
            {
                return new CharacterSaveResult(
                    CharacterSaveStatus.RejectedPath,
                    null,
                    "Character directory is a link/reparse point.");
            }

            _fileSystem.CreateDirectory(_paths.Root);
            _fileSystem.CreateDirectory(directory);
            temporary = _paths.Temporary(normalized.Document.Id);
            _fileSystem.DeleteFile(temporary);
            string json = SerializeTrusted(normalized.Document);
            _fileSystem.WriteAllTextDurable(temporary, json);
            token.ThrowIfCancellationRequested();

            string primary = _paths.Primary(normalized.Document.Id);
            if (_fileSystem.FileExists(primary))
            {
                _fileSystem.ReplaceFileWithBackup(
                    temporary,
                    primary,
                    _paths.Backup(normalized.Document.Id));
            }
            else
            {
                _fileSystem.MoveFile(temporary, primary);
            }

            SaveCount++;
            return new CharacterSaveResult(CharacterSaveStatus.Saved, normalized.Document);
        }
        catch (OperationCanceledException)
        {
            if (temporary is not null)
                SafeDeleteTemporary(temporary);
            return new CharacterSaveResult(CharacterSaveStatus.Cancelled, null);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            if (temporary is not null)
                SafeDeleteTemporary(temporary);
            return new CharacterSaveResult(CharacterSaveStatus.IoFailure, null, exception.Message);
        }
    }

    private string SerializeTrusted(CharacterDocument document)
    {
        CharacterValidationResult validation = CharacterDocumentValidator.Validate(document, _featureCatalog);
        if (!validation.IsValid)
        {
            string detail = string.Join("; ", validation.Errors.Select(error => $"{error.Path}: {error.Message}"));
            throw new ArgumentException(detail, nameof(document));
        }
        CharacterDocumentPolicy.ValidatePaintManifest(document.Paint);
        return JsonSerializer.Serialize(
            document with { SchemaVersion = CharacterDocumentPolicy.CurrentSchemaVersion },
            SerializeOptions);
    }

    private CharacterDeleteResult DeleteCore(Guid id, CancellationToken token)
    {
        if (id == Guid.Empty)
            return new CharacterDeleteResult(CharacterDeleteStatus.RejectedPath, id, "Empty character ID.");

        try
        {
            token.ThrowIfCancellationRequested();
            string directory = _paths.Directory(id);
            if (!_fileSystem.DirectoryExists(directory))
                return new CharacterDeleteResult(CharacterDeleteStatus.NotFound, id);
            if (_fileSystem.IsReparsePoint(directory))
            {
                return new CharacterDeleteResult(
                    CharacterDeleteStatus.RejectedPath,
                    id,
                    "Character directory is a link/reparse point.");
            }

            _fileSystem.DeleteDirectory(directory, recursive: true);
            DeleteCount++;
            return new CharacterDeleteResult(CharacterDeleteStatus.Deleted, id);
        }
        catch (OperationCanceledException)
        {
            return new CharacterDeleteResult(CharacterDeleteStatus.Cancelled, id);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new CharacterDeleteResult(CharacterDeleteStatus.IoFailure, id, exception.Message);
        }
    }

    private LoadAttempt TryLoadFile(string path, Guid expectedId, CancellationToken token)
    {
        if (!_fileSystem.FileExists(path))
            return LoadAttempt.Missing;
        if (_fileSystem.IsReparsePoint(path))
            return LoadAttempt.Invalid("Character document is a link/reparse point.");

        token.ThrowIfCancellationRequested();
        string json = _fileSystem.ReadAllText(path);
        CharacterDecodeResult decoded = CharacterDocumentPolicy.DecodeAndMigrate(json);
        if (decoded.Status == CharacterDecodeStatus.UnsupportedFutureVersion)
            return LoadAttempt.Future(decoded.Detail ?? "Character document uses a newer schema.");
        if (!decoded.IsSuccess || decoded.Document is null)
            return LoadAttempt.Invalid(decoded.Detail ?? "Character document is malformed.");

        CharacterNormalizationResult normalized = CharacterDocumentNormalizer.Normalize(decoded.Document);
        CharacterValidationResult validation = CharacterDocumentValidator.Validate(normalized.Document, _featureCatalog);
        if (!validation.IsValid)
            return LoadAttempt.Invalid(string.Join("; ", validation.Errors));
        if (normalized.Document.Id != expectedId)
            return LoadAttempt.Invalid("Character document ID does not match its directory.");

        return LoadAttempt.Valid(normalized.Document);
    }

    private string Quarantine(string sourcePath)
    {
        int suffix = 0;
        string destination;
        do
        {
            destination = _paths.Quarantine(sourcePath, _utcNow(), suffix++);
        }
        while (_fileSystem.FileExists(destination));

        _fileSystem.MoveFile(sourcePath, destination);
        return destination;
    }

    private void SafeDeleteTemporary(string path)
    {
        try
        {
            _fileSystem.DeleteFile(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Preserve the original operation result. A stale .tmp is never a load source.
        }
    }

    private enum AttemptStatus
    {
        Missing,
        Valid,
        Invalid,
        Future,
    }

    private readonly record struct LoadAttempt(
        AttemptStatus Status,
        CharacterDocument? Document,
        string? Detail)
    {
        public static LoadAttempt Missing { get; } = new(AttemptStatus.Missing, null, null);
        public static LoadAttempt Valid(CharacterDocument document) => new(AttemptStatus.Valid, document, null);
        public static LoadAttempt Invalid(string detail) => new(AttemptStatus.Invalid, null, detail);
        public static LoadAttempt Future(string detail) => new(AttemptStatus.Future, null, detail);
    }
}
