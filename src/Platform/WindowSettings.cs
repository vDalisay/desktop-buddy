using Godot;

namespace DesktopBuddy.Platform;

/// <summary>
/// Immutable window/presentation settings the desktop shell applies as a unit
/// (`ARCHITECTURE.md` §5, §14; `DECISIONS.md` presentation defaults). Position and
/// size are desktop pixels; transparency/topmost are window flags; MSAA and V-sync
/// are the rendering choices exposed in settings. Zoom is a view transform owned by
/// the sandbox camera, not the window, so it is deliberately not here. Editor mode
/// temporarily changes border, resizability, transparency, and topmost state, then
/// restores the captured unit.
/// </summary>
public readonly record struct WindowSettings(
    Rect2I Rect,
    bool Transparent,
    bool AlwaysOnTop,
    int MsaaLevel,
    bool Vsync,
    bool Borderless = true,
    bool Resizable = false,
    int MaxFps = 0)
{
    /// <summary>First-run defaults: 480x360 at the origin, transparent, topmost, 2x MSAA, V-sync on.</summary>
    public static WindowSettings Defaults => new(
        new Rect2I(0, 0, 480, 360),
        Transparent: true,
        AlwaysOnTop: true,
        MsaaLevel: 2,
        Vsync: true,
        Borderless: true,
        Resizable: false);
}
