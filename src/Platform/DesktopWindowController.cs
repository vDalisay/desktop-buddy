using System;
using System.Collections.Generic;
using DesktopBuddy.Diagnostics;
using DesktopBuddy.Domain.Physics;
using DesktopBuddy.Domain.Platform;
using Godot;
using DomainInputMode = DesktopBuddy.Domain.Platform.InputMode;

namespace DesktopBuddy.Platform;

/// <summary>
/// Godot-side desktop window service. Window footprint and interaction mode are independent:
/// Compact always captures its complete client rectangle. FullscreenOverlay captures the
/// monitor in Play and uses Godot's supported whole-window mouse passthrough in Work. The
/// horizontal recovery toolbar is a separate native window and therefore remains interactive.
/// </summary>
public partial class DesktopWindowController : Node, IDesktopWindowService
{
    private const string Category = "Window";
    private const int VsyncOffMaximumFps = 240;

    /// <summary>Smallest compact window that still leaves a full room under the frame chrome.</summary>
    public static readonly Vector2I CompactMinimumSize = new(
        RoomLayoutPolicy.MinimumRoomWidth,
        RoomLayoutPolicy.MinimumRoomHeight + UI.Win98.Win98ThemeFactory.ChromeHeight);

    private IWindowsDesktopAdapter _adapter = new EmulatedWindowsDesktopAdapter();
    private bool _headless;
    private Rect2I _lastAppliedRect = WindowSettings.Defaults.Rect;
    private WindowSettings _lastAppliedSettings = WindowSettings.Defaults;
    private WindowSettings _compactSettings = WindowSettings.Defaults;
    private IReadOnlyList<Rect2I> _workModeHitRegions = Array.Empty<Rect2I>();
    private bool _suppressClientBoundsChanged;
    private Rect2I? _pendingTransitionRect;

    public DomainInputMode InputMode { get; private set; } = DomainInputMode.Work;
    public WindowLayoutMode LayoutMode { get; private set; } = WindowLayoutMode.Compact;
    public int FullscreenMonitor { get; private set; }
    public bool TransparencyActive { get; private set; }
    public bool FullscreenOverlayAvailable => _adapter.TransparencyAvailable;
    public bool MainWindowMousePassthrough { get; private set; }
    public Rect2I CompactRect => _compactSettings.Rect;

    public IWindowsDesktopAdapter Adapter => _adapter;
    public WindowSettings CurrentSettings => CaptureWindowSettings();
    public int AppliedSettingsCount { get; private set; }

    public event Action<Rect2I>? ClientBoundsChanged;
    public event Action? WindowFocusLost;
    public event Action<WindowLayoutMode>? LayoutModeChanged;

    /// <summary>Inject the native/emulated adapter before adding to the tree.</summary>
    public void Configure(IWindowsDesktopAdapter adapter)
    {
        _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
    }

    public override void _Ready()
    {
        _headless = DisplayServer.GetName() == "headless";
        if (!_headless)
        {
            Window window = GetWindow();
            window.SizeChanged += OnSizeChanged;
            window.FocusExited += OnFocusExited;
        }

        Log.Info(Category,
            $"DesktopWindowController ready (native={_adapter.IsNative} headless={_headless}).");
    }

    public Rect2I UsableMonitorRect => ResolveContainingMonitor(CurrentWindowRect());

    /// <summary>How many displays the monitor picker can offer.</summary>
    public int MonitorCount => _headless
        ? Math.Max(1, _adapter.GetUsableMonitorRects().Count)
        : Math.Max(1, DisplayServer.GetScreenCount());

    public void ApplyWindowSettings(WindowSettings settings)
    {
        bool wantTransparent = settings.Transparent && _adapter.TransparencyAvailable;
        if (WorkCompanionActive)
        {
            // The companion owns the native window. Compact geometry applied now would
            // teleport it back to the normal room's rectangle, so it is recorded as the
            // rect ExitWorkCompanionWindow will restore instead.
            _preWorkCompanionSettings = settings with { Transparent = wantTransparent };
            AppliedSettingsCount++;
            if (!_headless)
                ApplyRenderSettings(settings);
            return;
        }

        TransparencyActive = wantTransparent;
        _lastAppliedRect = settings.Rect;
        _lastAppliedSettings = settings with { Transparent = wantTransparent };
        if (LayoutMode == WindowLayoutMode.Compact)
            _compactSettings = _lastAppliedSettings;
        AppliedSettingsCount++;

        if (!_headless)
        {
            Window window = GetWindow();
            // The room policy rejects client boxes below its floor, so the OS window may
            // never get there: this clamps every resize path (grips, OS drag, restore).
            window.MinSize = CompactMinimumSize;
            window.Borderless = settings.Borderless;
            window.Unresizable = !settings.Resizable;
            window.AlwaysOnTop = settings.AlwaysOnTop;
            window.Transparent = wantTransparent;
            GetViewport().TransparentBg = wantTransparent;
            if (window.Mode == Window.ModeEnum.Windowed)
            {
                window.Size = settings.Rect.Size;
                window.Position = settings.Rect.Position;
            }
            ApplyRenderSettings(settings);
        }

        ApplyCurrentInputPolicy();

        if (!wantTransparent && settings.Transparent)
            Log.Warn(Category, "Transparency unavailable; using opaque bordered fallback.");
    }

    public WindowSettings CaptureWindowSettings()
    {
        if (_headless)
            return _lastAppliedSettings with { Rect = _lastAppliedRect };

        Window window = GetWindow();
        return new WindowSettings(
            new Rect2I(window.Position, window.Size),
            window.Transparent,
            window.AlwaysOnTop,
            MsaaLevel(GetViewport().Msaa2D),
            DisplayServer.WindowGetVsyncMode() != DisplayServer.VSyncMode.Disabled,
            window.Borderless,
            !window.Unresizable,
            _lastAppliedSettings.MaxFps);
    }

    /// <summary>Recover a captured rect against the current monitor topology.</summary>
    public WindowSettings RecoverWindowSettings(WindowSettings captured) =>
        captured with { Rect = ResolvePlacement(captured.Rect) };

    /// <summary>
    /// Applies semantic interaction mode. Native hit-region subclassing is deliberately
    /// disabled: HTTRANSPARENT only redispatches within one UI thread and did not pass clicks
    /// to arbitrary desktop applications. Full-screen Work instead toggles the native Godot
    /// window's whole-window MousePassthrough flag.
    /// </summary>
    public void SetInputMode(DomainInputMode mode, IReadOnlyList<Rect2I> workModeHitRegions)
    {
        InputMode = mode;
        _workModeHitRegions = workModeHitRegions ?? Array.Empty<Rect2I>();
        ApplyCurrentInputPolicy();
    }

    public bool TrySetLayoutMode(WindowLayoutMode mode, int monitorIndex = 0)
    {
        // F11 and the maximize box still reach their handlers while the shell layer is hidden
        // behind the companion. Honouring them mid-session resized the companion to the whole
        // monitor, and the deferred FinishLayoutTransition then overwrote the companion rect
        // with the compact one.
        if (WorkCompanionActive)
            return false;

        int resolvedMonitor = ResolveMonitorIndex(monitorIndex);
        if (mode == WindowLayoutMode.FullscreenOverlay && !_adapter.TransparencyAvailable)
        {
            Log.Warn(Category,
                "Full-screen overlay requested but per-pixel transparency is unavailable.");
            return false;
        }

        if (mode == LayoutMode &&
            (mode != WindowLayoutMode.FullscreenOverlay || resolvedMonitor == FullscreenMonitor))
        {
            ApplyCurrentInputPolicy();
            return true;
        }

        if (LayoutMode == WindowLayoutMode.Compact)
            _compactSettings = CaptureWindowSettings();

        _suppressClientBoundsChanged = true;
        FullscreenMonitor = resolvedMonitor;
        LayoutMode = mode;

        if (mode == WindowLayoutMode.FullscreenOverlay)
            EnterFullscreenOverlay(resolvedMonitor);
        else
            EnterCompact();

        ApplyCurrentInputPolicy();
        LayoutModeChanged?.Invoke(mode);

        _pendingTransitionRect = mode == WindowLayoutMode.FullscreenOverlay
            ? MonitorRect(resolvedMonitor)
            : _compactSettings.Rect;
        Callable.From(FinishLayoutTransition).CallDeferred();
        return true;
    }

    public void RestoreBottomRight(int marginPixels)
    {
        Rect2I usable = UsableMonitorRect;
        PixelRect anchored = WindowPlacementPolicy.FirstLaunch(
            ToPixelRect(usable), _lastAppliedRect.Size.X, _lastAppliedRect.Size.Y);
        MoveTo(anchored);
    }

    public Rect2I ResolvePlacement(Rect2I? storedRect)
    {
        IReadOnlyList<Rect2I> monitors = _adapter.GetUsableMonitorRects();
        var usable = new List<PixelRect>(monitors.Count);
        foreach (Rect2I rect in monitors)
            usable.Add(ToPixelRect(rect));

        if (storedRect is Rect2I stored)
            return ToRect2I(WindowPlacementPolicy.Recover(ToPixelRect(stored), usable).Rect);

        PixelRect first = WindowPlacementPolicy.FirstLaunch(usable[0]);
        return ToRect2I(first);
    }

    private void EnterFullscreenOverlay(int monitorIndex)
    {
        Rect2I target = MonitorRect(monitorIndex);
        _lastAppliedRect = target;
        _lastAppliedSettings = _compactSettings with
        {
            Rect = target,
            Transparent = true,
            Borderless = true,
            Resizable = false,
            AlwaysOnTop = false,
        };
        TransparencyActive = true;

        if (_headless)
            return;

        Window window = GetWindow();
        window.CurrentScreen = monitorIndex;
        window.Borderless = true;
        window.Unresizable = true;
        window.Transparent = true;
        GetViewport().TransparentBg = true;

        // The overlay must not be topmost, and the flag has to be cleared before entering
        // fullscreen: Godot refuses always-on-top on a fullscreen window and clearing it
        // afterwards drops the window back to Windowed. A topmost owner also sits above its own
        // owned windows, which left the toolbar and panels visible through the transparent
        // pixels but never hit-testable. Covering the monitor already puts the overlay in front.
        window.AlwaysOnTop = false;
        window.Mode = Window.ModeEnum.Fullscreen;
        ApplyRenderSettings(_compactSettings);
    }

    private void EnterCompact()
    {
        WindowSettings recovered = RecoverWindowSettings(_compactSettings);
        _compactSettings = recovered;
        _lastAppliedSettings = recovered;
        _lastAppliedRect = recovered.Rect;
        TransparencyActive = recovered.Transparent && _adapter.TransparencyAvailable;

        if (!_headless)
            GetWindow().Mode = Window.ModeEnum.Windowed;
        ApplyWindowSettings(recovered);
    }

    private void ApplyCurrentInputPolicy()
    {
        bool passthrough = LayoutMode == WindowLayoutMode.FullscreenOverlay &&
            InputMode == DomainInputMode.Work;
        MainWindowMousePassthrough = passthrough;

        // Explicitly disable the obsolete WM_NCHITTEST path. The separate native toolbar
        // window owns all Work-mode controls while the main overlay passes every mouse event.
        _adapter.SetPlayModeCapture();
        if (!_headless)
            GetWindow().MousePassthrough = passthrough;
        LogWindowState("input-policy");
    }

    private void FinishLayoutTransition()
    {
        Rect2I rect = _pendingTransitionRect ?? CurrentWindowRect();
        _pendingTransitionRect = null;
        // Entering Work from the full-screen overlay drops to Compact first, and this deferred
        // finish lands a frame later — after the companion rect was applied. Adopting the
        // compact rect here made WorkCompanionRect stale, which the next drag or the 45s
        // recovery tick then pushed to the real window.
        if (WorkCompanionActive)
        {
            _suppressClientBoundsChanged = false;
            return;
        }
        _lastAppliedRect = rect;
        _lastAppliedSettings = _lastAppliedSettings with { Rect = rect };
        _suppressClientBoundsChanged = false;
        ClientBoundsChanged?.Invoke(rect);
        LogWindowState("layout-transition");
    }

    private Rect2I MonitorRect(int monitorIndex)
    {
        if (!_headless)
        {
            Vector2I position = DisplayServer.ScreenGetPosition(monitorIndex);
            Vector2I size = DisplayServer.ScreenGetSize(monitorIndex);
            if (size.X > 0 && size.Y > 0)
                return new Rect2I(position, size);
        }

        IReadOnlyList<Rect2I> monitors = _adapter.GetUsableMonitorRects();
        return monitors[Math.Clamp(monitorIndex, 0, monitors.Count - 1)];
    }

    private int ResolveMonitorIndex(int requested)
    {
        int count = _headless
            ? _adapter.GetUsableMonitorRects().Count
            : Math.Max(1, DisplayServer.GetScreenCount());
        return Math.Clamp(requested, 0, count - 1);
    }

    private void ApplyRenderSettings(WindowSettings settings)
    {
        var msaa = settings.MsaaLevel switch
        {
            2 => Viewport.Msaa.Msaa2X,
            4 => Viewport.Msaa.Msaa4X,
            8 => Viewport.Msaa.Msaa8X,
            _ => Viewport.Msaa.Disabled,
        };
        GetViewport().Msaa2D = RenderingServer.GetCurrentRenderingMethod() == "gl_compatibility"
            ? Viewport.Msaa.Disabled
            : msaa;
        GetViewport().Msaa3D = msaa;
        ApplyFrameSettings(settings.Vsync, settings.MaxFps);
    }

    /// <summary>
    /// Moves the buddy onto one monitor, centered in its usable area. Full-screen overlay just
    /// re-takes the layout on the new monitor instead, since it owns the whole screen.
    /// </summary>
    public bool MoveToMonitor(int monitorIndex)
    {
        int index = ResolveMonitorIndex(monitorIndex);
        if (LayoutMode == WindowLayoutMode.FullscreenOverlay)
            return TrySetLayoutMode(WindowLayoutMode.FullscreenOverlay, index);

        Rect2I monitor = MonitorRect(index);
        Vector2I size = CurrentWindowRect().Size;
        var position = new Vector2I(
            monitor.Position.X + Math.Max(0, (monitor.Size.X - size.X) / 2),
            monitor.Position.Y + Math.Max(0, (monitor.Size.Y - size.Y) / 2));
        ApplyWindowSettings(_lastAppliedSettings with { Rect = new Rect2I(position, size) });
        return true;
    }

    /// <summary>Topmost state on its own, without re-placing the window.</summary>
    public void SetAlwaysOnTop(bool alwaysOnTop)
    {
        _lastAppliedSettings = _lastAppliedSettings with { AlwaysOnTop = alwaysOnTop };
        if (LayoutMode == WindowLayoutMode.Compact)
            _compactSettings = _compactSettings with { AlwaysOnTop = alwaysOnTop };
        if (!_headless)
            GetWindow().AlwaysOnTop = alwaysOnTop;
    }

    /// <summary>
    /// The single place V-sync and the frame cap are applied, so the Settings rows change them
    /// live without re-applying (and re-placing) the whole window. A chosen cap wins over the
    /// V-sync-derived default; zero means "let V-sync decide".
    /// </summary>
    public void ApplyFrameSettings(bool vsync, int maxFps)
    {
        if (_headless)
            return;

        DisplayServer.WindowSetVsyncMode(vsync
            ? DisplayServer.VSyncMode.Enabled
            : DisplayServer.VSyncMode.Disabled);
        Engine.MaxFps = maxFps > 0 ? maxFps : vsync ? 0 : VsyncOffMaximumFps;
    }

    private void MoveTo(PixelRect rect)
    {
        if (WorkCompanionActive)
        {
            MoveWorkCompanion(new Vector2I(rect.X, rect.Y));
            return;
        }

        _lastAppliedRect = ToRect2I(rect);
        _lastAppliedSettings = _lastAppliedSettings with { Rect = _lastAppliedRect };
        if (LayoutMode == WindowLayoutMode.Compact)
            _compactSettings = _lastAppliedSettings;
        if (_headless)
            return;

        Window window = GetWindow();
        window.Size = new Vector2I(rect.Width, rect.Height);
        window.Position = new Vector2I(rect.X, rect.Y);
    }

    private Rect2I CurrentWindowRect()
    {
        if (_headless)
            return _lastAppliedRect;
        Window window = GetWindow();
        return new Rect2I(window.Position, window.Size);
    }

    private Rect2I ResolveContainingMonitor(Rect2I windowRect)
    {
        IReadOnlyList<Rect2I> monitors = _adapter.GetUsableMonitorRects();
        PixelRect window = ToPixelRect(windowRect);
        int best = 0;
        long bestArea = -1;
        for (int i = 0; i < monitors.Count; i++)
        {
            long area = window.IntersectionArea(ToPixelRect(monitors[i]));
            if (area > bestArea)
            {
                bestArea = area;
                best = i;
            }
        }
        return monitors[best];
    }

    private void OnSizeChanged()
    {
        Rect2I rect = CurrentWindowRect();
        if (WorkCompanionActive)
        {
            AdoptWorkCompanionSize(rect.Size);
            return;
        }

        _lastAppliedRect = rect;
        _lastAppliedSettings = CaptureWindowSettings();
        // Only record a compact rect from a settled compact window. TrySetLayoutMode flips
        // LayoutMode to Compact before EnterCompact leaves native fullscreen, so the resize
        // Windows sends on the way out still reports the monitor-sized overlay. Recording it
        // overwrote the saved compact rect with the whole screen, and because the placement
        // policy caps size at the monitor rather than a fixed ceiling, nothing rejected it —
        // compact then restored full-screen-sized forever after.
        // A maximized window is still Compact layout, but its rect must never become the saved
        // compact rect or restore/relaunch would come back monitor-sized.
        if (LayoutMode == WindowLayoutMode.Compact &&
            !WorkCompanionActive &&
            !_suppressClientBoundsChanged &&
            (_headless || GetWindow().Mode == Window.ModeEnum.Windowed))
            _compactSettings = _lastAppliedSettings;
        if (!_suppressClientBoundsChanged)
            ClientBoundsChanged?.Invoke(rect);
    }

    private void OnFocusExited() => WindowFocusLost?.Invoke();

    public override void _ExitTree()
    {
        if (!_headless && GodotObject.IsInstanceValid(GetWindow()))
            GetWindow().MousePassthrough = false;
        _adapter.Shutdown();
    }

    private static int MsaaLevel(Viewport.Msaa value) => value switch
    {
        Viewport.Msaa.Msaa2X => 2,
        Viewport.Msaa.Msaa4X => 4,
        Viewport.Msaa.Msaa8X => 8,
        _ => 0,
    };

    private static PixelRect ToPixelRect(Rect2I r) =>
        new(r.Position.X, r.Position.Y, r.Size.X, r.Size.Y);
    private static Rect2I ToRect2I(PixelRect r) =>
        new(r.X, r.Y, r.Width, r.Height);
}
