using System;
using DesktopBuddy.Domain.Platform;
using Godot;

namespace DesktopBuddy.CharacterEditor;

/// <summary>
/// Extends the existing horizontal dock and Settings window with the explicit interaction
/// toggle and compact/full-screen overlay choice. It intentionally reuses the production dock
/// surface rather than introducing a second settings architecture.
/// </summary>
public partial class CharacterEditorHost
{
    private bool _workPlayControlsComposed;

    public Button InteractionModeButton { get; private set; } = null!;
    public Button WindowLayoutButton { get; private set; } = null!;

    public override void _Process(double delta)
    {
        if (!_workPlayControlsComposed && IsInitialized)
            ComposeWorkPlayControls();
    }

    private void ComposeWorkPlayControls()
    {
        if (_workPlayControlsComposed ||
            !GodotObject.IsInstanceValid(SettingsButton) ||
            !GodotObject.IsInstanceValid(_settingsPanel))
        {
            return;
        }

        InteractionModeButton = new Button
        {
            Name = "DockInteractionModeButton",
            FocusMode = Control.FocusModeEnum.None,
        };
        InteractionModeButton.Pressed += _sandbox.Shell.ToggleInteractionMode;
        SettingsButton.GetParent().AddChild(InteractionModeButton);

        WindowLayoutButton = _settingsPanel.AddAction(
            "Window Layout",
            "Switch between the compact buddy box and a transparent full-screen overlay.",
            async () =>
            {
                bool changed = await _sandbox.Shell.ToggleWindowLayoutAsync();
                if (!changed)
                {
                    _status.Text =
                        "Full-screen overlay requires per-pixel transparency on this display.";
                }
                UpdateWorkPlayLabels();
            });
        WindowLayoutButton.Name = "WindowLayoutToggleButton";

        _sandbox.Shell.InputModeChanged += OnInteractionModeChanged;
        _sandbox.Shell.WindowLayoutChanged += OnWindowLayoutChanged;
        TreeExiting += DisconnectWorkPlayControls;
        _workPlayControlsComposed = true;
        UpdateWorkPlayLabels();
        Callable.From(RefreshDockHitRegions).CallDeferred();
    }

    private void OnInteractionModeChanged(InputMode mode) => UpdateWorkPlayLabels();

    private void OnWindowLayoutChanged(WindowLayoutMode mode)
    {
        UpdateWorkPlayLabels();
        Callable.From(RefreshDockHitRegions).CallDeferred();
    }

    private void UpdateWorkPlayLabels()
    {
        if (!_workPlayControlsComposed)
            return;

        InteractionModeButton.Text = _sandbox.Shell.Mode == InputMode.Work
            ? "TOGGLE PLAY MODE"
            : "TOGGLE WORK MODE";

        bool fullscreen = _sandbox.Shell.LayoutMode == WindowLayoutMode.FullscreenOverlay;
        WindowLayoutButton.Text = fullscreen
            ? "USE COMPACT WINDOW"
            : "USE FULL-SCREEN OVERLAY";
        WindowLayoutButton.Disabled =
            !fullscreen && !_sandbox.Window.FullscreenOverlayAvailable;
        WindowLayoutButton.TooltipText = WindowLayoutButton.Disabled
            ? "Per-pixel transparency is unavailable on the current display path."
            : fullscreen
                ? "Restore the saved compact buddy window."
                : "Cover the monitor transparently; Work passes empty-area clicks through.";
    }

    private void DisconnectWorkPlayControls()
    {
        if (!_workPlayControlsComposed || !GodotObject.IsInstanceValid(_sandbox.Shell))
            return;
        _sandbox.Shell.InputModeChanged -= OnInteractionModeChanged;
        _sandbox.Shell.WindowLayoutChanged -= OnWindowLayoutChanged;
    }
}
