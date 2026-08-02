using Godot;

namespace DesktopBuddy.Platform;

/// <summary>
/// Immutable window/presentation settings the desktop shell applies as a unit.
/// Position and size are desktop pixels. Editor mode temporarily changes border,
/// resizability, transparency, and topmost state, then restores the captured unit.
/// </summary>
public readonly record struct WindowSettings(
    Rect2I Rect,
    bool Transparent,
    bool AlwaysOnTop,
    int MsaaLevel,
    bool Vsync,
    bool Borderless = true,
    bool Resizable = false)
{
    public static WindowSettings Defaults => new(
        new Rect2I(0, 0, 480, 360),
        Transparent: true,
        AlwaysOnTop: true,
        MsaaLevel: 2,
        Vsync: true,
        Borderless: true,
        Resizable: false);
}
