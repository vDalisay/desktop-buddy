using DesktopBuddy.Onboarding;
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
    private DesktopBuddy.Economy.EconomyService? _economy;
    private readonly List<Color> _swatches = [.. DefaultSwatches];
    private readonly Dictionary<EnvironmentPaintTool, Button> _toolButtons = [];
    private Control _blocker = null!;
    private PanelContainer _panel = null!;
    private Win98PinnablePanel _panelPin = null!;
    private PanelContainer _palettePanel = null!;
    private GridContainer _swatchGrid = null!;
    private ColorRect _current = null!;
    private ColorPickerButton _picker = null!;
    private Button _undo = null!;
    private MenuButton _shapes = null!;
    private Label _status = null!;
    private Label _brushSize = null!;
    private EnvironmentPaintCursor _cursor = null!;
    private EnvironmentCurveGuideOverlay _curveGuide = null!;
    private PanelContainer _confirm = null!;
    private byte[]? _baseline;
    private bool _painting;
    private bool _saving;
    private int _selectedSwatch = -1;
    private double _sprayPulseAccumulator;
    private Vector2? _curveStart;
    private Vector2? _curveEnd;
    private Vector2? _curveFirstBend;
    private Vector2? _curveSecondBend;

    public bool IsOpen => GodotObject.IsInstanceValid(_blocker) && _blocker.Visible;
    private EnvironmentCanvas Canvas => _presenter.Canvas;
    internal bool PanelVisibleForTest => _panel.Visible;

    /// <param name="economy">
    /// Optional: when the composition has an economy, a finished session pays for what was
    /// painted. Scenarios that compose the editor alone simply leave it out.
    /// </param>
    public void Configure(
        EnvironmentBackgroundPresenter presenter,
        EnvironmentPaintStore store,
        DesktopBuddy.Economy.EconomyService? economy = null)
    {
        _presenter = presenter ?? throw new ArgumentNullException(nameof(presenter));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _economy = economy;
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
        if (key.Keycode == Key.Delete && _selectedSwatch >= 0 && TutorialInputGate.AllowsPaletteEditing)
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
                    ClearCurveGuide();
                    _painting = false;
                    _panel.Visible = true;
                    SetStatus("Curved Line cancelled.");
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
        ClearCurveGuide();
        SelectTool(EnvironmentPaintTool.Brush);
        Refresh();
    }

    private void Build()
    {
        _blocker = new Control { Name = "EnvironmentBackgroundInputBlocker", Visible = false, MouseFilter = Control.MouseFilterEnum.Stop };
        AddChild(_blocker);
        _blocker.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _blocker.GuiInput += OnCanvasInput;

        _curveGuide = new EnvironmentCurveGuideOverlay
        {
            Name = "EnvironmentCurveGuideOverlay",
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Visible = false,
        };
        _blocker.AddChild(_curveGuide);
        _curveGuide.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

        _cursor = new EnvironmentPaintCursor { Name = "EnvironmentPaintCursor", MouseFilter = Control.MouseFilterEnum.Ignore };
        _blocker.AddChild(_cursor);

        // Compact: the panel used to reserve room for two status lines that are gone, and its
        // sections now carry their own frames and margins (owner instruction 2026-08-21).
        _panel = Win98Dialog.Create("PaintBackgroundPanel", "Paint Background", new Vector2(452, 392), out VBoxContainer body, RequestClose, draggable: false);
        _blocker.AddChild(_panel);
        _panel.Visible = true;
        _panelPin = new Win98PinnablePanel { Name = "PaintBackgroundPinController" };
        AddChild(_panelPin);
        _panelPin.Configure(_panel, new Vector2I(492, 452), "PaintBackgroundWindow");

        var toolsGroup = new Win98GroupBox { Name = "PaintToolsGroup" };
        toolsGroup.Configure("Tools");
        body.AddChild(toolsGroup);
        var grid = new GridContainer { Name = "PaintToolGrid", Columns = 4 };
        grid.AddThemeConstantOverride("h_separation", Win98ThemeFactory.Gap);
        grid.AddThemeConstantOverride("v_separation", Win98ThemeFactory.Gap);
        toolsGroup.Content.AddChild(grid);
        grid.AddChild(ToolButton("PaintBrushButton", "Brush  [B]", EnvironmentPaintTool.Brush));
        grid.AddChild(ToolButton("PaintPenButton", "Pen  [P]", EnvironmentPaintTool.Pen));
        grid.AddChild(ToolButton("PaintSprayButton", "Spray  [S]", EnvironmentPaintTool.Spray));
        grid.AddChild(ToolButton("PaintFillButton", "Bucket Fill  [F]", EnvironmentPaintTool.Fill));
        grid.AddChild(ToolButton("PaintEraserButton", "Eraser  [E]", EnvironmentPaintTool.Eraser));
        grid.AddChild(ToolButton("PaintPickButton", "Pick  [I]", EnvironmentPaintTool.PickColor));

        _shapes = new MenuButton
        {
            Name = "PaintShapesButton",
            Text = "Shapes  ▸",
            Flat = false,
            ToggleMode = true,
            CustomMinimumSize = new Vector2(104, 28),
            TooltipText = "Draw Square, Circle, Straight Line, or Curved Line.",
        };
        PopupMenu shapeMenu = _shapes.GetPopup();
        Win98MenuStyle.Apply(shapeMenu);
        shapeMenu.AddItem("Square", (int)EnvironmentPaintTool.Square);
        shapeMenu.AddItem("Circle", (int)EnvironmentPaintTool.Circle);
        shapeMenu.AddItem("Straight Line", (int)EnvironmentPaintTool.Line);
        shapeMenu.AddItem("Curved Line  [C]", (int)EnvironmentPaintTool.CurvedLine);
        shapeMenu.IdPressed += id => SelectTool((EnvironmentPaintTool)id);
        grid.AddChild(_shapes);

        _undo = new Button { Name = "PaintUndoButton", Text = "Undo  [Ctrl+Z]", CustomMinimumSize = new Vector2(104, 28) };
        _undo.Pressed += UndoStroke;
        grid.AddChild(_undo);

        var sizeGroup = new Win98GroupBox { Name = "PaintBrushSizeGroup" };
        sizeGroup.Configure("Brush Size");
        body.AddChild(sizeGroup);
        var size = new HBoxContainer { Name = "PaintBrushSizeRow" };
        size.AddThemeConstantOverride("separation", Win98ThemeFactory.Gap);
        size.AddChild(SizeButton("−", -1));
        _brushSize = new Label { HorizontalAlignment = HorizontalAlignment.Center, CustomMinimumSize = new Vector2(54, 28), VerticalAlignment = VerticalAlignment.Center };
        size.AddChild(_brushSize);
        size.AddChild(SizeButton("+", 1));
        sizeGroup.Content.AddChild(size);

        var colorGroup = new Win98GroupBox { Name = "PaintColorGroup" };
        colorGroup.Configure("Colors");
        body.AddChild(colorGroup);
        _palettePanel = new PanelContainer { Name = "PaintBackgroundPalettePanel" };
        colorGroup.Content.AddChild(_palettePanel);
        var inset = new PanelContainer { Name = "PaintPalette" };
        inset.AddThemeStyleboxOverride("panel", Win98ThemeFactory.Recessed(Win98ThemeFactory.Face, 2));
        _palettePanel.AddChild(inset);
        var paletteMargin = new MarginContainer();
        foreach (string side in new[] { "margin_left", "margin_right", "margin_top", "margin_bottom" })
            paletteMargin.AddThemeConstantOverride(side, Win98ThemeFactory.Gap);
        inset.AddChild(paletteMargin);
        var paletteRow = new HBoxContainer();
        paletteRow.AddThemeConstantOverride("separation", Win98ThemeFactory.Gap);
        paletteMargin.AddChild(paletteRow);
        _current = new ColorRect { Name = "PaintBackgroundCurrentColor", CustomMinimumSize = new Vector2(48, 40) };
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

        // The running commentary under the palette is gone (owner instruction 2026-08-21): the
        // tool hints repeated the buttons and their tooltips, and the dirty line repeated what
        // Save and Exit is for. The label stays for the things the player cannot otherwise find
        // out — a failed save, a full palette — and hides itself whenever it has nothing to say.
        _status = new Label
        {
            Name = "PaintToolStatus",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            Visible = false,
        };
        body.AddChild(_status);

        var actions = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        body.AddChild(actions);
        Win98Dialog.Action(actions, "Reset", Reset).Name = "PaintResetButton";
        Win98Dialog.Action(actions, "Save and Exit", Save).Name = "PaintSaveButton";
        Win98Dialog.Action(actions, "Cancel", RequestClose).Name = "PaintCancelButton";

        _confirm = Win98Dialog.Create("BackgroundUnsavedDialog", "Unsaved Background", new Vector2(360, 170), out VBoxContainer confirmBody);
        _blocker.AddChild(_confirm);
        confirmBody.AddChild(new Label { Text = "Save the painted background before closing?", AutowrapMode = TextServer.AutowrapMode.WordSmart });
        confirmBody.AddChild(new Control { Name = "BackgroundUnsavedSpacer", SizeFlagsVertical = Control.SizeFlags.ExpandFill });
        var confirmActions = new HBoxContainer { Name = "BackgroundUnsavedActions", Alignment = BoxContainer.AlignmentMode.End };
        confirmBody.AddChild(confirmActions);
        Win98Dialog.Action(confirmActions, "Save and Exit", Save).Name = "PaintConfirmSaveButton";
        Win98Dialog.Action(confirmActions, "Discard", Discard).Name = "PaintDiscardButton";
        Win98Dialog.Action(confirmActions, "Keep Editing", () => _confirm.Visible = false).Name = "PaintKeepEditingButton";
    }

    private Button ToolButton(string name, string text, EnvironmentPaintTool tool)
    {
        var button = new Button
        {
            Name = name,
            Text = text,
            ToggleMode = true,
            CustomMinimumSize = new Vector2(104, 28),
        };
        button.Pressed += () => SelectTool(tool);
        _toolButtons[tool] = button;
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
        // A themed panel rather than a ColorRect: a ColorRect's colour is a snapshot, and this
        // one sat behind the paint bucket icon in the old grey after a palette change.
        var background = new Panel { MouseFilter = Control.MouseFilterEnum.Ignore };
        background.AddThemeStyleboxOverride("panel", Win98ThemeFactory.Flat(Win98ThemeFactory.Face));
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
                // Same rule as the character palette: while a prompt is asking for a colour,
                // the swatches can be picked but not edited away.
                if (input is InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: true } &&
                    TutorialInputGate.AllowsPaletteEditing)
                {
                    RemoveSwatch(captured);
                }
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
        if (_swatches.Count >= MaximumSwatches)
        {
            SetStatus("The palette is full; replace a color instead.");
            return;
        }
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
        if (tool != EnvironmentPaintTool.CurvedLine)
            ClearCurveGuide();
        Canvas.Tool = tool;
        _sprayPulseAccumulator = 0;
        SetStatus(string.Empty);
        Refresh();
    }

    private void OnCanvasInput(InputEvent input)
    {
        if (input is InputEventMouse mouse)
        {
            if (_confirm.Visible && _confirm.GetGlobalRect().HasPoint(mouse.GlobalPosition))
                return;
            if (_panel.Visible && _panel.GetGlobalRect().HasPoint(mouse.GlobalPosition))
                return;
        }
        if (_confirm.Visible)
        {
            _blocker.AcceptEvent();
            return;
        }
        switch (input)
        {
            case InputEventMouseMotion motion:
                UpdateCursor(motion.Position);
                if (_painting && Canvas.Tool == EnvironmentPaintTool.PickColor)
                    PickColor(motion.Position);
                else if (_painting && TryCanonical(motion.Position, out double moveX, out double moveY))
                {
                    Canvas.Continue(moveX, moveY);
                    TrackCurveMotion(Canvas.CurvePhase, moveX, moveY);
                }
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: true }:
                if (Canvas.CancelPendingCurve())
                {
                    ClearCurveGuide();
                    _painting = false;
                    _panel.Visible = true;
                    SetStatus("Curved Line cancelled.");
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
                TrackCurvePress(Canvas.CurvePhase, x, y);
                if (Canvas.Tool == EnvironmentPaintTool.PickColor) PickColor(click.Position);
                else Canvas.Begin(x, y);
                UpdateCurveGuide();
                UpdateCursor(click.Position);
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false } release when _painting:
                if (Canvas.Tool != EnvironmentPaintTool.PickColor)
                {
                    if (TryCanonical(release.Position, out double endX, out double endY))
                    {
                        TrackCurveMotion(Canvas.CurvePhase, endX, endY);
                        Canvas.End(endX, endY);
                    }
                    else Canvas.End(double.NaN, double.NaN);
                }
                _painting = false;
                _sprayPulseAccumulator = 0;
                _panel.Visible = true;
                if (!Canvas.CurvePending)
                    ClearCurveGuide();
                else
                    UpdateCurveGuide();
                UpdateCursor(release.Position);
                UpdateCurveStatusAfterRelease();
                Refresh();
                break;
        }
        _blocker.AcceptEvent();
    }

    private void TrackCurvePress(EnvironmentCurvePhase phase, double x, double y)
    {
        if (Canvas.Tool != EnvironmentPaintTool.CurvedLine) return;
        Vector2 point = new((float)x, (float)y);
        switch (phase)
        {
            case EnvironmentCurvePhase.Idle:
                _curveStart = point;
                _curveEnd = point;
                _curveFirstBend = null;
                _curveSecondBend = null;
                break;
            case EnvironmentCurvePhase.AwaitFirstBend:
                _curveFirstBend = point;
                break;
            case EnvironmentCurvePhase.AwaitSecondBend:
                _curveSecondBend = point;
                break;
        }
    }

    private void TrackCurveMotion(EnvironmentCurvePhase phase, double x, double y)
    {
        if (Canvas.Tool != EnvironmentPaintTool.CurvedLine) return;
        Vector2 point = new((float)x, (float)y);
        switch (phase)
        {
            case EnvironmentCurvePhase.BaselineDragging:
                _curveEnd = point;
                break;
            case EnvironmentCurvePhase.FirstBendDragging:
                _curveFirstBend = point;
                break;
            case EnvironmentCurvePhase.SecondBendDragging:
                _curveSecondBend = point;
                break;
        }
        UpdateCurveGuide();
    }

    private void UpdateCurveGuide()
    {
        if (!Canvas.CurvePending || !_curveStart.HasValue || !_curveEnd.HasValue)
        {
            _curveGuide.Visible = false;
            return;
        }
        var points = new List<Vector2>(4) { _curveStart.Value, _curveEnd.Value };
        if (_curveFirstBend.HasValue) points.Add(_curveFirstBend.Value);
        if (_curveSecondBend.HasValue) points.Add(_curveSecondBend.Value);
        _curveGuide.SetGuide(BackgroundRect(), points);
        _curveGuide.Visible = true;
    }

    private void ClearCurveGuide()
    {
        _curveStart = null;
        _curveEnd = null;
        _curveFirstBend = null;
        _curveSecondBend = null;
        if (GodotObject.IsInstanceValid(_curveGuide))
        {
            _curveGuide.Visible = false;
            _curveGuide.QueueRedraw();
        }
    }

    private void UpdateCurveStatusAfterRelease()
    {
        if (Canvas.Tool != EnvironmentPaintTool.CurvedLine) return;
        SetStatus(Canvas.CurvePhase switch
        {
            EnvironmentCurvePhase.AwaitFirstBend => "Curved Line active — endpoint circles mark the baseline. Drag a point on the line for bend 1.",
            EnvironmentCurvePhase.AwaitSecondBend => "Curved Line active — bend 1 is marked. Drag a second point to finish the curve.",
            EnvironmentCurvePhase.Idle => "Curved Line committed. The control points are cleared; drag to start another curve.",
            _ => _status.Text,
        });
    }

    private void PickColor(Vector2 position)
    {
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
        Rect2 background = BackgroundRect();
        float width = Canvas.BrushDiameter * background.Size.X / EnvironmentCanvasPolicy.Size;
        float height = Canvas.BrushDiameter * background.Size.Y / EnvironmentCanvasPolicy.Size;
        _cursor.Diameter = new Vector2(width, height);
        _cursor.Shape = PaintCursorGizmos.ShapeFor(Canvas.Tool);
        _cursor.ShowBrush = IsOpen && Canvas.Tool != EnvironmentPaintTool.PickColor;
        _cursor.ShowSample = _painting && Canvas.Tool == EnvironmentPaintTool.PickColor;
        _cursor.QueueRedraw();
    }

    private bool TryCanonical(Vector2 screen, out double x, out double y)
    {
        Rect2 room = BackgroundRect();
        x = 0;
        y = 0;
        if (room.Size.X <= 0 || room.Size.Y <= 0 || !room.HasPoint(screen)) return false;
        Canvas.PixelAspect = room.Size.X / room.Size.Y;
        x = Math.Clamp((screen.X - room.Position.X) / room.Size.X, 0, 1);
        y = Math.Clamp((screen.Y - room.Position.Y) / room.Size.Y, 0, 1);
        return true;
    }

    private Rect2 BackgroundRect() =>
        _presenter.TryGetScreenRect(out Rect2 rect) ? rect : GetViewport().GetVisibleRect();

    private void UndoStroke()
    {
        bool pendingCurve = Canvas.CurvePending;
        bool changed = Canvas.Undo();
        if (pendingCurve) ClearCurveGuide();
        SetStatus(pendingCurve && changed ? "Curved Line cancelled." : changed ? "Undid the last change." : "Nothing left to undo.");
        Refresh();
    }

    private void Reset()
    {
        Canvas.Reset();
        ClearCurveGuide();
        SetStatus("Background reset to blank.");
        Refresh();
    }

    private async void Save()
    {
        if (_saving) return;
        if (Canvas.CurvePending)
        {
            SetStatus("Finish or cancel the Curved Line before saving and exiting.");
            return;
        }
        _saving = true;
        SetStatus("Saving…");
        try
        {
            await _store.SaveAsync(Canvas.Pixels);
            Canvas.MarkSaved();
            _baseline = Canvas.ClonePixels();
            Close();
        }
        catch (Exception exception)
        {
            SetStatus($"Save failed: {exception.Message}");
            _confirm.Visible = false;
        }
        finally { _saving = false; }
    }

    private void RequestClose()
    {
        if (_saving) return;
        if (Canvas.CancelPendingCurve()) ClearCurveGuide();
        if (Canvas.IsDirty)
        {
            _confirm.Visible = true;
            return;
        }
        Close();
    }

    private void Discard()
    {
        Canvas.CancelPendingCurve();
        ClearCurveGuide();
        if (_baseline is not null) Canvas.Replace(_baseline);
        Close();
    }

    private void Close()
    {
        PayForPaintedRoom();
        _panelPin.Dock();
        Canvas.CancelPendingCurve();
        ClearCurveGuide();
        _confirm.Visible = false;
        _blocker.Visible = false;
        _panel.Visible = true;
        _painting = false;
        _sprayPulseAccumulator = 0;
    }

    /// <summary>
    /// Pays for what this session left on the walls, up to the one-session ceiling. The
    /// baseline the editor already snapshots on Open is what it is measured against, so a
    /// discarded session pays nothing and no session can be collected twice.
    /// </summary>
    private void PayForPaintedRoom()
    {
        if (_economy is null || _baseline is null)
            return;

        long changed = DesktopBuddy.Domain.Painting.PaintSessionPayout.ChangedPixels(
            _baseline, Canvas.Pixels.Span);
        long milli = DesktopBuddy.Domain.Painting.PaintSessionPayout.MilliCredits(changed);
        _baseline = Canvas.ClonePixels();
        LastPaintSessionMilliCredits = milli;
        if (milli <= 0)
            return;

        _economy.DepositPassive(milli);
        SetStatus($"Paid {DesktopBuddy.Ui.ContentDisplayName.Credits(milli)} for this painting.");
    }

    /// <summary>What the last finished room painting paid — the scenario oracle.</summary>
    public long LastPaintSessionMilliCredits { get; private set; }

    /// <summary>The one way the status line is written, so it can hide itself when empty.</summary>
    private void SetStatus(string text)
    {
        if (!GodotObject.IsInstanceValid(_status))
            return;
        _status.Text = text;
        _status.Visible = text.Length > 0;
    }

    private void Refresh()
    {
        _undo.Disabled = !Canvas.CanUndo && !Canvas.CurvePending;
        _brushSize.Text = $"{Canvas.BrushDiameter}px";
        foreach ((EnvironmentPaintTool tool, Button button) in _toolButtons)
            button.ButtonPressed = Canvas.Tool == tool;
        bool shapeActive = IsShape(Canvas.Tool);
        _shapes.ButtonPressed = shapeActive;
        _shapes.Text = shapeActive ? $"{ShapeName(Canvas.Tool)}  ▸" : "Shapes  ▸";
        if (Canvas.CurvePending) UpdateCurveGuide();
    }

    private static bool IsShape(EnvironmentPaintTool tool) => tool is
        EnvironmentPaintTool.Square or EnvironmentPaintTool.Circle or
        EnvironmentPaintTool.Line or EnvironmentPaintTool.CurvedLine;

    private static string ShapeName(EnvironmentPaintTool tool) => tool switch
    {
        EnvironmentPaintTool.Square => "Square",
        EnvironmentPaintTool.Circle => "Circle",
        EnvironmentPaintTool.Line => "Straight Line",
        EnvironmentPaintTool.CurvedLine => "Curved Line",
        _ => "Shapes",
    };

    private sealed partial class EnvironmentPaintCursor : Control
    {
        public Vector2 Diameter { get; set; }
        public PaintCursorShape Shape { get; set; } = PaintCursorShape.Circle;
        public bool ShowBrush { get; set; }
        public bool ShowSample { get; set; }
        public Color SampleColor { get; set; } = Colors.White;

        public override void _Draw()
        {
            if (ShowBrush) PaintCursorGizmos.DrawBrushCursor(this, Vector2.Zero, Diameter, Shape);
            if (ShowSample) PaintCursorGizmos.DrawPickPreview(this, Vector2.Zero, SampleColor);
        }
    }

    private sealed partial class EnvironmentCurveGuideOverlay : Control
    {
        private Rect2 _room;
        private readonly List<Vector2> _points = [];

        public void SetGuide(Rect2 room, IReadOnlyList<Vector2> normalizedPoints)
        {
            _room = room;
            _points.Clear();
            for (int index = 0; index < normalizedPoints.Count; index++)
                _points.Add(normalizedPoints[index]);
            QueueRedraw();
        }

        public override void _Draw()
        {
            Color selection = Win98ThemeFactory.Selection;
            foreach (Vector2 normalized in _points)
            {
                Vector2 point = _room.Position + new Vector2(
                    normalized.X * _room.Size.X,
                    normalized.Y * _room.Size.Y);
                DrawCircle(point, 6f, Colors.White);
                DrawArc(point, 6f, 0, Mathf.Tau, 24, selection, 2f, antialiased: true);
                DrawCircle(point, 2f, selection);
            }
        }
    }
}
