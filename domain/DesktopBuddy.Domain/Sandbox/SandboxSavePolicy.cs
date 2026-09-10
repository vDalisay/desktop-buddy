using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using DesktopBuddy.Domain.Content;
using DesktopBuddy.Domain.Environment;
using DesktopBuddy.Domain.Persistence;

namespace DesktopBuddy.Domain.Sandbox;

public sealed record PlacedSandboxPartSave
{
    public Guid PartId { get; set; }
    public string DefinitionId { get; set; } = string.Empty;
    public float CanonicalX { get; set; }
    public float CanonicalY { get; set; }
    public float RotationDegrees { get; set; }
    public float? MassScale { get; set; }
    public float? Bounce { get; set; }
    public float? GravityScale { get; set; }
    public bool Frozen { get; set; }

    public static PlacedSandboxPartSave FromPart(PlacedSandboxPart part)
    {
        ArgumentNullException.ThrowIfNull(part);
        return new PlacedSandboxPartSave
        {
            PartId = part.PartId.Value,
            DefinitionId = part.DefinitionId.ToString(),
            CanonicalX = part.Position.X,
            CanonicalY = part.Position.Y,
            RotationDegrees = part.RotationDegrees,
            MassScale = part.Overrides.MassScale,
            Bounce = part.Overrides.Bounce,
            GravityScale = part.Overrides.GravityScale,
            Frozen = part.Overrides.Frozen,
        };
    }

    public PlacedSandboxPart CreatePart() => new(
        SandboxPartId.From(PartId),
        SemanticDefinitionId.Parse(DefinitionId),
        new CanonicalRoomPosition(CanonicalX, CanonicalY),
        PlacedSandboxPart.NormalizeRotation(RotationDegrees),
        new SandboxPartOverrides(MassScale, Bounce, GravityScale, Frozen).Clamped());
}

/// <summary>
/// Versioned disk DTO for <c>user://scenes/&lt;scene-id&gt;/sandbox.json</c>. Systemic construction
/// keeps its own document beside the Scene root so it can grow a schema — devices, links, materials
/// — without moving the Scene root's version, exactly as the Scene model reserved.
/// </summary>
public sealed record SandboxDocumentSave
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public long Revision { get; set; }
    public List<PlacedSandboxPartSave> Parts { get; set; } = [];

    public static SandboxDocumentSave FromDocument(SandboxDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return new SandboxDocumentSave
        {
            Revision = document.Revision,
            Parts = document.Parts.Select(PlacedSandboxPartSave.FromPart).ToList(),
        };
    }

    public SandboxDocument CreateDocument() =>
        new(Parts.Select(part => part.CreatePart()), Revision);
}

public readonly record struct SandboxDocumentDecodeResult(
    SaveDecodeStatus Status,
    SandboxDocument? Document,
    string? Detail = null);

/// <summary>
/// Strict engine-free JSON boundary for one Scene's systemic parts. Decode rebuilds validated
/// models rather than trusting DTO fields, so a hand-edited or corrupt file cannot introduce
/// duplicate part IDs, unknown definitions, unbounded overrides or an oversized room.
/// </summary>
public static class SandboxSavePolicy
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    public static string Serialize(SandboxDocument document)
    {
        SandboxDocumentSave save = SandboxDocumentSave.FromDocument(document);
        return JsonSerializer.Serialize(save, Options);
    }

    /// <summary>
    /// Decodes one sandbox document. Parts naming a definition this build does not ship are dropped
    /// rather than failing the load: an unknown part must not cost the player the rest of the room.
    /// </summary>
    public static SandboxDocumentDecodeResult Decode(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new SandboxDocumentDecodeResult(SaveDecodeStatus.Malformed, null, "Sandbox payload was empty.");

        int schema;
        try
        {
            using JsonDocument probe = JsonDocument.Parse(json);
            bool hasSchema =
                probe.RootElement.TryGetProperty("schemaVersion", out JsonElement element) ||
                probe.RootElement.TryGetProperty("SchemaVersion", out element);
            if (!hasSchema || !element.TryGetInt32(out schema))
                return new SandboxDocumentDecodeResult(SaveDecodeStatus.Malformed, null, "Missing schemaVersion.");
        }
        catch (JsonException exception)
        {
            return new SandboxDocumentDecodeResult(SaveDecodeStatus.Malformed, null, exception.Message);
        }

        if (schema > SandboxDocumentSave.CurrentSchemaVersion)
        {
            return new SandboxDocumentDecodeResult(
                SaveDecodeStatus.UnsupportedFutureVersion,
                null,
                $"Sandbox schema {schema} is newer than {SandboxDocumentSave.CurrentSchemaVersion}.");
        }
        if (schema != SandboxDocumentSave.CurrentSchemaVersion)
            return new SandboxDocumentDecodeResult(SaveDecodeStatus.Invalid, null, $"Unsupported sandbox schema {schema}.");

        try
        {
            SandboxDocumentSave save = JsonSerializer.Deserialize<SandboxDocumentSave>(json, Options)
                ?? throw new JsonException("Sandbox payload was null.");
            var parts = new List<PlacedSandboxPart>(save.Parts.Count);
            foreach (PlacedSandboxPartSave part in save.Parts)
            {
                if (!SemanticDefinitionId.TryParse(part.DefinitionId, out SemanticDefinitionId definitionId) ||
                    !SandboxPartCatalogue.TryGet(definitionId, out _))
                {
                    continue;
                }
                parts.Add(part.CreatePart());
            }

            return new SandboxDocumentDecodeResult(
                SaveDecodeStatus.Valid,
                new SandboxDocument(parts, save.Revision));
        }
        catch (JsonException exception)
        {
            return new SandboxDocumentDecodeResult(SaveDecodeStatus.Malformed, null, exception.Message);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return new SandboxDocumentDecodeResult(SaveDecodeStatus.Invalid, null, exception.Message);
        }
    }
}
