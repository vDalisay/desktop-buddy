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
/// adapter reports it is unavailable, without touching gameplay.
/// </summary>
public partial class DesktopWindowController : Node, IDesktopWindowService
{
    private const string Category = "Window";

    private IWindowsDesktopAdapter _adapter = new EmulatedWindowsDesktopAdapter();
    private bool _headless;
    private Rect2I _lastAppliedRect = WindowSettings.Defaults.Rect;
    private IReadOnlyList<Rect2I> _workModeHitRegions = Array.Empty<Rect2I>();

    public DomainInputMode InputMode { get; private set; } = DomainInputMode.Work;
    public bool TransparencyActive { get; private set; }

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

        if (_headless)
        {
            return;
        }

        Window window = GetWindow();
        window.Borderless = true;
        window.AlwaysOnTop = settings.AlwaysOnTop;
        window.Transparent = wantTransparent;
        GetViewport().TransparentBg = wantTransparent;
        window.Size = settings.Rect.Size;
        window.Position = settings.Rect.Position;

        ApplyRenderSettings(settings);

        if (!wantTransparent && settings.Transparent)
        {
            Log.Warn(Category, "Transparency unavailable; using opaque bordered fallback.");
        }
    }

    public void SetInputMode(DomainInputMode mode, IReadOnlyList<Rect2I> workModeHitRegions)
    {
        InputMode = mode;
        _workModeHitRegions = workModeHitRegions ?? Array.Empty<Rect2I>();
        if (mode == DomainInputMode.Work)
        {
            _adapter.SetWorkModeHitRegions(_workModeHitRegions);
        }
        else
        {
            _adapter.SetPlayModeCapture();
        }
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
        {
            usable.Add(ToPixelRect(rect));
        }

        if (storedRect is Rect2I stored)
        {
            return ToRect2I(WindowPlacementPolicy.Recover(ToPixelRect(stored), usable).Rect);
        }

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
        DisplayServer.WindowSetVsyncMode(settings.Vsync
            ? DisplayServer.VSyncMode.Enabled
            : DisplayServer.VSyncMode.Disabled);
    }

    private void MoveTo(PixelRect rect)
    {
        _lastAppliedRect = ToRect2I(rect);
        if (_headless)
        {
            return;
        }

        Window window = GetWindow();
        window.Size = new Vector2I(rect.Width, rect.Height);
        window.Position = new Vector2I(rect.X, rect.Y);
    }

    private Rect2I CurrentWindowRect()
    {
        if (_headless)
        {
            return _lastAppliedRect;
        }

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
        ClientBoundsChanged?.Invoke(rect);
    }

    private void OnFocusExited() => WindowFocusLost?.Invoke();

    public override void _ExitTree() => _adapter.Shutdown();

    private static PixelRect ToPixelRect(Rect2I r) => new(r.Position.X, r.Position.Y, r.Size.X, r.Size.Y);

    private static Rect2I ToRect2I(PixelRect r) => new(r.X, r.Y, r.Width, r.Height);
}
