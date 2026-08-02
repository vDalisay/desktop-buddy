using System;
using System.Collections.Generic;
using DesktopBuddy.Diagnostics;
using DesktopBuddy.Domain.Platform;
using Godot;
using DomainInputMode = DesktopBuddy.Domain.Platform.InputMode;

namespace DesktopBuddy.Platform;

/// <summary>
/// Godot-side desktop window service (`ARCHITECTURE.md` §9). It applies window
/// flags, size, and position through Godot APIs, resolves placement with the
/// Godot-free <see cref="WindowPlacementPolicy"/>, and delegates the native-only
/// concerns (monitor topology, DPI, Work-Mode passthrough) to an injected
/// <see cref="IWindowsDesktopAdapter"/>. Editor/headless runs inject the emulated
/// adapter, so composition and the mode-transition journeys stay green with no
/// Windows dependency. Transparency falls back to an opaque bordered box when the
/// adapter reports it is unavailable, without touching gameplay. All window state
/// is applied and captured as one immutable <see cref="WindowSettings"/> unit, so
/// editor mode cannot restore only a subset.
/// </summary>
public partial class DesktopWindowController : Node, IDesktopWindowService
{
    private const string Category = "Window";
    // V-sync already caps rendering at the display rate. With V-sync disabled,
    // cap this always-on overlay so it cannot free-spin a desktop GPU.
    private const int VsyncOffMaximumFps = 240;

    private IWindowsDesktopAdapter _adapter = new EmulatedWindowsDesktopAdapter();
    private bool _headless;
    private Rect2I _lastAppliedRect = WindowSettings.Defaults.Rect;
    private WindowSettings _lastAppliedSettings = WindowSettings.Defaults;
    private IReadOnlyList<Rect2I> _workModeHitRegions = Array.Empty<Rect2I>();

    public DomainInputMode InputMode { get; private set; } = DomainInputMode.Work;
    public bool TransparencyActive { get; private set; }
    /// <summary>
    /// The configured platform adapter, so the composition root can bind the §24
    /// suspend/resume/session-lock seam without owning adapter selection itself.
    /// </summary>
    public IWindowsDesktopAdapter Adapter => _adapter;
    public WindowSettings CurrentSettings => CaptureWindowSettings();
    public int AppliedSettingsCount { get; private set; }

    public event Action<Rect2I>? ClientBoundsChanged;
    public event Action? WindowFocusLost;

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

        Log.Info(Category, $"DesktopWindowController ready (native={_adapter.IsNative} headless={_headless}).");
    }

    public Rect2I UsableMonitorRect => ResolveContainingMonitor(CurrentWindowRect());

    public void ApplyWindowSettings(WindowSettings settings)
    {
        bool wantTransparent = settings.Transparent && _adapter.TransparencyAvailable;
        TransparencyActive = wantTransparent;
        _lastAppliedRect = settings.Rect;
        _lastAppliedSettings = settings with { Transparent = wantTransparent };
        AppliedSettingsCount++;

        if (_headless)
            return;

        Window window = GetWindow();
        window.Borderless = settings.Borderless;
        window.Unresizable = !settings.Resizable;
        window.AlwaysOnTop = settings.AlwaysOnTop;
        window.Transparent = wantTransparent;
        GetViewport().TransparentBg = wantTransparent;
        window.Size = settings.Rect.Size;
        window.Position = settings.Rect.Position;
        ApplyRenderSettings(settings);

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
            !window.Unresizable);
    }

    /// <summary>Recover a captured rect against the current monitor topology.</summary>
    public WindowSettings RecoverWindowSettings(WindowSettings captured) =>
        captured with { Rect = ResolvePlacement(captured.Rect) };

    public void SetInputMode(DomainInputMode mode, IReadOnlyList<Rect2I> workModeHitRegions)
    {
        InputMode = mode;
        _workModeHitRegions = workModeHitRegions ?? Array.Empty<Rect2I>();
        if (mode == DomainInputMode.Work)
            _adapter.SetWorkModeHitRegions(_workModeHitRegions);
        else
            _adapter.SetPlayModeCapture();
    }

    public void RestoreBottomRight(int marginPixels)
    {
        Rect2I usable = UsableMonitorRect;
        PixelRect anchored = WindowPlacementPolicy.FirstLaunch(
            ToPixelRect(usable), _lastAppliedRect.Size.X, _lastAppliedRect.Size.Y);
        MoveTo(anchored);
    }

    /// <summary>
    /// Resolve the launch placement: first launch anchors lower-right on the primary
    /// monitor; a stored rect is recovered against the current monitor topology.
    /// </summary>
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

    private void ApplyRenderSettings(WindowSettings settings)
    {
        var msaa = settings.MsaaLevel switch
        {
            2 => Viewport.Msaa.Msaa2X,
            4 => Viewport.Msaa.Msaa4X,
            8 => Viewport.Msaa.Msaa8X,
            _ => Viewport.Msaa.Disabled,
        };
        GetViewport().Msaa2D = msaa;
        GetViewport().Msaa3D = msaa; // M3.5: 3D presentation pass shares the MSAA setting.
        DisplayServer.WindowSetVsyncMode(settings.Vsync
            ? DisplayServer.VSyncMode.Enabled
            : DisplayServer.VSyncMode.Disabled);
        Engine.MaxFps = settings.Vsync ? 0 : VsyncOffMaximumFps;
    }

    private void MoveTo(PixelRect rect)
    {
        _lastAppliedRect = ToRect2I(rect);
        _lastAppliedSettings = _lastAppliedSettings with { Rect = _lastAppliedRect };
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
        _lastAppliedRect = rect;
        _lastAppliedSettings = CaptureWindowSettings();
        ClientBoundsChanged?.Invoke(rect);
    }

    private void OnFocusExited() => WindowFocusLost?.Invoke();

    public override void _ExitTree() => _adapter.Shutdown();

    private static int MsaaLevel(Viewport.Msaa value) => value switch
    {
        Viewport.Msaa.Msaa2X => 2,
        Viewport.Msaa.Msaa4X => 4,
        Viewport.Msaa.Msaa8X => 8,
        _ => 0,
    };

    private static PixelRect ToPixelRect(Rect2I r) => new(r.Position.X, r.Position.Y, r.Size.X, r.Size.Y);
    private static Rect2I ToRect2I(PixelRect r) => new(r.X, r.Y, r.Width, r.Height);
}
