using System;
using System.Collections.Generic;

namespace DesktopBuddy.Domain.Characters;

public enum CharacterDecodeStatus
{
    Valid,
    Malformed,
    UnsupportedFutureVersion,
    MissingMigrationStep,
}

public readonly record struct CharacterDecodeResult(
    CharacterDecodeStatus Status,
    CharacterDocument? Document,
    string? Detail = null)
{
    public bool IsSuccess => Status == CharacterDecodeStatus.Valid && Document is not null;
}

public readonly record struct CharacterNormalizationResult(
    CharacterDocument Document,
    IReadOnlyList<string> ChangedFields)
{
    public bool Changed => ChangedFields.Count > 0;
}

public readonly record struct CharacterValidationIssue(string Path, string Message);

public readonly record struct CharacterValidationResult(
    IReadOnlyList<CharacterValidationIssue> Errors)
{
    public bool IsValid => Errors.Count == 0;
}

public readonly record struct CharacterCompileWarning(
    string Path,
    string OriginalFeatureId,
    string ResolvedFeatureId,
    string Message);

public readonly record struct CharacterCompileResult(
    CompiledCharacterAppearance? Appearance,
    IReadOnlyList<CharacterCompileWarning> Warnings,
    IReadOnlyList<CharacterValidationIssue> Errors)
{
    public bool IsSuccess => Appearance is not null && Errors.Count == 0;
}

public readonly record struct PartColorSet(
    Rgba32 Head,
    Rgba32 Torso,
    Rgba32 LeftHand,
    Rgba32 RightHand,
    Rgba32 LeftFoot,
    Rgba32 RightFoot);

public readonly record struct CompiledFeatureAppearance(
    string ResolvedFeatureId,
    NormalizedFeatureTransform Transform,
    Rgba32 Color);

public sealed record CompiledCharacterAppearance(
    Guid CharacterId,
    PartColorSet PartColors,
    CompiledFeatureAppearance Eyes,
    CompiledFeatureAppearance Brows,
    CompiledFeatureAppearance Mouth,
    CompiledFeatureAppearance TorsoAccent);
