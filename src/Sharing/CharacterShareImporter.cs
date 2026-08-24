using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DesktopBuddy.Domain.Painting;
using DesktopBuddy.Domain.Sharing;
using DesktopBuddy.Persistence.Characters;
using DesktopBuddy.Persistence.Sharing;

namespace DesktopBuddy.Sharing;

public sealed class CharacterShareImporter
{
    private readonly WorkshopStagingStore _staging;
    private readonly CharacterStore _characters;
    private readonly CharacterPaintStore _paintStore;
    private readonly CharacterSharePayloadValidator _payloadValidator;
    private readonly Func<DateTimeOffset> _utcNow;

    public CharacterShareImporter(
        WorkshopStagingStore staging,
        CharacterStore characters,
        Func<DateTimeOffset>? utcNow = null)
    {
        _staging = staging ?? throw new ArgumentNullException(nameof(staging));
        _characters = characters ?? throw new ArgumentNullException(nameof(characters));
        _paintStore = characters.CreatePaintStore();
        _payloadValidator = new CharacterSharePayloadValidator(characters.FeatureCatalog);
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
    }

    public async Task<CharacterShareImportResult> ImportAsync(
        string steamInstallFolder,
        WorkshopImportSource source,
        CancellationToken token = default)
    {
        Guid operationId = Guid.NewGuid();
        WorkshopIncomingStaging incoming;
        try
        {
            token.ThrowIfCancellationRequested();
            incoming = _staging.SnapshotIncoming(steamInstallFolder, operationId);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException)
        {
            return Failure(exception.Message);
        }

        try
        {
            ShareFolderReadResult folder = new ShareFolderReader().Read(incoming.ContentRoot, ShareContentType.BuddyCharacter);
            CharacterSharePayloadResult payloadResult = _payloadValidator.Validate(folder);
            if (!payloadResult.IsSuccess || payloadResult.Payload is null)
                return Quarantine(incoming, payloadResult.Validation.Issues.Count == 0 ? "invalid" : payloadResult.Validation.Issues[0].Code.ToString(), string.Join("; ", payloadResult.Validation.Issues));

            CharacterSharePayload payload = payloadResult.Payload;
            Guid localId;
            do { localId = Guid.NewGuid(); }
            while (_characters.ContainsStoredCharacter(localId));

            var surfaces = new Dictionary<PaintPart, ReadOnlyMemory<byte>>();
            foreach ((PaintPart part, byte[] pixels) in payload.Surfaces)
                surfaces.Add(part, pixels);

            var localDocument = payload.Document with { Id = localId };
            CharacterPaintSaveResult saved = await _paintStore.SaveAsync(localDocument, surfaces, token);
            if (!saved.IsSuccess)
                return Quarantine(incoming, "save-failed", saved.Detail ?? saved.Character.Detail ?? "Imported buddy could not be saved.");

            string manifestPath = Path.Combine(incoming.ContentRoot, ShareManifestPolicy.ManifestFileName);
            byte[] manifestBytes = File.ReadAllBytes(manifestPath);
            WorkshopProvenance provenance = WorkshopProvenanceStore.Create(
                source.PublishedFileId,
                source.SteamTimeUpdated,
                _utcNow(),
                manifestBytes,
                ShareContentTypes.BuddyCharacter);
            string? provenanceWarning = null;
            try
            {
                WorkshopProvenanceStore.Write(_characters.Paths.Directory(localId), provenance);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // The character transaction already succeeded. Provenance is non-authoritative;
                // losing it must not roll back or corrupt the new local character.
                provenanceWarning = $"Imported successfully, but provenance could not be written: {exception.Message}";
            }

            _staging.Cleanup(operationId);
            return new CharacterShareImportResult(
                true,
                localId,
                payload.Warnings,
                Detail: provenanceWarning);
        }
        catch (OperationCanceledException)
        {
            _staging.Cleanup(operationId);
            return Failure("Buddy import cancelled.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException)
        {
            return Quarantine(incoming, "io", exception.Message);
        }
    }

    private CharacterShareImportResult Quarantine(WorkshopIncomingStaging incoming, string reason, string detail)
    {
        try
        {
            string path = _staging.Quarantine(incoming, reason);
            return new CharacterShareImportResult(false, null, Array.Empty<DesktopBuddy.Domain.Characters.CharacterCompileWarning>(), path, detail);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Failure($"{detail}; quarantine failed: {exception.Message}");
        }
    }

    private static CharacterShareImportResult Failure(string detail) =>
        new(false, null, Array.Empty<DesktopBuddy.Domain.Characters.CharacterCompileWarning>(), null, detail);
}
