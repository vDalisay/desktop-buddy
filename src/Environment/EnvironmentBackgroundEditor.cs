using System;
using System.Collections.Generic;
using DesktopBuddy.Domain.Environment;
using DesktopBuddy.Persistence;
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
    private PanelContainer _confirm = null!;
    private byte[]? _baseline;
    private bool _painting;
    private bool _saving;

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

    public override void _UnhandledInput(InputEvent input)
    {
        if (!IsOpen || input is not InputEventKey { Pressed: true, Echo: false } key) return;
        switch (key.Keycode)
        {
            case Key.Escape: RequestClose(); break;
            case Key.B: SelectTool(EnvironmentPaintTool.Brush); break;
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

        _panel = Win98Dialog.Create("PaintBackgroundPanel", "Paint Background", new Vector2(430, 330), out VBoxContainer body, RequestClose);
        _blocker.AddChild(_panel);
        _panel.Visible = true;

        var columns = new HBoxContainer();
        columns.AddThemeConstantOverride("separation", 12);
        body.AddChild(columns);
        columns.AddChild(ToolColumn("Tools",
            ("Brush  [B]", () => SelectTool(EnvironmentPaintTool.Brush)),
            ("Fill color  [F]", () => SelectTool(EnvironmentPaintTool.Fill))));
        var shapes = new MenuButton
        {
            Name = "PaintShapesButton",
            Text = "Shapes  ▸",
            Flat = false,
            CustomMinimumSize = new Vector2(150, 28),
            TooltipText = "Click and drag to draw a shape.",
        };
        PopupMenu shapeMenu = shapes.GetPopup();
        Win98MenuStyle.Apply(shapeMenu);
        shapeMenu.AddItem("Square", (int)EnvironmentPaintTool.Square);
        shapeMenu.AddItem("Circle", (int)EnvironmentPaintTool.Circle);
        shapeMenu.AddItem("Straight Line", (int)EnvironmentPaintTool.Line);
        shapeMenu.IdPressed += id => SelectTool((EnvironmentPaintTool)id);
        ((VBoxContainer)columns.GetChild(0)).AddChild(shapes);

        columns.AddChild(ToolColumn("Brush",
            ("Eraser  [E]", () => SelectTool(EnvironmentPaintTool.Eraser)),
            ("Pick Color  [I]", () => SelectTool(EnvironmentPaintTool.PickColor))));
        _undo = new Button { Name = "PaintUndoButton", Text = "Undo  [Ctrl+Z]", CustomMinimumSize = new Vector2(150, 28) };
        _undo.Pressed += UndoStroke;
        ((VBoxContainer)columns.GetChild(1)).AddChild(_undo);

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
            CustomMinimumSize = new Vector2(38, 28),
            EditAlpha = false,
            TooltipText = "Choose a custom color.",
        };
        _picker.ColorChanged += SelectColor;
        paletteRow.AddChild(_picker);

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

    private static VBoxContainer ToolColumn(string title, params (string Text, Action Pressed)[] buttons)
    {
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 4);
        column.AddChild(new Label { Text = title });
        foreach ((string text, Action pressed) in buttons)
        {
            var button = new Button { Name = $"Paint{text.Split(' ')[0]}Button", Text = text, CustomMinimumSize = new Vector2(150, 28) };
            button.Pressed += pressed;
            column.AddChild(button);
        }
        return column;
    }

    private void RebuildSwatches()
    {
        foreach (Node child in _swatchGrid.GetChildren()) child.QueueFree();
        foreach (Color swatch in _swatches)
        {
            var cell = new ColorRect { Color = swatch, CustomMinimumSize = new Vector2(20, 18), MouseFilter = Control.MouseFilterEnum.Stop };
            cell.GuiInput += input =>
            {
                if (input is not InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true }) return;
                SelectColor(swatch);
                cell.AcceptEvent();
            };
            _swatchGrid.AddChild(cell);
        }
    }

    private void AddCustomSwatch()
    {
        if (_swatches.Count >= MaximumSwatches) { _status.Text = "The palette is full; replace a color instead."; return; }
        _swatches.Add(_picker.Color);
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
        _status.Text = tool switch
        {
            EnvironmentPaintTool.Brush => "Brush: drag anywhere on the background to paint.",
            EnvironmentPaintTool.Eraser => "Eraser: drag to restore the blank background.",
            EnvironmentPaintTool.Fill => "Fill color: click an area to flood it.",
            EnvironmentPaintTool.PickColor => "Pick Color: click the background to take its color.",
            EnvironmentPaintTool.Square => "Square: drag to define the shape.",
            EnvironmentPaintTool.Circle => "Circle: drag to define the shape.",
            _ => "Straight Line: drag from one end to the other.",
        };
        Refresh();
    }

    /// <summary>
    /// Paint input. The window hides for the duration of a drag so it never covers the part of the
    /// room being painted, and comes back when the button is released.
    /// </summary>
    private void OnCanvasInput(InputEvent input)
    {
        if (_confirm.Visible) { _blocker.AcceptEvent(); return; }
        switch (input)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } click when TryCanonical(click.Position, out double x, out double y):
                _painting = true;
                _panel.Visible = false;
                Canvas.Begin(x, y);
                SyncPickedColor();
                break;
            case InputEventMouseMotion motion when _painting && TryCanonical(motion.Position, out double x, out double y):
                Canvas.Continue(x, y);
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false } release when _painting:
                if (TryCanonical(release.Position, out double endX, out double endY)) Canvas.End(endX, endY);
                else Canvas.End(double.NaN, double.NaN);
                _painting = false;
                _panel.Visible = true;
                SyncPickedColor();
                Refresh();
                break;
        }
        _blocker.AcceptEvent();
    }

    private void SyncPickedColor()
    {
        if (Canvas.Tool != EnvironmentPaintTool.PickColor) return;
        Color picked = Color.Color8(Canvas.Color.Red, Canvas.Color.Green, Canvas.Color.Blue);
        _current.Color = picked;
        _picker.Color = picked;
    }

    private bool TryCanonical(Vector2 screen, out double x, out double y)
    {
        Rect2 room = EnvironmentRoomRect.Resolve(this);
        x = 0;
        y = 0;
        if (room.Size.X <= 0 || room.Size.Y <= 0) return false;
        x = Math.Clamp((screen.X - room.Position.X) / room.Size.X, 0, 1);
        y = Math.Clamp((screen.Y - room.Position.Y) / room.Size.Y, 0, 1);
        return true;
    }

    private void UndoStroke()
    {
        _status.Text = Canvas.Undo() ? "Undid the last change." : "Nothing left to undo.";
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
        if (Canvas.IsDirty) { _confirm.Visible = true; return; }
        Close();
    }

    private void Discard()
    {
        if (_baseline is not null) Canvas.Replace(_baseline);
        Close();
    }

    private void Close()
    {
        _confirm.Visible = false;
        _blocker.Visible = false;
        _panel.Visible = true;
        _painting = false;
    }

    private void Refresh()
    {
        _dirty.Text = Canvas.IsDirty ? "Unsaved changes" : "No unsaved changes";
        _undo.Disabled = !Canvas.CanUndo;
    }
}
