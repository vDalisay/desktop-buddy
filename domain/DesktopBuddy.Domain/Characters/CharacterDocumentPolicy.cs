using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using DesktopBuddy.Domain.Painting;

namespace DesktopBuddy.Domain.Characters;

public static class CharacterDocumentPolicy
{
    public const int CurrentSchemaVersion = 2;

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    private static readonly IReadOnlyDictionary<int, Func<JsonElement, JsonElement>> Migrations =
        new Dictionary<int, Func<JsonElement, JsonElement>>
        {
            [1] = MigrateSchema1To2,
        };

    public static CharacterDecodeResult DecodeAndMigrate(string json)
    {
        if (json is null)
            return new CharacterDecodeResult(CharacterDecodeStatus.Malformed, null, "JSON is required.");
        try
        {
            using JsonDocument parsed = JsonDocument.Parse(json);
            if (parsed.RootElement.ValueKind != JsonValueKind.Object)
                return Malformed("Character payload must be a JSON object.");
            if (!parsed.RootElement.TryGetProperty("schemaVersion", out JsonElement schemaElement) ||
                !schemaElement.TryGetInt32(out int schemaVersion))
                return Malformed("Missing or invalid schemaVersion.");
            if (schemaVersion > CurrentSchemaVersion)
                return new CharacterDecodeResult(CharacterDecodeStatus.UnsupportedFutureVersion, null,
                    $"Schema {schemaVersion} is newer than {CurrentSchemaVersion}.");

            JsonElement current = parsed.RootElement.Clone();
            while (schemaVersion < CurrentSchemaVersion)
            {
                if (!Migrations.TryGetValue(schemaVersion, out Func<JsonElement, JsonElement>? migrate))
                    return new CharacterDecodeResult(CharacterDecodeStatus.MissingMigrationStep, null,
                        $"No migration exists from schema {schemaVersion} to {schemaVersion + 1}.");
                current = migrate(current);
                schemaVersion++;
            }

            ValidateKnownJsonShapes(current);
            RawCharacterDocument raw = current.Deserialize<RawCharacterDocument>(Options)
                ?? throw new JsonException("Character payload was null.");
            return new CharacterDecodeResult(CharacterDecodeStatus.Valid, CreateCurrent(raw));
        }
        catch (Exception exception) when (exception is JsonException or FormatException or OverflowException)
        {
            return Malformed(exception.Message);
        }
    }

    public static string Serialize(CharacterDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        CharacterValidationResult validation = CharacterDocumentValidator.Validate(document);
        if (!validation.IsValid)
        {
            string detail = string.Join("; ", validation.Errors.Select(error => $"{error.Path}: {error.Message}"));
            throw new ArgumentException(detail, nameof(document));
        }
        ValidatePaintManifest(document.Paint);
        return JsonSerializer.Serialize(document with { SchemaVersion = CurrentSchemaVersion }, Options);
    }

    public static void ValidatePaintManifest(CharacterPaintManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        foreach ((PaintPart part, string path) in manifest.Declared())
        {
            if (!string.Equals(path, PaintPolicy.WhitelistedPaths[part], StringComparison.Ordinal))
                throw new FormatException($"Paint path '{path}' is not allowed for {part}.");
            if (!PaintPolicy.TryResolvePart(path, out PaintPart resolved) || resolved != part)
                throw new FormatException($"Paint path '{path}' does not match {part}.");
        }
    }

    private static JsonElement MigrateSchema1To2(JsonElement source)
    {
        JsonObject root = JsonNode.Parse(source.GetRawText())?.AsObject()
            ?? throw new JsonException("Schema 1 payload was not an object.");
        root["schemaVersion"] = 2;
        root["paint"] = new JsonObject();
        using JsonDocument migrated = JsonDocument.Parse(root.ToJsonString());
        return migrated.RootElement.Clone();
    }

    private static void ValidateKnownJsonShapes(JsonElement root)
    {
        if (root.TryGetProperty("partColors", out JsonElement colors))
        {
            RequireKind(colors, JsonValueKind.Object, "partColors");
            foreach (string name in new[] { "head", "torso", "leftHand", "rightHand", "leftFoot", "rightFoot" })
                RequireOptionalKind(colors, name, JsonValueKind.String, $"partColors.{name}");
        }
        if (root.TryGetProperty("features", out JsonElement features))
        {
            RequireKind(features, JsonValueKind.Object, "features");
            foreach (string name in new[] { "eyes", "brows", "mouth", "torsoAccent" })
                ValidateFeatureShape(features, name);
        }
        if (root.TryGetProperty("paint", out JsonElement paint))
        {
            RequireKind(paint, JsonValueKind.Object, "paint");
            foreach (string name in new[] { "head", "torso", "leftHand", "rightHand", "leftFoot", "rightFoot" })
                RequireOptionalKind(paint, name, JsonValueKind.String, $"paint.{name}");
        }
    }

    private static void ValidateFeatureShape(JsonElement features, string propertyName)
    {
        if (!features.TryGetProperty(propertyName, out JsonElement feature))
            return;
        string path = $"features.{propertyName}";
        RequireKind(feature, JsonValueKind.Object, path);
        RequireOptionalKind(feature, "featureId", JsonValueKind.String, $"{path}.featureId");
        RequireOptionalKind(feature, "offsetX", JsonValueKind.Number, $"{path}.offsetX");
        RequireOptionalKind(feature, "offsetY", JsonValueKind.Number, $"{path}.offsetY");
        RequireOptionalKind(feature, "scale", JsonValueKind.Number, $"{path}.scale");
        RequireOptionalKind(feature, "color", JsonValueKind.String, $"{path}.color");
    }

    private static void RequireOptionalKind(JsonElement parent, string propertyName, JsonValueKind expected, string path)
    {
        if (parent.TryGetProperty(propertyName, out JsonElement value))
            RequireKind(value, expected, path);
    }

    private static void RequireKind(JsonElement value, JsonValueKind expected, string path)
    {
        if (value.ValueKind != expected)
            throw new JsonException($"{path} must be a JSON {expected.ToString().ToLowerInvariant()}.");
    }

    private static CharacterDocument CreateCurrent(RawCharacterDocument raw)
    {
        if (raw.SchemaVersion != CurrentSchemaVersion)
            throw new JsonException("Migration did not produce the current schema.");
        if (raw.Id is null || !Guid.TryParseExact(raw.Id, "D", out Guid id))
            throw new JsonException("Character ID must be a canonical GUID in D format.");
        if (raw.DisplayName is null)
            throw new JsonException("Display name is required.");

        CharacterPartColors colorDefaults = CharacterPartColors.BuiltIn;
        CharacterFeatureSet featureDefaults = CharacterFeatureSet.BuiltIn;
        RawPartColors? colors = raw.PartColors;
        RawFeatureSet? features = raw.Features;
        RawPaintManifest? paint = raw.Paint;
        CharacterPaintManifest manifest = new()
        {
            Head = paint?.Head,
            Torso = paint?.Torso,
            LeftHand = paint?.LeftHand,
            RightHand = paint?.RightHand,
            LeftFoot = paint?.LeftFoot,
            RightFoot = paint?.RightFoot,
        };
        ValidatePaintManifest(manifest);

        var extensionData = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        if (raw.ExtensionData is not null)
            foreach ((string key, JsonElement value) in raw.ExtensionData)
                extensionData.Add(key, value.Clone());

        return new CharacterDocument
        {
            SchemaVersion = CurrentSchemaVersion,
            Id = id,
            DisplayName = raw.DisplayName,
            PartColors = new CharacterPartColors
            {
                Head = colors?.Head ?? colorDefaults.Head,
                Torso = colors?.Torso ?? colorDefaults.Torso,
                LeftHand = colors?.LeftHand ?? colorDefaults.LeftHand,
                RightHand = colors?.RightHand ?? colorDefaults.RightHand,
                LeftFoot = colors?.LeftFoot ?? colorDefaults.LeftFoot,
                RightFoot = colors?.RightFoot ?? colorDefaults.RightFoot,
            },
            Features = new CharacterFeatureSet
            {
                Eyes = CreateFeature(features?.Eyes, featureDefaults.Eyes),
                Brows = CreateFeature(features?.Brows, featureDefaults.Brows),
                Mouth = CreateFeature(features?.Mouth, featureDefaults.Mouth),
                TorsoAccent = CreateFeature(features?.TorsoAccent, featureDefaults.TorsoAccent),
            },
            Paint = manifest,
            ExtensionData = extensionData,
        };
    }

    private static CharacterFeatureDocument CreateFeature(RawFeature? raw, CharacterFeatureDocument defaults) => new()
    {
        FeatureId = raw?.FeatureId ?? defaults.FeatureId,
        OffsetX = raw?.OffsetX ?? defaults.OffsetX,
        OffsetY = raw?.OffsetY ?? defaults.OffsetY,
        Scale = raw?.Scale ?? defaults.Scale,
        Color = raw?.Color ?? defaults.Color,
    };

    private static CharacterDecodeResult Malformed(string detail) =>
        new(CharacterDecodeStatus.Malformed, null, detail);

    private sealed class RawCharacterDocument
    {
        public int SchemaVersion { get; init; }
        public string? Id { get; init; }
        public string? DisplayName { get; init; }
        public RawPartColors? PartColors { get; init; }
        public RawFeatureSet? Features { get; init; }
        public RawPaintManifest? Paint { get; init; }
        [JsonExtensionData] public Dictionary<string, JsonElement>? ExtensionData { get; init; }
    }

    private sealed class RawPartColors
    {
        public Rgba32? Head { get; init; }
        public Rgba32? Torso { get; init; }
        public Rgba32? LeftHand { get; init; }
        public Rgba32? RightHand { get; init; }
        public Rgba32? LeftFoot { get; init; }
        public Rgba32? RightFoot { get; init; }
    }

    private sealed class RawFeatureSet
    {
        public RawFeature? Eyes { get; init; }
        public RawFeature? Brows { get; init; }
        public RawFeature? Mouth { get; init; }
        public RawFeature? TorsoAccent { get; init; }
    }

    private sealed class RawFeature
    {
        public string? FeatureId { get; init; }
        public double? OffsetX { get; init; }
        public double? OffsetY { get; init; }
        public double? Scale { get; init; }
        public Rgba32? Color { get; init; }
    }

    private sealed class RawPaintManifest
    {
        public string? Head { get; init; }
        public string? Torso { get; init; }
        public string? LeftHand { get; init; }
        public string? RightHand { get; init; }
        public string? LeftFoot { get; init; }
        public string? RightFoot { get; init; }
    }
}
