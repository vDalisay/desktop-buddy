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
        ProcessPainting();

        if (!_workPlayControlsComposed && IsInitialized)
            ComposeWorkPlayControls();

        if (!_workPlayControlsComposed || !GodotObject.IsInstanceValid(_desktopToolbar))
            return;

        bool wantedVisible = !IsEditorOpen && _sandbox.Window.Adapter.IsWindowVisible;
        if (_lastToolbarVisible != wantedVisible)
        {
            _lastToolbarVisible = wantedVisible;
            if (wantedVisible)
            {
                _desktopToolbar.Show();
                RaiseToolbar();
                Callable.From(PlaceToolbarAfterLayout).CallDeferred();
            }
            else
            {
                _desktopToolbar.Hide();
            }
        }

        if (!wantedVisible)
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

        InteractionModeButton = new Button
        {
            Name = "DockInteractionModeButton",
            FocusMode = Control.FocusModeEnum.None,
            TooltipText = "Switch between interacting with the buddy and clicking through to the desktop.",
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

        // Compact labels keep the native toolbar inside the narrow compact buddy window.
        OpenCharacterEditorButton.Text = "Editor";
        OpenCharacterEditorButton.TooltipText = "Open the Character Editor.";
        _desktopToolbar.Attach(OpenCharacterEditorButton);
        _desktopToolbar.Attach(ShopButton);
        _desktopToolbar.Attach(ToolsButton);
        _desktopToolbar.Attach(SettingsButton);
        _desktopToolbar.Bar.AddChild(InteractionModeButton);
        InteractionModeButton.Visible = true;
        if (GodotObject.IsInstanceValid(oldDockContainer))
            oldDockContainer!.Visible = false;

        _sandbox.SetOverlayWorkModeHitRegions(Array.Empty<Rect2>());

        _sandbox.Shell.InputModeChanged += OnInteractionModeChanged;
        _sandbox.Shell.WindowLayoutChanged += OnWindowLayoutChanged;
        GetWindow().FocusEntered += RaiseToolbar;
        TreeExiting += DisconnectWorkPlayControls;
        _workPlayControlsComposed = true;
        UpdateWorkPlayLabels();
        _lastToolbarMainRect = default;
        _lastToolbarVisible = true;
        Callable.From(PlaceToolbarAfterLayout).CallDeferred();
    }

    private Rect2I LiveMainWindowRect()
    {
        Window mainWindow = GetWindow();
        return new Rect2I(mainWindow.Position, mainWindow.Size);
    }

    private void PlaceToolbarAfterLayout()
    {
        if (!_workPlayControlsComposed || !GodotObject.IsInstanceValid(_desktopToolbar))
            return;
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
        Callable.From(PlaceToolbarAfterLayout).CallDeferred();
    }

    private void RaiseToolbar()
    {
        if (_workPlayControlsComposed && GodotObject.IsInstanceValid(_desktopToolbar))
            _desktopToolbar.RaiseAboveOwner();
    }

    private void UpdateWorkPlayLabels()
    {
        if (!_workPlayControlsComposed)
            return;

        InteractionModeButton.Text = _sandbox.Shell.Mode == InputMode.Work
            ? "Play"
            : "Work";

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
