using DesktopBuddy.Domain.Sandbox;
using DesktopBuddy.UI.Win98;
using Godot;

namespace DesktopBuddy.Sandbox;

/// <summary>
/// A sunken well showing the part that is about to be placed, scaled to fit.
///
/// <para>It draws the same geometry and material colour <see cref="SandboxPartBody"/> puts in the
/// room, so the preview is the part rather than an illustration of it. It is deliberately the 2D
/// shape: built parts have no 3D model yet — that is the NF-3 rendering packet — and a preview
/// promising a look the room does not deliver would be worse than an honest one.</para>
/// </summary>
public partial class SandboxPartPreview : Control
{
    private const float Padding = 10.0f;

    private SandboxPartDefinition? _definition;

    public void Show(SandboxPartDefinition? definition)
    {
        _definition = definition;
        QueueRedraw();
    }

    public override void _Draw()
    {
        Vector2 size = Size;
        DrawStyleBox(Win98ThemeFactory.Recessed(Win98ThemeFactory.Light, 2), new Rect2(Vector2.Zero, size));
        if (_definition is not { } definition)
            return;

        // One scale for both axes, so a Metal Plate still reads as long and thin next to a Wheel.
        float scale = Mathf.Min(
            (size.X - Padding * 2.0f) / definition.Width,
            (size.Y - Padding * 2.0f) / definition.Height);
        scale = Mathf.Min(scale, 1.0f);
        Vector2 centre = size * 0.5f;
        Color fill = SandboxPartBody.FillFor(definition.Material);

        if (definition.Shape == SandboxPartShape.Circle)
        {
            float radius = definition.Radius * scale;
            DrawCircle(centre, radius, fill, true, -1.0f, true);
            DrawArc(centre, radius, 0.0f, Mathf.Tau, 32, SandboxPartBody.OutlineColor, 2.0f, true);
            DrawLine(centre, centre + new Vector2(radius, 0.0f), SandboxPartBody.OutlineColor, 2.0f, true);
            return;
        }

        var drawn = new Vector2(definition.Width * scale, definition.Height * scale);
        var rect = new Rect2(centre - drawn * 0.5f, drawn);
        DrawRect(rect, fill, filled: true);
        DrawRect(rect, SandboxPartBody.OutlineColor, filled: false, 2.0f);
    }
}
