using System;
using DesktopBuddy.Diagnostics;
using Godot;

namespace DesktopBuddy.Platform;

/// <summary>
/// Selects the desktop adapter: the real <see cref="WindowsDesktopAdapter"/> only
/// on a Windows standalone run with a live display server, and the deterministic
/// <see cref="EmulatedWindowsDesktopAdapter"/> for headless, editor, browser, and
/// non-Windows runs. Browser builds deliberately report transparency as unavailable:
/// their canvas is the presentation surface rather than a composited desktop window,
/// so enabling per-pixel window transparency can leave the WebGL framebuffer clear.
/// This keeps native P/Invoke off the CI/headless/browser path entirely
/// (`ARCHITECTURE.md` §9) — the automated gates never touch native code. If native
/// attachment throws, it falls back to the emulated adapter so the shell still
/// composes (the §16 opaque/full-capture fallback intent).
/// </summary>
public static class WindowsDesktopAdapterFactory
{
    public static IWindowsDesktopAdapter Create()
    {
        bool headless = DisplayServer.GetName() == "headless";
        if (OperatingSystem.IsBrowser())
        {
            // A browser canvas is not a desktop compositor surface. Treat transparency as
            // unsupported so DesktopWindowController keeps both Window.Transparent and the
            // root viewport's TransparentBg disabled for Web exports.
            return new EmulatedWindowsDesktopAdapter(transparencyAvailable: false);
        }

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
