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
            Eyes = NormalizeFeature(document.Features.Eyes, "features.eyes", changed),
            Brows = NormalizeFeature(document.Features.Brows, "features.brows", changed),
            Mouth = NormalizeFeature(document.Features.Mouth, "features.mouth", changed),
            TorsoAccent = NormalizeFeature(
                document.Features.TorsoAccent,
                "features.torsoAccent",
                changed),
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

        return feature with
        {
            OffsetX = offsetX,
            OffsetY = offsetY,
            Scale = scale,
        };
    }

    private static double ClampFinite(double value, double minimum, double maximum) =>
        double.IsFinite(value) ? Math.Clamp(value, minimum, maximum) : value;
}
