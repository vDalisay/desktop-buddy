namespace DesktopBuddy.AssetForge.Core;

/// <summary>
/// Produces developer-only 1024x1024 guide PNGs. These are reference layers, not valid final
/// cosmetic sources: the guide layer must be hidden/removed before the artist exports source.png.
/// </summary>
public static class AuthoringTemplateGenerator
{
    public static byte[] CreateGlassesTemplatePng()
    {
        const int size = AssetForgeGenerator.SourceSize;
        byte[] pixels = new byte[size * size * 4];

        // Actual source-art coordinate convention used by the Glasses v1 generator.
        DrawEllipse(pixels, size, 512, 500, 310, 360, 2, 120, 120, 120, 95);
        DrawDashedVertical(pixels, size, 512, 155, 845, 8, 2, 90, 150, 220, 145);
        DrawDashedHorizontal(pixels, size, 505, 165, 859, 8, 2, 90, 150, 220, 145);

        // Eye centers and recommended frame envelope.
        DrawCross(pixels, size, 360, 505, 15, 2, 40, 185, 125, 200);
        DrawCross(pixels, size, 664, 505, 15, 2, 40, 185, 125, 200);
        DrawRect(pixels, size, 190, 355, 834, 655, 2, 225, 170, 50, 155);

        // Temple-root guide zones. The generator derives actual roots from the outer mask bounds.
        DrawRect(pixels, size, 175, 425, 245, 585, 2, 205, 85, 130, 160);
        DrawRect(pixels, size, 779, 425, 849, 585, 2, 205, 85, 130, 160);

        return PngCodec.EncodeRgba8(new RgbaImage(size, size, pixels));
    }

    private static void DrawRect(byte[] pixels, int size, int x0, int y0, int x1, int y1, int thickness, byte r, byte g, byte b, byte a)
    {
        Fill(pixels, size, x0, y0, x1, y0 + thickness, r, g, b, a);
        Fill(pixels, size, x0, y1 - thickness, x1, y1, r, g, b, a);
        Fill(pixels, size, x0, y0, x0 + thickness, y1, r, g, b, a);
        Fill(pixels, size, x1 - thickness, y0, x1, y1, r, g, b, a);
    }

    private static void DrawCross(byte[] pixels, int size, int cx, int cy, int radius, int thickness, byte r, byte g, byte b, byte a)
    {
        Fill(pixels, size, cx - radius, cy - thickness / 2, cx + radius + 1, cy + (thickness + 1) / 2, r, g, b, a);
        Fill(pixels, size, cx - thickness / 2, cy - radius, cx + (thickness + 1) / 2, cy + radius + 1, r, g, b, a);
    }

    private static void DrawDashedVertical(byte[] pixels, int size, int x, int y0, int y1, int dash, int thickness, byte r, byte g, byte b, byte a)
    {
        for (int y = y0; y < y1; y += dash * 2)
            Fill(pixels, size, x - thickness / 2, y, x + (thickness + 1) / 2, Math.Min(y + dash, y1), r, g, b, a);
    }

    private static void DrawDashedHorizontal(byte[] pixels, int size, int y, int x0, int x1, int dash, int thickness, byte r, byte g, byte b, byte a)
    {
        for (int x = x0; x < x1; x += dash * 2)
            Fill(pixels, size, x, y - thickness / 2, Math.Min(x + dash, x1), y + (thickness + 1) / 2, r, g, b, a);
    }

    private static void DrawEllipse(byte[] pixels, int size, int cx, int cy, int rx, int ry, int thickness, byte r, byte g, byte b, byte a)
    {
        for (int y = Math.Max(0, cy - ry - thickness); y <= Math.Min(size - 1, cy + ry + thickness); y++)
        for (int x = Math.Max(0, cx - rx - thickness); x <= Math.Min(size - 1, cx + rx + thickness); x++)
        {
            double nx = (double)(x - cx) / rx;
            double ny = (double)(y - cy) / ry;
            double d = nx * nx + ny * ny;
            double tolerance = Math.Max((double)thickness / rx, (double)thickness / ry) * 2.0;
            if (Math.Abs(d - 1.0) <= tolerance) Set(pixels, size, x, y, r, g, b, a);
        }
    }

    private static void Fill(byte[] pixels, int size, int x0, int y0, int x1, int y1, byte r, byte g, byte b, byte a)
    {
        x0 = Math.Clamp(x0, 0, size); x1 = Math.Clamp(x1, 0, size);
        y0 = Math.Clamp(y0, 0, size); y1 = Math.Clamp(y1, 0, size);
        for (int y = y0; y < y1; y++)
        for (int x = x0; x < x1; x++) Set(pixels, size, x, y, r, g, b, a);
    }

    private static void Set(byte[] pixels, int size, int x, int y, byte r, byte g, byte b, byte a)
    {
        int index = (y * size + x) * 4;
        pixels[index] = r;
        pixels[index + 1] = g;
        pixels[index + 2] = b;
        pixels[index + 3] = a;
    }
}
