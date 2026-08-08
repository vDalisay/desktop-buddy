using System;
using DesktopBuddy.Domain.Environment;
using Godot;

namespace DesktopBuddy.Environment;

/// <summary>Visual-only 3D decoration kept in an authored band around the z=0 buddy plane.</summary>
public partial class EnvironmentDecorationPresenter : Node3D
{
    public PlacedDecoration Placed { get; private set; }

    public void Configure(in PlacedDecoration placed, EnvironmentDecorationResource definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (!string.Equals(placed.DefinitionId.Value, definition.DefinitionId, StringComparison.Ordinal))
            throw new ArgumentException("Placed and authored decoration IDs do not match.", nameof(definition));
        Placed = placed;
        foreach (Node child in GetChildren()) child.QueueFree();
        AddChild(Quad("DecorationOutline", definition.VisualSize, definition.SecondaryColor));
        var inner = Quad("DecorationFill", definition.VisualSize * .86f, definition.PrimaryColor);
        inner.Position = new Vector3(0, 0, .05f);
        AddChild(inner);
        RotationDegrees = new Vector3(0, 0, -placed.RotationDegrees);
        Position = new Vector3(Position.X, Position.Y, ZFor(placed.RenderBand));
    }

    public static float ZFor(DecorationRenderBand band) => band switch
    {
        DecorationRenderBand.Background => -95f,
        DecorationRenderBand.Wallpaper => -90f,
        DecorationRenderBand.WallDecoration => -50f,
        DecorationRenderBand.BehindBuddyFloor => -10f,
        DecorationRenderBand.FrontDecoration => 10f,
        _ => throw new ArgumentOutOfRangeException(nameof(band), band, null),
    };

    private static MeshInstance3D Quad(string name, Vector2 size, Color color)
    {
        var material = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            AlbedoColor = color,
        };
        return new MeshInstance3D
        {
            Name = name,
            Mesh = new QuadMesh { Size = size, Material = material },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
    }
}
