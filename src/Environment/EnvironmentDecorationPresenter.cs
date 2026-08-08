using System;
using DesktopBuddy.Domain.Environment;
using Godot;

namespace DesktopBuddy.Environment;

/// <summary>Visual-only renderer for one trusted placed decoration definition.</summary>
public partial class EnvironmentDecorationPresenter : Node2D
{
    private EnvironmentDecorationResource? _definition;

    public PlacedDecoration Placed { get; private set; }

    public void Configure(in PlacedDecoration placed, EnvironmentDecorationResource definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (!string.Equals(placed.DefinitionId.Value, definition.DefinitionId, StringComparison.Ordinal))
            throw new ArgumentException("Placed and authored decoration IDs do not match.", nameof(definition));
        Placed = placed;
        _definition = definition;
        RotationDegrees = placed.RotationDegrees;
        ZIndex = ZFor(placed.RenderBand);
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_definition is null) return;
        Vector2 half = _definition.VisualSize * .5f;
        Rect2 bounds = new(-half, _definition.VisualSize);
        Color primary = _definition.PrimaryColor;
        Color secondary = _definition.SecondaryColor;
        switch (_definition.VisualKind)
        {
            case EnvironmentDecorationVisualKind.FloorLamp:
                DrawLine(new Vector2(0, -half.Y * .4f), new Vector2(0, half.Y), secondary, 4);
                DrawRect(new Rect2(-half.X * .55f, -half.Y, half.X * 1.1f, half.Y * .65f), primary);
                DrawRect(new Rect2(-half.X * .5f, half.Y * .85f, half.X, half.Y * .15f), secondary);
                break;
            case EnvironmentDecorationVisualKind.Sofa:
                DrawRect(bounds, secondary);
                DrawRect(bounds.Grow(-4), primary);
                DrawLine(new Vector2(0, -half.Y + 4), new Vector2(0, half.Y - 4), secondary, 2);
                break;
            case EnvironmentDecorationVisualKind.Painting:
                DrawRect(bounds, secondary);
                DrawRect(bounds.Grow(-5), primary);
                DrawLine(new Vector2(-half.X + 7, half.Y - 8), new Vector2(0, -half.Y + 9), secondary, 3);
                DrawLine(new Vector2(0, -half.Y + 9), new Vector2(half.X - 7, half.Y - 8), secondary, 3);
                break;
            case EnvironmentDecorationVisualKind.Wallpaper:
                DrawRect(bounds, primary);
                for (float x = -half.X; x < half.X; x += 12) DrawLine(new Vector2(x, -half.Y), new Vector2(x + 16, half.Y), secondary, 2);
                break;
            case EnvironmentDecorationVisualKind.Plant:
                DrawRect(new Rect2(-half.X * .45f, 0, half.X * .9f, half.Y), secondary);
                DrawCircle(new Vector2(-half.X * .25f, -half.Y * .2f), half.X * .45f, primary);
                DrawCircle(new Vector2(half.X * .25f, -half.Y * .4f), half.X * .5f, primary);
                break;
            case EnvironmentDecorationVisualKind.Table:
                DrawRect(new Rect2(-half.X, -half.Y, _definition.VisualSize.X, half.Y * .35f), primary);
                DrawRect(new Rect2(-half.X * .8f, -half.Y * .65f, half.X * .2f, half.Y * 1.65f), secondary);
                DrawRect(new Rect2(half.X * .6f, -half.Y * .65f, half.X * .2f, half.Y * 1.65f), secondary);
                break;
            default:
                throw new InvalidOperationException("Unsupported Environment visual kind.");
        }
    }

    public static int ZFor(DecorationRenderBand band) => band switch
    {
        DecorationRenderBand.Background => -40,
        DecorationRenderBand.Wallpaper => -30,
        DecorationRenderBand.WallDecoration => -20,
        DecorationRenderBand.BehindBuddyFloor => -10,
        DecorationRenderBand.FrontDecoration => 10,
        _ => throw new ArgumentOutOfRangeException(nameof(band), band, null),
    };
}
