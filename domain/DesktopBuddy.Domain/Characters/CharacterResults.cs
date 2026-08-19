using System;
using System.Collections.Generic;
using System.Linq;

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

public sealed class CompiledColorChannels : IEquatable<CompiledColorChannels>
{
    private readonly string[] _ids;
    private readonly Rgba32[] _colors;

    public CompiledColorChannels(IEnumerable<KeyValuePair<string, Rgba32>> channels)
    {
        ArgumentNullException.ThrowIfNull(channels);
        KeyValuePair<string, Rgba32>[] ordered = channels
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .ToArray();
        if (ordered.Any(pair => string.IsNullOrWhiteSpace(pair.Key)) ||
            ordered.Select(pair => pair.Key).Distinct(StringComparer.Ordinal).Count() != ordered.Length)
            throw new ArgumentException("Compiled color channel IDs must be non-empty and unique.", nameof(channels));
        _ids = ordered.Select(pair => pair.Key).ToArray();
        _colors = ordered.Select(pair => pair.Value).ToArray();
    }

    public int Count => _ids.Length;

    public bool TryGetValue(string id, out Rgba32 color)
    {
        int index = Array.BinarySearch(_ids, id, StringComparer.Ordinal);
        if (index >= 0)
        {
            color = _colors[index];
            return true;
        }
        color = default;
        return false;
    }

    public bool Equals(CompiledColorChannels? other) =>
        other is not null && _ids.SequenceEqual(other._ids, StringComparer.Ordinal) && _colors.SequenceEqual(other._colors);
    public override bool Equals(object? obj) => obj is CompiledColorChannels other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        for (int index = 0; index < _ids.Length; index++)
        {
            hash.Add(_ids[index], StringComparer.Ordinal);
            hash.Add(_colors[index]);
        }
        return hash.ToHashCode();
    }

    public static CompiledColorChannels Primary(Rgba32 color) => new(
        [new KeyValuePair<string, Rgba32>(CosmeticDefinition.PrimaryColorChannel, color)]);
}

public readonly record struct CompiledFeatureAppearance
{
    public CompiledFeatureAppearance(
        string resolvedFeatureId,
        NormalizedFeatureTransform transform,
        Rgba32 color)
        : this(resolvedFeatureId, transform, CompiledColorChannels.Primary(color))
    {
    }

    public CompiledFeatureAppearance(
        string resolvedFeatureId,
        NormalizedFeatureTransform transform,
        CompiledColorChannels colorChannels)
    {
        ResolvedFeatureId = resolvedFeatureId;
        Transform = transform;
        ColorChannels = colorChannels;
    }

    public string ResolvedFeatureId { get; init; }
    public NormalizedFeatureTransform Transform { get; init; }
    public CompiledColorChannels ColorChannels { get; init; }
    public Rgba32 Color => ColorChannels is not null && ColorChannels.TryGetValue(CosmeticDefinition.PrimaryColorChannel, out Rgba32 color)
        ? color
        : default;
}

public sealed record CompiledCharacterAppearance
{
    public CompiledCharacterAppearance(
        Guid characterId,
        PartColorSet partColors,
        CompiledFeatureAppearance eyes,
        CompiledFeatureAppearance brows,
        CompiledFeatureAppearance mouth,
        CompiledFeatureAppearance torsoAccent)
        : this(
            characterId,
            partColors,
            Default(CharacterFeatureIds.FaceClassicPlate),
            Default(CharacterFeatureIds.HairNone),
            brows,
            eyes,
            Default(CharacterFeatureIds.NoseNone),
            mouth,
            Default(CharacterFeatureIds.EarsNone),
            torsoAccent,
            Default(CharacterFeatureIds.GlassesNone),
            Default(CharacterFeatureIds.HeadwearNone),
            Default(CharacterFeatureIds.TopNone),
            Default(CharacterFeatureIds.ShoesNone))
    {
    }

    public CompiledCharacterAppearance(
        Guid characterId,
        PartColorSet partColors,
        CompiledFeatureAppearance face,
        CompiledFeatureAppearance hair,
        CompiledFeatureAppearance brows,
        CompiledFeatureAppearance eyes,
        CompiledFeatureAppearance nose,
        CompiledFeatureAppearance mouth,
        CompiledFeatureAppearance ears,
        CompiledFeatureAppearance accessories,
        CompiledFeatureAppearance glasses,
        CompiledFeatureAppearance headwear,
        CompiledFeatureAppearance tops,
        CompiledFeatureAppearance shoes)
    {
        CharacterId = characterId;
        PartColors = partColors;
        Face = face;
        Hair = hair;
        Brows = brows;
        Eyes = eyes;
        Nose = nose;
        Mouth = mouth;
        Ears = ears;
        Accessories = accessories;
        Glasses = glasses;
        Headwear = headwear;
        Tops = tops;
        Shoes = shoes;
    }

    private readonly Rgba32? _favoriteColor;

    public Guid CharacterId { get; init; }
    public PartColorSet PartColors { get; init; }

    /// <summary>
    /// The character's fixed favourite colour. Falls back to the torso colour for any
    /// appearance compiled without one, so every existing construction site stays valid.
    /// </summary>
    public Rgba32 FavoriteColor
    {
        get => _favoriteColor ?? PartColors.Torso;
        init => _favoriteColor = value;
    }
    public CompiledFeatureAppearance Face { get; init; }
    public CompiledFeatureAppearance Hair { get; init; }
    public CompiledFeatureAppearance Brows { get; init; }
    public CompiledFeatureAppearance Eyes { get; init; }
    public CompiledFeatureAppearance Nose { get; init; }
    public CompiledFeatureAppearance Mouth { get; init; }
    public CompiledFeatureAppearance Ears { get; init; }
    public CompiledFeatureAppearance Accessories { get; init; }
    public CompiledFeatureAppearance Glasses { get; init; }
    public CompiledFeatureAppearance Headwear { get; init; }
    public CompiledFeatureAppearance Tops { get; init; }
    public CompiledFeatureAppearance Shoes { get; init; }
    public CompiledFeatureAppearance TorsoAccent { get => Accessories; init => Accessories = value; }

    private static CompiledFeatureAppearance Default(string id) => new(
        id,
        NormalizedFeatureTransform.Identity,
        new CompiledColorChannels([]));
}
