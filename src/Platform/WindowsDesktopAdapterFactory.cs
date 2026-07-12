using System;
using DesktopBuddy.Diagnostics;
using Godot;

namespace DesktopBuddy.Platform;

/// <summary>
/// Selects the desktop adapter: the real <see cref="WindowsDesktopAdapter"/> only
/// on a Windows standalone run with a live display server, and the deterministic
/// <see cref="EmulatedWindowsDesktopAdapter"/> for headless, editor, and non-Windows
/// runs. This keeps native P/Invoke off the CI/headless path entirely
/// (`ARCHITECTURE.md` §9) — the automated gates never touch native code. If native
/// attachment throws, it falls back to the emulated adapter so the shell still
/// composes (the §16 opaque/full-capture fallback intent).
/// </summary>
public static class WindowsDesktopAdapterFactory
{
    public static IWindowsDesktopAdapter Create()
    {
        bool headless = DisplayServer.GetName() == "headless";
        if (!OperatingSystem.IsWindows() || headless)
        {
            return new EmulatedWindowsDesktopAdapter();
        }

        try
        {
            return new WindowsDesktopAdapter();
        }
        catch (Exception e)
        {
            Log.Error("WinAdapter", $"Native adapter attach failed ({e.Message}); using emulated fallback.");
            return new EmulatedWindowsDesktopAdapter();
        }
    }
}
