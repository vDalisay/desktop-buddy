using DesktopBuddy.App;
using DesktopBuddy.Domain.Presentation;
using Godot;

namespace DesktopBuddy.Buddy.Presentation3D;

/// <summary>
/// Typed, immutable image of the owner-accepted Variant C production look (2026-07-15):
/// soft matte toon shading from a built-in <see cref="StandardMaterial3D"/> (Lambert diffuse,
/// toon specular), a transparent-safe shadowless two-light rig, and an ink inverted-hull
/// outline. This is the required nested reference of <see cref="BuddyVisualProfile"/>; the
/// future character editor may replace base colours and bounded look options but never writes
/// rig/drive tuning. All tunables live here as data — never as code literals
/// (M3_5_MATERIALS_AND_LOOK_PLAN.md). The one production style is a single typed configuration;
/// the enum fields exist to mirror the Godot material settings for validation and inspection,
/// not as a shader-choice catalog.
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

    public override Godot.Collections.Array<string> Validate()
    {
        var errors = new Godot.Collections.Array<string>();
        foreach (string error in ToData().Validate())
        {
            errors.Add(error);
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
