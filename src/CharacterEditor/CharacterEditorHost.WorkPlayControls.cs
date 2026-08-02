using System;
using DesktopBuddy.Domain.Platform;
using DesktopBuddy.Ui;
using Godot;

namespace DesktopBuddy.CharacterEditor;

/// <summary>
/// Extends the approved dock/settings surface with explicit interaction and window-layout
/// controls. The horizontal bar is moved into its own native window so the full-screen main
/// overlay can pass every mouse event through in Work mode without losing its recovery toggle.
/// </summary>
public partial class CharacterEditorHost
{
    private bool _workPlayControlsComposed;
    private DesktopToolbarWindow _desktopToolbar = null!;
    private Rect2I _lastToolbarMainRect;
    private bool _lastToolbarVisible;

    public Button InteractionModeButton { get; private set; } = null!;
    public Button WindowLayoutButton { get; private set; } = null!;
    public DesktopToolbarWindow DesktopToolbar => _desktopToolbar;

    public override void _Process(double delta)
    {
        if (!_workPlayControlsComposed && IsInitialized)
            ComposeWorkPlayControls();

        if (!_workPlayControlsComposed || !GodotObject.IsInstanceValid(_desktopToolbar))
            return;

        bool wantedVisible = !IsEditorOpen && _sandbox.Window.Adapter.IsWindowVisible;
        if (_lastToolbarVisible != wantedVisible)
        {
            _lastToolbarVisible = wantedVisible;
            if (wantedVisible)
                _desktopToolbar.Show();
            else
                _desktopToolbar.Hide();
        }

        if (!wantedVisible)
            return;

        Rect2I mainRect = _sandbox.Window.CurrentSettings.Rect;
        if (mainRect != _lastToolbarMainRect)
        {
            _lastToolbarMainRect = mainRect;
            _desktopToolbar.Place(mainRect);
        }
    }

    private void ComposeWorkPlayControls()
    {
        if (_workPlayControlsComposed ||
            !GodotObject.IsInstanceValid(SettingsButton) ||
            !GodotObject.IsInstanceValid(_settingsPanel))
        {
            return;
        }

        // The full-rect Control is layout only. Actual editor/prompt Controls still stop
        // input, while transparent empty space reaches Compact Work activation or gameplay.
        Control? uiRoot = GetNodeOrNull<Control>("CharacterEditorUiRoot");
        if (GodotObject.IsInstanceValid(uiRoot))
            uiRoot!.MouseFilter = Control.MouseFilterEnum.Ignore;

        InteractionModeButton = new Button
        {
            Name = "DockInteractionModeButton",
            FocusMode = Control.FocusModeEnum.None,
        };
        InteractionModeButton.Pressed += _sandbox.Shell.ToggleInteractionMode;

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

        _desktopToolbar = new DesktopToolbarWindow();
        _desktopToolbar.Configure();
        AddChild(_desktopToolbar);
        _sandbox.Shell.RegisterOwnedWindow(_desktopToolbar);

        Control oldBar = SettingsButton.GetParent<Control>();
        Control? oldDockContainer = oldBar.GetParentOrNull<Control>();
        ShopButton.Reparent(_desktopToolbar.Bar);
        ToolsButton.Reparent(_desktopToolbar.Bar);
        SettingsButton.Reparent(_desktopToolbar.Bar);
        _desktopToolbar.Bar.AddChild(InteractionModeButton);
        if (GodotObject.IsInstanceValid(oldDockContainer))
            oldDockContainer!.Visible = false;

        // No controls remain in the main overlay. In full-screen Work it can therefore be
        // entirely mouse-passthrough while this separate HWND stays interactive.
        _sandbox.SetOverlayWorkModeHitRegions(Array.Empty<Rect2>());

        _sandbox.Shell.InputModeChanged += OnInteractionModeChanged;
        _sandbox.Shell.WindowLayoutChanged += OnWindowLayoutChanged;
        TreeExiting += DisconnectWorkPlayControls;
        _workPlayControlsComposed = true;
        UpdateWorkPlayLabels();
        _lastToolbarMainRect = default;
        _lastToolbarVisible = true;
        _desktopToolbar.Place(_sandbox.Window.CurrentSettings.Rect);
    }

    private void OnInteractionModeChanged(InputMode mode) => UpdateWorkPlayLabels();

    private void OnWindowLayoutChanged(WindowLayoutMode mode)
    {
        UpdateWorkPlayLabels();
        _lastToolbarMainRect = default;
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
