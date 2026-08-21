using System;
using System.Collections.Generic;
using Godot;

namespace DesktopBuddy.UI;

/// <summary>
/// Chunky 16×16 reward icons in the Win98 palette, generated in code and scaled 4× with
/// nearest filtering by the popup. Owner artwork can replace any of them by adding a
/// Texture2D-compatible asset at <c>res://assets/ui/reward_icons/{slug}.svg</c>; callers do
/// not change. All pixels are original project art.
///
/// ponytail: the tiny drawing primitives are a second copy of the ones in
/// PaintToolIconProvider. Extract a shared pixel helper only if a third icon set appears.
/// </summary>
public static class RewardIconProvider
{
    public const string Milestone = "milestone";
    public const string Trophy = "trophy";

    private const int Size = 16;
    private static readonly Dictionary<string, Texture2D> Cache = new(StringComparer.Ordinal);

    private static readonly Color Ink = Colors.Black;
    private static readonly Color Grey = Color.Color8(128, 128, 128);
    private static readonly Color Silver = Color.Color8(192, 192, 192);
    private static readonly Color White = Colors.White;
    private static readonly Color Navy = Color.Color8(0, 0, 128);
    private static readonly Color Blue = Color.Color8(0, 0, 255);
    private static readonly Color Red = Color.Color8(255, 0, 0);
    private static readonly Color Maroon = Color.Color8(128, 0, 0);
    private static readonly Color Yellow = Color.Color8(255, 255, 0);
    private static readonly Color Olive = Color.Color8(128, 128, 0);
    private static readonly Color Green = Color.Color8(0, 128, 0);
    private static readonly Color Lime = Color.Color8(0, 255, 0);
    private static readonly Color Brown = Color.Color8(160, 96, 32);

    /// <summary>Icon for a catalogue content id such as <c>tool.baseball_bat</c>.</summary>
    public static Texture2D ForContent(string contentId)
    {
        if (string.IsNullOrWhiteSpace(contentId))
            return For(string.Empty);
        int dot = contentId.LastIndexOf('.');
        return For(dot >= 0 && dot < contentId.Length - 1 ? contentId[(dot + 1)..] : contentId);
    }

    public static Texture2D For(string slug)
    {
        if (Cache.TryGetValue(slug, out Texture2D? cached))
            return cached;

        // Authored art wins over the drawn fallback. The .png set is the one the capture
        // scenario renders from each tool's own shipped mesh, which is what the player
        // recognises; .svg stays ahead of it as the hand-authored override.
        Texture2D? authored = null;
        foreach (string extension in new[] { "svg", "png" })
        {
            string path = $"res://assets/ui/reward_icons/{slug}.{extension}";
            if (!ResourceLoader.Exists(path))
                continue;
            authored = GD.Load<Texture2D>(path);
            if (authored is not null)
                break;
        }

        Texture2D texture = authored ?? Generate(slug);
        Cache[slug] = texture;
        return texture;
    }

    private static Texture2D Generate(string slug)
    {
        Image image = Image.CreateFromData(Size, Size, false, Image.Format.Rgba8, new byte[Size * Size * 4]);
        switch (slug)
        {
            case "grab":
            case "power_grab":
                // Open hand: palm plus four fingers and a thumb.
                Rect(image, 5, 7, 11, 13, Silver);
                Outline(image, 5, 7, 11, 13, Ink);
                for (int x = 6; x <= 10; x += 2)
                {
                    Rect(image, x, 3, x, 7, Silver);
                    Rect(image, x - 1, 3, x - 1, 7, Ink);
                    Pixel(image, x + 1, 3, Ink);
                    Pixel(image, x, 2, Ink);
                }
                Rect(image, 3, 9, 4, 11, Silver);
                Pixel(image, 2, 9, Ink); Pixel(image, 2, 10, Ink); Pixel(image, 2, 11, Ink);
                if (slug == "power_grab")
                {
                    Pixel(image, 13, 3, Yellow); Pixel(image, 12, 4, Yellow); Pixel(image, 13, 4, Yellow);
                    Pixel(image, 12, 5, Yellow); Pixel(image, 14, 5, Yellow); Pixel(image, 13, 6, Yellow);
                }
                break;

            case "pet": // Brush.
                Line(image, 4, 12, 11, 5, Brown, 2);
                Rect(image, 10, 3, 13, 6, Grey);
                Outline(image, 10, 3, 13, 6, Ink);
                Rect(image, 2, 12, 5, 14, Navy);
                Pixel(image, 1, 14, Navy);
                break;

            case "tickle": // Feather.
                Line(image, 4, 13, 12, 3, Ink, 1);
                for (int step = 0; step < 7; step++)
                {
                    int x = 5 + step;
                    int y = 12 - step;
                    Pixel(image, x + 1, y - 1, White); Pixel(image, x + 2, y - 1, White);
                    Pixel(image, x - 1, y + 1, Silver); Pixel(image, x - 2, y + 1, Silver);
                }
                break;

            case "baseball_bat":
                Line(image, 3, 13, 12, 4, Brown, 2);
                Pixel(image, 12, 3, Brown); Pixel(image, 13, 3, Brown); Pixel(image, 13, 4, Brown);
                Rect(image, 2, 13, 4, 15, Ink);
                break;

            case "boxing_glove":
                Disc(image, 8, 7, 5, Red);
                Rect(image, 3, 8, 6, 11, Red);
                Rect(image, 4, 12, 11, 14, Maroon);
                Outline(image, 4, 12, 11, 14, Ink);
                Pixel(image, 5, 6, White); Pixel(image, 6, 5, White);
                break;

            case "baseball":
                Disc(image, 8, 8, 6, White);
                Ring(image, 8, 8, 6, Ink);
                Pixel(image, 5, 5, Red); Pixel(image, 4, 7, Red); Pixel(image, 4, 9, Red); Pixel(image, 5, 11, Red);
                Pixel(image, 11, 5, Red); Pixel(image, 12, 7, Red); Pixel(image, 12, 9, Red); Pixel(image, 11, 11, Red);
                break;

            case "soccer_ball":
                Disc(image, 8, 8, 6, White);
                Ring(image, 8, 8, 6, Ink);
                Rect(image, 7, 6, 9, 8, Ink);
                Pixel(image, 5, 10, Ink); Pixel(image, 6, 11, Ink);
                Pixel(image, 11, 10, Ink); Pixel(image, 10, 11, Ink);
                Pixel(image, 8, 3, Ink); Pixel(image, 3, 8, Ink); Pixel(image, 13, 8, Ink);
                break;

            case "meal":
                Rect(image, 2, 4, 13, 6, Brown);   // Top bun.
                Rect(image, 2, 7, 13, 8, Lime);    // Lettuce.
                Rect(image, 2, 9, 13, 10, Maroon); // Patty.
                Rect(image, 2, 11, 13, 12, Brown); // Bottom bun.
                Outline(image, 2, 4, 13, 12, Ink);
                Pixel(image, 5, 3, Brown); Pixel(image, 9, 3, Brown);
                break;

            case "drink":
                Rect(image, 4, 5, 11, 5, White);
                Outline(image, 4, 5, 11, 14, Ink);
                Rect(image, 5, 6, 10, 13, Red);
                Rect(image, 5, 6, 6, 13, Silver);
                Line(image, 9, 5, 12, 1, White, 1);
                break;

            case "repair_kit":
                Rect(image, 2, 5, 13, 13, Silver);
                Outline(image, 2, 5, 13, 13, Ink);
                Rect(image, 6, 3, 9, 4, Grey);
                Outline(image, 6, 3, 9, 4, Ink);
                Rect(image, 7, 7, 8, 12, Red);
                Rect(image, 5, 9, 10, 10, Red);
                break;

            case "grenade":
                Disc(image, 8, 10, 5, Olive);
                Ring(image, 8, 10, 5, Ink);
                Rect(image, 7, 3, 9, 5, Grey);
                Outline(image, 7, 3, 9, 5, Ink);
                Rect(image, 10, 2, 11, 6, Grey);
                Pixel(image, 5, 2, Silver); Pixel(image, 4, 3, Silver); Pixel(image, 4, 4, Silver);
                break;

            case "nerf_blaster":
            case "pistol":
            {
                Color body = slug == "pistol" ? Grey : Color.Color8(255, 128, 0);
                Rect(image, 2, 5, 12, 8, body);
                Outline(image, 2, 5, 12, 8, Ink);
                Rect(image, 3, 9, 6, 13, body);
                Outline(image, 3, 9, 6, 13, Ink);
                Rect(image, 12, 6, 14, 7, slug == "pistol" ? Ink : Blue);
                break;
            }

            case "shotgun":
                Rect(image, 5, 5, 15, 7, Grey);
                Outline(image, 5, 5, 15, 7, Ink);
                Rect(image, 5, 8, 12, 9, Ink);
                Line(image, 2, 11, 6, 7, Brown, 3);
                Rect(image, 0, 10, 3, 14, Brown);
                break;

            case "fire_sprayer":
                Disc(image, 8, 10, 5, Red);
                Disc(image, 8, 11, 3, Color.Color8(255, 160, 0));
                Pixel(image, 8, 9, Yellow); Pixel(image, 8, 10, Yellow); Pixel(image, 7, 11, Yellow); Pixel(image, 9, 11, Yellow);
                Pixel(image, 8, 2, Red); Pixel(image, 7, 3, Red); Pixel(image, 8, 3, Red);
                Pixel(image, 7, 4, Red); Pixel(image, 9, 4, Red); Pixel(image, 8, 5, Red); Pixel(image, 9, 5, Red);
                break;

            case Milestone: // Session milestone: a filled star.
                Star(image, Yellow, Olive);
                break;

            case Trophy: // Lifetime milestone: the "achievement" cup.
                Rect(image, 4, 2, 11, 7, Yellow);
                Outline(image, 4, 2, 11, 7, Olive);
                Pixel(image, 5, 8, Yellow); Pixel(image, 10, 8, Yellow);
                Rect(image, 6, 8, 9, 9, Yellow);
                Rect(image, 2, 3, 3, 5, Yellow); Pixel(image, 3, 6, Yellow);
                Rect(image, 12, 3, 13, 5, Yellow); Pixel(image, 12, 6, Yellow);
                Rect(image, 7, 10, 8, 11, Olive);
                Rect(image, 4, 12, 11, 13, Yellow);
                Outline(image, 4, 12, 11, 13, Olive);
                Pixel(image, 6, 4, White); Pixel(image, 6, 5, White);
                break;

            default: // A wrapped gift: something arrived and we do not know what.
                Rect(image, 2, 6, 13, 14, Silver);
                Outline(image, 2, 6, 13, 14, Ink);
                Rect(image, 2, 5, 13, 7, Green);
                Rect(image, 7, 5, 8, 14, Green);
                Pixel(image, 6, 3, Green); Pixel(image, 5, 4, Green); Pixel(image, 6, 4, Green);
                Pixel(image, 9, 3, Green); Pixel(image, 10, 4, Green); Pixel(image, 9, 4, Green);
                break;
        }

        var texture = ImageTexture.CreateFromImage(image);
        return texture;
    }

    private static void Pixel(Image image, int x, int y, Color color)
    {
        if (x >= 0 && y >= 0 && x < Size && y < Size)
            image.SetPixel(x, y, color);
    }

    private static void Rect(Image image, int x0, int y0, int x1, int y1, Color color)
    {
        for (int y = y0; y <= y1; y++)
        for (int x = x0; x <= x1; x++)
            Pixel(image, x, y, color);
    }

    private static void Outline(Image image, int x0, int y0, int x1, int y1, Color color)
    {
        Rect(image, x0, y0, x1, y0, color);
        Rect(image, x0, y1, x1, y1, color);
        Rect(image, x0, y0, x0, y1, color);
        Rect(image, x1, y0, x1, y1, color);
    }

    private static void Line(Image image, int x0, int y0, int x1, int y1, Color color, int width)
    {
        int steps = Math.Max(Math.Abs(x1 - x0), Math.Abs(y1 - y0));
        for (int step = 0; step <= steps; step++)
        {
            double t = steps == 0 ? 0.0 : step / (double)steps;
            int x = (int)Math.Round(x0 + ((x1 - x0) * t));
            int y = (int)Math.Round(y0 + ((y1 - y0) * t));
            Rect(image, x, y, x + width - 1, y + width - 1, color);
        }
    }

    private static void Disc(Image image, int cx, int cy, int radius, Color color)
    {
        for (int y = cy - radius; y <= cy + radius; y++)
        for (int x = cx - radius; x <= cx + radius; x++)
        {
            if (((x - cx) * (x - cx)) + ((y - cy) * (y - cy)) <= radius * radius)
                Pixel(image, x, y, color);
        }
    }

    private static void Ring(Image image, int cx, int cy, int radius, Color color)
    {
        for (int y = cy - radius; y <= cy + radius; y++)
        for (int x = cx - radius; x <= cx + radius; x++)
        {
            double distance = Math.Sqrt(((x - cx) * (x - cx)) + ((y - cy) * (y - cy)));
            if (Math.Abs(distance - radius) <= 0.6)
                Pixel(image, x, y, color);
        }
    }

    private static void Star(Image image, Color fill, Color edge)
    {
        // Hand-plotted five-point star: a maths circle-of-points star at 16 px reads as mush.
        int[] left = [7, 6, 6, 5, 1, 2, 3, 2, 4, 4, 3, 2];
        int[] right = [8, 9, 9, 10, 14, 13, 12, 13, 11, 11, 12, 13];
        for (int row = 0; row < left.Length; row++)
        {
            int y = 2 + row;
            Rect(image, left[row], y, right[row], y, fill);
            Pixel(image, left[row], y, edge);
            Pixel(image, right[row], y, edge);
        }
    }
}
