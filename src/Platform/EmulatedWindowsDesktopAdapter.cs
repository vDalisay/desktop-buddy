using System;
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
    private Rect2I[] _hitRegions = new Rect2I[16];
    private int _hitRegionCount;

    public bool IsNative => false;
    public bool TransparencyAvailable { get; }
    public bool IsWindowVisible { get; private set; } = true;

    public event Action? SystemSuspending;
    public event Action? SystemResumed;
    public event Action<bool>? SessionLockChanged;

    /// <summary>Deterministic §24 stimuli so headless scenarios drive the real seam
    /// instead of calling the lifecycle coordinator directly.</summary>
    public void RaiseSuspending() => SystemSuspending?.Invoke();

    public void RaiseResumed() => SystemResumed?.Invoke();

    public void RaiseSessionLockChanged(bool locked) => SessionLockChanged?.Invoke(locked);

    /// <summary>Regions from the last <see cref="SetWorkModeHitRegions"/> call (test observability).</summary>
    public IReadOnlyList<Rect2I> LastWorkModeHitRegions => new ArraySegment<Rect2I>(
        _hitRegions, 0, _hitRegionCount);

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
        if (regions.Count > _hitRegions.Length)
            System.Array.Resize(ref _hitRegions, regions.Count);
        for (int index = 0; index < regions.Count; index++)
            _hitRegions[index] = regions[index];
        _hitRegionCount = regions.Count;
        PlayModeCaptured = false;
    }

    public void SetPlayModeCapture() => PlayModeCaptured = true;

    public void SetWindowVisible(bool visible) => IsWindowVisible = visible;

    public void Shutdown() => Log.Info("Platform", "EmulatedWindowsDesktopAdapter shutdown.");
}
