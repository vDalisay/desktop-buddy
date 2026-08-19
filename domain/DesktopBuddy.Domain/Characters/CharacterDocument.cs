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

    /// <summary>
    /// The buddy's one fixed favourite colour. Frozen by the normalizer the first time the
    /// document is seen — that is, when the character is created — so that later repaints of
    /// the torso never move it. Null only in a document that has not been normalized yet.
    /// </summary>
    public Rgba32? FavoriteColor { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement> ExtensionData { get; init; } = new(StringComparer.Ordinal);

    public static CharacterDocument CreateDefault(Guid id, string displayName) => new()
    {
        Id = id,
        DisplayName = displayName,
        FavoriteColor = CharacterPartColors.BuiltInTorso,
    };
}

public sealed record CharacterPartColors
{
    public static Rgba32 BuiltInHead { get; } = Rgba32.Parse("#7AC7FF");
    public static Rgba32 BuiltInTorso { get; } = Rgba32.Parse("#45A3E0");
    public static Rgba32 BuiltInHand { get; } = Rgba32.Parse("#8FD4FF");
    public static Rgba32 BuiltInFoot { get; } = Rgba32.Parse("#61B8F0");

    public static CharacterPartColors BuiltIn { get; } = new();

    public Rgba32 Head { get; init; } = BuiltInHead;
    public Rgba32 Torso { get; init; } = BuiltInTorso;
    public Rgba32 LeftHand { get; init; } = BuiltInHand;
    public Rgba32 RightHand { get; init; } = BuiltInHand;
    public Rgba32 LeftFoot { get; init; } = BuiltInFoot;
    public Rgba32 RightFoot { get; init; } = BuiltInFoot;
}

/// <summary>
/// Canonical Buddy Studio appearance selections. Eyebrows/Accessories are the schema-3
/// names; Brows/TorsoAccent remain source-compatible aliases for the existing renderer/editor
/// and are excluded from JSON so old code can migrate incrementally without duplicating data.
/// </summary>
public sealed record CharacterFeatureSet
{
    private CharacterFeatureDocument _eyebrows = CharacterFeatureDocument.Create(CharacterFeatureIds.BrowsSoftArc);
    private CharacterFeatureDocument _accessories = CharacterFeatureDocument.Create(CharacterFeatureIds.AccentNone);

    public static CharacterFeatureSet BuiltIn { get; } = new();

    public CharacterFeatureDocument Face { get; init; } = CharacterFeatureDocument.Create(CharacterFeatureIds.FaceClassicPlate);
    public CharacterFeatureDocument Hair { get; init; } = CharacterFeatureDocument.Create(CharacterFeatureIds.HairNone);
    public CharacterFeatureDocument Eyebrows { get => _eyebrows; init => _eyebrows = value; }
    public CharacterFeatureDocument Eyes { get; init; } = CharacterFeatureDocument.Create(CharacterFeatureIds.EyesSoftOval);
    public CharacterFeatureDocument Nose { get; init; } = CharacterFeatureDocument.Create(CharacterFeatureIds.NoseNone);
    public CharacterFeatureDocument Mouth { get; init; } = CharacterFeatureDocument.Create(CharacterFeatureIds.MouthRounded);
    public CharacterFeatureDocument Ears { get; init; } = CharacterFeatureDocument.Create(CharacterFeatureIds.EarsNone);
    public CharacterFeatureDocument Accessories { get => _accessories; init => _accessories = value; }
    public CharacterFeatureDocument Glasses { get; init; } = CharacterFeatureDocument.Create(CharacterFeatureIds.GlassesNone);
    public CharacterFeatureDocument Headwear { get; init; } = CharacterFeatureDocument.Create(CharacterFeatureIds.HeadwearNone);
    public CharacterFeatureDocument Tops { get; init; } = CharacterFeatureDocument.Create(CharacterFeatureIds.TopNone);
    public CharacterFeatureDocument Shoes { get; init; } = CharacterFeatureDocument.Create(CharacterFeatureIds.ShoesNone);

    [JsonIgnore]
    public CharacterFeatureDocument Brows { get => _eyebrows; init => _eyebrows = value; }

    [JsonIgnore]
    public CharacterFeatureDocument TorsoAccent { get => _accessories; init => _accessories = value; }
}

public sealed record CharacterFeatureDocument
{
    public string FeatureId { get; init; } = string.Empty;
    public double OffsetX { get; init; }
    public double OffsetY { get; init; }
    public double Scale { get; init; } = 1.0;
    public Rgba32 Color { get; init; } = Rgba32.Parse("#183042");

    /// <summary>
    /// Named color-channel seam for Buddy Studio. Launch content still renders the legacy
    /// Color property as its single primary channel; this map is introduced only after the
    /// renderer consumes it, so schema 3 stays backward-compatible with current rendering.
    /// </summary>
    public Dictionary<string, Rgba32> Colors { get; init; } = new(StringComparer.Ordinal);

    public static CharacterFeatureDocument Create(string featureId) => new()
    {
        FeatureId = featureId,
    };
}
