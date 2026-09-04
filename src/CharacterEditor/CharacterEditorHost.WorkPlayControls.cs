using System;
using DesktopBuddy.Diagnostics;
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
    private const string DockDiagnosticsCategory = "DockDiagnostics";

    private bool _workPlayControlsComposed;
    private Control _compactDockContainer = null!;
    private DesktopToolbarWindow _desktopToolbar = null!;
    private Button _fullscreenModeButton = null!;
    private Rect2I _lastToolbarMainRect;
    private bool _lastToolbarVisible;
    private bool _lastCompactDockVisible;
    private ulong _dockDiagnosticFrame;
    private string? _lastDockDiagnosticSignature;

    public Button InteractionModeButton { get; private set; } = null!;
    public Button WindowLayoutButton { get; private set; } = null!;
    public DesktopToolbarWindow DesktopToolbar => _desktopToolbar;

    public override void _Process(double delta)
    {
        ProcessPainting();
        AlignEditorToWindowChrome();
        _dockDiagnosticFrame++;

        if (!_workPlayControlsComposed && IsInitialized)
            ComposeWorkPlayControls();

        if (!_workPlayControlsComposed)
        {
            TraceInitializationWait();
            return;
        }

        UpdateDockVisibility();
        TraceDockStateWhenChanged();

        if (!_lastToolbarVisible)
            return;

        Rect2I mainRect = LiveMainWindowRect();
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

        Control? uiRoot = GetNodeOrNull<Control>("CharacterEditorUiRoot");
        if (GodotObject.IsInstanceValid(uiRoot))
            uiRoot!.MouseFilter = Control.MouseFilterEnum.Ignore;

        Control compactBar = SettingsButton.GetParent<Control>();
        if (compactBar.Name.ToString().StartsWith("HBoxContainer", StringComparison.Ordinal))
            compactBar.Name = "FloatingDockBar";
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

        ComposePresentationRows();
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
            },
            group: DisplayGroup);
        WindowLayoutButton.Name = "WindowLayoutToggleButton";

        // Full-screen Work passes the main window through to the desktop, so it needs a small
        // independent recovery surface. These are proxy controls; no existing Control is ever
        // reparented across native windows.
        _desktopToolbar = new DesktopToolbarWindow { Visible = false };
        _desktopToolbar.Configure();
        AddChild(_desktopToolbar);
        _sandbox.Shell.RegisterOwnedWindow(_desktopToolbar);
        _desktopToolbar.AddAction("Editor", "FullscreenEditorButton", () => _ = OpenEditorAsync());
        _desktopToolbar.AddAction("Inventory", "FullscreenShopButton", () =>
            _shopWindow.Toggle(WindowAnchor(0)));
        _desktopToolbar.AddAction("Tools", "FullscreenToolsButton", () =>
            _toolWindow.Toggle(WindowAnchor(1)));
        _desktopToolbar.AddAction("Settings", "FullscreenSettingsButton", () =>
            _settingsWindow.Toggle(WindowAnchor(2)));
        _fullscreenModeButton = _desktopToolbar.AddAction(
            "Play",
            "FullscreenInteractionModeButton",
            _sandbox.Shell.ToggleInteractionMode);
        // Recovery must never depend on opening Settings first.
        _desktopToolbar.AddAction("Compact", "FullscreenCompactButton", () => _ =
            _sandbox.Shell.ToggleWindowLayoutAsync());

        // The native toolbar is the only solid control in full-screen Work. Compact mode
        // captures its complete client rectangle and uses the original in-window dock.
        _sandbox.SetOverlayWorkModeHitRegions(Array.Empty<Rect2>());

        _sandbox.Shell.InputModeChanged += OnInteractionModeChanged;
        _sandbox.Shell.WindowLayoutChanged += OnWindowLayoutChanged;
        TreeExiting += DisconnectWorkPlayControls;
        _workPlayControlsComposed = true;
        _lastToolbarMainRect = default;
        _lastToolbarVisible = false;
        _lastCompactDockVisible = true;
        UpdateWorkPlayLabels();
        UpdateDockVisibility(force: true);

        Log.Info(DockDiagnosticsCategory,
            "Work/play controls composed; waiting one layout frame before measuring the dock.");
        Callable.From(() => TraceDockState("post-compose-deferred", force: true)).CallDeferred();
    }

    private void UpdateDockVisibility(bool force = false)
    {
        bool fullscreen = _sandbox.Shell.LayoutMode == WindowLayoutMode.FullscreenOverlay;
        bool compactVisible = !IsEditorOpen && !fullscreen &&
            !_sandbox.Window.WorkCompanionActive;
        if (force || compactVisible != _lastCompactDockVisible)
        {
            _lastCompactDockVisible = compactVisible;
            if (GodotObject.IsInstanceValid(_compactDockContainer))
                _compactDockContainer.Visible = compactVisible;
            Callable.From(RefreshDockHitRegions).CallDeferred();
            TraceDockState("visibility-change", force: true);
        }

        // Full-screen Play keeps the in-window Win98 strip, so the native toolbar would just be a
        // second menu floating over it. It stays only for full-screen Work, where the main window
        // is click-through and the strip cannot be reached at all.
        bool toolbarVisible = !IsEditorOpen && fullscreen &&
            _sandbox.Shell.Mode == InputMode.Work &&
            _sandbox.Window.Adapter.IsWindowVisible;
        if (!force && toolbarVisible == _lastToolbarVisible)
            return;

        _lastToolbarVisible = toolbarVisible;
        if (toolbarVisible)
        {
            DockWindow.ShowOwned(_desktopToolbar);
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
    }

    private void OnInteractionModeChanged(InputMode mode)
    {
        UpdateWorkPlayLabels();
        UpdateDockVisibility(force: true);
        TraceDockState("input-mode-change", force: true);
    }

    private void OnWindowLayoutChanged(WindowLayoutMode mode)
    {
        UpdateWorkPlayLabels();
        _lastToolbarMainRect = default;
        UpdateDockVisibility(force: true);
        TraceDockState("window-layout-change", force: true);
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

    private void TraceInitializationWait()
    {
        if (_dockDiagnosticFrame is not (1 or 30 or 120 or 300))
            return;

        bool charactersReady = _context is not null && _context.Characters is not null;
        bool selectionRuntimeValid = GodotObject.IsInstanceValid(_selectionRuntime);
        bool coordinatorReady = selectionRuntimeValid && _selectionRuntime.Coordinator is not null;
        bool settingsButtonReady = GodotObject.IsInstanceValid(SettingsButton);

        Log.Info(DockDiagnosticsCategory,
            $"Host not composed at frame {_dockDiagnosticFrame}: " +
            $"isInitialized={IsInitialized} charactersReady={charactersReady} " +
            $"selectionRuntimeValid={selectionRuntimeValid} coordinatorReady={coordinatorReady} " +
            $"settingsButtonReady={settingsButtonReady} insideTree={IsInsideTree()} path={GetPath()}.");

        if (_dockDiagnosticFrame == 300 && !IsInitialized)
        {
            Log.Error(DockDiagnosticsCategory,
                "CharacterEditorHost still has not initialized after 300 rendered frames; " +
                "the compact dock cannot exist because BuildUi has not run.");
        }
    }

    private void TraceDockStateWhenChanged()
    {
        // Layout settles asynchronously. Sample often during startup and then once per second;
        // TraceDockState itself suppresses identical output.
        if (_dockDiagnosticFrame < 180 || _dockDiagnosticFrame % 60 == 0)
            TraceDockState("sample", force: false);
    }

    private void TraceDockState(string reason, bool force)
    {
        if (!_workPlayControlsComposed)
            return;

        Control? uiRoot = GetNodeOrNull<Control>("CharacterEditorUiRoot");
        Control? compactBar = GodotObject.IsInstanceValid(SettingsButton)
            ? SettingsButton.GetParentOrNull<Control>()
            : null;
        Control? win98CommandBar = GetTree().Root.FindChild(
            "Win98CommandBar", true, false) as Control;
        Rect2 viewportRect = GetViewport().GetVisibleRect();
        bool expectedCompact = !IsEditorOpen &&
            _sandbox.Shell.LayoutMode == WindowLayoutMode.Compact &&
            !_sandbox.Window.WorkCompanionActive &&
            !(GodotObject.IsInstanceValid(win98CommandBar) && win98CommandBar!.IsVisibleInTree());

        string signature =
            $"expectedCompact={expectedCompact};editor={IsEditorOpen};" +
            $"layout={_sandbox.Shell.LayoutMode};mode={_sandbox.Shell.Mode};" +
            $"window={LiveMainWindowRect()};viewport={viewportRect};" +
            $"win98Bar={DescribeControl(win98CommandBar)};" +
            $"uiRoot={DescribeControl(uiRoot)};" +
            $"dock={DescribeControl(_compactDockContainer)};" +
            $"bar={DescribeControl(compactBar)};" +
            $"shop={DescribeControl(ShopButton)};tools={DescribeControl(ToolsButton)};" +
            $"settings={DescribeControl(SettingsButton)};editorButton={DescribeControl(OpenCharacterEditorButton)};" +
            $"modeButton={DescribeControl(InteractionModeButton)}";

        if (!force && string.Equals(signature, _lastDockDiagnosticSignature, StringComparison.Ordinal))
            return;
        _lastDockDiagnosticSignature = signature;
        Log.Info(DockDiagnosticsCategory, $"{reason}: {signature}");

        if (!expectedCompact)
            return;

        if (!GodotObject.IsInstanceValid(_compactDockContainer))
        {
            Log.Error(DockDiagnosticsCategory,
                "Compact dock is expected, but its container instance is invalid.");
            return;
        }

        if (!_compactDockContainer.IsVisibleInTree())
        {
            Log.Warn(DockDiagnosticsCategory,
                "Compact dock is expected, but IsVisibleInTree is false. Inspect the logged parent chain and visibility values.");
        }

        Rect2 dockRect = _compactDockContainer.GetGlobalRect();
        if (dockRect.Size.X <= 0 || dockRect.Size.Y <= 0)
        {
            Log.Warn(DockDiagnosticsCategory,
                $"Compact dock has a zero/negative layout size: globalRect={dockRect} minimum={_compactDockContainer.GetCombinedMinimumSize()}.");
        }
        else if (!viewportRect.Intersects(dockRect, includeBorders: true))
        {
            Log.Warn(DockDiagnosticsCategory,
                $"Compact dock is outside the viewport: dock={dockRect} viewport={viewportRect}.");
        }

        WarnIfButtonUnavailable(ShopButton, "Inventory");
        WarnIfButtonUnavailable(ToolsButton, "Tools");
        WarnIfButtonUnavailable(SettingsButton, "Settings");
        WarnIfButtonUnavailable(OpenCharacterEditorButton, "Character Editor");
        WarnIfButtonUnavailable(InteractionModeButton, "Play/Work");
    }

    private static string DescribeControl(Control? control)
    {
        if (!GodotObject.IsInstanceValid(control))
            return "invalid";

        string parent = control!.GetParent() is Node parentNode
            ? $"{parentNode.Name}:{parentNode.GetType().Name}"
            : "none";
        return
            $"path={control.GetPath()},parent={parent},insideTree={control.IsInsideTree()}," +
            $"visible={control.Visible},visibleInTree={control.IsVisibleInTree()}," +
            $"position={control.Position},size={control.Size},globalRect={control.GetGlobalRect()}," +
            $"minimum={control.GetCombinedMinimumSize()},children={control.GetChildCount()}," +
            $"mouse={control.MouseFilter},z={control.ZIndex},clip={control.ClipContents}," +
            $"modulateA={control.Modulate.A:0.###},selfModulateA={control.SelfModulate.A:0.###}";
    }

    private static void WarnIfButtonUnavailable(Button? button, string label)
    {
        if (!GodotObject.IsInstanceValid(button))
        {
            Log.Warn(DockDiagnosticsCategory, $"{label} dock button instance is invalid.");
            return;
        }

        if (!button!.IsVisibleInTree() || button.Size.X <= 0 || button.Size.Y <= 0)
        {
            Log.Warn(DockDiagnosticsCategory,
                $"{label} dock button is unavailable: {DescribeControl(button)}.");
        }
    }

    private void DisconnectWorkPlayControls()
    {
        if (!_workPlayControlsComposed || !GodotObject.IsInstanceValid(_sandbox.Shell))
            return;
        _sandbox.Shell.InputModeChanged -= OnInteractionModeChanged;
        _sandbox.Shell.WindowLayoutChanged -= OnWindowLayoutChanged;
    }
}
