using System;
using DesktopBuddy.Domain.Environment;
using DesktopBuddy.Persistence;
using DesktopBuddy.UI.Win98;
using Godot;

namespace DesktopBuddy.Environment;

public partial class EnvironmentBackgroundEditor : CanvasLayer
{
    private static readonly Color[] Swatches =
    [
        Color.Color8(192, 192, 192), Color.Color8(128, 160, 192), Color.Color8(212, 190, 148),
        Color.Color8(158, 188, 148), Color.Color8(170, 150, 190), Color.Color8(76, 84, 96),
    ];

    private EnvironmentProgressState _state = null!;
    private SaveCoordinator _saves = null!;
    private EnvironmentBackgroundPresenter _presenter = null!;
    private EnvironmentBackgroundEditSession? _session;
    private Control _blocker = null!;
    private PanelContainer _panel = null!;
    private OptionButton _zone = null!;
    private ColorPickerButton _picker = null!;
    private Label _dirty = null!;
    private Label _status = null!;
    private PanelContainer _confirm = null!;
    private bool _saving;

    public bool IsOpen => GodotObject.IsInstanceValid(_blocker) && _blocker.Visible;

    public void Configure(EnvironmentProgressState state, SaveCoordinator saves, EnvironmentBackgroundPresenter presenter)
    {
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _saves = saves ?? throw new ArgumentNullException(nameof(saves));
        _presenter = presenter ?? throw new ArgumentNullException(nameof(presenter));
    }

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        Layer = 120;
        Build();
        _state.Changed += OnStateChanged;
        _presenter.Apply(_state.Layout.Background);
    }

    public override void _ExitTree() => _state.Changed -= OnStateChanged;

    public override void _UnhandledInput(InputEvent input)
    {
        if (!IsOpen || input is not InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape }) return;
        RequestClose();
        GetViewport().SetInputAsHandled();
    }

    public void Open()
    {
        if (_saving || IsOpen) return;
        _session = new EnvironmentBackgroundEditSession(_state.Layout);
        _blocker.Visible = true;
        _panel.Visible = true;
        _confirm.Visible = false;
        _status.Text = "Choose a room zone, then pick a color.";
        SetZone(EnvironmentBackgroundZone.Wall);
        Refresh();
        _zone.GrabFocus();
    }

    private void Build()
    {
        _blocker = new Control { Name = "EnvironmentBackgroundInputBlocker", Visible = false, MouseFilter = Control.MouseFilterEnum.Stop };
        AddChild(_blocker);
        _blocker.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _blocker.GuiInput += input => _blocker.AcceptEvent();

        _panel = Win98Dialog.Create("PaintBackgroundPanel", "Paint Background", new Vector2(410, 330), out VBoxContainer body, RequestClose);
        _blocker.AddChild(_panel);
        _panel.Visible = true;

        body.AddChild(new Label { Text = "Room zone" });
        _zone = new OptionButton { Name = "BackgroundZoneSelector", CustomMinimumSize = new Vector2(180, 30) };
        _zone.AddItem("Wall", (int)EnvironmentBackgroundZone.Wall);
        _zone.AddItem("Floor", (int)EnvironmentBackgroundZone.Floor);
        _zone.ItemSelected += index => SetZone((EnvironmentBackgroundZone)_zone.GetItemId((int)index));
        body.AddChild(_zone);

        body.AddChild(new Label { Text = "Color" });
        _picker = new ColorPickerButton { Name = "BackgroundColorPicker", CustomMinimumSize = new Vector2(180, 42), EditAlpha = false, TooltipText = "Choose the selected zone color." };
        _picker.ColorChanged += OnColorChanged;
        body.AddChild(_picker);

        var swatchRow = new HBoxContainer();
        swatchRow.AddThemeConstantOverride("separation", 6);
        body.AddChild(swatchRow);
        foreach (Color swatch in Swatches)
        {
            var button = new ColorRect { Color = swatch, CustomMinimumSize = new Vector2(34, 26), MouseFilter = Control.MouseFilterEnum.Stop };
            button.GuiInput += input =>
            {
                if (input is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true })
                {
                    _picker.Color = swatch;
                    OnColorChanged(swatch);
                    button.AcceptEvent();
                }
            };
            swatchRow.AddChild(button);
        }

        _dirty = new Label { Text = "No unsaved changes" };
        body.AddChild(_dirty);
        _status = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        body.AddChild(_status);

        var actions = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        body.AddChild(actions);
        Win98Dialog.Action(actions, "Reset", Reset);
        Win98Dialog.Action(actions, "Save", Save);
        Win98Dialog.Action(actions, "Cancel", RequestClose);

        _confirm = Win98Dialog.Create("BackgroundUnsavedDialog", "Unsaved Background", new Vector2(360, 170), out VBoxContainer confirmBody);
        _blocker.AddChild(_confirm);
        confirmBody.AddChild(new Label { Text = "Save the background changes before closing?", AutowrapMode = TextServer.AutowrapMode.WordSmart });
        var confirmActions = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        confirmBody.AddChild(confirmActions);
        Win98Dialog.Action(confirmActions, "Save", Save);
        Win98Dialog.Action(confirmActions, "Discard", Discard);
        Win98Dialog.Action(confirmActions, "Keep Editing", () => _confirm.Visible = false);
    }

    private void SetZone(EnvironmentBackgroundZone zone)
    {
        if (_session is null) return;
        int index = _zone.GetItemIndex((int)zone);
        if (index >= 0) _zone.Select(index);
        EnvironmentColor color = _session.Working.ColorFor(zone);
        _picker.Color = Color.Color8(color.Red, color.Green, color.Blue, color.Alpha);
    }

    private void OnColorChanged(Color color)
    {
        if (_session is null) return;
        var zone = (EnvironmentBackgroundZone)_zone.GetSelectedId();
        _session.SetColor(zone, new EnvironmentColor((byte)color.R8, (byte)color.G8, (byte)color.B8));
        _presenter.Apply(_session.Working);
        Refresh();
    }

    private void Reset()
    {
        if (_session is null) return;
        _session.Reset();
        SetZone((EnvironmentBackgroundZone)_zone.GetSelectedId());
        _presenter.Apply(_session.Working);
        Refresh();
    }

    private async void Save()
    {
        if (_session is null || _saving) return;
        _saving = true;
        _status.Text = "Saving…";
        try
        {
            await _saves.CommitBackgroundAsync(_session);
            _session = new EnvironmentBackgroundEditSession(_state.Layout);
            _presenter.Apply(_state.Layout.Background);
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
        if (_saving || _session is null) return;
        if (_session.IsDirty) { _confirm.Visible = true; return; }
        Close();
    }

    private void Discard()
    {
        _session?.Cancel();
        _presenter.Apply(_state.Layout.Background);
        Close();
    }

    private void Close()
    {
        _confirm.Visible = false;
        _blocker.Visible = false;
        _session = null;
    }

    private void Refresh() => _dirty.Text = _session?.IsDirty == true ? "Unsaved changes" : "No unsaved changes";
    private void OnStateChanged() { if (!IsOpen) _presenter.Apply(_state.Layout.Background); }
}
