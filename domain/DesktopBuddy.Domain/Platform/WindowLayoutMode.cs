namespace DesktopBuddy.Domain.Platform;

/// <summary>
/// Physical desktop-window footprint. Compact owns only the buddy box; FullscreenOverlay
/// covers one monitor and may independently capture all input or pass empty areas through.
/// </summary>
public enum WindowLayoutMode
{
    Compact,
    FullscreenOverlay,
}
