namespace DesktopBuddy.AssetForge.Core;

/// <summary>
/// Canonical 1024x1024 coloring-page coordinates for Buddy visual replacements. Source placement
/// is literal: these same constants drive both the exported guide and generated mesh coordinates.
/// The artist removes the guide layer before importing the clean source PNG.
/// </summary>
public static class PartReplacementTemplateSpace
{
    public const int CanvasSize = 1024;

    public const float TorsoCenterX = 512f;
    public const float TorsoCenterY = 500f;
    public const float TorsoRadiusPixels = 300f;

    public const float FootCenterX = 512f;
    public const float FootCenterY = 520f;
    public const float FootRadiusPixels = 270f;
    public const int FootGroundY = 800;

    public static System.Numerics.Vector2 MapPixel(AssetCategory category, float x, float y) => category switch
    {
        AssetCategory.TorsoShape => new(
            (x - TorsoCenterX) / TorsoRadiusPixels,
            (TorsoCenterY - y) / TorsoRadiusPixels),
        AssetCategory.FootShape => new(
            (x - FootCenterX) / FootRadiusPixels,
            (FootCenterY - y) / FootRadiusPixels),
        _ => throw new ArgumentOutOfRangeException(nameof(category), category, "No Buddy replacement template space exists for this category."),
    };
}

public static class PartReplacementTemplateGenerator
{
    public static byte[] CreateTorsoPng()
    {
        int size = PartReplacementTemplateSpace.CanvasSize;
        byte[] pixels = new byte[size * size * 4];
        int cx = (int)PartReplacementTemplateSpace.TorsoCenterX;
        int cy = (int)PartReplacementTemplateSpace.TorsoCenterY;
        int radius = (int)PartReplacementTemplateSpace.TorsoRadiusPixels;

        DrawReferenceDisc(pixels, size, cx, cy, radius, 104, 184, 235, 38);
        DrawCircle(pixels, size, cx, cy, radius, 3, 35, 79, 106, 100);
        DashedVertical(pixels, size, cx, 120, 900, 11, 2, 56, 124, 168, 88);
        DrawCross(pixels, size, cx, cy - radius, 15, 2, 45, 142, 202, 125); // head/neck direction
        DrawCross(pixels, size, cx - radius, cy, 12, 2, 45, 142, 202, 95);  // left connector direction
        DrawCross(pixels, size, cx + radius, cy, 12, 2, 45, 142, 202, 95);  // right connector direction
        DrawCross(pixels, size, cx, cy + radius, 15, 2, 45, 142, 202, 125); // lower/foot direction
        DrawRect(pixels, size, 145, 120, 879, 900, 2, 84, 112, 128, 58);
        return PngCodec.EncodeRgba8(new RgbaImage(size, size, pixels));
    }

    public static byte[] CreateFootPng()
    {
        int size = PartReplacementTemplateSpace.CanvasSize;
        byte[] pixels = new byte[size * size * 4];
        int cx = (int)PartReplacementTemplateSpace.FootCenterX;
        int cy = (int)PartReplacementTemplateSpace.FootCenterY;
        int radius = (int)PartReplacementTemplateSpace.FootRadiusPixels;

        DrawReferenceDisc(pixels, size, cx, cy, radius, 104, 184, 235, 38);
        DrawCircle(pixels, size, cx, cy, radius, 3, 35, 79, 106, 100);
        DashedVertical(pixels, size, cx, 120, PartReplacementTemplateSpace.FootGroundY, 11, 2, 56, 124, 168, 88);
        DashedHorizontal(pixels, size, PartReplacementTemplateSpace.FootGroundY, 120, 904, 12, 2, 66, 116, 72, 100);
        DrawCross(pixels, size, cx, cy - radius, 16, 2, 45, 142, 202, 125); // ankle
        DrawRect(pixels, size, 175, 170, 849, PartReplacementTemplateSpace.FootGroundY, 2, 84, 112, 128, 58);

        // Forward-direction arrow. It is a guide only and is removed from the clean source.
        int arrowY = cy;
        Fill(pixels, size, cx + 55, arrowY - 2, cx + 170, arrowY + 3, 45, 142, 202, 90);
        Fill(pixels, size, cx + 150, arrowY - 18, cx + 170, arrowY + 19, 45, 142, 202, 90);
        return PngCodec.EncodeRgba8(new RgbaImage(size, size, pixels));
    }

    private static void DrawReferenceDisc(byte[] pixels, int size, int cx, int cy, int radius, byte r, byte g, byte b, byte a)
    {
        int r2 = radius * radius;
        for (int y = Math.Max(0, cy - radius); y <= Math.Min(size - 1, cy + radius); y++)
        for (int x = Math.Max(0, cx - radius); x <= Math.Min(size - 1, cx + radius); x++)
        {
            int dx = x - cx;
            int dy = y - cy;
            if (dx * dx + dy * dy <= r2) Write(pixels, size, x, y, r, g, b, a);
        }
    }

    private static void DrawCircle(byte[] pixels, int size, int cx, int cy, int radius, int thickness, byte r, byte g, byte b, byte a)
    {
        int inner = Math.Max(0, radius - thickness);
        int outer = radius + thickness;
        int inner2 = inner * inner;
        int outer2 = outer * outer;
        for (int y = Math.Max(0, cy - outer); y <= Math.Min(size - 1, cy + outer); y++)
        for (int x = Math.Max(0, cx - outer); x <= Math.Min(size - 1, cx + outer); x++)
        {
            int dx = x - cx;
            int dy = y - cy;
            int d2 = dx * dx + dy * dy;
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
        Fill(pixels, size, cx - radius, cy - t, cx + radius, cy + t + 1, r, g, b, a);
        Fill(pixels, size, cx - t, cy - radius, cx + t + 1, cy + radius, r, g, b, a);
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
        pixels[i] = r;
        pixels[i + 1] = g;
        pixels[i + 2] = b;
        pixels[i + 3] = a;
    }
}
