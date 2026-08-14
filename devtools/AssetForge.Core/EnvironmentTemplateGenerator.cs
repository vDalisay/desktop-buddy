namespace DesktopBuddy.AssetForge.Core;

public static class EnvironmentTemplateSpace
{
    public const int CanvasSize = 1024;
    public const int FloorY = 880;
    public const int CenterX = 512;
    public const int SafeLeft = 180;
    public const int SafeRight = 844;
    public const int SafeTop = 100;
    public const int SafeBottom = FloorY;
    public const int LampShadeTop = 170;
    public const int LampShadeBottom = 430;
    public const int LampEmitterX = 512;
    public const int LampEmitterY = 300;
}

public static class EnvironmentTemplateGenerator
{
    public static byte[] CreateLampPng()
    {
        int size = EnvironmentTemplateSpace.CanvasSize;
        byte[] pixels = new byte[size * size * 4];
        DrawRect(pixels, size,
            EnvironmentTemplateSpace.SafeLeft, EnvironmentTemplateSpace.SafeTop,
            EnvironmentTemplateSpace.SafeRight, EnvironmentTemplateSpace.SafeBottom,
            2, 84, 112, 128, 58);
        DashedVertical(pixels, size, EnvironmentTemplateSpace.CenterX, 70, 930, 12, 2, 56, 124, 168, 90);
        DashedHorizontal(pixels, size, EnvironmentTemplateSpace.FloorY, 100, 924, 12, 2, 66, 116, 72, 120);

        // Suggested floor/base contact zone.
        DrawRect(pixels, size, 390, 820, 634, EnvironmentTemplateSpace.FloorY, 3, 48, 128, 72, 90);
        // Suggested shade volume; art may extend outside it but the box helps scale consistency.
        DrawRect(pixels, size, 300, EnvironmentTemplateSpace.LampShadeTop, 724, EnvironmentTemplateSpace.LampShadeBottom, 3, 45, 142, 202, 85);
        DrawCircle(pixels, size, EnvironmentTemplateSpace.LampEmitterX, EnvironmentTemplateSpace.LampEmitterY,
            54, 3, 238, 173, 55, 125);
        DrawCross(pixels, size, EnvironmentTemplateSpace.LampEmitterX, EnvironmentTemplateSpace.LampEmitterY,
            18, 2, 238, 173, 55, 145);

        // Small translucent Buddy scale reference at lower-left. It is guide-only and must be
        // hidden/removed before importing clean source art.
        DrawReferenceDisc(pixels, size, 250, 700, 70, 104, 184, 235, 30);
        DrawReferenceDisc(pixels, size, 250, 805, 88, 104, 184, 235, 24);
        return PngCodec.EncodeRgba8(new RgbaImage(size, size, pixels));
    }

    private static void DrawReferenceDisc(byte[] pixels, int size, int cx, int cy, int radius, byte r, byte g, byte b, byte a)
    {
        int r2 = radius * radius;
        for (int y = Math.Max(0, cy - radius); y <= Math.Min(size - 1, cy + radius); y++)
        for (int x = Math.Max(0, cx - radius); x <= Math.Min(size - 1, cx + radius); x++)
        {
            int dx = x - cx, dy = y - cy;
            if (dx * dx + dy * dy <= r2) Write(pixels, size, x, y, r, g, b, a);
        }
    }

    private static void DrawCircle(byte[] pixels, int size, int cx, int cy, int radius, int thickness, byte r, byte g, byte b, byte a)
    {
        int inner = Math.Max(0, radius - thickness), outer = radius + thickness;
        int inner2 = inner * inner, outer2 = outer * outer;
        for (int y = Math.Max(0, cy - outer); y <= Math.Min(size - 1, cy + outer); y++)
        for (int x = Math.Max(0, cx - outer); x <= Math.Min(size - 1, cx + outer); x++)
        {
            int dx = x - cx, dy = y - cy, d2 = dx * dx + dy * dy;
            if (d2 >= inner2 && d2 <= outer2) Write(pixels, size, x, y, r, g, b, a);
        }
    }

    private static void DrawRect(byte[] pixels, int size, int x0, int y0, int x1, int y1, int t, byte r, byte g, byte b, byte a)
    {
        Fill(pixels, size, x0, y0, x1, y0 + t, r, g, b, a);
        Fill(pixels, size, x0, y1 - t, x1, y1, r, g, b, a);
        Fill(pixels, size, x0, y0, x0 + t, y1, r, g, b, a);
        Fill(pixels, size, x1 - t, y0, x1, y1, r, g, b, a);
    }

    private static void DrawCross(byte[] pixels, int size, int cx, int cy, int radius, int t, byte r, byte g, byte b, byte a)
    {
        Fill(pixels, size, cx - radius, cy - t, cx + radius + 1, cy + t + 1, r, g, b, a);
        Fill(pixels, size, cx - t, cy - radius, cx + t + 1, cy + radius + 1, r, g, b, a);
    }

    private static void DashedVertical(byte[] pixels, int size, int x, int y0, int y1, int dash, int t, byte r, byte g, byte b, byte a)
    {
        for (int y = y0; y < y1; y += dash * 2)
            Fill(pixels, size, x - t, y, x + t + 1, Math.Min(y + dash, y1), r, g, b, a);
    }

    private static void DashedHorizontal(byte[] pixels, int size, int y, int x0, int x1, int dash, int t, byte r, byte g, byte b, byte a)
    {
        for (int x = x0; x < x1; x += dash * 2)
            Fill(pixels, size, x, y - t, Math.Min(x + dash, x1), y + t + 1, r, g, b, a);
    }

    private static void Fill(byte[] pixels, int size, int x0, int y0, int x1, int y1, byte r, byte g, byte b, byte a)
    {
        for (int y = Math.Max(0, y0); y < Math.Min(size, y1); y++)
        for (int x = Math.Max(0, x0); x < Math.Min(size, x1); x++) Write(pixels, size, x, y, r, g, b, a);
    }

    private static void Write(byte[] pixels, int size, int x, int y, byte r, byte g, byte b, byte a)
    {
        int i = (y * size + x) * 4;
        pixels[i] = r; pixels[i + 1] = g; pixels[i + 2] = b; pixels[i + 3] = a;
    }
}
