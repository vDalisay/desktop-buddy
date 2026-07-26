using System;

namespace DesktopBuddy.App;

/// <summary>
/// Drains finalizable managed wrappers while the Godot native runtime is still
/// available. Godot 4.6 collection wrappers expose no public dispose method;
/// allowing their finalizers to run after native shutdown can access freed
/// interop state on Windows.
/// </summary>
public static class GodotInteropShutdown
{
    public static void PrepareForQuit()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }
}
