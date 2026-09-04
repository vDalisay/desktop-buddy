using System;
using System.Collections.Generic;
using DesktopBuddy.Domain.Characters;
using DesktopBuddy.Domain.Painting;
using DesktopBuddy.Domain.Sharing;
using DesktopBuddy.Persistence.Sharing;

namespace DesktopBuddy.Sharing;

public readonly record struct WorkshopImportSource(
    ulong PublishedFileId,
    long SteamTimeUpdated,
    string DisplayName,
    string Description = "");

public sealed record ShareExportResult(
    bool Success,
    WorkshopPublishStaging? Staging,
    ShareManifest? Manifest,
    string? Detail = null);

public sealed record RoomShareImportResult(
    bool Success,
    RoomPaintingLibraryEntry? Entry,
    string? QuarantinePath = null,
    string? Detail = null);

public sealed record CharacterShareImportResult(
    bool Success,
    Guid? LocalCharacterId,
    IReadOnlyList<CharacterCompileWarning> Warnings,
    string? QuarantinePath = null,
    string? Detail = null);

public sealed record CharacterSharePayload(
    CharacterDocument Document,
    IReadOnlyDictionary<PaintPart, byte[]> Surfaces,
    IReadOnlyList<CharacterCompileWarning> Warnings);

public sealed record CharacterSharePayloadResult(
    CharacterSharePayload? Payload,
    ShareValidationResult Validation)
{
    public bool IsSuccess => Payload is not null && Validation.IsValid;
}
