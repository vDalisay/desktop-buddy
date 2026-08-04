using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DesktopBuddy.Domain.Characters;

public sealed record CharacterDocument
{
    public int SchemaVersion { get; init; } = CharacterDocumentPolicy.CurrentSchemaVersion;
    public Guid Id { get; init; }
    public string DisplayName { get; init; } = string.Empty;
    public CharacterPartColors PartColors { get; init; } = CharacterPartColors.BuiltIn;
    public CharacterFeatureSet Features { get; init; } = CharacterFeatureSet.BuiltIn;
    public CharacterPaintManifest Paint { get; init; } = CharacterPaintManifest.Empty;

    [JsonExtensionData]
    public Dictionary<string, JsonElement> ExtensionData { get; init; } = new(StringComparer.Ordinal);

    public static CharacterDocument CreateDefault(Guid id, string displayName) => new()
    {
        Id = id,
        DisplayName = displayName,
    };
}

public sealed record CharacterPartColors
{
    public static Rgba32 BuiltInHead { get; } = Rgba32.Parse("#C69A6B");
    public static Rgba32 BuiltInTorso { get; } = Rgba32.Parse("#AA7446");
    public static Rgba32 BuiltInHand { get; } = Rgba32.Parse("#D1A374");
    public static Rgba32 BuiltInFoot { get; } = Rgba32.Parse("#B88052");

    public static CharacterPartColors BuiltIn { get; } = new();

    public Rgba32 Head { get; init; } = BuiltInHead;
    public Rgba32 Torso { get; init; } = BuiltInTorso;
    public Rgba32 LeftHand { get; init; } = BuiltInHand;
    public Rgba32 RightHand { get; init; } = BuiltInHand;
    public Rgba32 LeftFoot { get; init; } = BuiltInFoot;
    public Rgba32 RightFoot { get; init; } = BuiltInFoot;
}

public sealed record CharacterFeatureSet
{
    public static CharacterFeatureSet BuiltIn { get; } = new();

    public CharacterFeatureDocument Eyes { get; init; } = CharacterFeatureDocument.Create(
        CharacterFeatureIds.EyesButton);
    public CharacterFeatureDocument Brows { get; init; } = CharacterFeatureDocument.Create(
        CharacterFeatureIds.BrowsSoftArc);
    public CharacterFeatureDocument Mouth { get; init; } = CharacterFeatureDocument.Create(
        CharacterFeatureIds.MouthRounded);
    public CharacterFeatureDocument TorsoAccent { get; init; } = CharacterFeatureDocument.Create(
        CharacterFeatureIds.AccentNone);
}

public sealed record CharacterFeatureDocument
{
    public string FeatureId { get; init; } = string.Empty;
    public double OffsetX { get; init; }
    public double OffsetY { get; init; }
    public double Scale { get; init; } = 1.0;
    public Rgba32 Color { get; init; } = Rgba32.Parse("#2A1A12");

    public static CharacterFeatureDocument Create(string featureId) => new()
    {
        FeatureId = featureId,
    };
}
