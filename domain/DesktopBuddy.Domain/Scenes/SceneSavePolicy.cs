using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using DesktopBuddy.Domain.Environment;
using DesktopBuddy.Domain.Persistence;

namespace DesktopBuddy.Domain.Scenes;

public sealed record BuddyPlacementSave
{
    public Guid PlacementId { get; init; }
    public Guid BuddyIdentityId { get; init; }
    public float CanonicalX { get; init; }
    public float CanonicalY { get; init; }

    public static BuddyPlacementSave FromPlacement(in BuddyPlacement placement) => new()
    {
        PlacementId = placement.PlacementId.Value,
        BuddyIdentityId = placement.BuddyIdentityId.Value,
        CanonicalX = placement.Position.X,
        CanonicalY = placement.Position.Y,
    };

    public BuddyPlacement CreatePlacement() => new(
        BuddyPlacementId.From(PlacementId),
        DesktopBuddy.Domain.Persistence.BuddyIdentityId.From(BuddyIdentityId),
        new CanonicalRoomPosition(CanonicalX, CanonicalY));
}

/// <summary>
/// Versioned disk DTO for <c>user://scenes/&lt;scene-id&gt;/scene.json</c>. Schema 2 makes the
/// complete Room Decorator state Scene-owned: the placed layout, purchased-but-unplaced storage and
/// environment revision are committed together. Schema 1 remains readable and upgrades to an empty
/// storage inventory at revision zero. The painted background is intentionally a sibling file and
/// systemic sandbox data gets its own versioned document later.
/// </summary>
public sealed record SceneDocumentSave
{
    public const int CurrentSchemaVersion = 2;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public Guid SceneId { get; init; }
    public string Name { get; init; } = string.Empty;
    public long EnvironmentRevision { get; init; }
    public int EnvironmentSchemaVersion { get; init; } = EnvironmentLayout.CurrentSchemaVersion;
    public List<PlacedDecorationSave> Decorations { get; init; } = [];
    public List<string> OwnedUnplaced { get; init; } = [];
    public List<BuddyPlacementSave> BuddyPlacements { get; init; } = [];

    /// <summary>Absent in saves written before the setting existed, which read as the default.</summary>
    public bool BuddiesCollide { get; init; } = true;

    public static SceneDocumentSave FromDocument(SceneDocument scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        return new SceneDocumentSave
        {
            SceneId = scene.SceneId.Value,
            Name = scene.Name,
            EnvironmentRevision = scene.EnvironmentRevision,
            EnvironmentSchemaVersion = scene.Environment.SchemaVersion,
            Decorations = scene.Environment.Decorations
                .Select(item => PlacedDecorationSave.FromPlaced(item))
                .ToList(),
            OwnedUnplaced = scene.OwnedUnplaced.Select(id => id.Value).ToList(),
            BuddyPlacements = scene.BuddyPlacements
                .Select(item => BuddyPlacementSave.FromPlacement(item))
                .ToList(),
            BuddiesCollide = scene.BuddiesCollide,
        };
    }

    public SceneDocument CreateDocument()
    {
        if (SchemaVersion is not (1 or CurrentSchemaVersion))
            throw new ArgumentOutOfRangeException(nameof(SchemaVersion), "Unsupported Scene document save schema.");
        if (EnvironmentSchemaVersion != EnvironmentLayout.CurrentSchemaVersion)
            throw new ArgumentOutOfRangeException(nameof(EnvironmentSchemaVersion), "Unsupported Scene environment schema.");
        if (Decorations is null || BuddyPlacements is null)
            throw new ArgumentException("Scene collections cannot be null.");
        if (SchemaVersion == CurrentSchemaVersion && OwnedUnplaced is null)
            throw new ArgumentException("Scene environment storage cannot be null.");

        long environmentRevision = SchemaVersion == 1 ? 0 : EnvironmentRevision;
        if (environmentRevision < 0)
            throw new ArgumentOutOfRangeException(nameof(EnvironmentRevision), "Scene environment revision cannot be negative.");

        var environment = new EnvironmentProgressSnapshot(
            environmentRevision,
            new EnvironmentLayout(
                Decorations.Select(item => item.CreatePlaced()),
                EnvironmentSchemaVersion),
            SchemaVersion == 1
                ? []
                : OwnedUnplaced.Select(ParseDecorationDefinitionId).ToArray());
        return new SceneDocument(
            DesktopBuddy.Domain.Scenes.SceneId.From(SceneId),
            Name,
            environment,
            BuddyPlacements.Select(item => item.CreatePlacement()),
            buddiesCollide: BuddiesCollide);
    }

    private static DecorationDefinitionId ParseDecorationDefinitionId(string value)
    {
        if (!DecorationDefinitionId.TryCreate(value, out DecorationDefinitionId id))
            throw new ArgumentException($"Invalid stored decoration definition ID '{value}'.", nameof(OwnedUnplaced));
        return id;
    }
}

/// <summary>
/// Small ordered index stored at <c>user://scenes/index.json</c>. It contains no Scene bodies; its
/// only responsibilities are stable ordering and the committed active Scene identity.
/// </summary>
public sealed record SceneIndexSave
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public long Revision { get; init; }
    public Guid ActiveSceneId { get; init; }
    public List<Guid> OrderedSceneIds { get; init; } = [];
}

public readonly record struct SceneDocumentDecodeResult(
    SaveDecodeStatus Status,
    SceneDocument? Scene,
    string? Detail = null);

public readonly record struct SceneIndexDecodeResult(
    SaveDecodeStatus Status,
    SceneIndexSave? Index,
    string? Detail = null);

/// <summary>
/// Strict engine-free JSON boundary for local Scene documents. Decode reconstructs the validated
/// semantic models instead of trusting DTO fields directly, so duplicate IDs, invalid anchors,
/// invalid environment entries and malformed names never reach runtime composition.
/// </summary>
public static class SceneSavePolicy
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    public static string SerializeScene(SceneDocument scene)
    {
        SceneDocumentSave save = SceneDocumentSave.FromDocument(scene);
        return JsonSerializer.Serialize(save, Options);
    }

    public static SceneDocumentDecodeResult DecodeScene(string json)
    {
        if (!TryReadSchema(json, out int schema, out SaveDecodeStatus failure, out string? detail))
            return new SceneDocumentDecodeResult(failure, null, detail);
        if (schema > SceneDocumentSave.CurrentSchemaVersion)
        {
            return new SceneDocumentDecodeResult(
                SaveDecodeStatus.UnsupportedFutureVersion,
                null,
                $"Scene schema {schema} is newer than {SceneDocumentSave.CurrentSchemaVersion}.");
        }
        if (schema is not (1 or SceneDocumentSave.CurrentSchemaVersion))
            return new SceneDocumentDecodeResult(SaveDecodeStatus.Invalid, null, $"Unsupported Scene schema {schema}.");

        try
        {
            SceneDocumentSave save = JsonSerializer.Deserialize<SceneDocumentSave>(json, Options)
                ?? throw new JsonException("Scene payload was null.");
            SceneDocument scene = save.CreateDocument();
            return new SceneDocumentDecodeResult(SaveDecodeStatus.Valid, scene);
        }
        catch (JsonException exception)
        {
            return new SceneDocumentDecodeResult(SaveDecodeStatus.Malformed, null, exception.Message);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return new SceneDocumentDecodeResult(SaveDecodeStatus.Invalid, null, exception.Message);
        }
    }

    public static string SerializeIndex(SceneIndexSave index)
    {
        ArgumentNullException.ThrowIfNull(index);
        ValidateIndex(index);
        return JsonSerializer.Serialize(index, Options);
    }

    public static SceneIndexDecodeResult DecodeIndex(string json)
    {
        if (!TryReadSchema(json, out int schema, out SaveDecodeStatus failure, out string? detail))
            return new SceneIndexDecodeResult(failure, null, detail);
        if (schema > SceneIndexSave.CurrentSchemaVersion)
        {
            return new SceneIndexDecodeResult(
                SaveDecodeStatus.UnsupportedFutureVersion,
                null,
                $"Scene index schema {schema} is newer than {SceneIndexSave.CurrentSchemaVersion}.");
        }
        if (schema != SceneIndexSave.CurrentSchemaVersion)
            return new SceneIndexDecodeResult(SaveDecodeStatus.Invalid, null, $"Unsupported Scene index schema {schema}.");

        try
        {
            SceneIndexSave index = JsonSerializer.Deserialize<SceneIndexSave>(json, Options)
                ?? throw new JsonException("Scene index payload was null.");
            ValidateIndex(index);
            return new SceneIndexDecodeResult(SaveDecodeStatus.Valid, index);
        }
        catch (JsonException exception)
        {
            return new SceneIndexDecodeResult(SaveDecodeStatus.Malformed, null, exception.Message);
        }
        catch (ArgumentException exception)
        {
            return new SceneIndexDecodeResult(SaveDecodeStatus.Invalid, null, exception.Message);
        }
    }

    public static SceneIndexSave CreateIndex(SceneLibraryState library, long revision)
    {
        ArgumentNullException.ThrowIfNull(library);
        if (revision < 0)
            throw new ArgumentOutOfRangeException(nameof(revision));
        if (library.Count == 0 || !library.ActiveSceneId.IsValid)
            throw new InvalidOperationException("A persisted Scene library requires one active Scene.");

        return new SceneIndexSave
        {
            Revision = revision,
            ActiveSceneId = library.ActiveSceneId.Value,
            OrderedSceneIds = library.Scenes.Select(scene => scene.SceneId.Value).ToList(),
        };
    }

    private static void ValidateIndex(SceneIndexSave index)
    {
        if (index.SchemaVersion != SceneIndexSave.CurrentSchemaVersion)
            throw new ArgumentException("Unsupported Scene index schema.", nameof(index));
        if (index.Revision < 0)
            throw new ArgumentException("Scene index revision cannot be negative.", nameof(index));
        if (index.ActiveSceneId == Guid.Empty)
            throw new ArgumentException("Scene index requires an active Scene ID.", nameof(index));
        if (index.OrderedSceneIds is null || index.OrderedSceneIds.Count == 0)
            throw new ArgumentException("Scene index requires at least one Scene.", nameof(index));

        var ids = new HashSet<Guid>();
        foreach (Guid id in index.OrderedSceneIds)
        {
            if (id == Guid.Empty || !ids.Add(id))
                throw new ArgumentException("Scene index IDs must be valid and unique.", nameof(index));
        }
        if (!ids.Contains(index.ActiveSceneId))
            throw new ArgumentException("Scene index active ID must appear in the ordered Scene list.", nameof(index));
    }

    private static bool TryReadSchema(
        string json,
        out int schema,
        out SaveDecodeStatus failure,
        out string? detail)
    {
        schema = 0;
        failure = SaveDecodeStatus.Malformed;
        detail = null;
        if (string.IsNullOrWhiteSpace(json))
        {
            detail = "Save payload was empty.";
            return false;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            bool hasSchema =
                document.RootElement.TryGetProperty("schemaVersion", out JsonElement element) ||
                document.RootElement.TryGetProperty("SchemaVersion", out element);
            if (!hasSchema || !element.TryGetInt32(out schema))
            {
                detail = "Missing schemaVersion.";
                return false;
            }
            return true;
        }
        catch (JsonException exception)
        {
            detail = exception.Message;
            return false;
        }
    }
}
