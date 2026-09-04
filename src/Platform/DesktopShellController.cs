using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using DesktopBuddy.App;
using DesktopBuddy.Buddy.Physics;
using DesktopBuddy.Diagnostics;
using DesktopBuddy.Domain.Persistence;
using DesktopBuddy.Domain.Physics;
using DesktopBuddy.Domain.Platform;
using DesktopBuddy.Persistence;
using DesktopBuddy.Sandbox;
using Godot;
using DomainInputMode = DesktopBuddy.Domain.Platform.InputMode;

namespace DesktopBuddy.Platform;

/// <summary>
/// Owns the independent interaction-mode and window-layout state machines. Compact windows
/// always capture their full client area. Full-screen Work captures only same-window toolbar
/// controls and passes every other pixel to the desktop; full-screen Play captures the monitor.
/// </summary>
public partial class DesktopShellController : Node
{
    private const string Category = "Shell";

    [Export] public DesktopWindowController Window { get; set; } = null!;
    [Export] public BoundaryController Boundaries { get; set; } = null!;

    private InputModeStateMachine _mode = new(DomainInputMode.Work);
    private Rect2 _innerBounds;
    private Vector2I? _pendingClientSize;
    private double _storedZoom = 1.0;
    private double _effectiveZoom = 1.0;
    private IReadOnlyList<Rect2I>? _dynamicWorkModeHitRegions;
    private readonly Rect2I[] _fallbackWorkModeHitRegion = new Rect2I[1];
    private readonly List<Rect2I> _fullscreenUiHitRegions = [];
    private readonly List<Window> _ownedWindows = [];
    private LocalSettingsSave _settings = new();
    private SaveCoordinator? _saves;
    private bool _runtimeConfigured;

    public DomainInputMode Mode => _mode.Current;
    public WindowLayoutMode LayoutMode => Window.LayoutMode;
    public bool GameplayInputEnabled => Mode == DomainInputMode.Play;
    public int ModeChangeCount { get; private set; }
    public double EffectiveZoom => _effectiveZoom;
    public LocalSettingsSave CurrentLocalSettings => _settings;

    public IReadOnlyList<Rect2I> LastWorkModeHitRegions { get; private set; } =
        Array.Empty<Rect2I>();

    public event Action<DomainInputMode>? InputModeChanged;
    public event Action<WindowLayoutMode>? WindowLayoutChanged;

    /// <summary>Inject loaded machine-local settings before the sandbox enters the tree.</summary>
    public void ConfigureRuntime(LocalSettingsSave settings, SaveCoordinator saves)
    {
        if (IsInsideTree())
            throw new InvalidOperationException("Desktop shell runtime must be configured before _Ready.");
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _saves = saves ?? throw new ArgumentNullException(nameof(saves));
        _saves.RegisterSettings(_settings);

        // The reduced itch.io distribution does not contain Work Mode. A fresh settings file
        // remembers Work by default, so honoring it here would boot an itch/Web build directly
        // into a mode the distribution intentionally removed. Pin that scope to Play before the
        // shell ever enters the tree, and reject later Work-mode transitions in Apply().
        DomainInputMode initialMode = DemoScope.IncludesWorkMode
            ? WindowInteractionSettings.ReadInputMode(_settings)
            : DomainInputMode.Play;
        _mode = new InputModeStateMachine(initialMode);

        if (DemoScope.IncludesWorkMode)
            HotkeyBinding.Apply(InputActions.ToggleInputMode, _settings.GlobalHotkey);
        else if (InputMap.HasAction(InputActions.ToggleInputMode))
            InputMap.ActionEraseEvents(InputActions.ToggleInputMode);
        _runtimeConfigured = true;
    }

    public override void _Ready()
    {
        if (!GodotObject.IsInstanceValid(Window) || !GodotObject.IsInstanceValid(Boundaries))
        {
            throw new InvalidOperationException(
                "DesktopShellController requires an injected window controller and boundary.");
        }

        Window.Configure(WindowsDesktopAdapterFactory.Create());
        Window.ClientBoundsChanged += OnClientBoundsChanged;
        Window.WindowFocusLost += OnWindowFocusLost;
        Window.LayoutModeChanged += OnWindowLayoutChanged;
        Boundaries.LayoutApplied += OnLayoutApplied;

        // Browser play has no movable/restorable native desktop window. Running the Windows
        // placement/fullscreen choreography against the root Web window has been leaving the
        // shell before BoundaryController.Initialize: the canvas renders, but CurrentLayout
        // remains 0x0, the old 2D puppet stays visible, and grab/recovery use construction-time
        // bounds. In Web the DOM canvas is the one authoritative client rectangle, so compose
        // the room directly from the viewport and then apply the already-scoped Play input mode.
        if (OperatingSystem.IsBrowser())
        {
            _storedZoom = _settings.ZoomPercent / 100.0;
            Vector2I browserClientSize = ResolveClientSize();
            GD.Print(
                $"DESKTOP_BUDDY_WEB_SHELL_INITIALIZING client={browserClientSize.X}x{browserClientSize.Y} zoom={_storedZoom:F2}");
            Boundaries.Initialize(browserClientSize, _storedZoom);
            ApplyMode(force: true);
            Log.Info(Category,
                $"Browser shell composed (layout={Window.LayoutMode} mode={_mode.Current} " +
                $"room={Boundaries.CurrentLayout.RoomWidth}x{Boundaries.CurrentLayout.RoomHeight}).");
            GD.Print(
                $"DESKTOP_BUDDY_WEB_SHELL_READY room={Boundaries.CurrentLayout.RoomWidth}x{Boundaries.CurrentLayout.RoomHeight}");
            return;
        }

        Rect2I? storedRect = _runtimeConfigured && _settings.Revision > 0
            ? WindowInteractionSettings.CompactRect(_settings)
            : null;
        Rect2I placement = Window.ResolvePlacement(storedRect);
        WindowSettings launch = WindowSettings.Defaults with
        {
            Rect = placement,
            Transparent = true,
            AlwaysOnTop = _settings.AlwaysOnTop,
            MsaaLevel = _settings.Msaa,
            Vsync = _settings.VSync,
            MaxFps = _settings.MaxFps,
        };
        Window.ApplyWindowSettings(launch);
        _storedZoom = _settings.ZoomPercent / 100.0;

        WindowLayoutMode requestedLayout = _runtimeConfigured
            ? WindowInteractionSettings.ReadLayout(_settings)
            : WindowLayoutMode.Compact;
        int monitor = _runtimeConfigured
            ? WindowInteractionSettings.ReadFullscreenMonitor(_settings)
            : 0;
        if (!Window.TrySetLayoutMode(requestedLayout, monitor))
            Window.TrySetLayoutMode(WindowLayoutMode.Compact, monitor);

        Vector2I clientSize = ResolveClientSize();
        Boundaries.Initialize(clientSize, _storedZoom);
        ApplyMode(force: true);
        // Everything the Settings rows can change has to arrive at boot too. Only EditSettings
        // used to apply these, so a saved interface palette or UI scale sat in settings.json and
        // showed up as the shipped grey until the player touched a settings row (owner report
        // 2026-08-25).
        ApplyPresentationSettings();

        Log.Info(Category,
            $"Shell composed (layout={Window.LayoutMode} mode={_mode.Current} transparency={Window.TransparencyActive}).");
    }

    public void PhysicsTick()
    {
        if (_pendingClientSize is Vector2I size)
        {
            _pendingClientSize = null;
            Boundaries.RequestLayout(RoomSizeFor(size), _storedZoom);
        }
    }

    public override void _Input(InputEvent @event)
    {
        if (@event.IsActionPressed(InputActions.ToggleInputMode))
        {
            ToggleInteractionMode();
            GetViewport().SetInputAsHandled();
            return;
        }

        if (@event is InputEventKey { Pressed: true, Echo: false, PhysicalKeycode: Key.Escape })
        {
            Apply(ShellInputEvent.EscapePressed);
            GetViewport().SetInputAsHandled();
        }
    }

    /// <summary>
    /// In Compact Work, the complete transparent client box accepts input. A left click not
    /// consumed by UI enters Play and is itself swallowed, so it can never also grab, swing,
    /// shoot, or spray. Full-screen Work receives no empty-area events by native design.
    /// </summary>
    public override void _UnhandledInput(InputEvent @event)
    {
        if (EditorBoundaryIsolationActive ||
            Window.LayoutMode != WindowLayoutMode.Compact ||
            _mode.Current != DomainInputMode.Work)
        {
            return;
        }

        if (@event is InputEventMouseButton
            {
                Pressed: true,
                ButtonIndex: MouseButton.Left,
            })
        {
            Apply(ShellInputEvent.BuddyInteraction);
            GetViewport().SetInputAsHandled();
        }
    }

    public void ToggleInteractionMode() => Apply(ShellInputEvent.GlobalToggle);

    public async Task<bool> ToggleWindowLayoutAsync()
    {
        WindowLayoutMode wanted = Window.LayoutMode == WindowLayoutMode.Compact
            ? WindowLayoutMode.FullscreenOverlay
            : WindowLayoutMode.Compact;
        if (wanted == WindowLayoutMode.FullscreenOverlay &&
            !Window.FullscreenOverlayAvailable)
        {
            return false;
        }

        bool changed = Window.TrySetLayoutMode(
            wanted,
            WindowInteractionSettings.ReadFullscreenMonitor(_settings));
        if (!changed)
            return false;

        ApplyMode(force: false);
        await PersistWindowStateAsync();
        return true;
    }

    /// <summary>Return control to Work Mode from a tray action or recovery path.</summary>
    public void ReturnToWorkMode()
    {
        EditorBoundaryIsolationActive = false;
        Apply(ShellInputEvent.TrayReturnToWork);
    }

    public void UpdateWorkModeHitRegions(
        IReadOnlyList<Rect2> worldRegions,
        IReadOnlyList<Rect2I> clientRegions)
    {
        ArgumentNullException.ThrowIfNull(worldRegions);
        ArgumentNullException.ThrowIfNull(clientRegions);
        if (worldRegions.Count != clientRegions.Count)
            throw new ArgumentException("World and client hit-region counts must match.");
        _dynamicWorkModeHitRegions = clientRegions;
        LastWorkModeHitRegions = clientRegions;
        ApplyMode(force: false);
    }

    private void Apply(ShellInputEvent input)
    {
        if (EditorBoundaryIsolationActive)
            return;

        // Work Mode is not merely hidden in the itch build; it is outside that distribution's
        // feature surface. Escape, stale settings, tray recovery, or a synthesized hotkey must
        // therefore never move the runtime back into Work after startup.
        if (!DemoScope.IncludesWorkMode)
            return;

        if (_mode.Apply(input))
        {
            ModeChangeCount++;
            ApplyMode(force: false);
            InputModeChanged?.Invoke(_mode.Current);
            _ = PersistWindowStateObservedAsync();
        }
    }

    private void ApplyMode(bool force)
    {
        Window.SetInputMode(_mode.Current, NativeFullscreenWorkHitRegions());
        // "Mute while working" follows the mode, so it has to be re-evaluated on every change.
        ApplyAudioSettings();
        if (force)
            ModeChangeCount = 0;
    }

    private IReadOnlyList<Rect2I> NativeFullscreenWorkHitRegions()
    {
        if (Window.LayoutMode != WindowLayoutMode.FullscreenOverlay)
            return Array.Empty<Rect2I>();

        _fullscreenUiHitRegions.Clear();
        IReadOnlyList<Rect2I> source = WorkModeHitRegions();
        // SandboxRoot appends same-window overlay regions after the six moving buddy parts.
        // In full-screen Work the buddy is display-only; only toolbar/UI rects remain solid.
        int firstUi = Math.Min(PuppetRigProfile.RequiredPartCount, source.Count);
        for (int index = firstUi; index < source.Count; index++)
            _fullscreenUiHitRegions.Add(source[index]);
        return _fullscreenUiHitRegions;
    }

    private IReadOnlyList<Rect2I> WorkModeHitRegions()
    {
        if (_dynamicWorkModeHitRegions is { Count: > 0 } dynamicRegions)
        {
            LastWorkModeHitRegions = dynamicRegions;
            return dynamicRegions;
        }

        if (_innerBounds.Size == Vector2.Zero)
        {
            LastWorkModeHitRegions = Array.Empty<Rect2I>();
            return LastWorkModeHitRegions;
        }

        PixelRect box = SandboxProjection.SandboxRectToClient(
            _innerBounds.Position.X,
            _innerBounds.Position.Y,
            _innerBounds.Size.X,
            _innerBounds.Size.Y,
            _effectiveZoom);
        _fallbackWorkModeHitRegion[0] = new Rect2I(box.X, box.Y, box.Width, box.Height);
        LastWorkModeHitRegions = _fallbackWorkModeHitRegion;
        return LastWorkModeHitRegions;
    }

    /// <summary>
    /// Refreshes the registered settings snapshot from the live window without writing. The
    /// quit path calls this because a plain window move emits no size signal, so the stored
    /// compact rectangle would otherwise only ever be as fresh as the last mode toggle.
    /// </summary>
    public void CaptureWindowStateForSave()
    {
        if (_saves is null)
            return;

        Rect2I compact = Window.LayoutMode == WindowLayoutMode.Compact &&
            !Window.WorkCompanionActive
            ? Window.CaptureWindowSettings().Rect
            : Window.CompactWindowSettings.Rect;
        _settings = WindowInteractionSettings.WithState(
            _settings,
            Window.LayoutMode,
            _mode.Current,
            compact,
            Window.FullscreenMonitor);
        _saves.RegisterSettings(_settings);
    }

    private void OnClientBoundsChanged(Rect2I bounds) => _pendingClientSize = bounds.Size;

    public void RegisterOwnedWindow(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        if (!_ownedWindows.Contains(window))
            _ownedWindows.Add(window);
    }

    private void OnWindowFocusLost() => CallDeferred(MethodName.ResolveFocusLoss);

    private void ResolveFocusLoss()
    {
        foreach (Window owned in _ownedWindows)
        {
            if (GodotObject.IsInstanceValid(owned) && owned.Visible && owned.HasFocus())
                return;
        }

        Apply(ShellInputEvent.FocusLost);
    }

    private void OnWindowLayoutChanged(WindowLayoutMode mode)
    {
        ApplyMode(force: false);
        WindowLayoutChanged?.Invoke(mode);
    }

    private void OnLayoutApplied(RoomLayout layout, Rect2 innerBounds)
    {
        _innerBounds = innerBounds;
        _effectiveZoom = layout.EffectiveZoom;
        ApplyMode(force: false);
    }

    private Vector2I ResolveClientSize()
    {
        Vector2 viewport = GetViewport().GetVisibleRect().Size;
        return RoomSizeFor(new Vector2I((int)viewport.X, (int)viewport.Y));
    }

    /// <summary>
    /// Client box minus the Win98 frame chrome, so the room floor is the top of the status
    /// bar instead of a line hidden behind it. Fullscreen and the Work companion hide the
    /// frame, so they keep the whole client box.
    /// </summary>
    private Vector2I RoomSizeFor(Vector2I client)
    {
        if (Window.LayoutMode == WindowLayoutMode.Compact && !Window.WorkCompanionActive)
            client.Y -= UI.Win98.Win98ThemeFactory.ChromeHeight;

        if (client.X < RoomLayoutPolicy.MinimumRoomWidth ||
            client.Y < RoomLayoutPolicy.MinimumRoomHeight)
        {
            client = new Vector2I(
                RoomLayoutPolicy.DefaultClientWidth,
                RoomLayoutPolicy.DefaultClientHeight);
        }

        return client;
    }

    private async Task PersistWindowStateAsync()
    {
        if (_saves is null)
            return;

        CaptureWindowStateForSave();
        await _saves.SaveRegisteredSettingsAsync();
    }

    private async Task PersistWindowStateObservedAsync()
    {
        try
        {
            await PersistWindowStateAsync();
        }
        catch (Exception exception)
        {
            Log.Error(Category,
                $"Window/input settings save failed; runtime state remains active: {exception.Message}");
        }
    }

    public override void _ExitTree()
    {
        if (GodotObject.IsInstanceValid(Window))
        {
            Window.ClientBoundsChanged -= OnClientBoundsChanged;
            Window.WindowFocusLost -= OnWindowFocusLost;
            Window.LayoutModeChanged -= OnWindowLayoutChanged;
        }

        if (GodotObject.IsInstanceValid(Boundaries))
            Boundaries.LayoutApplied -= OnLayoutApplied;
    }
}
