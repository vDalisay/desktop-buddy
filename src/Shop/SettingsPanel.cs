using System;
using System.Collections.Generic;
using DesktopBuddy.App;
using DesktopBuddy.Ui;
using DesktopBuddy.UI;
using DesktopBuddy.UI.Win98;
using Godot;

namespace DesktopBuddy.Shop;

/// <summary>
/// The Settings window: one row per action, in the same chrome as the shop and tool picker.
/// It owns no settings logic — each row is a label and a button the composition root supplies,
/// so the FR-003.2 entries (Character Editor today, Reset Progress next) stay where they live.
/// </summary>
public partial class SettingsPanel : PanelContainer
{
    private VBoxContainer _list = null!;
    private Label _status = null!;
    private Label _description = null!;

    private const string DefaultDescription =
        "Hover any setting to see what it does.";

    public bool IsInitialized { get; private set; }

    /// <summary>
    /// One row, wired so its authored description reaches the footer. Hover is hooked on both
    /// the row and its control: a Stop-filtered control takes the pick from its own parent, so
    /// hooking only the row would blank the text the moment the cursor reached the checkbox.
    /// </summary>
    private HBoxContainer DescribedRow(
        string? group,
        string label,
        Label value,
        Control control,
        string description)
    {
        HBoxContainer line = PanelChrome.Row(Group(group), label, value, control);
        line.MouseEntered += () => ShowDescription(description);
        control.MouseEntered += () => ShowDescription(description);
        return line;
    }

    /// <summary>Last row hovered wins, and it stays put on the way out.</summary>
    private void ShowDescription(string description)
    {
        if (!string.IsNullOrWhiteSpace(description))
            _description.Text = description;
    }

    public void Configure()
    {
        Name = "SettingsPanel";
        PanelChrome.Parts parts = PanelChrome.Build(this, "SettingsActionList");
        _list = parts.List;
        _status = parts.Status;
        _description = parts.Description;
        _description.Text = DefaultDescription;
        VisibilityChanged += OnVisibilityChanged;
        IsInitialized = true;
    }

    /// <summary>
    /// The rows of one named group box, created on first use so groups appear in the order
    /// their first row is added.
    /// </summary>
    private VBoxContainer Group(string? caption)
    {
        if (caption is null)
            return _list;
        if (_groups.TryGetValue(caption, out Win98GroupBox? existing))
            return existing.Content;

        var group = new Win98GroupBox { Name = $"Settings{caption.Replace(" ", string.Empty, StringComparison.Ordinal)}Group" };
        group.Configure(caption);
        _list.AddChild(group);
        _groups.Add(caption, group);
        return group.Content;
    }

    /// <summary>Adds one labelled action row and returns its button.</summary>
    public Button AddAction(
        string label,
        string description,
        Action pressed,
        string? group = null,
        string buttonText = "Open")
    {
        ArgumentNullException.ThrowIfNull(pressed);
        var button = new Button { Text = buttonText };
        button.Pressed += pressed;
        DescribedRow(group, label, new Label(), button, description);
        button.TooltipText = description;
        _actions.Add(label, button);
        return button;
    }

    /// <summary>
    /// A 0–1 slider row showing its value as a percentage. <paramref name="changed"/> fires on
    /// every step so the change is audible while dragging; <paramref name="committed"/> fires
    /// once the control is released, so a drag is one save rather than twenty.
    /// </summary>
    public HSlider AddSlider(
        string label,
        string description,
        float value,
        Action<float> changed,
        Action? committed = null,
        string? group = null)
    {
        ArgumentNullException.ThrowIfNull(changed);
        var readout = new Label();
        var slider = new HSlider
        {
            Name = ControlName(label),
            MinValue = 0.0,
            MaxValue = 1.0,
            Step = 0.05,
            Value = value,
            TooltipText = description,
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
        };
        readout.Text = Percent(value);
        slider.ValueChanged += changedValue =>
        {
            readout.Text = Percent((float)changedValue);
            UiFeedbackAudioBootstrap.TryPlaySliderTick(this);
            changed((float)changedValue);
        };
        if (committed is not null)
            slider.DragEnded += _ => committed();

        DescribedRow(group, label, readout, slider, description);
        slider.CustomMinimumSize = new Vector2(Win98ThemeFactory.Px(140), 0);
        _controls.Add(label, slider);
        return slider;
    }

    /// <summary>An on/off row: a period square check field, not a modern switch.</summary>
    public CheckBox AddToggle(
        string label,
        string description,
        bool value,
        Action<bool> changed,
        string? group = null)
    {
        ArgumentNullException.ThrowIfNull(changed);
        var toggle = new CheckBox
        {
            Name = ControlName(label),
            ButtonPressed = value,
            TooltipText = description,
        };
        toggle.Toggled += pressed => changed(pressed);
        DescribedRow(group, label, new Label(), toggle, description);
        _controls.Add(label, toggle);
        return toggle;
    }

    /// <summary>A row that picks one of a fixed set of values.</summary>
    public OptionButton AddChoice(
        string label,
        string description,
        IReadOnlyList<string> options,
        int selected,
        Action<int> changed,
        string? group = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(changed);
        var choice = new OptionButton
        {
            Name = ControlName(label),
            TooltipText = description,
        };
        foreach (string option in options)
            choice.AddItem(option);
        choice.Selected = Math.Clamp(selected, 0, options.Count - 1);
        choice.ItemSelected += index => changed((int)index);
        Win98MenuStyle.Apply(choice.GetPopup());
        DescribedRow(group, label, new Label(), choice, description);
        _controls.Add(label, choice);
        return choice;
    }

    /// <summary>
    /// A rebind row: pressing the button listens for a complete chord. Bare modifiers keep the
    /// capture active and Escape restores this row's own previous chord, so switching between
    /// multiple hotkey rows can never leak one shortcut into another.
    /// </summary>
    public Button AddHotkey(
        string label,
        string description,
        string chord,
        Action<string> changed,
        string? group = null)
    {
        ArgumentNullException.ThrowIfNull(changed);
        var button = new Button { Name = ControlName(label), Text = chord, TooltipText = description };
        button.Pressed += () => BeginHotkeyCapture(button, label, changed);
        DescribedRow(group, label, new Label(), button, description);
        _controls.Add(label, button);
        return button;
    }

    public override void _Input(InputEvent @event)
    {
        if (_capturing is null || @event is not InputEventKey { Pressed: true, Echo: false } key)
            return;

        GetViewport().SetInputAsHandled();
        if (key.Keycode == Key.Escape)
        {
            CancelHotkeyCapture("Shortcut change cancelled.");
            return;
        }

        // Pressing Ctrl/Shift/Alt is part of entering a chord, not a failed binding attempt.
        if (!HotkeyBinding.IsCompleteChord(key))
        {
            _status.Text = $"Press the main key for {_captureLabel}; Escape cancels.";
            return;
        }

        Button button = _capturing;
        Action<string>? callback = _captureCallback;
        string label = _captureLabel ?? "shortcut";
        string chord = HotkeyBinding.Format(key);
        ClearHotkeyCapture();
        button.Text = chord;
        callback?.Invoke(chord);
        _status.Text = $"{label}: {chord}.";
    }

    /// <summary>The action button for one row (test observability).</summary>
    public Button? ActionFor(string label) =>
        _actions.TryGetValue(label, out Button? button) ? button : null;

    /// <summary>The slider, toggle, or choice control for one row (test observability).</summary>
    public Control? ControlFor(string label) =>
        _controls.TryGetValue(label, out Control? control) ? control : null;

    private void BeginHotkeyCapture(Button button, string label, Action<string> changed)
    {
        if (_capturing is not null)
            CancelHotkeyCapture();
        _capturing = button;
        _captureCallback = changed;
        _captureOriginalChord = button.Text;
        _captureLabel = label;
        button.Text = "Press keys...";
        button.GrabFocus();
        _status.Text = $"Press a shortcut for {label}; Escape cancels.";
    }

    private void CancelHotkeyCapture(string? status = null)
    {
        if (_capturing is not null && _captureOriginalChord is not null)
            _capturing.Text = _captureOriginalChord;
        ClearHotkeyCapture();
        if (status is not null && GodotObject.IsInstanceValid(_status))
            _status.Text = status;
    }

    private void ClearHotkeyCapture()
    {
        _capturing = null;
        _captureCallback = null;
        _captureOriginalChord = null;
        _captureLabel = null;
    }

    private void OnVisibilityChanged()
    {
        if (!IsVisibleInTree())
            CancelHotkeyCapture();
    }

    private static string ControlName(string label) =>
        $"Settings{label.Replace(" ", string.Empty, StringComparison.Ordinal)}Control";

    private static string Percent(float value) => $"{Mathf.RoundToInt(value * 100.0f)}%";

    private readonly Dictionary<string, Button> _actions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Control> _controls = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Win98GroupBox> _groups = new(StringComparer.Ordinal);
    private Button? _capturing;
    private Action<string>? _captureCallback;
    private string? _captureOriginalChord;
    private string? _captureLabel;
}