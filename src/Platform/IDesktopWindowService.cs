using System;
using System.Collections.Generic;
using DesktopBuddy.Domain.Platform;
using Godot;

namespace DesktopBuddy.Platform;

/// <summary>
/// Application-facing desktop window seam (`ARCHITECTURE.md` §5). Owns window
/// flags, placement, Work/Play input mode, and the usable-monitor query, and
/// exposes them without referencing buddy/tool components. Native Windows work
/// (hit testing, tray, hotkey, DPI) sits behind <see cref="IWindowsDesktopAdapter"/>;
/// editor/headless runs use the emulated adapter so this seam stays testable.
/// </summary>
public interface IDesktopWindowService
{
    InputMode InputMode { get; }

    /// <summary>Usable work-area rect of the monitor currently containing the window.</summary>
    Rect2I UsableMonitorRect { get; }

    /// <summary>True when per-pixel transparency is active; false means the opaque fallback.</summary>
    bool TransparencyActive { get; }

    void ApplyWindowSettings(WindowSettings settings);

    /// <summary>
    /// Set the input mode. In Work Mode <paramref name="workModeHitRegions"/> are the
    /// only client areas that receive pointer input (buddy, menu/HUD, borders); other
    /// transparent pixels pass through. In Play Mode the whole box captures input.
    /// </summary>
    void SetInputMode(InputMode mode, IReadOnlyList<Rect2I> workModeHitRegions);

    /// <summary>Re-anchor the window 16 px inside the lower-right of the usable work area.</summary>
    void RestoreBottomRight(int marginPixels);

    event Action<Rect2I> ClientBoundsChanged;
    event Action WindowFocusLost;
}
