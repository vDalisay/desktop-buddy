using DesktopBuddy.CharacterEditor;
using DesktopBuddy.Domain.Painting;
using Godot;

namespace DesktopBuddy.UI.Win98;

/// <summary>
/// Completes the Win98 paint picker with paint mutations plus pan/eyedropper interaction modes.
/// Stable node names/tool semantics are independent from the replaceable toolbar artwork.
/// </summary>
public partial class Win98PaintToolBootstrap : Node
{
    private PaintCanvasControl? _canvas;
    private Button? _brush;
    private Button? _eraser;
    private Button? _spray;
    private Button? _eyedropper;
    private Button? _curve;
    private Button? _pan;
    private ColorPickerButton? _colorPicker;
    private ColorRect? _currentColor;

    public override void _Ready() => ProcessMode = ProcessModeEnum.Always;

    public override void _Process(double delta)
    {
        if (GodotObject.IsInstanceValid(_canvas) &&
            GodotObject.IsInstanceValid(_spray) &&
            GodotObject.IsInstanceValid(_eyedropper) &&
            GodotObject.IsInstanceValid(_curve) &&
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

        PaintToolIconProvider.Apply(
            _brush!, PaintToolIconProvider.Brush, "Brush", "Paint with the current color and Brush Size.");
        PaintToolIconProvider.Apply(
            _eraser!, PaintToolIconProvider.Eraser, "Eraser", "Erase buddy paint with the current Brush Size.");

        _spray = ToolButton(
            "PaintSprayButton",
            PaintToolIconProvider.Spray,
            "Spray",
            "Airbrush sparse paint inside the current Brush Size envelope (S).");
        _eyedropper = ToolButton(
            "PaintEyedropperButton",
            PaintToolIconProvider.PickColor,
            "Pick",
            "Sample an existing painted color from the buddy.");
        _curve = ToolButton(
            "PaintCurveButton",
            PaintToolIconProvider.Curve,
            "Curve",
            "Draw a baseline, then drag it twice to make a curved line (C).");
        _pan = ToolButton(
            "PaintPanButton",
            PaintToolIconProvider.Pan,
            "Hand",
            "Pan the buddy viewport with the left mouse button.");

        // Locked ordering: Brush | Eraser / Spray | Pick / Curve | Hand.
        picker!.AddChild(_spray);
        picker.AddChild(_eyedropper);
        picker.AddChild(_curve);
        picker.AddChild(_pan);

        _brush!.Pressed += SelectBrush;
        _eraser!.Pressed += SelectEraser;
        _spray.Pressed += SelectSpray;
        _eyedropper.Pressed += SelectEyedropper;
        _curve.Pressed += SelectCurve;
        _pan.Pressed += SelectPan;
        _canvas!.ColorSampled += ApplySampledColor;
        SelectBrush();
    }

    public override void _UnhandledKeyInput(InputEvent input)
    {
        if (!GodotObject.IsInstanceValid(_canvas) || !_canvas!.Visible ||
            input is not InputEventKey { Pressed: true, Echo: false } key || key.CtrlPressed || key.AltPressed)
        {
            return;
        }

        switch (key.Keycode)
        {
            case Key.S:
                SelectSpray();
                break;
            case Key.C:
                SelectCurve();
                break;
            default:
                return;
        }
        GetViewport().SetInputAsHandled();
    }

    private static Button ToolButton(string name, string iconId, string fallbackText, string tooltip)
    {
        var button = new Button
        {
            Name = name,
            ToggleMode = true,
            CustomMinimumSize = new Vector2(52, 32),
            FocusMode = Control.FocusModeEnum.All,
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        PaintToolIconProvider.Apply(button, iconId, fallbackText, tooltip);
        return button;
    }

    private void SelectBrush() => SelectPaintMutation(PaintTool.Brush, _brush);
    private void SelectEraser() => SelectPaintMutation(PaintTool.Eraser, _eraser);
    private void SelectSpray() => SelectPaintMutation(PaintTool.Spray, _spray);
    private void SelectCurve() => SelectPaintMutation(PaintTool.Curve, _curve);

    private void SelectPaintMutation(PaintTool tool, Button? button)
    {
        if (!ReadyForSelection()) return;
        _canvas!.PanToolActive = false;
        _canvas.EyedropperToolActive = false;
        _canvas.SelectPaintTool(tool);
        SetPressed(button);
        _canvas.MouseDefaultCursorShape = Control.CursorShape.Cross;
        _canvas.QueueRedraw();
    }

    private void SelectEyedropper()
    {
        if (!ReadyForSelection()) return;
        _canvas!.CancelCurve();
        _canvas.PanToolActive = false;
        _canvas.EyedropperToolActive = true;
        SetPressed(_eyedropper);
        _canvas.MouseDefaultCursorShape = Control.CursorShape.PointingHand;
        _canvas.QueueRedraw();
    }

    private void SelectPan()
    {
        if (!ReadyForSelection()) return;
        _canvas!.CancelCurve();
        _canvas.PanToolActive = true;
        _canvas.EyedropperToolActive = false;
        SetPressed(_pan);
        _canvas.MouseDefaultCursorShape = Control.CursorShape.Drag;
        _canvas.QueueRedraw();
    }

    private void SetPressed(Button? selected)
    {
        _brush!.ButtonPressed = ReferenceEquals(selected, _brush);
        _eraser!.ButtonPressed = ReferenceEquals(selected, _eraser);
        _spray!.ButtonPressed = ReferenceEquals(selected, _spray);
        _eyedropper!.ButtonPressed = ReferenceEquals(selected, _eyedropper);
        _curve!.ButtonPressed = ReferenceEquals(selected, _curve);
        _pan!.ButtonPressed = ReferenceEquals(selected, _pan);
    }

    private void ApplySampledColor(PaintColor sampled)
    {
        var color = new Color(sampled.R / 255f, sampled.G / 255f, sampled.B / 255f, 1f);
        if (GodotObject.IsInstanceValid(_colorPicker)) _colorPicker!.Color = color;
        if (GodotObject.IsInstanceValid(_currentColor)) _currentColor!.Color = color;
    }

    private bool ReadyForSelection() =>
        GodotObject.IsInstanceValid(_canvas) &&
        GodotObject.IsInstanceValid(_brush) &&
        GodotObject.IsInstanceValid(_eraser) &&
        GodotObject.IsInstanceValid(_spray) &&
        GodotObject.IsInstanceValid(_eyedropper) &&
        GodotObject.IsInstanceValid(_curve) &&
        GodotObject.IsInstanceValid(_pan);
}
