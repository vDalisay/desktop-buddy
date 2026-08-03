using System;
using DesktopBuddy.Domain.Platform;
using DesktopBuddy.Ui;
using Godot;

namespace DesktopBuddy.CharacterEditor;

/// <summary>
/// Keeps the normal dock inside the compact buddy window. A separate native toolbar is used
/// only for the full-screen overlay, whose Work mode deliberately passes the main window
/// through to the desktop.
/// </summary>
public partial class CharacterEditorHost
{
    private bool _workPlayControlsComposed;
    private Control _compactDockContainer = null!;
    private DesktopToolbarWindow _desktopToolbar = null!;
    private Button _fullscreenModeButton = null!;
    private Rect2I _lastToolbarMainRect;
    private bool _lastToolbarVisible;
    private bool _lastCompactDockVisible;

    public Button InteractionModeButton { get; private set; } = null!;
    public Button WindowLayoutButton { get; private set; } = null!;
    public DesktopToolbarWindow DesktopToolbar => _desktopToolbar;

    public override void _Process(double delta)
    {
        ProcessPainting();

        if (!_workPlayControlsComposed && IsInitialized)
            ComposeWorkPlayControls();
        if (!_workPlayControlsComposed)
            return;

        UpdateDockVisibility();
        if (!_lastToolbarVisible)
            return;

        Rect2I mainRect = LiveMainWindowRect();
        if (mainRect != _lastToolbarMainRect)
        {
            _lastToolbarMainRect = mainRect;
            _desktopToolbar.Place(mainRect);
            RaiseToolbar();
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

        Control? uiRoot = GetNodeOrNull<Control>("CharacterEditorUiRoot");
        if (GodotObject.IsInstanceValid(uiRoot))
            uiRoot!.MouseFilter = Control.MouseFilterEnum.Ignore;

        Control compactBar = SettingsButton.GetParent<Control>();
        _compactDockContainer = compactBar.GetParent<Control>();
        _compactDockContainer.Visible = true;

        InteractionModeButton = new Button
        {
            Name = "DockInteractionModeButton",
            FocusMode = Control.FocusModeEnum.None,
            MouseFilter = Control.MouseFilterEnum.Stop,
            TooltipText = "Switch between interacting with the buddy and clicking through to the desktop.",
        };
        InteractionModeButton.Pressed += _sandbox.Shell.ToggleInteractionMode;
        compactBar.AddChild(InteractionModeButton);

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

        // Full-screen Work passes the main window through to the desktop, so it needs a small
        // independent recovery surface. These are proxy controls; no existing Control is ever
        // reparented across native windows.
        _desktopToolbar = new DesktopToolbarWindow { Visible = false };
        _desktopToolbar.Configure();
        AddChild(_desktopToolbar);
        _sandbox.Shell.RegisterOwnedWindow(_desktopToolbar);
        _desktopToolbar.AddAction("Editor", "FullscreenEditorButton", () => _ = OpenEditorAsync());
        _desktopToolbar.AddAction("Shop", "FullscreenShopButton", () =>
            _shopWindow.Toggle(WindowAnchor(0)));
        _desktopToolbar.AddAction("Tools", "FullscreenToolsButton", () =>
            _toolWindow.Toggle(WindowAnchor(1)));
        _desktopToolbar.AddAction("Settings", "FullscreenSettingsButton", () =>
            _settingsWindow.Toggle(WindowAnchor(2)));
        _fullscreenModeButton = _desktopToolbar.AddAction(
            "Play",
            "FullscreenInteractionModeButton",
            _sandbox.Shell.ToggleInteractionMode);

        // The native toolbar is the only solid control in full-screen Work. Compact mode
        // captures its complete client rectangle and uses the original in-window dock.
        _sandbox.SetOverlayWorkModeHitRegions(Array.Empty<Rect2>());

        _sandbox.Shell.InputModeChanged += OnInteractionModeChanged;
        _sandbox.Shell.WindowLayoutChanged += OnWindowLayoutChanged;
        GetWindow().FocusEntered += RaiseToolbar;
        TreeExiting += DisconnectWorkPlayControls;
        _workPlayControlsComposed = true;
        _lastToolbarMainRect = default;
        _lastToolbarVisible = false;
        _lastCompactDockVisible = true;
        UpdateWorkPlayLabels();
        UpdateDockVisibility(force: true);
    }

    private void UpdateDockVisibility(bool force = false)
    {
        bool fullscreen = _sandbox.Shell.LayoutMode == WindowLayoutMode.FullscreenOverlay;
        bool compactVisible = !IsEditorOpen && !fullscreen;
        if (force || compactVisible != _lastCompactDockVisible)
        {
            _lastCompactDockVisible = compactVisible;
            if (GodotObject.IsInstanceValid(_compactDockContainer))
                _compactDockContainer.Visible = compactVisible;
            Callable.From(RefreshDockHitRegions).CallDeferred();
        }

        bool toolbarVisible = !IsEditorOpen && fullscreen &&
            _sandbox.Window.Adapter.IsWindowVisible;
        if (!force && toolbarVisible == _lastToolbarVisible)
            return;

        _lastToolbarVisible = toolbarVisible;
        if (toolbarVisible)
        {
            _desktopToolbar.Show();
            _lastToolbarMainRect = default;
            Callable.From(PlaceToolbarAfterLayout).CallDeferred();
        }
        else
        {
            _desktopToolbar.Hide();
        }
    }

    private Rect2I LiveMainWindowRect()
    {
        Window mainWindow = GetWindow();
        return new Rect2I(mainWindow.Position, mainWindow.Size);
    }

    private void PlaceToolbarAfterLayout()
    {
        if (!_workPlayControlsComposed || !_lastToolbarVisible ||
            !GodotObject.IsInstanceValid(_desktopToolbar))
        {
            return;
        }

        _lastToolbarMainRect = LiveMainWindowRect();
        _desktopToolbar.Place(_lastToolbarMainRect);
        RaiseToolbar();
    }

    private void OnInteractionModeChanged(InputMode mode)
    {
        UpdateWorkPlayLabels();
        RaiseToolbar();
    }

    private void OnWindowLayoutChanged(WindowLayoutMode mode)
    {
        UpdateWorkPlayLabels();
        _lastToolbarMainRect = default;
        UpdateDockVisibility(force: true);
    }

    private void RaiseToolbar()
    {
        if (_lastToolbarVisible && GodotObject.IsInstanceValid(_desktopToolbar))
            _desktopToolbar.RaiseAboveOwner();
    }

    private void UpdateWorkPlayLabels()
    {
        if (!_workPlayControlsComposed)
            return;

        string modeLabel = _sandbox.Shell.Mode == InputMode.Work ? "Play" : "Work";
        InteractionModeButton.Text = modeLabel;
        _fullscreenModeButton.Text = modeLabel;

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
        GetWindow().FocusEntered -= RaiseToolbar;
    }
}
