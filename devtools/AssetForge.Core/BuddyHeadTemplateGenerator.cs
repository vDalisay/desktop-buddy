namespace DesktopBuddy.AssetForge.Core;

/// <summary>
/// Generates the glasses@2 coloring-page guide. The low-opacity Buddy head is a reference layer;
/// artists should hide/remove it before exporting the clean source PNG.
/// </summary>
public static class BuddyHeadTemplateGenerator
{
    public static byte[] CreatePng()
    {
        const int size = GlassesTemplateSpace.CanvasSize;
        byte[] pixels = new byte[size * size * 4];
        DrawHead(pixels, size);

        int cx = (int)MathF.Round(GlassesTemplateSpace.HeadCenterX);
        int cy = (int)MathF.Round(GlassesTemplateSpace.HeadCenterY);
        int radius = (int)MathF.Round(GlassesTemplateSpace.HeadRadiusPixels);
        int eyeY = (int)MathF.Round(GlassesTemplateSpace.RecommendedEyeLineY);
        int leftEyeX = (int)MathF.Round(GlassesTemplateSpace.LeftEyeCenterX);
        int rightEyeX = (int)MathF.Round(GlassesTemplateSpace.RightEyeCenterX);

        DrawCircleOutline(pixels, size, cx, cy, radius, 3, 24, 48, 66, 92);
        DrawDashedVertical(pixels, size, cx, cy - radius + 18, cy + radius - 18, 9, 2, 72, 103, 124, 62);
        DrawDashedHorizontal(pixels, size, eyeY, cx - radius + 26, cx + radius - 26, 10, 2, 47, 118, 166, 88);
        DrawCross(pixels, size, leftEyeX, eyeY, 14, 2, 28, 133, 202, 118);
        DrawCross(pixels, size, rightEyeX, eyeY, 14, 2, 28, 133, 202, 118);
        return PngCodec.EncodeRgba8(new RgbaImage(size, size, pixels));
    }

    private static void DrawHead(byte[] pixels, int size)
    {
        float cx = GlassesTemplateSpace.HeadCenterX;
        float cy = GlassesTemplateSpace.HeadCenterY;
        float radius = GlassesTemplateSpace.HeadRadiusPixels;
        int x0 = Math.Max(0, (int)(cx - radius));
        int x1 = Math.Min(size - 1, (int)(cx + radius));
        int y0 = Math.Max(0, (int)(cy - radius));
        int y1 = Math.Min(size - 1, (int)(cy + radius));
        for (int y = y0; y <= y1; y++)
        for (int x = x0; x <= x1; x++)
        {
            float nx = (x + 0.5f - cx) / radius;
            float ny = (y + 0.5f - cy) / radius;
            float distanceSquared = nx * nx + ny * ny;
            if (distanceSquared > 1.0f) continue;
            float edge = MathF.Sqrt(distanceSquared);
            float light = Math.Clamp(1.02f - nx * 0.07f - ny * 0.10f, 0.84f, 1.10f);
            Write(pixels, size, x, y,
                (byte)Math.Clamp((int)(108 * light), 0, 255),
                (byte)Math.Clamp((int)(187 * light), 0, 255),
                (byte)Math.Clamp((int)(239 * light), 0, 255),
                (byte)Math.Clamp((int)(34 + edge * 9), 0, 255));
        }
    }

    private static void DrawCircleOutline(byte[] pixels, int size, int cx, int cy, int radius, int thickness, byte r, byte g, byte b, byte a)
    {
        float inner = radius - thickness;
        float outer = radius + thickness;
        float innerSquared = inner * inner;
        float outerSquared = outer * outer;
        for (int y = Math.Max(0, cy - radius - thickness); y <= Math.Min(size - 1, cy + radius + thickness); y++)
        for (int x = Math.Max(0, cx - radius - thickness); x <= Math.Min(size - 1, cx + radius + thickness); x++)
        {
            float dx = x - cx;
            float dy = y - cy;
            float d2 = dx * dx + dy * dy;
            if (d2 >= innerSquared && d2 <= outerSquared) Write(pixels, size, x, y, r, g, b, a);
        }
    }

    private static void DrawCross(byte[] pixels, int size, int cx, int cy, int radius, int thickness, byte r, byte g, byte b, byte a)
    {
        Fill(pixels, size, cx - radius, cy - thickness, cx + radius, cy + thickness, r, g, b, a);
        Fill(pixels, size, cx - thickness, cy - radius, cx + thickness, cy + radius, r, g, b, a);
    }

    private static void DrawDashedVertical(byte[] pixels, int size, int x, int y0, int y1, int dash, int thickness, byte r, byte g, byte b, byte a)
    {
        for (int y = y0; y < y1; y += dash * 2) Fill(pixels, size, x - thickness, y, x + thickness, Math.Min(y + dash, y1), r, g, b, a);
    }

    private static void DrawDashedHorizontal(byte[] pixels, int size, int y, int x0, int x1, int dash, int thickness, byte r, byte g, byte b, byte a)
    {
        for (int x = x0; x < x1; x += dash * 2) Fill(pixels, size, x, y - thickness, Math.Min(x + dash, x1), y + thickness, r, g, b, a);
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
