using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using DesktopBuddy.Domain.Characters;
using DesktopBuddy.Domain.Painting;
using DesktopBuddy.Domain.Sharing;
using DesktopBuddy.Persistence.Characters;
using DesktopBuddy.Persistence.Sharing;

namespace DesktopBuddy.Sharing;

public sealed class CharacterShareExporter
{
    private static readonly JsonSerializerOptions CharacterJson = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly WorkshopStagingStore _staging;
    private readonly CharacterPaintStore _paintStore;
    private readonly CharacterFeatureCatalog _featureCatalog;
    private readonly CharacterSharePayloadValidator _payloadValidator;
    private readonly string _appVersion;

    public CharacterShareExporter(
        WorkshopStagingStore staging,
        CharacterStore characters,
        string appVersion)
    {
        _staging = staging ?? throw new ArgumentNullException(nameof(staging));
        ArgumentNullException.ThrowIfNull(characters);
        _paintStore = characters.CreatePaintStore();
        _featureCatalog = characters.FeatureCatalog;
        _payloadValidator = new CharacterSharePayloadValidator(_featureCatalog);
        _appVersion = string.IsNullOrWhiteSpace(appVersion) ? "development" : appVersion;
    }

    public async Task<ShareExportResult> ExportAsync(
        Guid characterId,
        Guid operationId,
        ReadOnlyMemory<byte>? previewPng = null,
        CancellationToken token = default)
    {
        CharacterPaintLoadResult loaded = await _paintStore.LoadAsync(characterId, token);
        if (!loaded.IsSuccess || loaded.Character.Document is null)
            return new ShareExportResult(false, null, null, loaded.Detail ?? loaded.Character.Detail ?? "Character could not be loaded.");

        WorkshopPublishStaging staging = _staging.CreatePublish(operationId);
        try
        {
            token.ThrowIfCancellationRequested();
            CharacterDocument document = loaded.Character.Document;
            CharacterValidationResult domainValidation = CharacterDocumentValidator.Validate(document, _featureCatalog);
            if (!domainValidation.IsValid)
                throw new InvalidDataException(string.Join("; ", domainValidation.Errors.Select(error => $"{error.Path}: {error.Message}")));
            CharacterDocumentPolicy.ValidatePaintManifest(document.Paint);

            byte[] characterBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(
                document with { SchemaVersion = CharacterDocumentPolicy.CurrentSchemaVersion },
                CharacterJson));
            if (characterBytes.Length > ShareManifestPolicy.MaximumCharacterJsonBytes)
                throw new InvalidDataException("character.json exceeds the share size limit.");

            var entries = new List<Sha256FileEntry>();
            WriteEntry(staging.ContentRoot, ShareManifestPolicy.CharacterFileName, characterBytes, entries);
            foreach ((PaintPart part, string path) in document.Paint.Declared())
            {
                if (!loaded.Surfaces.TryGetValue(part, out byte[]? pixels))
                    throw new InvalidDataException($"Character declares paint for {part} but no decoded surface was loaded.");
                byte[] encoded = PaintPngCodec.Encode(pixels);
                WriteEntry(staging.ContentRoot, path, encoded, entries);
            }

            ShareManifest manifest = ShareManifestPolicy.Create(
                ShareContentType.BuddyCharacter,
                document.Id.ToString("D"),
                _appVersion,
                entries);
            byte[] manifestBytes = ShareManifestPolicy.Serialize(manifest);
            WorkshopStagingStore.WriteOwnedFile(staging.ContentRoot, ShareManifestPolicy.ManifestFileName, manifestBytes);

            if (previewPng is { } preview && preview.Length > 0)
            {
                if (preview.Length > 2 * 1024 * 1024)
                    throw new InvalidDataException("Workshop preview exceeds the 2 MiB local preview budget.");
                File.WriteAllBytes(staging.PreviewPath, preview.ToArray());
            }

            ShareFolderReadResult folder = new ShareFolderReader().Read(staging.ContentRoot, ShareContentType.BuddyCharacter);
            CharacterSharePayloadResult verified = _payloadValidator.Validate(folder);
            if (!verified.IsSuccess)
            {
                _staging.Cleanup(operationId);
                return new ShareExportResult(false, null, null, string.Join("; ", verified.Validation.Issues));
            }

            return new ShareExportResult(true, staging, manifest);
        }
        catch (OperationCanceledException)
        {
            _staging.Cleanup(operationId);
            return new ShareExportResult(false, null, null, "Buddy share export cancelled.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException or JsonException)
        {
            _staging.Cleanup(operationId);
            return new ShareExportResult(false, null, null, exception.Message);
        }
    }

    private static void WriteEntry(
        string contentRoot,
        string path,
        byte[] bytes,
        ICollection<Sha256FileEntry> entries)
    {
        WorkshopStagingStore.WriteOwnedFile(contentRoot, path, bytes);
        entries.Add(new Sha256FileEntry
        {
            Path = path,
            Sha256 = Convert.ToHexString(SHA256.HashData(bytes)),
            EncodedBytes = bytes.LongLength,
        });
    }
}
