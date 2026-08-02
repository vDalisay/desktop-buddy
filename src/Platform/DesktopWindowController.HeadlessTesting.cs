using System;
using Godot;

namespace DesktopBuddy.Platform;

public partial class DesktopWindowController
{
    /// <summary>
    /// Headless scenario seam for an OS client-bounds notification. It is rejected on a
    /// real display so production code cannot synthesize resize events.
    /// </summary>
    public void NotifyHeadlessClientBoundsChanged(Rect2I bounds)
    {
        if (DisplayServer.GetName() != "headless")
            throw new InvalidOperationException("Synthetic bounds changes are headless-only.");
        _lastAppliedRect = bounds;
        _lastAppliedSettings = _lastAppliedSettings with { Rect = bounds };
        ClientBoundsChanged?.Invoke(bounds);
    }

    /// <summary>Headless scenario seam for an OS focus-loss notification.</summary>
    public void NotifyHeadlessFocusLost()
    {
        if (DisplayServer.GetName() != "headless")
            throw new InvalidOperationException("Synthetic focus loss is headless-only.");
        WindowFocusLost?.Invoke();
    }
}
