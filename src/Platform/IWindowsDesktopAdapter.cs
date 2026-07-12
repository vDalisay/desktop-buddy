using System.Collections.Generic;
using Godot;

namespace DesktopBuddy.Platform;

/// <summary>
/// Native Windows seam behind the desktop window service (`ARCHITECTURE.md` §9).
/// The controller uses Godot APIs first for window flags/size/position; this
/// adapter owns the parts Godot cannot do portably or correctly on Windows:
/// usable-monitor topology, per-monitor DPI, and Work-Mode pointer passthrough
/// via native hit testing. Tray, global hotkey, launch-at-login, and the §24
/// lifecycle messages are added with the native implementation (Task 4). The
/// emulated adapter satisfies this seam deterministically for editor/headless/CI.
/// </summary>
public interface IWindowsDesktopAdapter
{
    /// <summary>True only for the real native adapter; false for the emulated one.</summary>
    bool IsNative { get; }

    /// <summary>Whether per-pixel transparency is available on this display path.</summary>
    bool TransparencyAvailable { get; }

    /// <summary>Usable work-area rects (taskbar excluded) for every monitor, in virtual-desktop pixels.</summary>
    IReadOnlyList<Rect2I> GetUsableMonitorRects();

    /// <summary>DPI scale factor for a monitor (1.0 = 96 DPI = 100%).</summary>
    float GetDpiScale(int monitorIndex);

    /// <summary>
    /// Install the Work-Mode passthrough regions: these client rects hit-test as
    /// solid; every other transparent pixel returns HTTRANSPARENT to the desktop.
    /// </summary>
    void SetWorkModeHitRegions(IReadOnlyList<Rect2I> regions);

    /// <summary>Capture the whole client box (Play Mode); no pointer passthrough.</summary>
    void SetPlayModeCapture();

    /// <summary>Restore any subclassed window procedure and native state on shutdown.</summary>
    void Shutdown();
}
