using System.Collections.Generic;
using DesktopBuddy.Diagnostics;
using Godot;

namespace DesktopBuddy.Platform;

/// <summary>
/// Deterministic non-native adapter for editor/headless/CI runs
/// (`ARCHITECTURE.md` §9). It reports a configurable monitor topology and records
/// the last hit-region request so headless journeys can assert the shell's
/// Work/Play wiring without a real Windows message pump. It performs no native
/// passthrough — CI never exercises real pointer passthrough (that is the
/// owner-manual §5 matrix).
/// </summary>
public sealed class EmulatedWindowsDesktopAdapter : IWindowsDesktopAdapter
{
    private readonly List<Rect2I> _monitors;
    private readonly float _dpiScale;

    public bool IsNative => false;
    public bool TransparencyAvailable { get; }

    /// <summary>Regions from the last <see cref="SetWorkModeHitRegions"/> call (test observability).</summary>
    public IReadOnlyList<Rect2I> LastWorkModeHitRegions { get; private set; } = System.Array.Empty<Rect2I>();

    /// <summary>True after the last Play-Mode capture request (test observability).</summary>
    public bool PlayModeCaptured { get; private set; }

    public EmulatedWindowsDesktopAdapter(
        IReadOnlyList<Rect2I>? monitors = null,
        float dpiScale = 1.0f,
        bool transparencyAvailable = true)
    {
        _monitors = monitors is { Count: > 0 }
            ? new List<Rect2I>(monitors)
            : new List<Rect2I> { new(0, 0, 1920, 1040) };
        _dpiScale = dpiScale;
        TransparencyAvailable = transparencyAvailable;
    }

    public IReadOnlyList<Rect2I> GetUsableMonitorRects() => _monitors;

    public float GetDpiScale(int monitorIndex) => _dpiScale;

    public void SetWorkModeHitRegions(IReadOnlyList<Rect2I> regions)
    {
        LastWorkModeHitRegions = new List<Rect2I>(regions);
        PlayModeCaptured = false;
    }

    public void SetPlayModeCapture() => PlayModeCaptured = true;

    public void Shutdown() => Log.Info("Platform", "EmulatedWindowsDesktopAdapter shutdown.");
}
