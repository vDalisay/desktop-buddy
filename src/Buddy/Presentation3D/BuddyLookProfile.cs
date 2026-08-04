using DesktopBuddy.App;
using DesktopBuddy.Domain.Presentation;
using Godot;

namespace DesktopBuddy.Buddy.Presentation3D;

/// <summary>
/// Typed, immutable image of the owner-accepted production look: soft matte toon shading,
/// a transparent-safe shadowless two-light rig, an ink inverted-hull outline, and optional
/// surface-detail textures. The character editor may replace base colours and paint pixels,
/// but never writes rig/drive tuning. All look tunables live here as data rather than code
/// literals (M3_5_MATERIALS_AND_LOOK_PLAN.md).
/// </summary>
[GlobalClass]
public partial class BuddyLookProfile : GameResource
{
    [Export] public BaseMaterial3D.DiffuseModeEnum DiffuseMode { get; set; } =
        BaseMaterial3D.DiffuseModeEnum.Lambert;
    [Export] public BaseMaterial3D.SpecularModeEnum SpecularMode { get; set; } =
        BaseMaterial3D.SpecularModeEnum.Toon;
    [Export(PropertyHint.Range, "0,1,0.001")]
    public float Specular { get; set; } = 0.08f;
    [Export(PropertyHint.Range, "0,1,0.001")]
    public float Roughness { get; set; } = 1.0f;

    [Export] public Color KeyColor { get; set; } = new(1.0f, 0.98f, 0.94f);
    [Export(PropertyHint.Range, "0,64,0.01,or_greater")]
    public float KeyEnergy { get; set; } = 0.75f;
    [Export] public Vector3 KeyEulerDegrees { get; set; } = new(-35.0f, -30.0f, 0.0f);

    [Export] public Color FillColor { get; set; } = new(0.85f, 0.90f, 1.0f);
    [Export(PropertyHint.Range, "0,64,0.01,or_greater")]
    public float FillEnergy { get; set; } = 0.70f;
    [Export] public Vector3 FillEulerDegrees { get; set; } = Vector3.Zero;

    [Export] public bool ShadowsEnabled { get; set; }

    [Export] public Color OutlineColor { get; set; } = new("183042");
    [Export(PropertyHint.Range, "0.001,64,0.001,or_greater")]
    public float OutlineGrowAmount { get; set; } = 1.5f;

    /// <summary>
    /// Neutral woven texture multiplied by each part's selected base colour. It belongs to
    /// the trusted look, not character paint, so painting remains a separate transparent shell.
    /// </summary>
    [Export] public Texture2D? FabricTexture { get; set; }

    /// <summary>
    /// Transparent seam-and-thread texture rendered above the paint shell and below face/accent
    /// decals. A null value disables the seam layer without changing trusted geometry.
    /// </summary>
    [Export] public Texture2D? SeamTexture { get; set; }

    [Export(PropertyHint.Range, "0.051,0.099,0.001")]
    public float SeamGrowAmount { get; set; } = 0.075f;

    public override Godot.Collections.Array<string> Validate()
    {
        var errors = new Godot.Collections.Array<string>();
        foreach (string error in ToData().Validate())
            errors.Add(error);

        if (!float.IsFinite(SeamGrowAmount) ||
            SeamGrowAmount <= BuddyLookMaterialLibrary.PaintShellGrowAmount ||
            SeamGrowAmount >= 0.1f)
        {
            errors.Add(
                $"{nameof(SeamGrowAmount)} must stay above the paint shell and below face/accent decals.");
        }

        return errors;
    }

    /// <summary>Projects the exported Godot fields into the pure-logic validation image.</summary>
    public BuddyLookData ToData() => new(
        (int)DiffuseMode,
        (int)SpecularMode,
        Specular,
        Roughness,
        ToLookColor(KeyColor),
        KeyEnergy,
        ToLookEuler(KeyEulerDegrees),
        ToLookColor(FillColor),
        FillEnergy,
        ToLookEuler(FillEulerDegrees),
        ShadowsEnabled,
        ToLookColor(OutlineColor),
        OutlineGrowAmount);

    private static LookColor ToLookColor(Color color) => new(color.R, color.G, color.B, color.A);

    private static LookEuler ToLookEuler(Vector3 degrees) => new(degrees.X, degrees.Y, degrees.Z);
}
