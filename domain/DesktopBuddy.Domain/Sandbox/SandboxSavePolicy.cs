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
    public float? PistonPush { get; set; }
    public float? TimerSeconds { get; set; }
    public string? Tool { get; set; }
    public float? Length { get; set; }
    public float? Thickness { get; set; }

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
            PistonPush = part.Overrides.PistonPush,
            TimerSeconds = part.Overrides.TimerSeconds,
            Tool = part.Overrides.Tool,
            Length = part.Overrides.Length,
            Thickness = part.Overrides.Thickness,
        };
    }

    public PlacedSandboxPart CreatePart() => new(
        SandboxPartId.From(PartId),
        SemanticDefinitionId.Parse(DefinitionId),
        new CanonicalRoomPosition(CanonicalX, CanonicalY),
        PlacedSandboxPart.NormalizeRotation(RotationDegrees),
        new SandboxPartOverrides(MassScale, Bounce, GravityScale, Frozen, PistonPush, TimerSeconds, Length, Thickness, Tool).Clamped());
}

public sealed record SandboxLinkSave
{
    public Guid LinkId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public Guid APartId { get; set; }
    public float AX { get; set; }
    public float AY { get; set; }
    /// <summary>Empty for a room anchor, whose point is then in canonical room space.</summary>
    public Guid BPartId { get; set; }
    public float BX { get; set; }
    public float BY { get; set; }
    public float Length { get; set; }
    /// <summary>Absent in rooms from before links had tuning: those were unbreakable, taut and free.</summary>
    public float? Strength { get; set; }
    public float? Elasticity { get; set; }
    public float? Stiffness { get; set; }

    public static SandboxLinkSave FromLink(SandboxLink link)
    {
        ArgumentNullException.ThrowIfNull(link);
        return new SandboxLinkSave
        {
            LinkId = link.LinkId.Value,
            Kind = link.Kind.ToString().ToLowerInvariant(),
            APartId = link.A.PartId.Value,
            AX = link.A.X,
            AY = link.A.Y,
            BPartId = link.B.PartId.Value,
            BX = link.B.X,
            BY = link.B.Y,
            Length = link.Length,
            Strength = link.Strength,
            Elasticity = link.Elasticity,
            Stiffness = link.Stiffness,
        };
    }

    /// <summary>The validated link, or null for one this build cannot represent.</summary>
    public SandboxLink? TryCreateLink()
    {
        if (LinkId == Guid.Empty || APartId == Guid.Empty ||
            !Enum.TryParse(Kind, ignoreCase: true, out SandboxLinkKind kind) || !Enum.IsDefined(kind))
        {
            return null;
        }
        var link = new SandboxLink(
            SandboxLinkId.From(LinkId),
            kind,
            SandboxLinkEnd.OnPart(SandboxPartId.From(APartId), AX, AY),
            BPartId == Guid.Empty
                ? SandboxLinkEnd.World(BX, BY)
                : SandboxLinkEnd.OnPart(SandboxPartId.From(BPartId), BX, BY),
            Length).WithTuning(Strength ?? 1.0f, Elasticity ?? 0.0f, Stiffness ?? 0.0f);
        return link.Problem() is null ? link : null;
    }
}

public sealed record SandboxWireSave
{
    public Guid WireId { get; set; }
    public Guid FromPartId { get; set; }
    public string FromPort { get; set; } = string.Empty;
    public Guid ToPartId { get; set; }
    public string ToPort { get; set; } = string.Empty;
    public string? Color { get; set; }

    public static SandboxWireSave FromWire(SandboxWire wire)
    {
        ArgumentNullException.ThrowIfNull(wire);
        return new SandboxWireSave
        {
            WireId = wire.WireId.Value,
            FromPartId = wire.From.Value,
            FromPort = wire.FromPort,
            ToPartId = wire.To.Value,
            ToPort = wire.ToPort,
            Color = wire.Color.ToString().ToLowerInvariant(),
        };
    }

    /// <summary>
    /// The wire, or null when a stored ID is empty; ports and parts are checked by the room. An
    /// unknown colour is only paint, so it falls back to green rather than costing the wire.
    /// </summary>
    public SandboxWire? TryCreateWire() =>
        WireId == Guid.Empty || FromPartId == Guid.Empty || ToPartId == Guid.Empty
            ? null
            : new SandboxWire(
                SandboxWireId.From(WireId),
                SandboxPartId.From(FromPartId),
                FromPort ?? string.Empty,
                SandboxPartId.From(ToPartId),
                ToPort ?? string.Empty,
                Enum.TryParse(Color, ignoreCase: true, out SandboxWireColor color) && Enum.IsDefined(color)
                    ? color
                    : SandboxWireColor.Green);
}

/// <summary>
/// Versioned disk DTO for <c>user://scenes/&lt;scene-id&gt;/sandbox.json</c>. Systemic construction
/// keeps its own document beside the Scene root so it can grow a schema — devices, links, materials
/// — without moving the Scene root's version, exactly as the Scene model reserved.
/// </summary>
public sealed record SandboxDocumentSave
{
    /// <summary>
    /// 2 added links, 3 signal wires, 4 device settings (Piston push, Timer interval), a beam's own
    /// length and thickness, link strength/stretch/stiffness and wire colours. An older room simply
    /// has none.
    /// </summary>
    public const int CurrentSchemaVersion = 5;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public long Revision { get; set; }
    public List<PlacedSandboxPartSave> Parts { get; set; } = [];
    public List<SandboxLinkSave> Links { get; set; } = [];
    public List<SandboxWireSave> Wires { get; set; } = [];

    public static SandboxDocumentSave FromDocument(SandboxDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return new SandboxDocumentSave
        {
            Revision = document.Revision,
            Parts = document.Parts.Select(PlacedSandboxPartSave.FromPart).ToList(),
            Links = document.Links.Select(SandboxLinkSave.FromLink).ToList(),
            Wires = document.Wires.Select(SandboxWireSave.FromWire).ToList(),
        };
    }
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
        if (schema < 1)
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

            // Same rule as parts: a link this build cannot honour — an unknown kind, a bad value,
            // an end on a part that was dropped above — is dropped, not allowed to fail the room.
            var kept = new HashSet<SandboxPartId>(parts.Select(part => part.PartId));
            var links = new List<SandboxLink>();
            foreach (SandboxLinkSave stored in save.Links ?? [])
            {
                SandboxLink? link = stored.TryCreateLink();
                if (link is null || !kept.Contains(link.A.PartId) ||
                    (!link.B.IsWorld && !kept.Contains(link.B.PartId)) ||
                    links.Count >= SandboxDocument.MaximumLinks ||
                    links.Any(other => other.LinkId == link.LinkId || other.Duplicates(link)))
                {
                    continue;
                }
                links.Add(link);
            }

            // And wires: one into a dropped part, onto a port its device lacks, or past the budget
            // is dropped and the rest of the machine still loads.
            var document = new SandboxDocument(parts, save.Revision, links: links);
            foreach (SandboxWireSave stored in save.Wires ?? [])
            {
                if (stored.TryCreateWire() is { } wire)
                    document.TryRestoreWire(wire);
            }

            return new SandboxDocumentDecodeResult(SaveDecodeStatus.Valid, document);
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
