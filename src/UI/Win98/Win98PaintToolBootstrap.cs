using DesktopBuddy.CharacterEditor;
using DesktopBuddy.Domain.Painting;
using Godot;

namespace DesktopBuddy.UI.Win98;

/// <summary>
/// Completes the authorized Win98 paint tool picker with explicit pan and eyedropper tools.
/// Brush, Eraser, Eyedropper and Pan remain mutually exclusive without widening the paint-domain
/// mutation enum: the two non-painting tools are canvas interaction modes.
/// </summary>
public partial class Win98PaintToolBootstrap : Node
{
    private PaintCanvasControl? _canvas;
    private Button? _brush;
    private Button? _eraser;
    private Button? _eyedropper;
    private Button? _pan;
    private ColorPickerButton? _colorPicker;
    private ColorRect? _currentColor;

    // The editor pauses the tree while open, which is exactly when its tool picker exists.
    public override void _Ready() => ProcessMode = ProcessModeEnum.Always;

    public override void _Process(double delta)
    {
        if (GodotObject.IsInstanceValid(_canvas) &&
            GodotObject.IsInstanceValid(_eyedropper) &&
            GodotObject.IsInstanceValid(_pan))
        {
            return;
        }

        _canvas = GetTree().Root.FindChild(
            "CharacterPaintCanvas", recursive: true, owned: false) as PaintCanvasControl;
        _brush = GetTree().Root.FindChild(
            "PaintBrushButton", recursive: true, owned: false) as Button;
        _eraser = GetTree().Root.FindChild(
            "PaintEraserButton", recursive: true, owned: false) as Button;
        _colorPicker = GetTree().Root.FindChild(
            "PaintColorWheel", recursive: true, owned: false) as ColorPickerButton;
        _currentColor = GetTree().Root.FindChild(
            "PaintCurrentColor", recursive: true, owned: false) as ColorRect;
        GridContainer? picker = GetTree().Root.FindChild(
            "Win98ToolPicker", recursive: true, owned: false) as GridContainer;

        if (!GodotObject.IsInstanceValid(_canvas) ||
            !GodotObject.IsInstanceValid(_brush) ||
            !GodotObject.IsInstanceValid(_eraser) ||
            !GodotObject.IsInstanceValid(picker))
        {
            return;
        }

        _eyedropper = ToolButton(
            "PaintEyedropperButton",
            "Pick",
            "Sample an existing painted color from the buddy.");
        _pan = ToolButton(
            "PaintPanButton",
            "Hand",
            "Pan the buddy viewport with the left mouse button.");
        picker!.AddChild(_eyedropper);
        picker.AddChild(_pan);

        _brush!.Pressed += SelectBrush;
        _eraser!.Pressed += SelectEraser;
        _eyedropper.Pressed += SelectEyedropper;
        _pan.Pressed += SelectPan;
        _canvas!.ColorSampled += ApplySampledColor;
        SelectBrush();
    }

    private static Button ToolButton(string name, string text, string tooltip) => new()
    {
        Name = name,
        Text = text,
        TooltipText = tooltip,
        ToggleMode = true,
        CustomMinimumSize = new Vector2(52, 32),
        FocusMode = Control.FocusModeEnum.All,
        MouseFilter = Control.MouseFilterEnum.Stop,
    };

    private void SelectBrush()
    {
        if (!ReadyForSelection())
            return;
        _canvas!.PanToolActive = false;
        _canvas.EyedropperToolActive = false;
        _canvas.Workspace.SelectedTool = PaintTool.Brush;
        SetPressed(_brush);
        _canvas.MouseDefaultCursorShape = Control.CursorShape.Cross;
        _canvas.QueueRedraw();
    }

    private void SelectEraser()
    {
        if (!ReadyForSelection())
            return;
        _canvas!.PanToolActive = false;
        _canvas.EyedropperToolActive = false;
        _canvas.Workspace.SelectedTool = PaintTool.Eraser;
        SetPressed(_eraser);
        _canvas.MouseDefaultCursorShape = Control.CursorShape.Cross;
        _canvas.QueueRedraw();
    }

    private void SelectEyedropper()
    {
        if (!ReadyForSelection())
            return;
        _canvas!.PanToolActive = false;
        _canvas.EyedropperToolActive = true;
        SetPressed(_eyedropper);
        _canvas.MouseDefaultCursorShape = Control.CursorShape.PointingHand;
        _canvas.QueueRedraw();
    }

    private void SelectPan()
    {
        if (!ReadyForSelection())
            return;
        _canvas!.PanToolActive = true;
        _canvas.EyedropperToolActive = false;
        SetPressed(_pan);
        _canvas.MouseDefaultCursorShape = Control.CursorShape.Drag;
        _canvas.QueueRedraw();
    }

    private void SetPressed(Button? selected)
    {
        _brush!.ButtonPressed = ReferenceEquals(selected, _brush);
        _eraser!.ButtonPressed = ReferenceEquals(selected, _eraser);
        _eyedropper!.ButtonPressed = ReferenceEquals(selected, _eyedropper);
        _pan!.ButtonPressed = ReferenceEquals(selected, _pan);
    }

    private void ApplySampledColor(PaintColor sampled)
    {
        var color = new Color(
            sampled.R / 255f,
            sampled.G / 255f,
            sampled.B / 255f,
            1f);
        if (GodotObject.IsInstanceValid(_colorPicker))
            _colorPicker!.Color = color;
        if (GodotObject.IsInstanceValid(_currentColor))
            _currentColor!.Color = color;
    }

    private bool ReadyForSelection() =>
        GodotObject.IsInstanceValid(_canvas) &&
        GodotObject.IsInstanceValid(_brush) &&
        GodotObject.IsInstanceValid(_eraser) &&
        GodotObject.IsInstanceValid(_eyedropper) &&
        GodotObject.IsInstanceValid(_pan);
}
