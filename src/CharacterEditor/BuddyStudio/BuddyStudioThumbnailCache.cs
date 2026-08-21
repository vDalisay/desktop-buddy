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
                Circle(image, 38, 34, 4, ink);
                Circle(image, 58, 34, 4, ink);
                Arc(image, 48, 44, 9, 5, 15, 165, ink, 2);
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
            case CharacterFeatureIds.HairBobBangs:
                // Cap, straight fringe band and the two ear-length side curtains with a flick.
                Ellipse(image, 48, 20, 26, 15, accent);
                Rect(image, 27, 20, 42, 7, accent);
                Ellipse(image, 25, 33, 6, 16, accent);
                Ellipse(image, 71, 33, 6, 16, accent);
                Ellipse(image, 22, 46, 6, 4, accent);
                Ellipse(image, 74, 46, 6, 4, accent);
                break;
            case CharacterFeatureIds.HairBuzzCut:
                Ellipse(image, 48, 22, 25, 13, accent);
                Line(image, 25, 26, 71, 26, ink, 2);
                break;

            case CharacterFeatureIds.EyesSoftOval:
                Ellipse(image, 38, 34, 6, 8, accent);
                Ellipse(image, 58, 34, 6, 8, accent);
                EllipseRing(image, 38, 34, 6, 8, ink, 2);
                EllipseRing(image, 58, 34, 6, 8, ink, 2);
                break;
            case CharacterFeatureIds.EyesGlossyOval:
                // White, iris, pupil, catchlight — the same four parts the runtime draws.
                MiiEye(image, 38, 34, 6, 8, accent, ink);
                MiiEye(image, 58, 34, 6, 8, accent, ink);
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
            case CharacterFeatureIds.EyesLashedOval:
                Ellipse(image, 38, 34, 6, 8, accent);
                Ellipse(image, 58, 34, 6, 8, accent);
                EllipseRing(image, 38, 34, 6, 8, ink, 2);
                EllipseRing(image, 58, 34, 6, 8, ink, 2);
                Line(image, 32, 28, 27, 25, ink, 2);
                Line(image, 33, 31, 27, 30, ink, 2);
                Line(image, 64, 28, 69, 25, ink, 2);
                Line(image, 63, 31, 69, 30, ink, 2);
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
            case CharacterFeatureIds.BrowsBushy:
                Arc(image, 38, 29, 10, 5, 200, 340, accent, 6);
                Arc(image, 58, 29, 10, 5, 200, 340, accent, 6);
                break;

            case CharacterFeatureIds.NoseButton:
                Ellipse(image, 48, 39, 6, 5, accent);
                EllipseRing(image, 48, 39, 6, 5, ink, 2);
                break;
            case CharacterFeatureIds.NoseTriangle:
                Triangle(image, 48, 44, 8, 9, accent);
                break;
            case CharacterFeatureIds.NoseBroadOval:
                Ellipse(image, 48, 40, 11, 4, accent);
                EllipseRing(image, 48, 40, 11, 4, ink, 2);
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
            case CharacterFeatureIds.MouthOval:
                EllipseRing(image, 48, 47, 8, 7, accent, 3);
                break;

            case CharacterFeatureIds.EarsRoundTabs:
                Ellipse(image, 21, 36, 6, 11, accent);
                Ellipse(image, 75, 36, 6, 11, accent);
                EllipseRing(image, 21, 36, 6, 11, ink, 2);
                EllipseRing(image, 75, 36, 6, 11, ink, 2);
                break;
            case CharacterFeatureIds.EarsPointedTips:
                Triangle(image, 17, 36, 8, 10, accent, pointsLeft: true);
                Triangle(image, 79, 36, 8, 10, accent, pointsLeft: false);
                break;
            case CharacterFeatureIds.EarsFlatDiscs:
                Circle(image, 21, 36, 9, accent);
                Circle(image, 75, 36, 9, accent);
                Ring(image, 21, 36, 9, ink, 2);
                Ring(image, 75, 36, 9, ink, 2);
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
            case CharacterFeatureIds.GlassesRoundWire:
                Ring(image, 37, 35, 9, accent, 2);
                Ring(image, 59, 35, 9, accent, 2);
                Rect(image, 46, 34, 4, 2, accent);
                break;
            case CharacterFeatureIds.GlassesShades:
                Rect(image, 28, 30, 18, 11, accent);
                Rect(image, 50, 30, 18, 11, accent);
                Rect(image, 28, 27, 40, 3, accent);
                break;

            case CharacterFeatureIds.HeadwearSoftCap:
                Ellipse(image, 48, 17, 24, 10, accent);
                Rect(image, 48, 20, 21, 5, accent);
                Line(image, 25, 22, 69, 22, ink, 2);
                break;
            case CharacterFeatureIds.HeadwearKnitBeanie:
                Ellipse(image, 48, 19, 25, 12, accent);
                Rect(image, 22, 24, 52, 6, accent);
                Circle(image, 48, 8, 5, accent);
                break;
            case CharacterFeatureIds.HeadwearWideBrim:
                Ellipse(image, 48, 18, 19, 10, accent);
                Ellipse(image, 48, 26, 34, 5, accent);
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

            // ---- Second cosmetic wave (owner instruction 2026-08-21) --------------------
            case CharacterFeatureIds.FaceWrinkles:
                // Two brow furrows, crow's feet fanning from each eye, nasolabial folds.
                Arc(image, 48, 27, 14, 5, 190, 350, accent, 2);
                Arc(image, 48, 22, 11, 4, 190, 350, accent, 2);
                Line(image, 30, 33, 24, 31, accent, 2);
                Line(image, 30, 36, 23, 36, accent, 2);
                Line(image, 30, 39, 24, 41, accent, 2);
                Line(image, 66, 33, 72, 31, accent, 2);
                Line(image, 66, 36, 73, 36, accent, 2);
                Line(image, 66, 39, 72, 41, accent, 2);
                Arc(image, 40, 45, 8, 9, 250, 340, accent, 2);
                Arc(image, 56, 45, 8, 9, 200, 290, accent, 2);
                break;
            case CharacterFeatureIds.FaceChiseledCheeks:
                Arc(image, 40, 38, 10, 13, 250, 350, accent, 3);
                Arc(image, 56, 38, 10, 13, 190, 290, accent, 3);
                Arc(image, 42, 48, 9, 7, 280, 350, accent, 2);
                Arc(image, 54, 48, 9, 7, 190, 260, accent, 2);
                break;
            case CharacterFeatureIds.FaceFreckles:
                Circle(image, 34, 38, 2, accent);
                Circle(image, 39, 43, 2, accent);
                Circle(image, 30, 44, 2, accent);
                Circle(image, 27, 41, 1, accent);
                Circle(image, 45, 41, 1, accent);
                Circle(image, 62, 38, 2, accent);
                Circle(image, 57, 43, 2, accent);
                Circle(image, 66, 44, 2, accent);
                Circle(image, 69, 41, 1, accent);
                Circle(image, 51, 41, 1, accent);
                break;
            case CharacterFeatureIds.FaceRosyCheeks:
                Ellipse(image, 33, 41, 8, 5, accent);
                Ellipse(image, 63, 41, 8, 5, accent);
                Line(image, 30, 44, 32, 38, accent, 1);
                Line(image, 34, 44, 36, 38, accent, 1);
                Line(image, 60, 44, 62, 38, accent, 1);
                Line(image, 64, 44, 66, 38, accent, 1);
                break;
            case CharacterFeatureIds.FaceStubble:
                // A jaw-hugging crescent and a moustache, not a slab across the mouth.
                Arc(image, 48, 35, 22, 21, 20, 160, accent, 5);
                Arc(image, 48, 35, 17, 16, 30, 150, accent, 4);
                Ellipse(image, 48, 51, 6, 4, accent);
                Arc(image, 43, 43, 5, 3, 190, 350, accent, 3);
                Arc(image, 53, 43, 5, 3, 190, 350, accent, 3);
                break;

            case CharacterFeatureIds.HairElderTufts:
                // Bald crown, side tufts, band round the back.
                Ellipse(image, 24, 32, 7, 11, accent);
                Ellipse(image, 72, 32, 7, 11, accent);
                Arc(image, 48, 30, 25, 22, 200, 340, accent, 4);
                break;

            case CharacterFeatureIds.EyesSleepyHalf:
                MiiEye(image, 38, 36, 6, 5, accent, ink);
                MiiEye(image, 58, 36, 6, 5, accent, ink);
                Line(image, 31, 31, 45, 31, accent, 3);
                Line(image, 51, 31, 65, 31, accent, 3);
                break;
            case CharacterFeatureIds.EyesAngrySlant:
                MiiEye(image, 38, 35, 6, 7, accent, ink);
                MiiEye(image, 58, 35, 6, 7, accent, ink);
                Line(image, 30, 25, 45, 31, accent, 4);
                Line(image, 66, 25, 51, 31, accent, 4);
                break;
            case CharacterFeatureIds.EyesWideSparkle:
                MiiEye(image, 38, 34, 8, 11, accent, ink);
                MiiEye(image, 58, 34, 8, 11, accent, ink);
                break;
            case CharacterFeatureIds.EyesNarrowSlit:
                Rect(image, 29, 33, 18, 3, accent);
                Rect(image, 49, 33, 18, 3, accent);
                break;
            case CharacterFeatureIds.EyesBigRound:
                MiiEye(image, 37, 34, 10, 10, accent, ink);
                MiiEye(image, 59, 34, 10, 10, accent, ink);
                break;

            case CharacterFeatureIds.NosePointedBeak:
                Triangle(image, 48, 48, 6, 13, accent);
                break;
            case CharacterFeatureIds.NoseWideFlat:
                Rect(image, 36, 38, 24, 5, accent);
                OutlineRect(image, 36, 38, 24, 5, ink, 1);
                break;
            case CharacterFeatureIds.NoseUpturned:
                Ellipse(image, 48, 40, 7, 6, accent);
                Ellipse(image, 48, 35, 5, 4, accent);
                break;
            case CharacterFeatureIds.NoseHooked:
                Ellipse(image, 47, 33, 4, 6, accent);
                Ellipse(image, 48, 39, 5, 6, accent);
                Ellipse(image, 49, 45, 6, 4, accent);
                break;
            case CharacterFeatureIds.NoseTinyDot:
                Circle(image, 48, 39, 3, accent);
                Ring(image, 48, 39, 3, ink, 1);
                break;

            case CharacterFeatureIds.MouthWideGrin:
                Arc(image, 48, 45, 16, 9, 190, 350, accent, 3);
                break;
            case CharacterFeatureIds.MouthFrown:
                Arc(image, 48, 52, 13, 8, 10, 170, accent, 3);
                break;
            case CharacterFeatureIds.MouthSmirk:
                Line(image, 36, 48, 52, 48, accent, 3);
                Line(image, 52, 48, 60, 43, accent, 3);
                break;
            case CharacterFeatureIds.MouthOpenSmile:
                Ellipse(image, 48, 47, 12, 8, accent);
                EllipseRing(image, 48, 47, 12, 8, ink, 2);
                break;
            case CharacterFeatureIds.MouthPucker:
                EllipseRing(image, 48, 47, 5, 6, accent, 3);
                break;

            case CharacterFeatureIds.EarsElf:
                Triangle(image, 14, 30, 6, 16, accent, pointsLeft: true);
                Triangle(image, 82, 30, 6, 16, accent, pointsLeft: false);
                Ellipse(image, 22, 38, 5, 8, accent);
                Ellipse(image, 74, 38, 5, 8, accent);
                break;

            case CharacterFeatureIds.GlassesSquareFrames:
                OutlineRect(image, 26, 27, 20, 17, accent, 3);
                OutlineRect(image, 50, 27, 20, 17, accent, 3);
                Rect(image, 45, 34, 6, 3, accent);
                break;
            case CharacterFeatureIds.GlassesCatEye:
                OutlineRect(image, 28, 30, 18, 11, accent, 3);
                OutlineRect(image, 50, 30, 18, 11, accent, 3);
                Line(image, 28, 30, 21, 24, accent, 3);
                Line(image, 68, 30, 75, 24, accent, 3);
                Rect(image, 46, 34, 4, 2, accent);
                break;
            case CharacterFeatureIds.GlassesAviators:
                Ellipse(image, 36, 36, 10, 9, accent);
                Ellipse(image, 60, 36, 10, 9, accent);
                Rect(image, 26, 26, 44, 3, accent);
                break;
            case CharacterFeatureIds.GlassesHalfMoon:
                Rect(image, 28, 36, 18, 6, accent);
                Rect(image, 50, 36, 18, 6, accent);
                Rect(image, 46, 37, 4, 2, accent);
                break;
            case CharacterFeatureIds.GlassesVisor:
                Rect(image, 24, 29, 48, 12, accent);
                Rect(image, 24, 25, 48, 4, accent);
                break;

            case CharacterFeatureIds.HeadwearBallCap:
                Ellipse(image, 48, 19, 24, 12, accent);
                Rect(image, 24, 22, 48, 4, accent);
                Rect(image, 48, 22, 26, 5, accent);
                Circle(image, 48, 9, 3, accent);
                break;
            case CharacterFeatureIds.HeadwearSunHat:
                // Wide drooping straw brim, rounded crown, dark ribbon.
                Ellipse(image, 48, 26, 34, 7, accent);
                Arc(image, 48, 26, 34, 9, 0, 180, accent, 3);
                Ellipse(image, 48, 17, 17, 10, accent);
                Rect(image, 31, 21, 34, 4, ink);
                break;
            case CharacterFeatureIds.HeadwearFedora:
                Ellipse(image, 48, 24, 30, 6, accent);
                Ellipse(image, 48, 15, 16, 11, accent);
                Rect(image, 32, 19, 32, 4, ink);
                break;

            default:
                // Closed shipped catalogue: unknown IDs should never reach this cache. A visible
                // fallback keeps a future mismatch obvious rather than silently inventing art.
                Line(image, 36, 25, 60, 49, ink, 3);
                Line(image, 60, 25, 36, 49, ink, 3);
                break;
        }
    }

    /// <summary>
    /// One eye the way the runtime renderer draws it: a pale white bounded by the authored
    /// colour, an iris filling most of it, a dark pupil, and a catchlight. Kept beside the
    /// tile art so a Studio tile never promises a different eye from the one that appears.
    /// </summary>
    private static void MiiEye(Image image, int cx, int cy, int rx, int ry, Color accent, Color ink)
    {
        var sclera = new Color("fbf7f0");
        int iris = Math.Max(2, (int)Math.Round(Math.Min(rx, ry) * 0.68));
        Ellipse(image, cx, cy, rx, ry, sclera);
        EllipseRing(image, cx, cy, rx, ry, accent, 2);
        Circle(image, cx, cy, iris, accent);
        Circle(image, cx, cy, Math.Max(1, iris / 2), ink);
        Circle(image, cx - (iris / 3), cy - (iris / 3), Math.Max(1, iris / 4), sclera);
    }

    /// <summary>Filled isosceles triangle: apex down by default, or sideways for ear tips.</summary>
    private static void Triangle(Image image, int cx, int cy, int halfWidth, int height, Color color, bool? pointsLeft = null)
    {
        for (int step = 0; step < height; step++)
        {
            double shrink = step / (double)Math.Max(1, height);
            int span = (int)Math.Round(halfWidth * (1.0 - shrink));
            if (pointsLeft is null)
                Rect(image, cx - span, cy - height + step, Math.Max(1, span * 2), 1, color);
            else if (pointsLeft.Value)
                Rect(image, cx + step, cy - span, 1, Math.Max(1, span * 2), color);
            else
                Rect(image, cx - step, cy - span, 1, Math.Max(1, span * 2), color);
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
