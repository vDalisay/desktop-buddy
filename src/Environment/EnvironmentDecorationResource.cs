using System;
using DesktopBuddy.App;
using DesktopBuddy.Domain.Environment;
using Godot;

namespace DesktopBuddy.Environment;

public enum EnvironmentDecorationVisualKind
{
    FloorLamp,
    Sofa,
    Painting,
    Wallpaper,
    Plant,
    Table,
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
    [Export] public EnvironmentDecorationVisualKind VisualKind { get; set; }
    [Export] public Vector2 VisualSize { get; set; } = new(64, 64);
    [Export] public Color PrimaryColor { get; set; } = Colors.Gray;
    [Export] public Color SecondaryColor { get; set; } = Colors.Black;

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
        if (!Enum.IsDefined(VisualKind)) errors.Add($"'{DefinitionId}' has an invalid visual kind");
        if (!float.IsFinite(VisualSize.X) || !float.IsFinite(VisualSize.Y) || VisualSize.X <= 0 || VisualSize.Y <= 0)
            errors.Add($"'{DefinitionId}' has an invalid visual size");
        return errors;
    }
}
