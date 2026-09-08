using System;
using DesktopBuddy.Domain.Characters;
using DesktopBuddy.Domain.Environment;
using DesktopBuddy.Domain.Physics;
using DesktopBuddy.Environment;
using Godot;

namespace DesktopBuddy.Buddy.Behavior;

/// <summary>
/// The two room surfaces the buddy can take an interest in — placed decorations and the painted
/// background — and the colour searches over them.
///
/// <para>Both come from <c>src/Environment</c>, which the reduced itch.io distribution compiles
/// out; <c>RoomInterestBootstrap.EnvironmentAbsent.cs</c> stands in there. Nothing is lost in that
/// build: <c>EnvironmentCustomizationBootstrap</c> already returns early under the itch scope, so
/// neither the decoration layer nor the background presenter is ever constructed and both searches
/// were already finding nothing.</para>
/// </summary>
public sealed partial class RoomInterestBootstrap
{
    private EnvironmentDecorationLayer? _layer;
    private EnvironmentBackgroundPresenter? _background;

    private void ResolveRoomSurfaces()
    {
        _layer ??= FindFirst<EnvironmentDecorationLayer>(GetTree().Root);
        _background ??= FindFirst<EnvironmentBackgroundPresenter>(GetTree().Root);
    }

    private void ForgetRoomSurfaces()
    {
        _layer = null;
        _background = null;
    }

    private bool TryFindClosestColourDecoration(Rgba32 favorite, out Vector2 point, out double score)
    {
        point = default;
        score = double.PositiveInfinity;
        if (!GodotObject.IsInstanceValid(_layer))
            return false;

        EnvironmentLayout layout = _layer!.VisibleLayout;
        if (layout.Decorations.Count == 0)
            return false;

        bool found = false;
        PlacedDecoration selected = default;
        foreach (PlacedDecoration placed in layout.Decorations)
        {
            if (EnvironmentDecorationRegistry.Find(placed.DefinitionId) is not
                    EnvironmentDecorationResource resource ||
                resource.Category == DecorationCategory.Wallpaper)
            {
                continue;
            }

            double candidate = Math.Min(
                ColourDistanceSquared(favorite, resource.PrimaryColor),
                ColourDistanceSquared(favorite, resource.SecondaryColor));
            if (candidate >= score)
                continue;

            score = candidate;
            selected = placed;
            found = true;
        }

        if (!found)
            return false;

        Rect2 room = _sandbox!.Boundaries.InnerBounds;
        if (room.Size.X <= 0.0f || room.Size.Y <= 0.0f)
            return false;

        (float screenX, float screenY) = EnvironmentPlacement.ToScreen(
            selected.Position,
            new RoomScreenBounds(room.Position.X, room.Position.Y, room.Size.X, room.Size.Y));
        point = new Vector2(screenX, screenY);
        return true;
    }

    /// <summary>
    /// The painted background, sampled on a coarse grid. Unpainted canvas is transparent, so
    /// only actually-drawn pixels are candidates; the wallpaper underneath is not the player's
    /// drawing and is deliberately not matched.
    /// </summary>
    private bool TryFindClosestPaintedColour(Rgba32 favorite, out Vector2 point, out double score)
    {
        point = default;
        score = double.PositiveInfinity;
        if (!GodotObject.IsInstanceValid(_background) ||
            !GodotObject.IsInstanceValid(_sandbox!.Boundaries))
        {
            return false;
        }

        RoomLayout layout = _sandbox.Boundaries.CurrentLayout;
        float width = (float)layout.RoomWidth;
        float height = (float)layout.RoomHeight;
        if (width <= 0.0f || height <= 0.0f)
            return false;

        ReadOnlySpan<byte> pixels = _background!.Canvas.Pixels.Span;
        int size = EnvironmentCanvasPolicy.Size;
        int bestX = 0;
        int bestY = 0;
        bool found = false;

        for (int y = PaintSampleStride / 2; y < size; y += PaintSampleStride)
        {
            for (int x = PaintSampleStride / 2; x < size; x += PaintSampleStride)
            {
                int index = ((y * size) + x) * EnvironmentCanvasPolicy.BytesPerPixel;
                if (pixels[index + 3] < PaintedAlphaThreshold)
                    continue;

                double candidate = ColourDistanceSquared(
                    favorite,
                    pixels[index] / 255.0,
                    pixels[index + 1] / 255.0,
                    pixels[index + 2] / 255.0);
                if (candidate >= score)
                    continue;

                score = candidate;
                bestX = x;
                bestY = y;
                found = true;
            }
        }

        if (!found)
            return false;

        // The paint quad spans the whole room with its origin at the room's top-left corner —
        // the same mapping EnvironmentBackgroundPresenter uses to place it.
        point = new Vector2(
            (bestX + 0.5f) / size * width,
            (bestY + 0.5f) / size * height);
        return true;
    }
}
