using System;
using System.Collections.Generic;

namespace DesktopBuddy.Domain.Platform;

/// <summary>Godot-free integer rectangle in desktop pixels (screen space).</summary>
public readonly record struct PixelRect(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;
    public int Bottom => Y + Height;

    /// <summary>Area of the overlap with <paramref name="other"/>; zero if disjoint.</summary>
    public long IntersectionArea(PixelRect other)
    {
        int left = Math.Max(X, other.X);
        int top = Math.Max(Y, other.Y);
        int right = Math.Min(Right, other.Right);
        int bottom = Math.Min(Bottom, other.Bottom);
        if (right <= left || bottom <= top)
        {
            return 0;
        }

        return (long)(right - left) * (bottom - top);
    }

    public bool Contains(PixelRect inner) =>
        inner.X >= X && inner.Y >= Y && inner.Right <= Right && inner.Bottom <= Bottom;
}

/// <summary>Resolved window placement: where the window sits and on which monitor.</summary>
public readonly record struct WindowPlacement(PixelRect Rect, int MonitorIndex);

/// <summary>
/// Confirmed window placement policy (`DECISIONS.md` "Overlay and Interface"):
/// first launch anchors the window 16 px inside the lower-right of the usable
/// work area; a stored position/size is clamped back into a usable monitor rect
/// so off-screen positions (monitor removal, topology change) always recover.
/// Default size is 480x360, minimum 360x270, and the maximum is the usable size
/// of the monitor that contains the window — there is no fixed pixel ceiling.
///
/// This is pure geometry so resize/restore behavior can be exhaustively tested
/// without an engine runtime; the Godot window service converts between Godot
/// <c>Rect2I</c> and <see cref="PixelRect"/> and supplies the usable rects.
/// </summary>
public static class WindowPlacementPolicy
{
    public const int DefaultWidth = 480;
    public const int DefaultHeight = 360;
    public const int MinimumWidth = 360;
    public const int MinimumHeight = 270;
    public const int FirstLaunchMargin = 16;

    /// <summary>
    /// Lower-right first-launch anchor inside <paramref name="usable"/>, 16 px
    /// from the right and bottom edges. The size is clamped to the minimum and to
    /// the usable size; the position is clamped so the window stays fully inside.
    /// </summary>
    public static PixelRect FirstLaunch(PixelRect usable, int width = DefaultWidth, int height = DefaultHeight)
    {
        (int w, int h) = ClampSize(width, height, usable.Width, usable.Height);
        int x = usable.Right - w - FirstLaunchMargin;
        int y = usable.Bottom - h - FirstLaunchMargin;
        // Never push the origin outside the usable area when the margin does not fit.
        x = Math.Max(usable.X, x);
        y = Math.Max(usable.Y, y);
        return new PixelRect(x, y, w, h);
    }

    /// <summary>
    /// Recover a stored window rect against the current monitor topology. The
    /// window is placed on the usable monitor it overlaps most; if it overlaps
    /// none (fully off-screen), it re-anchors lower-right on the primary monitor.
    /// Size is clamped to the chosen monitor; position is clamped so the whole
    /// window is on-screen.
    /// </summary>
    public static WindowPlacement Recover(PixelRect stored, IReadOnlyList<PixelRect> usableMonitors)
    {
        if (usableMonitors is null || usableMonitors.Count == 0)
        {
            throw new ArgumentException("At least one usable monitor rect is required.", nameof(usableMonitors));
        }

        int bestIndex = -1;
        long bestArea = 0;
        for (int i = 0; i < usableMonitors.Count; i++)
        {
            long area = stored.IntersectionArea(usableMonitors[i]);
            if (area > bestArea)
            {
                bestArea = area;
                bestIndex = i;
            }
        }

        if (bestIndex < 0)
        {
            // Fully off-screen: re-anchor on the primary monitor as if first launch,
            // preserving the stored size where it fits.
            PixelRect reanchored = FirstLaunch(usableMonitors[0], stored.Width, stored.Height);
            return new WindowPlacement(reanchored, 0);
        }

        PixelRect clamped = ClampToMonitor(stored, usableMonitors[bestIndex]);
        return new WindowPlacement(clamped, bestIndex);
    }

    /// <summary>Clamp a window rect so it lies fully inside a single usable monitor rect.</summary>
    public static PixelRect ClampToMonitor(PixelRect window, PixelRect usable)
    {
        (int w, int h) = ClampSize(window.Width, window.Height, usable.Width, usable.Height);
        int x = Math.Clamp(window.X, usable.X, usable.Right - w);
        int y = Math.Clamp(window.Y, usable.Y, usable.Bottom - h);
        return new PixelRect(x, y, w, h);
    }

    private static (int Width, int Height) ClampSize(int width, int height, int maxWidth, int maxHeight)
    {
        int w = Math.Clamp(width, MinimumWidth, Math.Max(MinimumWidth, maxWidth));
        int h = Math.Clamp(height, MinimumHeight, Math.Max(MinimumHeight, maxHeight));
        return (w, h);
    }
}
