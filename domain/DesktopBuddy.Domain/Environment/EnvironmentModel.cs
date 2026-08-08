using System;
using System.Collections.Generic;
using System.Linq;

namespace DesktopBuddy.Domain.Environment;

public enum DecorationCategory { Lamp, Sofa, Painting, Wallpaper, Plant, Table }
public enum DecorationAnchorKind { Floor, Wall, RoomSurface }
public enum DecorationInteractionKind { None }
public enum DecorationRenderBand { Background, Wallpaper, WallDecoration, BehindBuddyFloor, FrontDecoration }

public readonly record struct DecorationDefinitionId
{
    public DecorationDefinitionId(string value)
    {
        if (!IsValid(value))
            throw new ArgumentException("Decoration IDs must use the 'decoration.' namespace and lowercase stable-ID characters.", nameof(value));
        Value = value;
    }

    public string Value { get; }
    public override string ToString() => Value;

    public static bool TryCreate(string? value, out DecorationDefinitionId id)
    {
        if (!IsValid(value)) { id = default; return false; }
        id = new DecorationDefinitionId(value!);
        return true;
    }

    private static bool IsValid(string? value)
    {
        if (value is null || value.Length is < 12 or > 96 || !value.StartsWith("decoration.", StringComparison.Ordinal))
            return false;
        for (int index = "decoration.".Length; index < value.Length; index++)
        {
            char character = value[index];
            if (!char.IsAsciiLetterLower(character) && !char.IsAsciiDigit(character) && character is not ('.' or '_'))
                return false;
        }
        return value[^1] is not ('.' or '_');
    }
}

public readonly record struct PlacedDecorationId
{
    public PlacedDecorationId(Guid value)
    {
        if (value == Guid.Empty)
            throw new ArgumentException("Placed decoration IDs cannot be empty.", nameof(value));
        Value = value;
    }

    public Guid Value { get; }
    public static PlacedDecorationId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString("D");
}

public readonly record struct CanonicalRoomPosition
{
    public CanonicalRoomPosition(float x, float y)
    {
        if (!float.IsFinite(x) || !float.IsFinite(y) || x is < 0f or > 1f || y is < 0f or > 1f)
            throw new ArgumentOutOfRangeException(nameof(x), "Canonical room coordinates must be finite values from 0 through 1.");
        X = x;
        Y = y;
    }
    public float X { get; }
    public float Y { get; }
}

public readonly record struct DecorationRotationPolicy
{
    public DecorationRotationPolicy(bool allowsRotation, int stepDegrees = 0)
    {
        if (allowsRotation && (stepDegrees <= 0 || stepDegrees >= 360 || 360 % stepDegrees != 0))
            throw new ArgumentOutOfRangeException(nameof(stepDegrees), "Rotation steps must divide 360 degrees exactly.");
        if (!allowsRotation && stepDegrees != 0)
            throw new ArgumentException("Non-rotating decorations cannot declare a rotation step.", nameof(stepDegrees));
        AllowsRotation = allowsRotation;
        StepDegrees = stepDegrees;
    }
    public bool AllowsRotation { get; }
    public int StepDegrees { get; }
    public static DecorationRotationPolicy Fixed => new(false);
}

public sealed record DecorationDefinition
{
    public DecorationDefinition(DecorationDefinitionId id, string displayNameKey, DecorationCategory category,
        long priceMilliCredits, DecorationAnchorKind anchorKind, DecorationRotationPolicy rotation,
        DecorationRenderBand renderBand, bool visible = true,
        DecorationInteractionKind interaction = DecorationInteractionKind.None)
    {
        if (id == default) throw new ArgumentException("A valid decoration ID is required.", nameof(id));
        if (string.IsNullOrWhiteSpace(displayNameKey)) throw new ArgumentException("A display-name key is required.", nameof(displayNameKey));
        if (!Enum.IsDefined(category) || !Enum.IsDefined(anchorKind) || !Enum.IsDefined(renderBand) || !Enum.IsDefined(interaction))
            throw new ArgumentOutOfRangeException(nameof(category), "Decoration policies must use declared enum values.");
        if (priceMilliCredits <= 0 || priceMilliCredits % 1000 != 0)
            throw new ArgumentOutOfRangeException(nameof(priceMilliCredits), "Decoration prices must be positive whole credits.");
        ValidateBand(category, anchorKind, renderBand);
        Id = id; DisplayNameKey = displayNameKey; Category = category; PriceMilliCredits = priceMilliCredits;
        AnchorKind = anchorKind; Rotation = rotation; RenderBand = renderBand; Visible = visible; Interaction = interaction;
    }
    public DecorationDefinitionId Id { get; }
    public string DisplayNameKey { get; }
    public DecorationCategory Category { get; }
    public long PriceMilliCredits { get; }
    public DecorationAnchorKind AnchorKind { get; }
    public DecorationRotationPolicy Rotation { get; }
    public DecorationRenderBand RenderBand { get; }
    public bool Visible { get; }
    public DecorationInteractionKind Interaction { get; }

    private static void ValidateBand(DecorationCategory category, DecorationAnchorKind anchor, DecorationRenderBand band)
    {
        bool valid = category switch
        {
            DecorationCategory.Wallpaper => anchor == DecorationAnchorKind.RoomSurface && band == DecorationRenderBand.Wallpaper,
            DecorationCategory.Painting => anchor == DecorationAnchorKind.Wall && band == DecorationRenderBand.WallDecoration,
            _ => anchor == DecorationAnchorKind.Floor && band is DecorationRenderBand.BehindBuddyFloor or DecorationRenderBand.FrontDecoration,
        };
        if (!valid) throw new ArgumentException("The category, anchor, and render band combination is not permitted.");
    }
}

public sealed class DecorationCatalogue
{
    private readonly Dictionary<DecorationDefinitionId, DecorationDefinition> _definitions = new();
    public DecorationCatalogue(IEnumerable<DecorationDefinition> definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        foreach (DecorationDefinition definition in definitions)
        {
            ArgumentNullException.ThrowIfNull(definition);
            if (!_definitions.TryAdd(definition.Id, definition))
                throw new ArgumentException($"Duplicate decoration definition '{definition.Id}'.", nameof(definitions));
        }
    }
    public IReadOnlyCollection<DecorationDefinition> Definitions => _definitions.Values;
    public bool TryGet(DecorationDefinitionId id, out DecorationDefinition definition) => _definitions.TryGetValue(id, out definition!);
}

public readonly record struct PlacedDecoration(PlacedDecorationId InstanceId, DecorationDefinitionId DefinitionId,
    CanonicalRoomPosition Position, int RotationDegrees, DecorationRenderBand RenderBand, long PurchasePriceMilliCredits);

public sealed class EnvironmentLayout
{
    public const int CurrentSchemaVersion = 1;
    public const int MaximumPlacedDecorations = 256;
    private readonly PlacedDecoration[] _decorations;

    public EnvironmentLayout(IEnumerable<PlacedDecoration>? decorations = null, int schemaVersion = CurrentSchemaVersion)
    {
        if (schemaVersion != CurrentSchemaVersion)
            throw new ArgumentOutOfRangeException(nameof(schemaVersion), "Unsupported environment layout schema version.");
        _decorations = decorations?.ToArray() ?? Array.Empty<PlacedDecoration>();
        if (_decorations.Length > MaximumPlacedDecorations)
            throw new ArgumentException("The environment layout exceeds the placed-decoration limit.", nameof(decorations));
        var instanceIds = new HashSet<PlacedDecorationId>();
        int wallpaperCount = 0;
        foreach (PlacedDecoration decoration in _decorations)
        {
            if (decoration.InstanceId == default || decoration.DefinitionId == default || !instanceIds.Add(decoration.InstanceId))
                throw new ArgumentException("Placed decorations require unique valid instance and definition IDs.", nameof(decorations));
            if (!Enum.IsDefined(decoration.RenderBand) || decoration.RotationDegrees is < 0 or >= 360 ||
                decoration.PurchasePriceMilliCredits < 0 || decoration.PurchasePriceMilliCredits % 1000 != 0)
                throw new ArgumentException("Placed decoration rotation or recorded purchase price is invalid.", nameof(decorations));
            if (decoration.RenderBand == DecorationRenderBand.Wallpaper && ++wallpaperCount > 1)
                throw new ArgumentException("Only one wallpaper may occupy the room wallpaper slot.", nameof(decorations));
        }
        SchemaVersion = schemaVersion;
    }
    public int SchemaVersion { get; }
    public IReadOnlyList<PlacedDecoration> Decorations => _decorations;
}
