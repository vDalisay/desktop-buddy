using System;
using System.Collections.Generic;
using System.Text.Json;

namespace DesktopBuddy.Domain.Characters;

public static class CharacterDocumentNormalizer
{
    public static CharacterNormalizationResult Normalize(CharacterDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var changed = new List<string>();

        string normalizedName = document.DisplayName.Trim();
        if (!string.Equals(normalizedName, document.DisplayName, StringComparison.Ordinal))
            changed.Add("displayName");

        CharacterFeatureSet features = new()
        {
            Face = NormalizeFeature(document.Features.Face, "features.face", changed),
            Hair = NormalizeFeature(document.Features.Hair, "features.hair", changed),
            Eyebrows = NormalizeFeature(document.Features.Eyebrows, "features.eyebrows", changed),
            Eyes = NormalizeFeature(document.Features.Eyes, "features.eyes", changed),
            Nose = NormalizeFeature(document.Features.Nose, "features.nose", changed),
            Mouth = NormalizeFeature(document.Features.Mouth, "features.mouth", changed),
            Ears = NormalizeFeature(document.Features.Ears, "features.ears", changed),
            Accessories = NormalizeFeature(document.Features.Accessories, "features.accessories", changed),
            Glasses = NormalizeFeature(document.Features.Glasses, "features.glasses", changed),
            Headwear = NormalizeFeature(document.Features.Headwear, "features.headwear", changed),
            Tops = NormalizeFeature(document.Features.Tops, "features.tops", changed),
            Shoes = NormalizeFeature(document.Features.Shoes, "features.shoes", changed),
        };

        var extensionData = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach ((string key, JsonElement value) in document.ExtensionData)
            extensionData.Add(key, value.Clone());

        CharacterDocument normalized = document with
        {
            DisplayName = normalizedName,
            PartColors = document.PartColors with { },
            Features = features,
            ExtensionData = extensionData,
        };

        return new CharacterNormalizationResult(normalized, changed.ToArray());
    }

    private static CharacterFeatureDocument NormalizeFeature(
        CharacterFeatureDocument feature,
        string path,
        ICollection<string> changed)
    {
        double offsetX = ClampFinite(
            feature.OffsetX,
            NormalizedFeatureTransform.MinimumOffset,
            NormalizedFeatureTransform.MaximumOffset);
        double offsetY = ClampFinite(
            feature.OffsetY,
            NormalizedFeatureTransform.MinimumOffset,
            NormalizedFeatureTransform.MaximumOffset);
        double scale = ClampFinite(
            feature.Scale,
            NormalizedFeatureTransform.MinimumScale,
            NormalizedFeatureTransform.MaximumScale);

        if (!offsetX.Equals(feature.OffsetX))
            changed.Add($"{path}.offsetX");
        if (!offsetY.Equals(feature.OffsetY))
            changed.Add($"{path}.offsetY");
        if (!scale.Equals(feature.Scale))
            changed.Add($"{path}.scale");

        var colors = feature.Colors is null
            ? new Dictionary<string, Rgba32>(StringComparer.Ordinal)
            : new Dictionary<string, Rgba32>(feature.Colors, StringComparer.Ordinal);
        if (feature.Colors is null)
            changed.Add($"{path}.colors");

        return feature with
        {
            OffsetX = offsetX,
            OffsetY = offsetY,
            Scale = scale,
            Colors = colors,
        };
    }

    private static double ClampFinite(double value, double minimum, double maximum) =>
        double.IsFinite(value) ? Math.Clamp(value, minimum, maximum) : value;
}
