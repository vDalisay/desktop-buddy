using DesktopBuddy.UI.Win98;
using Godot;

namespace DesktopBuddy.Environment;

/// <summary>
/// Presentation-only PAINT-R6 pass for Paint Background. It waits for the editor's runtime
/// controls, then converts compact tool/action buttons to the same stable semantic icon mapping
/// used by Paint Buddy. Icons sit next to the tool name; popup entries and Save/Reset/Cancel
/// remain textual for discoverability.
/// </summary>
public partial class EnvironmentPaintToolIconBootstrap : Node
{
    private static readonly Vector2 CompactToolSize = new(112, 32);
    private bool _applied;

    public override void _Ready() => ProcessMode = ProcessModeEnum.Always;

    public override void _Process(double delta)
    {
        if (_applied) return;
        if (GetTree().Root.FindChild(
                nameof(EnvironmentBackgroundEditor), recursive: true, owned: false) is not EnvironmentBackgroundEditor editor)
        {
            return;
        }

        if (editor.FindChild("PaintBrushButton", true, false) is not Button brush ||
            editor.FindChild("PaintPenButton", true, false) is not Button pen ||
            editor.FindChild("PaintSprayButton", true, false) is not Button spray ||
            editor.FindChild("PaintEraserButton", true, false) is not Button eraser ||
            editor.FindChild("PaintPickButton", true, false) is not Button pick ||
            editor.FindChild("PaintFillButton", true, false) is not Button fill ||
            editor.FindChild("PaintShapesButton", true, false) is not MenuButton shapes ||
            editor.FindChild("PaintUndoButton", true, false) is not Button undo)
        {
            return;
        }

        Apply(brush, PaintToolIconProvider.Brush, "Brush", "Brush: paint with the current color and Brush Size (B).");
        Apply(pen, PaintToolIconProvider.Pen, "Pen", "Pen: solid round nib matching the cursor ring (P).");
        Apply(spray, PaintToolIconProvider.Spray, "Spray", "Spray: airbrush with the current Brush Size (S).");
        Apply(eraser, PaintToolIconProvider.Eraser, "Eraser", "Eraser: restore the blank background with the current Brush Size (E).");
        Apply(pick, PaintToolIconProvider.PickColor, "Pick", "Pick Color: sample what is rendered under the pointer (I).");
        Apply(fill, PaintToolIconProvider.Fill, "Bucket Fill", "Fill Color: flood the clicked paint region (F).");
        Apply(shapes, PaintToolIconProvider.Shapes, "Shapes", "Shapes: Square, Circle, Straight Line, or Curved Line.");
        Apply(undo, PaintToolIconProvider.Undo, "Undo", "Undo the last background paint action (Ctrl+Z).");
        _applied = true;
    }

    private static void Apply(Button button, string icon, string fallback, string tooltip)
    {
        PaintToolIconProvider.Apply(button, icon, fallback, tooltip, keepText: true);
        button.CustomMinimumSize = CompactToolSize;
        button.FocusMode = Control.FocusModeEnum.All;
    }
}
