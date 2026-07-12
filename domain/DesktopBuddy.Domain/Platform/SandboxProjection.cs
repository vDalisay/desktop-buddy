using System;

namespace DesktopBuddy.Domain.Platform;

/// <summary>
/// Projects sandbox world rectangles into client pixels for the desktop shell's
/// Work-Mode hit regions. The sandbox camera sits at the room centre with world
/// zoom <c>Z</c>, and the room is derived as <c>client size / Z</c>
/// (`ARCHITECTURE.md` §21), so world (0,0) maps to client (0,0) and a world span
/// scales by <c>Z</c> into pixels: <c>client = world · Z</c>. Keeping this pure
/// lets the native <see cref="IWindowsDesktopAdapter"/> receive client-pixel rects
/// (which is what `WM_NCHITTEST` needs) while the geometry stays engine-free and
/// exhaustively testable.
/// </summary>
public static class SandboxProjection
{
    public static PixelRect SandboxRectToClient(double worldX, double worldY, double worldWidth, double worldHeight, double zoom)
    {
        if (!(zoom > 0.0) || !double.IsFinite(zoom))
        {
            throw new ArgumentOutOfRangeException(nameof(zoom), zoom, "Zoom must be finite and positive.");
        }

        int x = (int)Math.Round(worldX * zoom, MidpointRounding.AwayFromZero);
        int y = (int)Math.Round(worldY * zoom, MidpointRounding.AwayFromZero);
        int w = (int)Math.Round(worldWidth * zoom, MidpointRounding.AwayFromZero);
        int h = (int)Math.Round(worldHeight * zoom, MidpointRounding.AwayFromZero);
        return new PixelRect(x, y, Math.Max(0, w), Math.Max(0, h));
    }
}
