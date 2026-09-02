using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using DesktopBuddy.Domain.Characters;

namespace DesktopBuddy.Persistence.Characters;

/// <summary>
/// Source-generated JSON metadata for the experimental static browser runtime. Runtime reflection
/// serialization stalls inside System.Text.Json under the pinned NativeAOT/Web toolchain, while
/// source-generated metadata keeps the exact same CharacterDocument wire shape without asking the
/// runtime to discover record metadata dynamically.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(CharacterDocument))]
internal sealed partial class BrowserCharacterJsonContext : JsonSerializerContext
{
}

/// <summary>Browser-only CharacterDocument JSON boundary backed entirely by generated metadata.</summary>
internal static class BrowserCharacterJson
{
    public static string Serialize(CharacterDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return JsonSerializer.Serialize(
            document with { SchemaVersion = CharacterDocumentPolicy.CurrentSchemaVersion },
            BrowserCharacterJsonContext.Default.CharacterDocument);
    }

    public static CharacterDecodeResult DecodeCurrentOrFallback(string json)
    {
        if (json is null)
            return new CharacterDecodeResult(CharacterDecodeStatus.Malformed, null, "JSON is required.");

        try
        {
            using JsonDocument parsed = JsonDocument.Parse(json);
            if (parsed.RootElement.ValueKind != JsonValueKind.Object)
                return new CharacterDecodeResult(
                    CharacterDecodeStatus.Malformed,
                    null,
                    "Character payload must be a JSON object.");
            if (!parsed.RootElement.TryGetProperty("schemaVersion", out JsonElement schemaElement) ||
                !schemaElement.TryGetInt32(out int schemaVersion))
            {
                return new CharacterDecodeResult(
                    CharacterDecodeStatus.Malformed,
                    null,
                    "Missing or invalid schemaVersion.");
            }
            if (schemaVersion > CharacterDocumentPolicy.CurrentSchemaVersion)
            {
                return new CharacterDecodeResult(
                    CharacterDecodeStatus.UnsupportedFutureVersion,
                    null,
                    $"Schema {schemaVersion} is newer than {CharacterDocumentPolicy.CurrentSchemaVersion}.");
            }

            // Older documents still need the authored migration chain. Browser-created documents
            // are always current schema; keeping the fallback preserves compatibility without
            // changing native persistence semantics.
            if (schemaVersion != CharacterDocumentPolicy.CurrentSchemaVersion)
                return CharacterDocumentPolicy.DecodeAndMigrate(json);

            CharacterDocument? document = JsonSerializer.Deserialize(
                json,
                BrowserCharacterJsonContext.Default.CharacterDocument);
            if (document is null)
            {
                return new CharacterDecodeResult(
                    CharacterDecodeStatus.Malformed,
                    null,
                    "Character payload was null.");
            }

            CharacterDocumentPolicy.ValidatePaintManifest(document.Paint);
            return new CharacterDecodeResult(CharacterDecodeStatus.Valid, document);
        }
        catch (Exception exception) when (exception is JsonException or FormatException or OverflowException)
        {
            return new CharacterDecodeResult(CharacterDecodeStatus.Malformed, null, exception.Message);
        }
    }
}
