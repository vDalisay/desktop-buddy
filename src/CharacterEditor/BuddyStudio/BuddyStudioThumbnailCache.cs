using System;
using System.Collections.Generic;
using DesktopBuddy.Domain.Characters;
using Godot;

namespace DesktopBuddy.CharacterEditor.BuddyStudio;

/// <summary>Small main-thread cache of original catalogue motifs; never touches the live rig.</summary>
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

        Circle(image, 48, 35, 25, paper);
        Ring(image, 48, 35, 25, ink, 2);
        DrawSlotMotif(image, definition, ink, accent);
        Texture2D texture = ImageTexture.CreateFromImage(image);
        Cache.Add(definition.Id, texture);
        return texture;
    }

    private static void DrawSlotMotif(Image image, CosmeticDefinition definition, Color ink, Color accent)
    {
        int variant = Math.Max(1, definition.SortOrder / 10 + 1);
        switch (definition.Slot)
        {
            case CharacterFeatureSlot.Hair:
            case CharacterFeatureSlot.Headwear:
                Rect(image, 25, 12, 46, 10 + variant * 2, accent);
                Ring(image, 48, 34, 25, ink, 2);
                break;
            case CharacterFeatureSlot.Eyes:
            case CharacterFeatureSlot.Glasses:
                Ring(image, 38, 33, 7 + variant, ink, 2);
                Ring(image, 58, 33, 7 + variant, ink, 2);
                Rect(image, 46, 31, 4, 3, ink);
                break;
            case CharacterFeatureSlot.Brows:
                Rect(image, 31, 24, 14, 2 + variant, accent);
                Rect(image, 51, 24, 14, 2 + variant, accent);
                break;
            case CharacterFeatureSlot.Nose:
                Circle(image, 48, 38, 3 + variant, accent);
                break;
            case CharacterFeatureSlot.Mouth:
                Rect(image, 37, 47, 22, 2 + variant, accent);
                break;
            case CharacterFeatureSlot.Ears:
                Circle(image, 22, 36, 5 + variant, accent);
                Circle(image, 74, 36, 5 + variant, accent);
                break;
            case CharacterFeatureSlot.Tops:
                Rect(image, 25, 47, 46, 18, accent);
                break;
            case CharacterFeatureSlot.Shoes:
                Rect(image, 26, 58, 19, 8, accent);
                Rect(image, 51, 58, 19, 8, accent);
                break;
            case CharacterFeatureSlot.Accessories:
                for (int index = 0; index < variant; index++)
                    Circle(image, 48 + index * 6 - variant * 3, 45, 3, accent);
                break;
            default:
                Ring(image, 48, 35, 12 + variant * 2, accent, 3);
                break;
        }
    }

    private static void Rect(Image image, int x, int y, int width, int height, Color color)
    {
        for (int py = Math.Max(0, y); py < Math.Min(Height, y + height); py++)
        for (int px = Math.Max(0, x); px < Math.Min(Width, x + width); px++)
            image.SetPixel(px, py, color);
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

    private static Color ToColor(Rgba32 color) => new(
        color.R / 255f,
        color.G / 255f,
        color.B / 255f,
        color.A / 255f);
}
