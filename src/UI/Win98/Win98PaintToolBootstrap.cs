using DesktopBuddy.CharacterEditor;
using DesktopBuddy.Domain.Painting;
using Godot;

namespace DesktopBuddy.UI.Win98;

/// <summary>
/// Completes the authorized Win98 paint tool picker with an explicit hand/pan tool and keeps
/// Brush, Eraser and Pan mutually exclusive without changing the paint-domain tool enum.
/// </summary>
public partial class Win98PaintToolBootstrap : Node
{
    private PaintCanvasControl? _canvas;
    private Button? _brush;
    private Button? _eraser;
    private Button? _pan;

    // The editor pauses the tree while open, which is exactly when its tool picker exists.
    public override void _Ready() => ProcessMode = ProcessModeEnum.Always;

    public override void _Process(double delta)
    {
        if (GodotObject.IsInstanceValid(_canvas) && GodotObject.IsInstanceValid(_pan))
            return;

        _canvas = GetTree().Root.FindChild(
            "CharacterPaintCanvas", recursive: true, owned: false) as PaintCanvasControl;
        _brush = GetTree().Root.FindChild(
            "PaintBrushButton", recursive: true, owned: false) as Button;
        _eraser = GetTree().Root.FindChild(
            "PaintEraserButton", recursive: true, owned: false) as Button;
        GridContainer? picker = GetTree().Root.FindChild(
            "Win98ToolPicker", recursive: true, owned: false) as GridContainer;

        if (!GodotObject.IsInstanceValid(_canvas) ||
            !GodotObject.IsInstanceValid(_brush) ||
            !GodotObject.IsInstanceValid(_eraser) ||
            !GodotObject.IsInstanceValid(picker))
        {
            return;
        }

        _pan = new Button
        {
            Name = "PaintPanButton",
            Text = "Hand",
            TooltipText = "Pan the buddy viewport with the left mouse button.",
            ToggleMode = true,
            CustomMinimumSize = new Vector2(52, 32),
            FocusMode = Control.FocusModeEnum.All,
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        picker!.AddChild(_pan);

        _brush!.Pressed += SelectBrush;
        _eraser!.Pressed += SelectEraser;
        _pan.Pressed += SelectPan;
        SelectBrush();
    }

    private void SelectBrush()
    {
        if (!ReadyForSelection())
            return;
        _canvas!.PanToolActive = false;
        _canvas.Workspace.SelectedTool = PaintTool.Brush;
        _brush!.ButtonPressed = true;
        _eraser!.ButtonPressed = false;
        _pan!.ButtonPressed = false;
        _canvas.MouseDefaultCursorShape = Control.CursorShape.Cross;
        _canvas.QueueRedraw();
    }

    private void SelectEraser()
    {
        if (!ReadyForSelection())
            return;
        _canvas!.PanToolActive = false;
        _canvas.Workspace.SelectedTool = PaintTool.Eraser;
        _brush!.ButtonPressed = false;
        _eraser!.ButtonPressed = true;
        _pan!.ButtonPressed = false;
        _canvas.MouseDefaultCursorShape = Control.CursorShape.Cross;
        _canvas.QueueRedraw();
    }

    private void SelectPan()
    {
        if (!ReadyForSelection())
            return;
        _canvas!.PanToolActive = true;
        _brush!.ButtonPressed = false;
        _eraser!.ButtonPressed = false;
        _pan!.ButtonPressed = true;
        _canvas.MouseDefaultCursorShape = Control.CursorShape.Drag;
        _canvas.QueueRedraw();
    }

    private bool ReadyForSelection() =>
        GodotObject.IsInstanceValid(_canvas) &&
        GodotObject.IsInstanceValid(_brush) &&
        GodotObject.IsInstanceValid(_eraser) &&
        GodotObject.IsInstanceValid(_pan);
}
