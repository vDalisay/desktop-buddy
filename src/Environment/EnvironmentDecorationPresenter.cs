using System;
using DesktopBuddy.Domain.Environment;
using Godot;

namespace DesktopBuddy.Environment;

/// <summary>Visual-only decoration kept in an authored band around the z=0 buddy plane.</summary>
public partial class EnvironmentDecorationPresenter : Node3D
{
    public PlacedDecoration Placed { get; private set; }

    public void Configure(in PlacedDecoration placed, EnvironmentDecorationResource definition, Vector2 roomSize = default)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (!string.Equals(placed.DefinitionId.Value, definition.DefinitionId, StringComparison.Ordinal))
            throw new ArgumentException("Placed and authored decoration IDs do not match.", nameof(definition));
        Placed = placed;
        foreach (Node child in GetChildren()) child.QueueFree();

        bool wallpaper = placed.RenderBand == DecorationRenderBand.Wallpaper && roomSize.X > 0 && roomSize.Y > 0;
        Vector2 renderSize = wallpaper ? roomSize : definition.VisualSize;
        EnvironmentDecorationVisualFactory.Populate3D(this, definition, renderSize);
        RotationDegrees = wallpaper ? Vector3.Zero : new Vector3(0, 0, -placed.RotationDegrees);
        Position = wallpaper
            ? new Vector3(roomSize.X * .5f, -roomSize.Y * .5f, ZFor(placed.RenderBand))
            : new Vector3(Position.X, Position.Y, ZFor(placed.RenderBand));
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
}
