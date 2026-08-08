using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace DesktopBuddy.Domain.Characters;

public enum CosmeticTransformPolicy
{
    None,
    MoveAndUniformScale,
}

public sealed record CosmeticTransformBounds
{
    public static CosmeticTransformBounds None { get; } = new(0, 0, 0, 0, 1, 1);
    public static CosmeticTransformBounds Standard { get; } = new(
        NormalizedFeatureTransform.MinimumOffset,
        NormalizedFeatureTransform.MaximumOffset,
        NormalizedFeatureTransform.MinimumOffset,
        NormalizedFeatureTransform.MaximumOffset,
        NormalizedFeatureTransform.MinimumScale,
        NormalizedFeatureTransform.MaximumScale);

    public CosmeticTransformBounds(
        double minimumOffsetX,
        double maximumOffsetX,
        double minimumOffsetY,
        double maximumOffsetY,
        double minimumScale,
        double maximumScale)
    {
        if (!double.IsFinite(minimumOffsetX) || !double.IsFinite(maximumOffsetX) ||
            !double.IsFinite(minimumOffsetY) || !double.IsFinite(maximumOffsetY) ||
            !double.IsFinite(minimumScale) || !double.IsFinite(maximumScale) ||
            minimumOffsetX > maximumOffsetX || minimumOffsetY > maximumOffsetY ||
            minimumScale <= 0 || minimumScale > maximumScale)
            throw new ArgumentException("Cosmetic transform bounds must be finite and ordered.");
        MinimumOffsetX = minimumOffsetX;
        MaximumOffsetX = maximumOffsetX;
        MinimumOffsetY = minimumOffsetY;
        MaximumOffsetY = maximumOffsetY;
        MinimumScale = minimumScale;
        MaximumScale = maximumScale;
    }

    public double MinimumOffsetX { get; }
    public double MaximumOffsetX { get; }
    public double MinimumOffsetY { get; }
    public double MaximumOffsetY { get; }
    public double MinimumScale { get; }
    public double MaximumScale { get; }

    public bool Contains(in NormalizedFeatureTransform transform) =>
        transform.OffsetX >= MinimumOffsetX && transform.OffsetX <= MaximumOffsetX &&
        transform.OffsetY >= MinimumOffsetY && transform.OffsetY <= MaximumOffsetY &&
        transform.Scale >= MinimumScale && transform.Scale <= MaximumScale;
}

public sealed record CosmeticColorChannelDefinition
{
    public CosmeticColorChannelDefinition(string id, Rgba32 defaultColor)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("Color channel ID is required.", nameof(id));
        Id = id;
        DefaultColor = defaultColor;
    }

    public string Id { get; }
    public Rgba32 DefaultColor { get; }
}

public sealed record CosmeticDefinition
{
    public const string PrimaryColorChannel = "primary";

    public CosmeticDefinition(
        string id,
        CharacterFeatureSlot slot,
        string displayNameKey,
        int sortOrder,
        bool isFreeDefault,
        CosmeticTransformPolicy transformPolicy,
        CosmeticTransformBounds transformBounds,
        NormalizedFeatureTransform defaultTransform,
        IEnumerable<CosmeticColorChannelDefinition>? colorChannels,
        string fallbackId,
        bool hidesHair = false)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("Cosmetic ID is required.", nameof(id));
        if (string.IsNullOrWhiteSpace(displayNameKey))
            throw new ArgumentException("Display-name key is required.", nameof(displayNameKey));
        if (string.IsNullOrWhiteSpace(fallbackId))
            throw new ArgumentException("Fallback ID is required.", nameof(fallbackId));
        if (sortOrder < 0)
            throw new ArgumentOutOfRangeException(nameof(sortOrder));
        ArgumentNullException.ThrowIfNull(transformBounds);
        if (!transformBounds.Contains(defaultTransform))
            throw new ArgumentException("Default transform must be inside its authored bounds.", nameof(defaultTransform));
        if (transformPolicy == CosmeticTransformPolicy.None && defaultTransform != NormalizedFeatureTransform.Identity)
            throw new ArgumentException("A non-transformable cosmetic must use the identity transform.", nameof(defaultTransform));

        CosmeticColorChannelDefinition[] channels = colorChannels?.ToArray() ?? [];
        if (channels.Select(channel => channel.Id).Distinct(StringComparer.Ordinal).Count() != channels.Length)
            throw new ArgumentException("Color channel IDs must be unique.", nameof(colorChannels));

        Id = id;
        Slot = slot == CharacterFeatureSlot.TorsoAccent ? CharacterFeatureSlot.Accessories : slot;
        DisplayNameKey = displayNameKey;
        SortOrder = sortOrder;
        IsFreeDefault = isFreeDefault;
        TransformPolicy = transformPolicy;
        TransformBounds = transformBounds;
        DefaultTransform = defaultTransform;
        ColorChannels = new ReadOnlyCollection<CosmeticColorChannelDefinition>(channels);
        FallbackId = fallbackId;
        HidesHair = hidesHair;
        if (HidesHair && Slot != CharacterFeatureSlot.Headwear)
            throw new ArgumentException("Only headwear may hide hair.", nameof(hidesHair));
    }

    public string Id { get; }
    public CharacterFeatureSlot Slot { get; }
    public string DisplayNameKey { get; }
    public int SortOrder { get; }
    public bool IsFreeDefault { get; }
    public CosmeticTransformPolicy TransformPolicy { get; }
    public CosmeticTransformBounds TransformBounds { get; }
    public NormalizedFeatureTransform DefaultTransform { get; }
    public IReadOnlyList<CosmeticColorChannelDefinition> ColorChannels { get; }
    public bool IsTintable => ColorChannels.Count > 0;
    public string FallbackId { get; }
    public bool HidesHair { get; }
}
