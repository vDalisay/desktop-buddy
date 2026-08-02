using System;
using DesktopBuddy.Domain.Characters;

namespace DesktopBuddy.Persistence.Characters;

public enum CharacterLoadStatus
{
    Loaded,
    BackupRecovered,
    NotFound,
    Invalid,
    UnsupportedFutureVersion,
    RejectedPath,
    Cancelled,
    IoFailure,
}

public readonly record struct CharacterLoadResult(
    CharacterLoadStatus Status,
    CharacterDocument? Document,
    string? Detail = null,
    string? QuarantinedPrimary = null,
    string? QuarantinedBackup = null)
{
    public bool IsSuccess =>
        Status is CharacterLoadStatus.Loaded or CharacterLoadStatus.BackupRecovered &&
        Document is not null;
}

public enum CharacterSaveStatus
{
    Saved,
    Invalid,
    RejectedPath,
    Cancelled,
    IoFailure,
}

public readonly record struct CharacterSaveResult(
    CharacterSaveStatus Status,
    CharacterDocument? Document,
    string? Detail = null)
{
    public bool IsSuccess => Status == CharacterSaveStatus.Saved && Document is not null;
}

public enum CharacterDeleteStatus
{
    Deleted,
    NotFound,
    RejectedPath,
    Cancelled,
    IoFailure,
}

public readonly record struct CharacterDeleteResult(
    CharacterDeleteStatus Status,
    Guid CharacterId,
    string? Detail = null)
{
    public bool IsSuccess => Status is CharacterDeleteStatus.Deleted or CharacterDeleteStatus.NotFound;
}

public enum CharacterIndexStatus
{
    Available,
    InvalidMetadata,
    UnsupportedFutureVersion,
    RejectedPath,
}

public sealed record CharacterIndexEntry(
    Guid CharacterId,
    string DirectoryName,
    string DisplayName,
    int? SchemaVersion,
    CharacterIndexStatus Status,
    string? Detail = null)
{
    public bool IsEnabled => Status == CharacterIndexStatus.Available;
}
