using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.Json;
using DesktopBuddy.Domain.Characters;

namespace DesktopBuddy.Persistence.Characters;

/// <summary>
/// Browser-only CharacterDocument JSON boundary. The pinned static browser runtime can stall in
/// System.Text.Json object serialization even with source-generated metadata, so this boundary
/// writes the small, stable character schema explicitly and only uses JsonDocument for parsing.
/// Native builds keep using the authored CharacterDocumentPolicy serializer/migrator.
/// </summary>
internal static class BrowserCharacterJson
{
    private static readonly HashSet<string> KnownRootProperties = new(StringComparer.Ordinal)
    {
        "schemaVersion", "id", "displayName", "partColors", "features", "paint", "favoriteColor",
    };

    public static string Serialize(CharacterDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        CharacterDocument current = document with { SchemaVersion = CharacterDocumentPolicy.CurrentSchemaVersion };
        CharacterDocumentPolicy.ValidatePaintManifest(current.Paint);

        var builder = new StringBuilder(4096);
        builder.Append('{');
        bool first = true;
        AppendNumberProperty(builder, ref first, "schemaVersion", current.SchemaVersion);
        AppendStringProperty(builder, ref first, "id", current.Id.ToString("D"));
        AppendStringProperty(builder, ref first, "displayName", current.DisplayName);
        AppendPartColors(builder, ref first, current.PartColors);
        AppendFeatures(builder, ref first, current.Features);
        AppendPaint(builder, ref first, current.Paint);
        if (current.FavoriteColor is Rgba32 favorite)
            AppendStringProperty(builder, ref first, "favoriteColor", favorite.ToString());

        foreach ((string key, JsonElement value) in current.ExtensionData)
        {
            if (KnownRootProperties.Contains(key))
                continue;
            AppendPropertyName(builder, ref first, key);
            builder.Append(value.GetRawText());
        }

        builder.Append('}');
        return builder.ToString();
    }

    public static CharacterDecodeResult DecodeCurrentOrFallback(string json)
    {
        if (json is null)
            return new CharacterDecodeResult(CharacterDecodeStatus.Malformed, null, "JSON is required.");

        try
        {
            using JsonDocument parsed = JsonDocument.Parse(json);
            JsonElement root = parsed.RootElement;
            RequireKind(root, JsonValueKind.Object, "character");

            int schemaVersion = ReadRequiredInt(root, "schemaVersion");
            if (schemaVersion > CharacterDocumentPolicy.CurrentSchemaVersion)
            {
                return new CharacterDecodeResult(
                    CharacterDecodeStatus.UnsupportedFutureVersion,
                    null,
                    $"Schema {schemaVersion} is newer than {CharacterDocumentPolicy.CurrentSchemaVersion}.");
            }
            if (schemaVersion < 1)
                return Malformed($"Schema {schemaVersion} is not supported.");

            string idText = ReadRequiredString(root, "id");
            if (!Guid.TryParseExact(idText, "D", out Guid id))
                throw new JsonException("Character ID must be a canonical GUID in D format.");
            string displayName = ReadRequiredString(root, "displayName");

            CharacterPartColors partColors = ReadPartColors(root);
            CharacterFeatureSet features = ReadFeatures(root, schemaVersion);
            CharacterPaintManifest paint = schemaVersion == 1
                ? CharacterPaintManifest.Empty
                : ReadPaint(root);
            Rgba32? favoriteColor = ReadOptionalNullableColor(root, "favoriteColor");

            var extensionData = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            foreach (JsonProperty property in root.EnumerateObject())
            {
                if (!KnownRootProperties.Contains(property.Name))
                    extensionData[property.Name] = property.Value.Clone();
            }

            var document = new CharacterDocument
            {
                SchemaVersion = CharacterDocumentPolicy.CurrentSchemaVersion,
                Id = id,
                DisplayName = displayName,
                PartColors = partColors,
                Features = features,
                Paint = paint,
                FavoriteColor = favoriteColor,
                ExtensionData = extensionData,
            };
            CharacterDocumentPolicy.ValidatePaintManifest(document.Paint);
            return new CharacterDecodeResult(CharacterDecodeStatus.Valid, document);
        }
        catch (Exception exception) when (exception is JsonException or FormatException or OverflowException)
        {
            return Malformed(exception.Message);
        }
    }

    private static CharacterPartColors ReadPartColors(JsonElement root)
    {
        CharacterPartColors defaults = CharacterPartColors.BuiltIn;
        if (!root.TryGetProperty("partColors", out JsonElement colors))
            return defaults;
        RequireKind(colors, JsonValueKind.Object, "partColors");
        return new CharacterPartColors
        {
            Head = ReadOptionalColor(colors, "head", defaults.Head),
            Torso = ReadOptionalColor(colors, "torso", defaults.Torso),
            LeftHand = ReadOptionalColor(colors, "leftHand", defaults.LeftHand),
            RightHand = ReadOptionalColor(colors, "rightHand", defaults.RightHand),
            LeftFoot = ReadOptionalColor(colors, "leftFoot", defaults.LeftFoot),
            RightFoot = ReadOptionalColor(colors, "rightFoot", defaults.RightFoot),
        };
    }

    private static CharacterFeatureSet ReadFeatures(JsonElement root, int sourceSchema)
    {
        CharacterFeatureSet defaults = CharacterFeatureSet.BuiltIn;
        JsonElement features = default;
        bool hasFeatures = root.TryGetProperty("features", out features);
        if (hasFeatures)
            RequireKind(features, JsonValueKind.Object, "features");

        CharacterFeatureDocument face = ReadFeature(features, hasFeatures, "face", null, defaults.Face);
        CharacterFeatureDocument hair = ReadFeature(features, hasFeatures, "hair", null, defaults.Hair);
        CharacterFeatureDocument eyebrows = ReadFeature(
            features, hasFeatures, "eyebrows", sourceSchema <= 2 ? "brows" : null, defaults.Eyebrows);
        CharacterFeatureDocument eyes = ReadFeature(features, hasFeatures, "eyes", null, defaults.Eyes);
        CharacterFeatureDocument nose = ReadFeature(features, hasFeatures, "nose", null, defaults.Nose);
        CharacterFeatureDocument mouth = ReadFeature(features, hasFeatures, "mouth", null, defaults.Mouth);
        CharacterFeatureDocument ears = ReadFeature(features, hasFeatures, "ears", null, defaults.Ears);
        CharacterFeatureDocument accessories = ReadFeature(
            features, hasFeatures, "accessories", sourceSchema <= 2 ? "torsoAccent" : null, defaults.Accessories);
        CharacterFeatureDocument glasses = ReadFeature(features, hasFeatures, "glasses", null, defaults.Glasses);
        CharacterFeatureDocument headwear = ReadFeature(features, hasFeatures, "headwear", null, defaults.Headwear);
        CharacterFeatureDocument tops = ReadFeature(features, hasFeatures, "tops", null, defaults.Tops);
        CharacterFeatureDocument shoes = ReadFeature(features, hasFeatures, "shoes", null, defaults.Shoes);

        if (sourceSchema < 4)
        {
            nose = MigrateSchema4Feature(nose, negateOffsetY: true, CharacterFeatureIds.NoseNone);
            ears = MigrateSchema4Feature(ears, negateOffsetY: true, CharacterFeatureIds.EarsNone);
            accessories = MigrateSchema4Feature(accessories, negateOffsetY: false, CharacterFeatureIds.AccentNone);
            glasses = MigrateSchema4Feature(glasses, negateOffsetY: true, CharacterFeatureIds.GlassesNone);
        }

        return new CharacterFeatureSet
        {
            Face = face,
            Hair = hair,
            Eyebrows = eyebrows,
            Eyes = eyes,
            Nose = nose,
            Mouth = mouth,
            Ears = ears,
            Accessories = accessories,
            Glasses = glasses,
            Headwear = headwear,
            Tops = tops,
            Shoes = shoes,
        };
    }

    private static CharacterFeatureDocument ReadFeature(
        JsonElement features,
        bool hasFeatures,
        string canonicalName,
        string? legacyPreferredName,
        CharacterFeatureDocument defaults)
    {
        if (!hasFeatures)
            return defaults;

        JsonElement feature;
        bool found = legacyPreferredName is not null && features.TryGetProperty(legacyPreferredName, out feature);
        if (!found)
            found = features.TryGetProperty(canonicalName, out feature);
        if (!found)
            return defaults;

        RequireKind(feature, JsonValueKind.Object, $"features.{canonicalName}");
        string featureId = ReadOptionalString(feature, "featureId", defaults.FeatureId);
        double offsetX = ReadOptionalDouble(feature, "offsetX", defaults.OffsetX);
        double offsetY = ReadOptionalDouble(feature, "offsetY", defaults.OffsetY);
        double scale = ReadOptionalDouble(feature, "scale", defaults.Scale);
        Rgba32 color = ReadOptionalColor(feature, "color", defaults.Color);

        var colors = new Dictionary<string, Rgba32>(defaults.Colors, StringComparer.Ordinal);
        if (feature.TryGetProperty("colors", out JsonElement colorMap))
        {
            RequireKind(colorMap, JsonValueKind.Object, $"features.{canonicalName}.colors");
            colors.Clear();
            foreach (JsonProperty property in colorMap.EnumerateObject())
                colors[property.Name] = ParseColor(property.Value, $"features.{canonicalName}.colors.{property.Name}");
        }

        return new CharacterFeatureDocument
        {
            FeatureId = featureId,
            OffsetX = offsetX,
            OffsetY = offsetY,
            Scale = scale,
            Color = color,
            Colors = colors,
        };
    }

    private static CharacterFeatureDocument MigrateSchema4Feature(
        CharacterFeatureDocument feature,
        bool negateOffsetY,
        string emptyId)
    {
        if (string.Equals(feature.FeatureId, emptyId, StringComparison.Ordinal))
            return feature with { OffsetX = 0.0, OffsetY = 0.0, Scale = 1.0 };
        if (!negateOffsetY)
            return feature;
        return feature with { OffsetY = feature.OffsetY == 0 ? 0.0 : -feature.OffsetY };
    }

    private static CharacterPaintManifest ReadPaint(JsonElement root)
    {
        if (!root.TryGetProperty("paint", out JsonElement paint))
            return CharacterPaintManifest.Empty;
        RequireKind(paint, JsonValueKind.Object, "paint");
        return new CharacterPaintManifest
        {
            Head = ReadOptionalNullableString(paint, "head"),
            Torso = ReadOptionalNullableString(paint, "torso"),
            LeftHand = ReadOptionalNullableString(paint, "leftHand"),
            RightHand = ReadOptionalNullableString(paint, "rightHand"),
            LeftFoot = ReadOptionalNullableString(paint, "leftFoot"),
            RightFoot = ReadOptionalNullableString(paint, "rightFoot"),
        };
    }

    private static void AppendPartColors(StringBuilder builder, ref bool first, CharacterPartColors colors)
    {
        AppendPropertyName(builder, ref first, "partColors");
        builder.Append('{');
        bool inner = true;
        AppendStringProperty(builder, ref inner, "head", colors.Head.ToString());
        AppendStringProperty(builder, ref inner, "torso", colors.Torso.ToString());
        AppendStringProperty(builder, ref inner, "leftHand", colors.LeftHand.ToString());
        AppendStringProperty(builder, ref inner, "rightHand", colors.RightHand.ToString());
        AppendStringProperty(builder, ref inner, "leftFoot", colors.LeftFoot.ToString());
        AppendStringProperty(builder, ref inner, "rightFoot", colors.RightFoot.ToString());
        builder.Append('}');
    }

    private static void AppendFeatures(StringBuilder builder, ref bool first, CharacterFeatureSet features)
    {
        AppendPropertyName(builder, ref first, "features");
        builder.Append('{');
        bool inner = true;
        AppendFeature(builder, ref inner, "face", features.Face);
        AppendFeature(builder, ref inner, "hair", features.Hair);
        AppendFeature(builder, ref inner, "eyebrows", features.Eyebrows);
        AppendFeature(builder, ref inner, "eyes", features.Eyes);
        AppendFeature(builder, ref inner, "nose", features.Nose);
        AppendFeature(builder, ref inner, "mouth", features.Mouth);
        AppendFeature(builder, ref inner, "ears", features.Ears);
        AppendFeature(builder, ref inner, "accessories", features.Accessories);
        AppendFeature(builder, ref inner, "glasses", features.Glasses);
        AppendFeature(builder, ref inner, "headwear", features.Headwear);
        AppendFeature(builder, ref inner, "tops", features.Tops);
        AppendFeature(builder, ref inner, "shoes", features.Shoes);
        builder.Append('}');
    }

    private static void AppendFeature(
        StringBuilder builder,
        ref bool first,
        string name,
        CharacterFeatureDocument feature)
    {
        AppendPropertyName(builder, ref first, name);
        builder.Append('{');
        bool inner = true;
        AppendStringProperty(builder, ref inner, "featureId", feature.FeatureId);
        AppendDoubleProperty(builder, ref inner, "offsetX", feature.OffsetX);
        AppendDoubleProperty(builder, ref inner, "offsetY", feature.OffsetY);
        AppendDoubleProperty(builder, ref inner, "scale", feature.Scale);
        AppendStringProperty(builder, ref inner, "color", feature.Color.ToString());
        AppendPropertyName(builder, ref inner, "colors");
        builder.Append('{');
        bool colorFirst = true;
        foreach ((string channel, Rgba32 color) in feature.Colors)
            AppendStringProperty(builder, ref colorFirst, channel, color.ToString());
        builder.Append('}');
        builder.Append('}');
    }

    private static void AppendPaint(StringBuilder builder, ref bool first, CharacterPaintManifest paint)
    {
        AppendPropertyName(builder, ref first, "paint");
        builder.Append('{');
        bool inner = true;
        AppendNullableStringProperty(builder, ref inner, "head", paint.Head);
        AppendNullableStringProperty(builder, ref inner, "torso", paint.Torso);
        AppendNullableStringProperty(builder, ref inner, "leftHand", paint.LeftHand);
        AppendNullableStringProperty(builder, ref inner, "rightHand", paint.RightHand);
        AppendNullableStringProperty(builder, ref inner, "leftFoot", paint.LeftFoot);
        AppendNullableStringProperty(builder, ref inner, "rightFoot", paint.RightFoot);
        builder.Append('}');
    }

    private static void AppendNullableStringProperty(StringBuilder builder, ref bool first, string name, string? value)
    {
        if (value is not null)
            AppendStringProperty(builder, ref first, name, value);
    }

    private static void AppendStringProperty(StringBuilder builder, ref bool first, string name, string value)
    {
        AppendPropertyName(builder, ref first, name);
        AppendQuoted(builder, value);
    }

    private static void AppendNumberProperty(StringBuilder builder, ref bool first, string name, int value)
    {
        AppendPropertyName(builder, ref first, name);
        builder.Append(value.ToString(CultureInfo.InvariantCulture));
    }

    private static void AppendDoubleProperty(StringBuilder builder, ref bool first, string name, double value)
    {
        if (!double.IsFinite(value))
            throw new ArgumentOutOfRangeException(name, value, "Character JSON numbers must be finite.");
        AppendPropertyName(builder, ref first, name);
        builder.Append(value.ToString("R", CultureInfo.InvariantCulture));
    }

    private static void AppendPropertyName(StringBuilder builder, ref bool first, string name)
    {
        if (!first)
            builder.Append(',');
        first = false;
        AppendQuoted(builder, name);
        builder.Append(':');
    }

    private static void AppendQuoted(StringBuilder builder, string value)
    {
        builder.Append('"');
        foreach (char ch in value)
        {
            switch (ch)
            {
                case '"': builder.Append("\\\""); break;
                case '\\': builder.Append("\\\\"); break;
                case '\b': builder.Append("\\b"); break;
                case '\f': builder.Append("\\f"); break;
                case '\n': builder.Append("\\n"); break;
                case '\r': builder.Append("\\r"); break;
                case '\t': builder.Append("\\t"); break;
                default:
                    if (ch < 0x20 || char.IsSurrogate(ch))
                    {
                        builder.Append("\\u");
                        builder.Append(((int)ch).ToString("X4", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        builder.Append(ch);
                    }
                    break;
            }
        }
        builder.Append('"');
    }

    private static int ReadRequiredInt(JsonElement owner, string name)
    {
        if (!owner.TryGetProperty(name, out JsonElement value) || !value.TryGetInt32(out int result))
            throw new JsonException($"Missing or invalid {name}.");
        return result;
    }

    private static string ReadRequiredString(JsonElement owner, string name)
    {
        if (!owner.TryGetProperty(name, out JsonElement value) || value.ValueKind != JsonValueKind.String)
            throw new JsonException($"{name} must be a string.");
        return value.GetString() ?? throw new JsonException($"{name} is required.");
    }

    private static string ReadOptionalString(JsonElement owner, string name, string fallback)
    {
        if (!owner.TryGetProperty(name, out JsonElement value))
            return fallback;
        if (value.ValueKind != JsonValueKind.String)
            throw new JsonException($"{name} must be a string.");
        return value.GetString() ?? throw new JsonException($"{name} must be a string.");
    }

    private static string? ReadOptionalNullableString(JsonElement owner, string name)
    {
        if (!owner.TryGetProperty(name, out JsonElement value))
            return null;
        if (value.ValueKind != JsonValueKind.String)
            throw new JsonException($"{name} must be a string.");
        return value.GetString();
    }

    private static double ReadOptionalDouble(JsonElement owner, string name, double fallback)
    {
        if (!owner.TryGetProperty(name, out JsonElement value))
            return fallback;
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetDouble(out double result))
            throw new JsonException($"{name} must be a number.");
        return result;
    }

    private static Rgba32 ReadOptionalColor(JsonElement owner, string name, Rgba32 fallback)
    {
        if (!owner.TryGetProperty(name, out JsonElement value))
            return fallback;
        return ParseColor(value, name);
    }

    private static Rgba32? ReadOptionalNullableColor(JsonElement owner, string name)
    {
        if (!owner.TryGetProperty(name, out JsonElement value) || value.ValueKind == JsonValueKind.Null)
            return null;
        return ParseColor(value, name);
    }

    private static Rgba32 ParseColor(JsonElement value, string path)
    {
        if (value.ValueKind != JsonValueKind.String || !Rgba32.TryParse(value.GetString(), out Rgba32 color))
            throw new JsonException($"{path} must use opaque #RRGGBB syntax.");
        return color;
    }

    private static void RequireKind(JsonElement value, JsonValueKind expected, string path)
    {
        if (value.ValueKind != expected)
            throw new JsonException($"{path} must be a JSON {expected.ToString().ToLowerInvariant()}.");
    }

    private static CharacterDecodeResult Malformed(string detail) =>
        new(CharacterDecodeStatus.Malformed, null, detail);
}
