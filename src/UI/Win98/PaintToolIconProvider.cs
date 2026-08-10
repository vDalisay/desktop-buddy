using System;
using System.Collections.Generic;
using Godot;

namespace DesktopBuddy.UI.Win98;

/// <summary>
/// Stable semantic paint-icon mapping. Final owner artwork can replace any generated placeholder
/// by adding a Texture2D-compatible asset at assets/ui/paint_tools/{id}.svg; callers do not change.
/// The procedural fallbacks are original project pixels, not recreations of Microsoft Paint art.
/// </summary>
public static class PaintToolIconProvider
{
    public const string Brush = "brush";
    public const string Eraser = "eraser";
    public const string Spray = "spray";
    public const string PickColor = "pick_color";
    public const string Curve = "curve";
    public const string Pan = "pan";
    public const string Fill = "fill";
    public const string Shapes = "shapes";
    public const string Undo = "undo";
    public const string Redo = "redo";
    public const string EraseAll = "erase_all";
    public const string ZoomIn = "zoom_in";
    public const string ZoomOut = "zoom_out";
    public const string ResetView = "reset_view";
    public const string RotateLeft = "rotate_left";
    public const string RotateRight = "rotate_right";

    private const int Size = 16;
    private static readonly Dictionary<string, Texture2D> Cache = new(StringComparer.Ordinal);

    public static Texture2D Resolve(string semanticId)
    {
        if (Cache.TryGetValue(semanticId, out Texture2D? cached))
            return cached;

        string path = $"res://assets/ui/paint_tools/{semanticId}.svg";
        Texture2D texture = ResourceLoader.Exists(path)
            ? GD.Load<Texture2D>(path) ?? Generate(semanticId)
            : Generate(semanticId);
        Cache[semanticId] = texture;
        return texture;
    }

    /// <summary>Applies an icon while retaining all semantics in node name, tooltip and shortcut.</summary>
    public static void Apply(Button button, string semanticId, string fallbackText, string tooltip)
    {
        ArgumentNullException.ThrowIfNull(button);
        button.Icon = Resolve(semanticId);
        button.Text = string.Empty;
        button.TooltipText = tooltip;
        button.SetMeta("paint_tool_id", semanticId);
        button.SetMeta("paint_tool_fallback_text", fallbackText);
    }

    private static Texture2D Generate(string id)
    {
        Image image = Image.CreateEmpty(Size, Size, false, Image.Format.Rgba8);
        image.Fill(Colors.Transparent);
        Color ink = Colors.Black;
        Color shade = Color.Color8(96, 96, 96);
        Color face = Color.Color8(192, 192, 192);
        Color accent = Color.Color8(0, 0, 128);

        switch (id)
        {
            case Brush:
                Diagonal(image, 3, 12, 11, 4, ink, 2);
                Rect(image, 2, 11, 5, 14, shade);
                Pixel(image, 1, 14, accent); Pixel(image, 2, 14, accent); Pixel(image, 3, 13, accent);
                break;
            case Eraser:
                Rect(image, 3, 6, 12, 12, ink);
                Rect(image, 4, 5, 11, 10, face);
                Rect(image, 8, 5, 11, 10, shade);
                break;
            case Spray:
                Rect(image, 5, 6, 10, 13, ink); Rect(image, 6, 7, 9, 12, face);
                Rect(image, 7, 3, 11, 6, ink); Pixel(image, 12, 3, ink);
                Pixel(image, 13, 2, accent); Pixel(image, 14, 5, accent); Pixel(image, 12, 7, accent); Pixel(image, 14, 9, accent);
                break;
            case PickColor:
                Diagonal(image, 3, 12, 11, 4, ink, 2);
                Rect(image, 9, 2, 12, 5, shade); Pixel(image, 2, 13, accent); Pixel(image, 2, 14, accent);
                break;
            case Curve:
                Pixel(image, 2, 11, ink); Pixel(image, 3, 9, ink); Pixel(image, 4, 7, ink); Pixel(image, 5, 6, ink);
                Pixel(image, 6, 6, ink); Pixel(image, 7, 7, ink); Pixel(image, 8, 9, ink); Pixel(image, 9, 10, ink);
                Pixel(image, 10, 10, ink); Pixel(image, 11, 9, ink); Pixel(image, 12, 7, ink); Pixel(image, 13, 5, ink);
                Pixel(image, 2, 12, accent); Pixel(image, 13, 4, accent);
                break;
            case Pan:
                Rect(image, 5, 6, 11, 12, ink); Rect(image, 6, 7, 10, 11, face);
                Rect(image, 5, 3, 6, 8, ink); Rect(image, 7, 2, 8, 7, ink); Rect(image, 9, 3, 10, 7, ink); Rect(image, 11, 5, 12, 9, ink);
                break;
            case Fill:
                Rect(image, 3, 5, 10, 11, ink); Rect(image, 4, 4, 9, 9, face);
                Diagonal(image, 9, 10, 12, 13, ink, 1); Pixel(image, 13, 13, accent); Pixel(image, 13, 14, accent);
                break;
            case Shapes:
                OutlineRect(image, 2, 3, 8, 9, ink); Circle(image, 10, 9, 4, ink);
                break;
            case Undo:
                Arrow(image, left: true, ink); break;
            case Redo:
                Arrow(image, left: false, ink); break;
            case EraseAll:
                Diagonal(image, 3, 3, 12, 12, ink, 2); Diagonal(image, 12, 3, 3, 12, ink, 2); break;
            case ZoomIn:
                Magnifier(image, ink); Rect(image, 6, 4, 7, 9, accent); Rect(image, 4, 6, 9, 7, accent); break;
            case ZoomOut:
                Magnifier(image, ink); Rect(image, 4, 6, 9, 7, accent); break;
            case ResetView:
                OutlineRect(image, 3, 3, 12, 12, ink); Rect(image, 7, 2, 8, 13, shade); Rect(image, 2, 7, 13, 8, shade); break;
            case RotateLeft:
                Rotate(image, left: true, ink); break;
            case RotateRight:
                Rotate(image, left: false, ink); break;
            default:
                OutlineRect(image, 3, 3, 12, 12, ink); Pixel(image, 7, 7, accent); Pixel(image, 8, 8, accent); break;
        }

        return ImageTexture.CreateFromImage(image);
    }

    private static void Pixel(Image image, int x, int y, Color color)
    {
        if (x >= 0 && y >= 0 && x < Size && y < Size) image.SetPixel(x, y, color);
    }

    private static void Rect(Image image, int x0, int y0, int x1, int y1, Color color)
    {
        for (int y = Math.Max(0, y0); y <= Math.Min(Size - 1, y1); y++)
        for (int x = Math.Max(0, x0); x <= Math.Min(Size - 1, x1); x++)
            image.SetPixel(x, y, color);
    }

    private static void OutlineRect(Image image, int x0, int y0, int x1, int y1, Color color)
    {
        Rect(image, x0, y0, x1, y0, color); Rect(image, x0, y1, x1, y1, color);
        Rect(image, x0, y0, x0, y1, color); Rect(image, x1, y0, x1, y1, color);
    }

    private static void Diagonal(Image image, int x0, int y0, int x1, int y1, Color color, int width)
    {
        int steps = Math.Max(Math.Abs(x1 - x0), Math.Abs(y1 - y0));
        for (int step = 0; step <= steps; step++)
        {
            double t = steps == 0 ? 0 : step / (double)steps;
            int x = (int)Math.Round(x0 + ((x1 - x0) * t));
            int y = (int)Math.Round(y0 + ((y1 - y0) * t));
            Rect(image, x, y, x + width - 1, y + width - 1, color);
        }
    }

    private static void Circle(Image image, int cx, int cy, int radius, Color color)
    {
        for (int y = cy - radius; y <= cy + radius; y++)
        for (int x = cx - radius; x <= cx + radius; x++)
        {
            double d = Math.Sqrt(((x - cx) * (x - cx)) + ((y - cy) * (y - cy)));
            if (Math.Abs(d - radius) <= 0.75) Pixel(image, x, y, color);
        }
    }

    private static void Arrow(Image image, bool left, Color color)
    {
        int sign = left ? -1 : 1;
        int tip = left ? 3 : 12;
        int tail = left ? 12 : 3;
        Rect(image, Math.Min(tip, tail), 7, Math.Max(tip, tail), 8, color);
        for (int i = 0; i < 4; i++)
        {
            Pixel(image, tip - (sign * i), 7 - i, color);
            Pixel(image, tip - (sign * i), 8 + i, color);
        }
    }

    private static void Magnifier(Image image, Color color)
    {
        Circle(image, 6, 6, 4, color);
        Diagonal(image, 9, 9, 13, 13, color, 2);
    }

    private static void Rotate(Image image, bool left, Color color)
    {
        for (int x = 4; x <= 11; x++) Pixel(image, x, 3, color);
        Rect(image, 3, 4, 4, 9, color); Rect(image, 11, 4, 12, 9, color);
        int tip = left ? 3 : 12;
        Pixel(image, tip, 10, color); Pixel(image, tip + (left ? 1 : -1), 11, color); Pixel(image, tip + (left ? 2 : -2), 10, color);
    }
}
