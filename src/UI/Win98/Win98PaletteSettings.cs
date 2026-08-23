using System;
using DesktopBuddy.Shop;
using Godot;

namespace DesktopBuddy.UI.Win98;

/// <summary>
/// The Settings rows that re-colour the interface, and the safety net around them (owner
/// instruction 2026-08-23).
///
/// <para>Picking colours stages them; nothing changes until Apply. Apply shows the new look at
/// once and then asks "Are you sure?" with a ten second countdown — Confirm keeps it, Return
/// puts the previous palette back, and so does letting the timer run out. That is the whole
/// point of the countdown: a palette that turns out to be unreadable cannot lock the player out
/// of the settings that would undo it, because doing nothing undoes it.</para>
///
/// <para>Default is the one exception: the shipped grey, navy and black cannot be unreadable,
/// so it applies and saves straight away.</para>
/// </summary>
public partial class Win98PaletteSettings : Node
{
    /// <summary>Seconds the confirmation waits before putting the old palette back.</summary>
    public const int ConfirmSeconds = 10;

    private const string ColorGroup = "Interface Colors";

    private Action<Win98Palette> _save = static _ => { };
    private Control _dialogHost = null!;
    private ColorPickerButton _facePicker = null!;
    private ColorPickerButton _barPicker = null!;
    private ColorPickerButton _textPicker = null!;
    private PanelContainer? _prompt;
    private Label? _countdown;
    private Timer? _tick;
    private Win98Palette _staged;
    private Win98Palette _previous;
    private int _remaining;

    /// <summary>The palette the pickers currently hold, applied or not.</summary>
    public Win98Palette Staged => _staged;

    /// <summary>Whether the "Are you sure?" prompt is up and the revert timer is running.</summary>
    public bool AwaitingConfirmation => _prompt is not null && _prompt.Visible;

    /// <summary>Seconds left before an unconfirmed palette is put back.</summary>
    public int SecondsRemaining => AwaitingConfirmation ? _remaining : 0;

    /// <summary>
    /// Builds the rows into an existing Settings panel. <paramref name="dialogHost"/> is the
    /// full-rect control the confirmation is centred in; <paramref name="save"/> persists a
    /// palette the player has committed to and is never called for an unconfirmed preview.
    /// </summary>
    public void Compose(SettingsPanel panel, Control dialogHost, Win98Palette current, Action<Win98Palette> save)
    {
        ArgumentNullException.ThrowIfNull(panel);
        ArgumentNullException.ThrowIfNull(dialogHost);
        ArgumentNullException.ThrowIfNull(save);

        ProcessMode = ProcessModeEnum.Always;
        _dialogHost = dialogHost;
        _save = save;
        _staged = current;
        _previous = current;

        _facePicker = panel.AddColor(
            "Window Color",
            "The colour every panel, button and menu is made of. Replaces the classic grey.",
            current.Face,
            color => _staged = _staged with { Face = color },
            ColorGroup);
        _barPicker = panel.AddColor(
            "Bar Color",
            "Title bars, menu highlights and selected rows. Replaces the classic blue.",
            current.Bar,
            color => _staged = _staged with { Bar = color },
            ColorGroup);
        _textPicker = panel.AddColor(
            "Font Color",
            "The colour of ordinary interface text. Replaces black.",
            current.Text,
            color => _staged = _staged with { Text = color },
            ColorGroup);

        panel.AddAction(
            "Apply Colors",
            $"Shows the chosen colours, then asks you to confirm. Without a confirmation they are put back after {ConfirmSeconds} seconds.",
            Apply,
            ColorGroup,
            buttonText: "Apply");
        panel.AddAction(
            "Default Colors",
            "Puts the original grey, blue and black back.",
            RestoreDefaults,
            ColorGroup,
            buttonText: "Default");
    }

    /// <summary>Shows the staged palette and starts the confirmation countdown.</summary>
    public void Apply()
    {
        // Re-applying while a prompt is already up must not overwrite the palette being held
        // for revert, or a second Apply would make the unconfirmed one permanent by accident.
        if (!AwaitingConfirmation)
            _previous = Win98ThemeFactory.Palette;

        Win98ThemeFactory.ApplyPalette(_staged);
        ShowPrompt();
    }

    /// <summary>Keeps the applied palette and writes it down.</summary>
    public void Confirm()
    {
        StopCountdown();
        _previous = Win98ThemeFactory.Palette;
        _save(Win98ThemeFactory.Palette);
    }

    /// <summary>Puts the palette from before the last Apply back, and forgets the preview.</summary>
    public void Revert()
    {
        StopCountdown();
        Win98ThemeFactory.ApplyPalette(_previous);
        _staged = _previous;
        SyncPickers(_previous);
    }

    private void RestoreDefaults()
    {
        StopCountdown();
        _staged = Win98Palette.Default;
        _previous = Win98Palette.Default;
        Win98ThemeFactory.ApplyPalette(Win98Palette.Default);
        SyncPickers(Win98Palette.Default);
        _save(Win98Palette.Default);
    }

    private void SyncPickers(Win98Palette palette)
    {
        if (GodotObject.IsInstanceValid(_facePicker))
            _facePicker.Color = palette.Face;
        if (GodotObject.IsInstanceValid(_barPicker))
            _barPicker.Color = palette.Bar;
        if (GodotObject.IsInstanceValid(_textPicker))
            _textPicker.Color = palette.Text;
    }

    private void ShowPrompt()
    {
        _prompt ??= BuildPrompt();
        _remaining = ConfirmSeconds;
        UpdateCountdown();
        _prompt.Show();
        _prompt.MoveToFront();
        _tick!.Start();
    }

    private PanelContainer BuildPrompt()
    {
        PanelContainer prompt = Win98Dialog.Create(
            "UiPaletteConfirmPrompt",
            "Keep these colors?",
            new Vector2(340, 170),
            out VBoxContainer body,
            onClose: Revert);

        body.AddChild(new Label
        {
            Name = "UiPaletteConfirmQuestion",
            Text = "Are you sure?",
            HorizontalAlignment = HorizontalAlignment.Center,
        });
        _countdown = new Label
        {
            Name = "UiPaletteConfirmCountdown",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        body.AddChild(_countdown);

        var buttons = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        body.AddChild(buttons);
        Win98Dialog.Action(buttons, "Confirm", Confirm).Name = "UiPaletteConfirmButton";
        Win98Dialog.Action(buttons, "Return", Revert).Name = "UiPaletteReturnButton";

        // One second per tick rather than a countdown read off _Process: the label only ever
        // shows whole seconds, and the timer keeps running while the tree is paused for an
        // editor, which is exactly when the player is looking at this prompt.
        _tick = new Timer { WaitTime = 1.0, Autostart = false, ProcessMode = ProcessModeEnum.Always };
        _tick.Timeout += OnTick;
        prompt.AddChild(_tick);
        _dialogHost.AddChild(prompt);
        return prompt;
    }

    private void OnTick()
    {
        _remaining--;
        if (_remaining <= 0)
        {
            Revert();
            return;
        }
        UpdateCountdown();
    }

    private void UpdateCountdown()
    {
        if (GodotObject.IsInstanceValid(_countdown))
            _countdown!.Text = $"Reverting in {_remaining}s.";
    }

    private void StopCountdown()
    {
        _tick?.Stop();
        _prompt?.Hide();
        _remaining = 0;
    }
}
