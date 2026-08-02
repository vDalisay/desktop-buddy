using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DesktopBuddy.Domain.Characters;

public static class CharacterDocumentPolicy
{
    public const int CurrentSchemaVersion = 1;

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    private static readonly IReadOnlyDictionary<int, Func<JsonElement, JsonElement>> Migrations =
        new Dictionary<int, Func<JsonElement, JsonElement>>();

    public static CharacterDecodeResult DecodeAndMigrate(string json)
    {
        if (json is null)
            return new CharacterDecodeResult(CharacterDecodeStatus.Malformed, null, "JSON is required.");

        try
        {
            using JsonDocument parsed = JsonDocument.Parse(json);
            if (parsed.RootElement.ValueKind != JsonValueKind.Object)
            {
                return new CharacterDecodeResult(
                    CharacterDecodeStatus.Malformed,
                    null,
                    "Character payload must be a JSON object.");
            }

            if (!parsed.RootElement.TryGetProperty("schemaVersion", out JsonElement schemaElement) ||
                !schemaElement.TryGetInt32(out int schemaVersion))
            {
                return new CharacterDecodeResult(
                    CharacterDecodeStatus.Malformed,
                    null,
                    "Missing or invalid schemaVersion.");
            }

            if (schemaVersion > CurrentSchemaVersion)
            {
                return new CharacterDecodeResult(
                    CharacterDecodeStatus.UnsupportedFutureVersion,
                    null,
                    $"Schema {schemaVersion} is newer than {CurrentSchemaVersion}.");
            }

            JsonElement current = parsed.RootElement.Clone();
            while (schemaVersion < CurrentSchemaVersion)
            {
                if (!Migrations.TryGetValue(schemaVersion, out Func<JsonElement, JsonElement>? migrate))
                {
                    return new CharacterDecodeResult(
                        CharacterDecodeStatus.MissingMigrationStep,
                        null,
                        $"No migration exists from schema {schemaVersion} to {schemaVersion + 1}.");
                }

                current = migrate(current);
                schemaVersion++;
            }

            RawCharacterDocument raw = current.Deserialize<RawCharacterDocument>(Options)
                ?? throw new JsonException("Character payload was null.");
            CharacterDocument document = CreateCurrent(raw);
            return new CharacterDecodeResult(CharacterDecodeStatus.Valid, document);
        }
        catch (JsonException exception)
        {
            return new CharacterDecodeResult(
                CharacterDecodeStatus.Malformed,
                null,
                exception.Message);
        }
        catch (FormatException exception)
        {
            return new CharacterDecodeResult(
                CharacterDecodeStatus.Malformed,
                null,
                exception.Message);
        }
        catch (OverflowException exception)
        {
            return new CharacterDecodeResult(
                CharacterDecodeStatus.Malformed,
                null,
                exception.Message);
        }
    }

    public static string Serialize(CharacterDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        CharacterValidationResult validation = CharacterDocumentValidator.Validate(document);
        if (!validation.IsValid)
        {
            string detail = string.Join(
                "; ",
                validation.Errors.Select(error => $"{error.Path}: {error.Message}"));
            throw new ArgumentException(detail, nameof(document));
        }

        return JsonSerializer.Serialize(document, Options);
    }

    private static CharacterDocument CreateCurrent(RawCharacterDocument raw)
    {
        if (raw.SchemaVersion != CurrentSchemaVersion)
            throw new JsonException("Migration did not produce the current schema.");
        if (raw.Id is null || !Guid.TryParseExact(raw.Id, "D", out Guid id))
            throw new JsonException("Character ID must be a canonical GUID in D format.");
        if (raw.DisplayName is null)
            throw new JsonException("Display name is required.");

        CharacterPartColors defaults = CharacterPartColors.BuiltIn;
        RawPartColors? colors = raw.PartColors;
        CharacterFeatureSet featureDefaults = CharacterFeatureSet.BuiltIn;
        RawFeatureSet? features = raw.Features;

        var extensionData = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        if (raw.ExtensionData is not null)
        {
            foreach ((string key, JsonElement value) in raw.ExtensionData)
                extensionData.Add(key, value.Clone());
        }

        return new CharacterDocument
        {
            SchemaVersion = CurrentSchemaVersion,
            Id = id,
            DisplayName = raw.DisplayName,
            PartColors = new CharacterPartColors
            {
                Head = colors?.Head ?? defaults.Head,
                Torso = colors?.Torso ?? defaults.Torso,
                LeftHand = colors?.LeftHand ?? defaults.LeftHand,
                RightHand = colors?.RightHand ?? defaults.RightHand,
                LeftFoot = colors?.LeftFoot ?? defaults.LeftFoot,
                RightFoot = colors?.RightFoot ?? defaults.RightFoot,
            },
            Features = new CharacterFeatureSet
            {
                Eyes = CreateFeature(features?.Eyes, featureDefaults.Eyes),
                Brows = CreateFeature(features?.Brows, featureDefaults.Brows),
                Mouth = CreateFeature(features?.Mouth, featureDefaults.Mouth),
                TorsoAccent = CreateFeature(
                    features?.TorsoAccent,
                    featureDefaults.TorsoAccent),
            },
            ExtensionData = extensionData,
        };
    }

    private static CharacterFeatureDocument CreateFeature(
        RawFeature? raw,
        CharacterFeatureDocument defaults) => new()
    {
        FeatureId = raw?.FeatureId ?? defaults.FeatureId,
        OffsetX = raw?.OffsetX ?? defaults.OffsetX,
        OffsetY = raw?.OffsetY ?? defaults.OffsetY,
        Scale = raw?.Scale ?? defaults.Scale,
        Color = raw?.Color ?? defaults.Color,
    };

    private sealed class RawCharacterDocument
    {
        public int SchemaVersion { get; init; }
        public string? Id { get; init; }
        public string? DisplayName { get; init; }
        public RawPartColors? PartColors { get; init; }
        public RawFeatureSet? Features { get; init; }

        [JsonExtensionData]
        public Dictionary<string, JsonElement>? ExtensionData { get; init; }
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
}
