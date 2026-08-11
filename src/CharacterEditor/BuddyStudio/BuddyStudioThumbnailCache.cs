using System;
using System.Collections.Generic;
using DesktopBuddy.Domain.Characters;
using Godot;

namespace DesktopBuddy.CharacterEditor.BuddyStudio;

/// <summary>
/// Main-thread cache of trusted appearance thumbnails. The thumbnail silhouettes are keyed by the
/// same stable shipped feature IDs as the runtime renderers, so the tile depicts the selected
/// appearance instead of a generic sort-order motif. It never touches or mutates the live rig.
/// </summary>
internal static class BuddyStudioThumbnailCache
{
    private const int Width = 96;
    private const int Height = 72;
    private static readonly Dictionary<string, Texture2D> Cache = new(StringComparer.Ordinal);

    public static int Count => Cache.Count;

    public static Texture2D For(CosmeticDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (Cache.TryGetValue(definition.Id, out Texture2D? cached))
            return cached;

        Image image = Image.CreateEmpty(Width, Height, false, Image.Format.Rgba8);
        image.Fill(new Color("d8d4c8"));
        Color ink = new("183042");
        Color paper = new("f4f1e8");
        Color accent = definition.ColorChannels.Count > 0
            ? ToColor(definition.ColorChannels[0].DefaultColor)
            : new Color("8293a1");

        DrawTrustedBase(image, definition.Slot, ink, paper);
        DrawTrustedAppearance(image, definition.Id, ink, accent);

        Texture2D texture = ImageTexture.CreateFromImage(image);
        Cache.Add(definition.Id, texture);
        return texture;
    }

    private static void DrawTrustedBase(Image image, CharacterFeatureSlot slot, Color ink, Color paper)
    {
        switch (slot)
        {
            case CharacterFeatureSlot.Accessories:
            case CharacterFeatureSlot.Tops:
                Ellipse(image, 48, 38, 24, 27, paper);
                EllipseRing(image, 48, 38, 24, 27, ink, 2);
                break;
            case CharacterFeatureSlot.Shoes:
                Circle(image, 35, 45, 15, paper);
                Circle(image, 61, 45, 15, paper);
                Ring(image, 35, 45, 15, ink, 2);
                Ring(image, 61, 45, 15, ink, 2);
                break;
            default:
                Circle(image, 48, 35, 25, paper);
                Ring(image, 48, 35, 25, ink, 2);
                break;
        }
    }

    private static void DrawTrustedAppearance(Image image, string id, Color ink, Color accent)
    {
        switch (id)
        {
            case CharacterFeatureIds.FaceClassicPlate:
                // The plate itself is the trusted base preview.
                break;

            case CharacterFeatureIds.HairNone:
            case CharacterFeatureIds.NoseNone:
            case CharacterFeatureIds.EarsNone:
            case CharacterFeatureIds.GlassesNone:
            case CharacterFeatureIds.HeadwearNone:
            case CharacterFeatureIds.TopNone:
            case CharacterFeatureIds.ShoesNone:
            case CharacterFeatureIds.AccentNone:
                break;

            case CharacterFeatureIds.HairShortSweep:
                // Mirrors the runtime's three overlapping sweep ellipsoids.
                Ellipse(image, 35, 18, 17, 8, accent);
                Ellipse(image, 50, 15, 21, 9, accent);
                Ellipse(image, 66, 20, 12, 6, accent);
                Line(image, 25, 22, 71, 22, ink, 2);
                break;

            case CharacterFeatureIds.EyesSoftOval:
                Ellipse(image, 38, 34, 6, 8, accent);
                Ellipse(image, 58, 34, 6, 8, accent);
                EllipseRing(image, 38, 34, 6, 8, ink, 2);
                EllipseRing(image, 58, 34, 6, 8, ink, 2);
                break;
            case CharacterFeatureIds.EyesRoundDot:
                Circle(image, 38, 34, 7, accent);
                Circle(image, 58, 34, 7, accent);
                Ring(image, 38, 34, 7, ink, 2);
                Ring(image, 58, 34, 7, ink, 2);
                break;
            case CharacterFeatureIds.EyesHorizontalLed:
                Rect(image, 30, 31, 16, 6, accent);
                Rect(image, 50, 31, 16, 6, accent);
                OutlineRect(image, 30, 31, 16, 6, ink, 2);
                OutlineRect(image, 50, 31, 16, 6, ink, 2);
                break;

            case CharacterFeatureIds.BrowsSoftArc:
                Arc(image, 38, 29, 9, 5, 200, 340, accent, 3);
                Arc(image, 58, 29, 9, 5, 200, 340, accent, 3);
                break;
            case CharacterFeatureIds.BrowsStraight:
                Line(image, 30, 28, 44, 27, accent, 3);
                Line(image, 52, 27, 66, 28, accent, 3);
                break;
            case CharacterFeatureIds.BrowsSegmented:
                Line(image, 30, 28, 36, 27, accent, 3);
                Line(image, 40, 27, 44, 28, accent, 3);
                Line(image, 52, 28, 56, 27, accent, 3);
                Line(image, 60, 27, 66, 28, accent, 3);
                break;

            case CharacterFeatureIds.NoseButton:
                Ellipse(image, 48, 39, 6, 5, accent);
                EllipseRing(image, 48, 39, 6, 5, ink, 2);
                break;

            case CharacterFeatureIds.MouthRounded:
                // Neutral rounded family: two connected lobes, deliberately reading as a small "3"/cat-like shape.
                Arc(image, 43, 47, 7, 5, 190, 350, accent, 3);
                Arc(image, 53, 47, 7, 5, 190, 350, accent, 3);
                break;
            case CharacterFeatureIds.MouthPixel:
                Line(image, 34, 50, 48, 42, accent, 3);
                Line(image, 48, 42, 62, 50, accent, 3);
                break;
            case CharacterFeatureIds.MouthLine:
                Line(image, 34, 47, 62, 47, accent, 3);
                break;

            case CharacterFeatureIds.EarsRoundTabs:
                Ellipse(image, 21, 36, 6, 11, accent);
                Ellipse(image, 75, 36, 6, 11, accent);
                EllipseRing(image, 21, 36, 6, 11, ink, 2);
                EllipseRing(image, 75, 36, 6, 11, ink, 2);
                break;

            case CharacterFeatureIds.AccentPanel:
                Rect(image, 33, 27, 30, 22, accent);
                OutlineRect(image, 33, 27, 30, 22, ink, 2);
                break;
            case CharacterFeatureIds.AccentChevron:
                Line(image, 31, 31, 48, 46, accent, 5);
                Line(image, 48, 46, 65, 31, accent, 5);
                break;
            case CharacterFeatureIds.AccentBolts:
                Circle(image, 38, 31, 4, accent);
                Circle(image, 58, 31, 4, accent);
                Circle(image, 38, 45, 4, accent);
                Circle(image, 58, 45, 4, accent);
                break;

            case CharacterFeatureIds.GlassesWorkClassic:
                // Same rectangular-frame vocabulary as the trusted runtime glasses renderer.
                OutlineRect(image, 28, 29, 18, 13, accent, 3);
                OutlineRect(image, 50, 29, 18, 13, accent, 3);
                Rect(image, 45, 34, 6, 3, accent);
                break;

            case CharacterFeatureIds.HeadwearSoftCap:
                Ellipse(image, 48, 17, 24, 10, accent);
                Rect(image, 48, 20, 21, 5, accent);
                Line(image, 25, 22, 69, 22, ink, 2);
                break;

            case CharacterFeatureIds.TopUtilityBib:
                Rect(image, 34, 30, 28, 25, accent);
                Rect(image, 34, 23, 6, 13, accent);
                Rect(image, 56, 23, 6, 13, accent);
                OutlineRect(image, 34, 30, 28, 25, ink, 2);
                break;

            case CharacterFeatureIds.ShoesSoftSteps:
                Ellipse(image, 34, 49, 16, 10, accent);
                Ellipse(image, 62, 49, 16, 10, accent);
                EllipseRing(image, 34, 49, 16, 10, ink, 2);
                EllipseRing(image, 62, 49, 16, 10, ink, 2);
                break;

            default:
                // Closed shipped catalogue: unknown IDs should never reach this cache. A visible
                // fallback keeps a future mismatch obvious rather than silently inventing art.
                Line(image, 36, 25, 60, 49, ink, 3);
                Line(image, 60, 25, 36, 49, ink, 3);
                break;
        }
    }

    private static void Rect(Image image, int x, int y, int width, int height, Color color)
    {
        for (int py = Math.Max(0, y); py < Math.Min(Height, y + height); py++)
        for (int px = Math.Max(0, x); px < Math.Min(Width, x + width); px++)
            image.SetPixel(px, py, color);
    }

    private static void OutlineRect(Image image, int x, int y, int width, int height, Color color, int thickness)
    {
        Rect(image, x, y, width, thickness, color);
        Rect(image, x, y + height - thickness, width, thickness, color);
        Rect(image, x, y, thickness, height, color);
        Rect(image, x + width - thickness, y, thickness, height, color);
    }

    private static void Circle(Image image, int cx, int cy, int radius, Color color)
    {
        int squared = radius * radius;
        for (int y = Math.Max(0, cy - radius); y <= Math.Min(Height - 1, cy + radius); y++)
        for (int x = Math.Max(0, cx - radius); x <= Math.Min(Width - 1, cx + radius); x++)
        {
            int dx = x - cx;
            int dy = y - cy;
            if (dx * dx + dy * dy <= squared)
                image.SetPixel(x, y, color);
        }
    }

    private static void Ellipse(Image image, int cx, int cy, int radiusX, int radiusY, Color color)
    {
        for (int y = Math.Max(0, cy - radiusY); y <= Math.Min(Height - 1, cy + radiusY); y++)
        for (int x = Math.Max(0, cx - radiusX); x <= Math.Min(Width - 1, cx + radiusX); x++)
        {
            double dx = (x - cx) / (double)Math.Max(1, radiusX);
            double dy = (y - cy) / (double)Math.Max(1, radiusY);
            if (dx * dx + dy * dy <= 1.0)
                image.SetPixel(x, y, color);
        }
    }

    private static void Ring(Image image, int cx, int cy, int radius, Color color, int thickness)
    {
        int outer = radius * radius;
        int innerRadius = Math.Max(0, radius - thickness);
        int inner = innerRadius * innerRadius;
        for (int y = Math.Max(0, cy - radius); y <= Math.Min(Height - 1, cy + radius); y++)
        for (int x = Math.Max(0, cx - radius); x <= Math.Min(Width - 1, cx + radius); x++)
        {
            int dx = x - cx;
            int dy = y - cy;
            int distance = dx * dx + dy * dy;
            if (distance <= outer && distance >= inner)
                image.SetPixel(x, y, color);
        }
    }

    private static void EllipseRing(Image image, int cx, int cy, int radiusX, int radiusY, Color color, int thickness)
    {
        int innerX = Math.Max(1, radiusX - thickness);
        int innerY = Math.Max(1, radiusY - thickness);
        for (int y = Math.Max(0, cy - radiusY); y <= Math.Min(Height - 1, cy + radiusY); y++)
        for (int x = Math.Max(0, cx - radiusX); x <= Math.Min(Width - 1, cx + radiusX); x++)
        {
            double outerX = (x - cx) / (double)Math.Max(1, radiusX);
            double outerY = (y - cy) / (double)Math.Max(1, radiusY);
            double innerDx = (x - cx) / (double)innerX;
            double innerDy = (y - cy) / (double)innerY;
            if (outerX * outerX + outerY * outerY <= 1.0 && innerDx * innerDx + innerDy * innerDy >= 1.0)
                image.SetPixel(x, y, color);
        }
    }

    private static void Line(Image image, int x0, int y0, int x1, int y1, Color color, int thickness)
    {
        int dx = Math.Abs(x1 - x0);
        int sx = x0 < x1 ? 1 : -1;
        int dy = -Math.Abs(y1 - y0);
        int sy = y0 < y1 ? 1 : -1;
        int error = dx + dy;
        while (true)
        {
            Circle(image, x0, y0, Math.Max(1, thickness / 2), color);
            if (x0 == x1 && y0 == y1)
                break;
            int twice = 2 * error;
            if (twice >= dy)
            {
                error += dy;
                x0 += sx;
            }
            if (twice <= dx)
            {
                error += dx;
                y0 += sy;
            }
        }
    }

    private static void Arc(
        Image image,
        int cx,
        int cy,
        int radiusX,
        int radiusY,
        double startDegrees,
        double endDegrees,
        Color color,
        int thickness)
    {
        const int segments = 18;
        int previousX = cx + (int)Math.Round(Math.Cos(startDegrees * Math.PI / 180.0) * radiusX);
        int previousY = cy + (int)Math.Round(Math.Sin(startDegrees * Math.PI / 180.0) * radiusY);
        for (int index = 1; index <= segments; index++)
        {
            double t = index / (double)segments;
            double degrees = startDegrees + (endDegrees - startDegrees) * t;
            int x = cx + (int)Math.Round(Math.Cos(degrees * Math.PI / 180.0) * radiusX);
            int y = cy + (int)Math.Round(Math.Sin(degrees * Math.PI / 180.0) * radiusY);
            Line(image, previousX, previousY, x, y, color, thickness);
            previousX = x;
            previousY = y;
        }
    }

    private static Color ToColor(Rgba32 color) => new(
        color.R / 255f,
        color.G / 255f,
        color.B / 255f,
        color.A / 255f);
}
