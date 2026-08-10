using DesktopBuddy.UI.Win98;
using Godot;

namespace DesktopBuddy.Environment;

/// <summary>
/// Presentation-only late pass for Paint Background. It waits for the editor's runtime controls,
/// then replaces compact tool/action words with the same semantic icon mapping used by Paint Buddy.
/// Popup entries and Save/Reset/Cancel remain textual for discoverability.
/// </summary>
public partial class EnvironmentPaintToolIconBootstrap : Node
{
    private bool _applied;

    public override void _Ready() => ProcessMode = ProcessModeEnum.Always;

    public override void _Process(double delta)
    {
        if (_applied) return;
        Node root = GetTree().Root;
        if (root.FindChild("PaintBrushButton", true, false) is not Button brush ||
            root.FindChild("PaintSprayButton", true, false) is not Button spray ||
            root.FindChild("PaintEraserButton", true, false) is not Button eraser ||
            root.FindChild("PaintPickButton", true, false) is not Button pick ||
            root.FindChild("PaintFillButton", true, false) is not Button fill ||
            root.FindChild("PaintShapesButton", true, false) is not MenuButton shapes ||
            root.FindChild("PaintUndoButton", true, false) is not Button undo)
        {
            return;
        }

        // Scope to the Environment editor: the similarly named Buddy buttons are under a different
        // parent, but Paint Background exists as its own root CanvasLayer and supplies Fill/Shapes.
        if (fill.GetParent()?.GetParent()?.GetParent() is not Node environmentPanelAncestor ||
            environmentPanelAncestor.GetParent() is not EnvironmentBackgroundEditor)
        {
            // Runtime hierarchy can gain an extra Margin/Panel wrapper; fall back to ancestry walk.
            if (!HasEnvironmentEditorAncestor(fill)) return;
        }

        Apply(brush, PaintToolIconProvider.Brush, "Brush", "Brush: paint with the current color and Brush Size (B).");
        Apply(spray, PaintToolIconProvider.Spray, "Spray", "Spray: airbrush with the current Brush Size (S).");
        Apply(eraser, PaintToolIconProvider.Eraser, "Eraser", "Eraser: restore the blank background with the current Brush Size (E).");
        Apply(pick, PaintToolIconProvider.PickColor, "Pick Color", "Pick Color: sample the room background (I).");
        Apply(fill, PaintToolIconProvider.Fill, "Fill", "Fill Color: flood the clicked region (F).");
        Apply(shapes, PaintToolIconProvider.Shapes, "Shapes", "Shapes: Square, Circle, Straight Line, or Curved Line.");
        Apply(undo, PaintToolIconProvider.Undo, "Undo", "Undo the last background paint action (Ctrl+Z).");
        _applied = true;
    }

    private static void Apply(Button button, string icon, string fallback, string tooltip) =>
        PaintToolIconProvider.Apply(button, icon, fallback, tooltip);

    private static bool HasEnvironmentEditorAncestor(Node node)
    {
        Node? current = node;
        while (current is not null)
        {
            if (current is EnvironmentBackgroundEditor) return true;
            current = current.GetParent();
        }
        return false;
    }
}
