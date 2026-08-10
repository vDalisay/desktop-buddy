using System;
using System.Collections.Generic;
using DesktopBuddy.Domain.Environment;
using DesktopBuddy.Persistence;
using DesktopBuddy.Painting;
using DesktopBuddy.UI.Win98;
using Godot;

namespace DesktopBuddy.Environment;

/// <summary>
/// Win98 paint window for the room background. Every tool writes to the shared
/// <see cref="EnvironmentCanvas"/>; the window steps out of the way while a drag is in progress.
/// </summary>
public partial class EnvironmentBackgroundEditor : CanvasLayer
{
    private static readonly Color[] DefaultSwatches =
    [
        Colors.Black, Color.Color8(128, 128, 128), Color.Color8(128, 0, 0), Color.Color8(128, 128, 0),
        Color.Color8(0, 128, 0), Color.Color8(0, 128, 128), Color.Color8(0, 0, 128), Color.Color8(128, 0, 128),
        Colors.White, Color.Color8(192, 192, 192), Color.Color8(255, 0, 0), Color.Color8(255, 255, 0),
        Color.Color8(0, 255, 0), Color.Color8(0, 255, 255), Color.Color8(0, 0, 255), Color.Color8(255, 0, 255),
    ];
    private const int MaximumSwatches = 24;
    private const double SprayPulseSeconds = 0.05;
    private const int MaximumSprayCatchUpPulses = 4;

    private EnvironmentBackgroundPresenter _presenter = null!;
    private EnvironmentPaintStore _store = null!;
    private readonly List<Color> _swatches = [.. DefaultSwatches];
    private Control _blocker = null!;
    private PanelContainer _panel = null!;
    private GridContainer _swatchGrid = null!;
    private ColorRect _current = null!;
    private ColorPickerButton _picker = null!;
    private Button _undo = null!;
    private Label _dirty = null!;
    private Label _status = null!;
    private Label _brushSize = null!;
    private EnvironmentPaintCursor _cursor = null!;
    private PanelContainer _confirm = null!;
    private byte[]? _baseline;
    private bool _painting;
    private bool _saving;
    private int _selectedSwatch = -1;
    private double _sprayPulseAccumulator;

    public bool IsOpen => GodotObject.IsInstanceValid(_blocker) && _blocker.Visible;
    private EnvironmentCanvas Canvas => _presenter.Canvas;
    internal bool PanelVisibleForTest => _panel.Visible;

    public void Configure(EnvironmentBackgroundPresenter presenter, EnvironmentPaintStore store)
    {
        _presenter = presenter ?? throw new ArgumentNullException(nameof(presenter));
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        Layer = 120;
        Build();
    }

    public override void _Process(double delta)
    {
        if (!IsOpen || !_painting || Canvas.Tool != EnvironmentPaintTool.Spray)
        {
            _sprayPulseAccumulator = 0;
            return;
        }

        _sprayPulseAccumulator += Math.Max(0, delta);
        int pulses = 0;
        while (_sprayPulseAccumulator >= SprayPulseSeconds && pulses++ < MaximumSprayCatchUpPulses)
        {
            _sprayPulseAccumulator -= SprayPulseSeconds;
            Vector2 pointer = _blocker.GetLocalMousePosition();
            if (TryCanonical(pointer, out double x, out double y))
                Canvas.Continue(x, y);
        }
        if (pulses >= MaximumSprayCatchUpPulses)
            _sprayPulseAccumulator = 0;
    }

    public override void _UnhandledInput(InputEvent input)
    {
        if (!IsOpen || input is not InputEventKey { Pressed: true, Echo: false } key) return;
        if (key.Keycode == Key.Delete && _selectedSwatch >= 0)
        {
            RemoveSwatch(_selectedSwatch);
            GetViewport().SetInputAsHandled();
            return;
        }
        switch (key.Keycode)
        {
            case Key.Escape:
                if (Canvas.CancelPendingCurve())
                {
                    _painting = false;
                    _panel.Visible = true;
                    _status.Text = "Curved Line cancelled.";
                    Refresh();
                }
                else RequestClose();
                break;
            case Key.B: SelectTool(EnvironmentPaintTool.Brush); break;
            case Key.P: SelectTool(EnvironmentPaintTool.Pen); break;
            case Key.S: SelectTool(EnvironmentPaintTool.Spray); break;
            case Key.C: SelectTool(EnvironmentPaintTool.CurvedLine); break;
            case Key.E: SelectTool(EnvironmentPaintTool.Eraser); break;
            case Key.I: SelectTool(EnvironmentPaintTool.PickColor); break;
            case Key.F: SelectTool(EnvironmentPaintTool.Fill); break;
            case Key.Z when key.CtrlPressed: UndoStroke(); break;
            default: return;
        }
        GetViewport().SetInputAsHandled();
    }

    public void Open()
    {
        if (_saving || IsOpen) return;
        _baseline = Canvas.ClonePixels();
        _blocker.Visible = true;
        _panel.Visible = true;
        _confirm.Visible = false;
        SelectTool(EnvironmentPaintTool.Brush);
        Refresh();
    }

    private void Build()
    {
        _blocker = new Control { Name = "EnvironmentBackgroundInputBlocker", Visible = false, MouseFilter = Control.MouseFilterEnum.Stop };
        AddChild(_blocker);
        _blocker.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _blocker.GuiInput += OnCanvasInput;
        _cursor = new EnvironmentPaintCursor { Name = "EnvironmentPaintCursor", MouseFilter = Control.MouseFilterEnum.Ignore };
        _blocker.AddChild(_cursor);

        _panel = Win98Dialog.Create("PaintBackgroundPanel", "Paint Background", new Vector2(430, 360), out VBoxContainer body, RequestClose);
        _blocker.AddChild(_panel);
        _panel.Visible = true;

        // "Tools" owns its whole row; every tool button sits in the grid below it.
        body.AddChild(new Label { Text = "Tools" });
        var grid = new GridContainer { Name = "PaintToolGrid", Columns = 4 };
        grid.AddThemeConstantOverride("h_separation", 4);
        grid.AddThemeConstantOverride("v_separation", 4);
        body.AddChild(grid);
        grid.AddChild(ToolButton("PaintBrushButton", "Brush  [B]", EnvironmentPaintTool.Brush));
        grid.AddChild(ToolButton("PaintPenButton", "Pen  [P]", EnvironmentPaintTool.Pen));
        grid.AddChild(ToolButton("PaintSprayButton", "Spray  [S]", EnvironmentPaintTool.Spray));
        grid.AddChild(ToolButton("PaintFillButton", "Fill  [F]", EnvironmentPaintTool.Fill));
        grid.AddChild(ToolButton("PaintEraserButton", "Eraser  [E]", EnvironmentPaintTool.Eraser));
        grid.AddChild(ToolButton("PaintPickButton", "Pick  [I]", EnvironmentPaintTool.PickColor));

        var shapes = new MenuButton
        {
            Name = "PaintShapesButton",
            Text = "Shapes  ▸",
            Flat = false,
            CustomMinimumSize = new Vector2(104, 28),
            TooltipText = "Draw Square, Circle, Straight Line, or Curved Line.",
        };
        PopupMenu shapeMenu = shapes.GetPopup();
        Win98MenuStyle.Apply(shapeMenu);
        shapeMenu.AddItem("Square", (int)EnvironmentPaintTool.Square);
        shapeMenu.AddItem("Circle", (int)EnvironmentPaintTool.Circle);
        shapeMenu.AddItem("Straight Line", (int)EnvironmentPaintTool.Line);
        shapeMenu.AddItem("Curved Line  [C]", (int)EnvironmentPaintTool.CurvedLine);
        shapeMenu.IdPressed += id => SelectTool((EnvironmentPaintTool)id);
        grid.AddChild(shapes);

        _undo = new Button { Name = "PaintUndoButton", Text = "Undo  [Ctrl+Z]", CustomMinimumSize = new Vector2(104, 28) };
        _undo.Pressed += UndoStroke;
        grid.AddChild(_undo);

        var size = new HBoxContainer { Name = "PaintBrushSizeRow" };
        size.AddChild(new Label { Text = "Brush Size", CustomMinimumSize = new Vector2(84, 28), VerticalAlignment = VerticalAlignment.Center });
        size.AddChild(SizeButton("−", -1));
        _brushSize = new Label { HorizontalAlignment = HorizontalAlignment.Center, CustomMinimumSize = new Vector2(54, 28), VerticalAlignment = VerticalAlignment.Center };
        size.AddChild(_brushSize);
        size.AddChild(SizeButton("+", 1));
        body.AddChild(size);

        var inset = new PanelContainer { Name = "PaintPalette" };
        inset.AddThemeStyleboxOverride("panel", Win98ThemeFactory.Recessed(Win98ThemeFactory.Face, 2));
        body.AddChild(inset);
        var paletteRow = new HBoxContainer();
        paletteRow.AddThemeConstantOverride("separation", 8);
        inset.AddChild(paletteRow);
        _current = new ColorRect { Name = "PaintCurrentColor", CustomMinimumSize = new Vector2(48, 40) };
        paletteRow.AddChild(_current);
        _swatchGrid = new GridContainer { Name = "PaintSwatches", Columns = 8 };
        paletteRow.AddChild(_swatchGrid);
        RebuildSwatches();
        var add = new Button { Name = "PaintAddSwatchButton", Text = "+", CustomMinimumSize = new Vector2(28, 28), TooltipText = "Add the chosen color to the palette." };
        add.Pressed += AddCustomSwatch;
        paletteRow.AddChild(add);
        _picker = new ColorPickerButton
        {
            Name = "PaintCustomColorButton",
            CustomMinimumSize = new Vector2(48, 40),
            EditAlpha = false,
            TooltipText = "Choose a custom color.",
        };
        _picker.ColorChanged += SelectColor;
        paletteRow.AddChild(_picker);
        AddColorPickerIcon(_picker);

        _dirty = new Label { Name = "PaintDirtyStatus", Text = "No unsaved changes" };
        body.AddChild(_dirty);
        _status = new Label { Name = "PaintToolStatus", AutowrapMode = TextServer.AutowrapMode.WordSmart, SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        body.AddChild(_status);

        var actions = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        body.AddChild(actions);
        Win98Dialog.Action(actions, "Reset", Reset).Name = "PaintResetButton";
        Win98Dialog.Action(actions, "Save", Save).Name = "PaintSaveButton";
        Win98Dialog.Action(actions, "Cancel", RequestClose).Name = "PaintCancelButton";

        _confirm = Win98Dialog.Create("BackgroundUnsavedDialog", "Unsaved Background", new Vector2(360, 170), out VBoxContainer confirmBody);
        _blocker.AddChild(_confirm);
        confirmBody.AddChild(new Label { Text = "Save the painted background before closing?", AutowrapMode = TextServer.AutowrapMode.WordSmart });
        var confirmActions = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        confirmBody.AddChild(confirmActions);
        Win98Dialog.Action(confirmActions, "Save", Save).Name = "PaintConfirmSaveButton";
        Win98Dialog.Action(confirmActions, "Discard", Discard).Name = "PaintDiscardButton";
        Win98Dialog.Action(confirmActions, "Keep Editing", () => _confirm.Visible = false).Name = "PaintKeepEditingButton";
    }

    private Button ToolButton(string name, string text, EnvironmentPaintTool tool)
    {
        var button = new Button { Name = name, Text = text, CustomMinimumSize = new Vector2(104, 28) };
        button.Pressed += () => SelectTool(tool);
        return button;
    }

    private Button SizeButton(string text, int direction)
    {
        var button = new Button { Text = text, CustomMinimumSize = new Vector2(42, 28) };
        button.Pressed += () => AdjustBrush(direction);
        return button;
    }

    private static void AddColorPickerIcon(ColorPickerButton picker)
    {
        var background = new ColorRect { Color = Win98ThemeFactory.Face, MouseFilter = Control.MouseFilterEnum.Ignore };
        picker.AddChild(background);
        background.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        var icon = new TextureRect
        {
            Name = "PaintColorWheelIcon",
            Texture = GD.Load<Texture2D>("res://assets/ui/win98/paint_bucket_brushes.svg"),
            MouseFilter = Control.MouseFilterEnum.Ignore,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
        };
        picker.AddChild(icon);
        icon.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        icon.OffsetLeft = icon.OffsetTop = 1;
        icon.OffsetRight = icon.OffsetBottom = -1;
    }

    private void RebuildSwatches()
    {
        foreach (Node child in _swatchGrid.GetChildren())
        {
            _swatchGrid.RemoveChild(child);
            child.QueueFree();
        }
        for (int index = 0; index < _swatches.Count; index++)
        {
            Color swatch = _swatches[index];
            var cell = new Button
            {
                Name = $"PaintSwatch{index}", ToggleMode = true, ButtonPressed = index == _selectedSwatch,
                CustomMinimumSize = new Vector2(20, 18), TooltipText = "Click to select. Right-click or press Delete to remove.",
            };
            ApplySwatchStyle(cell, swatch);
            int captured = index;
            cell.Pressed += () => SelectSwatch(captured);
            cell.GuiInput += input =>
            {
                if (input is InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: true }) RemoveSwatch(captured);
            };
            _swatchGrid.AddChild(cell);
        }
    }

    private static void ApplySwatchStyle(Button button, Color color)
    {
        var normal = new StyleBoxFlat { BgColor = color, BorderColor = Colors.Black };
        normal.SetBorderWidthAll(1);
        var selected = new StyleBoxFlat { BgColor = color, BorderColor = Win98ThemeFactory.Selection };
        selected.SetBorderWidthAll(3);
        button.AddThemeStyleboxOverride("normal", normal);
        button.AddThemeStyleboxOverride("hover", normal);
        button.AddThemeStyleboxOverride("pressed", selected);
        button.AddThemeStyleboxOverride("hover_pressed", selected);
        button.AddThemeStyleboxOverride("focus", selected);
    }

    private void SelectSwatch(int index)
    {
        if (index < 0 || index >= _swatches.Count) return;
        _selectedSwatch = index;
        SelectColor(_swatches[index]);
        RebuildSwatches();
        if (_swatchGrid.GetChild(index) is Button selected) selected.GrabFocus();
    }

    private void RemoveSwatch(int index)
    {
        if (index < 0 || index >= _swatches.Count) return;
        _swatches.RemoveAt(index);
        _selectedSwatch = Math.Min(index, _swatches.Count - 1);
        if (_selectedSwatch >= 0) SelectColor(_swatches[_selectedSwatch]);
        RebuildSwatches();
    }

    private void AddCustomSwatch()
    {
        if (_swatches.Count >= MaximumSwatches) { _status.Text = "The palette is full; replace a color instead."; return; }
        _swatches.Add(_picker.Color);
        _selectedSwatch = _swatches.Count - 1;
        RebuildSwatches();
        SelectColor(_picker.Color);
    }

    private void SelectColor(Color color)
    {
        Canvas.Color = new EnvironmentColor((byte)color.R8, (byte)color.G8, (byte)color.B8);
        _current.Color = color;
        _picker.Color = color;
    }

    private void SelectTool(EnvironmentPaintTool tool)
    {
        Canvas.Tool = tool;
        _sprayPulseAccumulator = 0;
        _status.Text = tool switch
        {
            EnvironmentPaintTool.Brush => "Brush: drag anywhere on the background to paint.",
            EnvironmentPaintTool.Pen => "Pen: drag to paint a solid round nib that matches the cursor ring.",
            EnvironmentPaintTool.Spray => "Spray: hold or drag to airbrush with the current Brush Size.",
            EnvironmentPaintTool.Eraser => "Eraser: drag to restore the blank background.",
            EnvironmentPaintTool.Fill => "Fill color: click an area to flood it.",
            EnvironmentPaintTool.PickColor => "Pick Color: click the background to take its color.",
            EnvironmentPaintTool.Square => "Square: drag to define the shape.",
            EnvironmentPaintTool.Circle => "Circle: drag to define the shape.",
            EnvironmentPaintTool.CurvedLine => "Curved Line: drag the baseline, then drag two points on the line to bend it.",
            _ => "Straight Line: drag from one end to the other.",
        };
        Refresh();
    }

    private void OnCanvasInput(InputEvent input)
    {
        if (_confirm.Visible) { _blocker.AcceptEvent(); return; }
        switch (input)
        {
            case InputEventMouseMotion motion:
                UpdateCursor(motion.Position);
                if (_painting && Canvas.Tool == EnvironmentPaintTool.PickColor)
                    PickColor(motion.Position);
                else if (_painting && TryCanonical(motion.Position, out double moveX, out double moveY))
                    Canvas.Continue(moveX, moveY);
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: true }:
                if (Canvas.CancelPendingCurve())
                {
                    _painting = false;
                    _panel.Visible = true;
                    _status.Text = "Curved Line cancelled.";
                    Refresh();
                }
                break;
            case InputEventMouseButton wheel when wheel.Pressed && wheel.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown:
                AdjustBrush(wheel.ButtonIndex == MouseButton.WheelUp ? 1 : -1);
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } click when TryCanonical(click.Position, out double x, out double y):
                _painting = true;
                _sprayPulseAccumulator = 0;
                _panel.Visible = false;
                if (Canvas.Tool == EnvironmentPaintTool.PickColor) PickColor(click.Position);
                else Canvas.Begin(x, y);
                UpdateCursor(click.Position);
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false } release when _painting:
                if (Canvas.Tool != EnvironmentPaintTool.PickColor)
                {
                    if (TryCanonical(release.Position, out double endX, out double endY)) Canvas.End(endX, endY);
                    else Canvas.End(double.NaN, double.NaN);
                }
                _painting = false;
                _sprayPulseAccumulator = 0;
                _panel.Visible = true;
                UpdateCursor(release.Position);
                UpdateCurveStatusAfterRelease();
                Refresh();
                break;
        }
        _blocker.AcceptEvent();
    }

    private void UpdateCurveStatusAfterRelease()
    {
        if (Canvas.Tool != EnvironmentPaintTool.CurvedLine) return;
        _status.Text = Canvas.CurvePhase switch
        {
            EnvironmentCurvePhase.AwaitFirstBend => "Curved Line: drag a point on the baseline to make the first bend.",
            EnvironmentCurvePhase.AwaitSecondBend => "Curved Line: drag a second point to make the final bend.",
            EnvironmentCurvePhase.Idle => "Curved Line committed. Drag to start another curve.",
            _ => _status.Text,
        };
    }

    /// <summary>
    /// Samples the rendered frame rather than the paint canvas: the player expects Pick Color to
    /// take whatever is under the cursor — decorations, the buddy, the bare room grey — and the
    /// canvas only knows about pixels it painted (everything else read back as black).
    /// </summary>
    private void PickColor(Vector2 position)
    {
        // ponytail: full framebuffer readback per sample; only runs while dragging the Pick tool.
        Image frame = GetViewport().GetTexture().GetImage();
        Vector2I size = frame.GetSize();
        if (size.X <= 0 || size.Y <= 0) return;
        var point = new Vector2I(
            Mathf.Clamp((int)position.X, 0, size.X - 1),
            Mathf.Clamp((int)position.Y, 0, size.Y - 1));
        Color picked = frame.GetPixelv(point);
        picked.A = 1;
        Canvas.Color = new EnvironmentColor((byte)picked.R8, (byte)picked.G8, (byte)picked.B8);
        _current.Color = picked;
        _picker.Color = picked;
        _cursor.SampleColor = picked;
        _cursor.QueueRedraw();
    }

    private void AdjustBrush(int direction)
    {
        Canvas.AdjustBrush(direction);
        Refresh();
        UpdateCursor(_blocker.GetLocalMousePosition());
    }

    private void UpdateCursor(Vector2 position)
    {
        _cursor.Position = position;
        _cursor.Diameter = Canvas.BrushDiameter * BackgroundRect().Size.X / EnvironmentCanvasPolicy.Size;
        // The ring is the only thing Pick Color must never sample: at the smallest brush it sits on
        // the pixel under the cursor, so every pick came back white. Pick has no brush size anyway.
        _cursor.ShowBrush = IsOpen && Canvas.Tool != EnvironmentPaintTool.PickColor;
        _cursor.ShowSample = _painting && Canvas.Tool == EnvironmentPaintTool.PickColor;
        _cursor.QueueRedraw();
    }

    private bool TryCanonical(Vector2 screen, out double x, out double y)
    {
        Rect2 room = BackgroundRect();
        x = 0;
        y = 0;
        if (room.Size.X <= 0 || room.Size.Y <= 0) return false;
        Canvas.PixelAspect = room.Size.X / room.Size.Y;
        x =Math.Clamp((screen.X - room.Position.X) / room.Size.X, 0, 1);
        y = Math.Clamp((screen.Y - room.Position.Y) / room.Size.Y, 0, 1);
        return true;
    }

    private Rect2 BackgroundRect() =>
        _presenter.TryGetScreenRect(out Rect2 rect) ? rect : GetViewport().GetVisibleRect();

    private void UndoStroke()
    {
        bool pendingCurve = Canvas.CurvePending;
        bool changed = Canvas.Undo();
        _status.Text = pendingCurve && changed ? "Curved Line cancelled." : changed ? "Undid the last change." : "Nothing left to undo.";
        Refresh();
    }

    private void Reset()
    {
        Canvas.Reset();
        _status.Text = "Background reset to blank.";
        Refresh();
    }

    private async void Save()
    {
        if (_saving) return;
        if (Canvas.CurvePending)
        {
            _status.Text = "Finish or cancel the Curved Line before saving.";
            return;
        }
        _saving = true;
        _status.Text = "Saving…";
        try
        {
            await _store.SaveAsync(Canvas.Pixels);
            Canvas.MarkSaved();
            _baseline = Canvas.ClonePixels();
            Close();
        }
        catch (Exception exception)
        {
            _status.Text = $"Save failed: {exception.Message}";
            _confirm.Visible = false;
        }
        finally { _saving = false; }
    }

    private void RequestClose()
    {
        if (_saving) return;
        Canvas.CancelPendingCurve();
        if (Canvas.IsDirty) { _confirm.Visible = true; return; }
        Close();
    }

    private void Discard()
    {
        Canvas.CancelPendingCurve();
        if (_baseline is not null) Canvas.Replace(_baseline);
        Close();
    }

    private void Close()
    {
        Canvas.CancelPendingCurve();
        _confirm.Visible = false;
        _blocker.Visible = false;
        _panel.Visible = true;
        _painting = false;
        _sprayPulseAccumulator = 0;
    }

    private void Refresh()
    {
        _dirty.Text = Canvas.IsDirty ? "Unsaved changes" : "No unsaved changes";
        _undo.Disabled = !Canvas.CanUndo && !Canvas.CurvePending;
        _brushSize.Text = $"{Canvas.BrushDiameter}px";
    }

    private sealed partial class EnvironmentPaintCursor : Control
    {
        public float Diameter { get; set; }
        public bool ShowBrush { get; set; }
        public bool ShowSample { get; set; }
        public Color SampleColor { get; set; } = Colors.White;

        public override void _Draw()
        {
            if (ShowBrush) PaintCursorGizmos.DrawBrushRing(this, Vector2.Zero, Diameter);
            if (ShowSample) PaintCursorGizmos.DrawPickPreview(this, Vector2.Zero, SampleColor);
        }
    }
}
