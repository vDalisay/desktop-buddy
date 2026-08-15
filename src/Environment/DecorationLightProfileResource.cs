using DesktopBuddy.App;
using Godot;

namespace DesktopBuddy.Environment;

/// <summary>
/// Trusted presentation metadata for generated lamps. Emission controls how bright the authored
/// bulb looks; LightEnabled controls a visual OmniLight3D. Neither creates collision or gameplay.
/// New literal-template presets bake their emitter to local generated-mesh coordinates so moving
/// the authored lamp inside the fixed 1024x1024 guide cannot desynchronise its glow/light origin.
/// </summary>
[GlobalClass]
public partial class DecorationLightProfileResource : GameResource
{
    [Export] public bool Enabled { get; set; }
    [Export(PropertyHint.Range, "0,8,0.05")] public float EmissionStrength { get; set; } = 1f;
    [Export] public bool LightEnabled { get; set; }
    [Export(PropertyHint.Range, "0,16,0.05")] public float Brightness { get; set; } = 1f;
    [Export(PropertyHint.Range, "1,1024,1")] public float Range { get; set; } = 180f;
    [Export] public Color Color { get; set; } = new(1f, .88f, .65f, 1f);
    /// <summary>Legacy normalized front-template emitter position mapped through VisualSize.</summary>
    [Export] public Vector2 EmitterPosition { get; set; } = new(.5f, .28f);
    /// <summary>
    /// True for literal-template generated assets whose authoritative local emitter has already
    /// been derived by Asset Forge. Kept explicit so old lamp@1 resources retain their old mapping.
    /// </summary>
    [Export] public bool UsesLocalEmitterPosition { get; set; }
    [Export] public Vector2 LocalEmitterPosition { get; set; } = Vector2.Zero;

    public override Godot.Collections.Array<string> Validate()
    {
        var errors = new Godot.Collections.Array<string>();
        if (!float.IsFinite(EmissionStrength) || EmissionStrength is < 0f or > 8f)
            errors.Add("EmissionStrength must be finite within 0..8.");
        if (!float.IsFinite(Brightness) || Brightness is < 0f or > 16f)
            errors.Add("Brightness must be finite within 0..16.");
        if (!float.IsFinite(Range) || Range is < 1f or > 1024f)
            errors.Add("Range must be finite within 1..1024.");
        if (!float.IsFinite(EmitterPosition.X) || !float.IsFinite(EmitterPosition.Y) ||
            EmitterPosition.X is < 0f or > 1f || EmitterPosition.Y is < 0f or > 1f)
            errors.Add("EmitterPosition must use normalized 0..1 template coordinates.");
        if (!float.IsFinite(LocalEmitterPosition.X) || !float.IsFinite(LocalEmitterPosition.Y))
            errors.Add("LocalEmitterPosition must contain finite generated-mesh coordinates.");
        return errors;
    }
}
