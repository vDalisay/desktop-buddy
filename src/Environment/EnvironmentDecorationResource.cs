using System;
using DesktopBuddy.App;
using DesktopBuddy.Domain.Environment;
using Godot;

namespace DesktopBuddy.Environment;

public enum EnvironmentDecorationVisualSource
{
    LegacyProcedural = 0,
    GeneratedMesh = 1,
}

public enum EnvironmentDecorationVisualKind
{
    FloorLamp,
    Sofa,
    Painting,
    Wallpaper,
    Plant,
    Table,
    ArcLamp,
    LoungeSofa,
    GeometricPainting,
    GridWallpaper,
    LeafyPlant,
    DiningTable,
}

[GlobalClass]
public partial class EnvironmentDecorationResource : GameResource
{
    [Export] public string DefinitionId { get; set; } = string.Empty;
    [Export] public string DisplayNameKey { get; set; } = string.Empty;
    [Export] public DecorationCategory Category { get; set; }
    [Export(PropertyHint.Range, "1,100000,1")] public int PriceCredits { get; set; } = 1;
    [Export] public DecorationAnchorKind AnchorKind { get; set; }
    [Export] public bool AllowsRotation { get; set; }
    [Export(PropertyHint.Range, "0,180,1")] public int RotationStepDegrees { get; set; }
    [Export] public DecorationRenderBand RenderBand { get; set; }
    [Export] public bool Visible { get; set; } = true;

    // Existing launch content remains on the semantic/procedural visual path. Asset Forge exports
    // only the GeneratedMesh path, whose file references live inside trusted project resources;
    // save files/player state continue to carry only DefinitionId.
    [Export] public EnvironmentDecorationVisualSource VisualSource { get; set; } = EnvironmentDecorationVisualSource.LegacyProcedural;
    [Export] public EnvironmentDecorationVisualKind VisualKind { get; set; }
    [Export] public Vector2 VisualSize { get; set; } = new(64, 64);
    [Export] public Color PrimaryColor { get; set; } = Colors.Gray;
    [Export] public Color SecondaryColor { get; set; } = Colors.Black;

    [Export] public PackedScene? GeneratedMesh { get; set; }
    [Export] public Texture2D? GeneratedAlbedo { get; set; }
    [Export] public Texture2D? Thumbnail { get; set; }
    [Export(PropertyHint.Range, "0.05,10,0.01")] public float DefaultScale { get; set; } = 1f;
    /// <summary>Normalized 2D pivot inside the authored visual; (0.5,1) is floor bottom-centre.</summary>
    [Export] public Vector2 Pivot { get; set; } = new(.5f, 1f);
    [Export] public int GeneratorVersion { get; set; }
    [Export] public string CanonicalAssetHash { get; set; } = string.Empty;
    [Export] public DecorationLightProfileResource? LightProfile { get; set; }

    public DecorationDefinition ToDefinition() => new(
        new DecorationDefinitionId(DefinitionId),
        DisplayNameKey,
        Category,
        checked(PriceCredits * 1000L),
        AnchorKind,
        new DecorationRotationPolicy(AllowsRotation, RotationStepDegrees),
        RenderBand,
        Visible);

    public override Godot.Collections.Array<string> Validate()
    {
        var errors = new Godot.Collections.Array<string>();
        try { _ = ToDefinition(); }
        catch (Exception exception) { errors.Add(exception.Message); }
        if (!Enum.IsDefined(VisualSource)) errors.Add($"'{DefinitionId}' has an invalid visual source");
        if (!float.IsFinite(VisualSize.X) || !float.IsFinite(VisualSize.Y) || VisualSize.X <= 0 || VisualSize.Y <= 0)
            errors.Add($"'{DefinitionId}' has an invalid visual size");
        if (!float.IsFinite(DefaultScale) || DefaultScale <= 0f || DefaultScale > 10f)
            errors.Add($"'{DefinitionId}' has an invalid generated visual scale");
        if (!float.IsFinite(Pivot.X) || !float.IsFinite(Pivot.Y) || Pivot.X is < 0f or > 1f || Pivot.Y is < 0f or > 1f)
            errors.Add($"'{DefinitionId}' has an invalid normalized pivot");

        if (VisualSource == EnvironmentDecorationVisualSource.LegacyProcedural)
        {
            if (!Enum.IsDefined(VisualKind)) errors.Add($"'{DefinitionId}' has an invalid visual kind");
        }
        else if (VisualSource == EnvironmentDecorationVisualSource.GeneratedMesh)
        {
            if (!GodotObject.IsInstanceValid(GeneratedMesh)) errors.Add($"'{DefinitionId}' generated visual is missing its mesh scene");
            if (!GodotObject.IsInstanceValid(GeneratedAlbedo)) errors.Add($"'{DefinitionId}' generated visual is missing its albedo texture");
            if (!GodotObject.IsInstanceValid(Thumbnail)) errors.Add($"'{DefinitionId}' generated visual is missing its thumbnail");
            if (GeneratorVersion <= 0) errors.Add($"'{DefinitionId}' generated visual has an invalid generator version");
            if (string.IsNullOrWhiteSpace(CanonicalAssetHash) || CanonicalAssetHash.Length != 64)
                errors.Add($"'{DefinitionId}' generated visual has an invalid canonical asset hash");
        }

        if (GodotObject.IsInstanceValid(LightProfile))
            foreach (string error in LightProfile!.Validate()) errors.Add($"'{DefinitionId}' light: {error}");
        return errors;
    }
}
