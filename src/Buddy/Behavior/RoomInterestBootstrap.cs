using System;
using DesktopBuddy.App;
using DesktopBuddy.Domain.Characters;
using DesktopBuddy.Domain.Environment;
using DesktopBuddy.Domain.Physics;
using DesktopBuddy.Environment;
using Godot;

namespace DesktopBuddy.Buddy.Behavior;

/// <summary>
/// Favourite-colour room awareness for the Demo. Every minute or few, the buddy looks around
/// for the thing in the room closest to its own fixed favourite colour — a placed decoration or
/// a patch of the painted background — and, if something is close enough, submits a temporary
/// point of interest to ambient autonomy. On arriving it looks at the colour, smiles, and gains
/// a little mood. It never edits the room, awards currency, persists personality state, or
/// drives physics independently of BehaviorArbiter.
///
/// <para>The favourite colour is the character document's frozen <c>FavoriteColor</c>, decided
/// when the character was created, not the live torso colour: repainting a buddy must not
/// silently retarget its personality (owner feedback 2026-08-19).</para>
/// </summary>
public partial class RoomInterestBootstrap : Node
{
    private const double FirstConsiderationSeconds = 8.0;

    /// <summary>
    /// The randomized gap between considerations. Owner-specified as "a minute, or five
    /// minutes, randomized between those times" (2026-08-19).
    /// </summary>
    private const double MinimumConsiderationSeconds = 60.0;
    private const double MaximumConsiderationSeconds = 300.0;

    private const int InterestDurationTicks = 6 * 120;
    private const int ArrivalGazeTicks = 3 * 120;

    /// <summary>
    /// How close a colour has to be to count as "its favourite", as a squared distance in
    /// unit RGB. 0.09 is a radius of 0.3, which admits a pink drawing for a pink buddy and
    /// rejects the merely least-wrong object in an otherwise unrelated room.
    /// </summary>
    private const double MatchThresholdSquared = 0.09;

    /// <summary>Painted-background sampling stride, in canvas pixels.</summary>
    private const int PaintSampleStride = 16;

    /// <summary>Below this alpha the canvas is unpainted and the wallpaper shows through.</summary>
    private const byte PaintedAlphaThreshold = 128;

    private SandboxRoot? _sandbox;
    private EnvironmentDecorationLayer? _layer;
    private EnvironmentBackgroundPresenter? _background;
    private double _secondsUntilConsideration = FirstConsiderationSeconds;
    private int _awaitedArrivalCount = -1;

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        SetProcess(true);
    }

    public override void _Process(double delta)
    {
        if (DisplayServer.GetName() == "headless")
            return;

        ResolveRuntime();
        if (!GodotObject.IsInstanceValid(_sandbox) || _sandbox!.Window.WorkCompanionActive)
            return;

        ObserveArrival();

        _secondsUntilConsideration -= Math.Max(0.0, delta);
        if (_secondsUntilConsideration > 0.0)
            return;
        _secondsUntilConsideration = NextConsiderationSeconds();

        if (_sandbox.Buddy.AutonomousMotion.HasRoomInterest)
            return;

        CompiledCharacterAppearance? appearance = _sandbox.VisualPresenter.RigView.ActiveAppearance;
        if (appearance is null)
            return;

        if (!TryFindFavouriteColour(appearance.FavoriteColor, out Vector2 point))
            return;

        _awaitedArrivalCount = _sandbox.Buddy.AutonomousMotion.RoomInterestArrivals + 1;
        _sandbox.Buddy.AutonomousMotion.SuggestRoomInterest(
            point, InterestDurationTicks, ArrivalGazeTicks);
    }

    /// <summary>
    /// The buddy reached the colour it set out for: the gaze is already held by the motion
    /// component, so this only performs the smile and the small mood gain.
    /// </summary>
    private void ObserveArrival()
    {
        if (_awaitedArrivalCount < 0 ||
            _sandbox!.Buddy.AutonomousMotion.RoomInterestArrivals < _awaitedArrivalCount)
        {
            return;
        }

        _awaitedArrivalCount = -1;
        if (GodotObject.IsInstanceValid(_sandbox.Reactions))
            _sandbox.Reactions.PlayColourSmile();
        if (GodotObject.IsInstanceValid(_sandbox.Pipeline))
            _sandbox.Pipeline.Progress.ApplyCareMood(1.0f);
    }

    private static double NextConsiderationSeconds() =>
        MinimumConsiderationSeconds +
        (Random.Shared.NextDouble() * (MaximumConsiderationSeconds - MinimumConsiderationSeconds));

    /// <summary>
    /// The closest favourite-colour match in the room, in world pixels, across both sources.
    /// Ties go to whichever is closer in colour, so a hand-painted pink wall can beat a
    /// merely pinkish sofa and vice versa.
    /// </summary>
    private bool TryFindFavouriteColour(Rgba32 favorite, out Vector2 point)
    {
        point = default;
        double best = MatchThresholdSquared;
        bool found = false;

        if (TryFindClosestColourDecoration(favorite, out Vector2 decoration, out double decorationScore) &&
            decorationScore < best)
        {
            best = decorationScore;
            point = decoration;
            found = true;
        }

        if (TryFindClosestPaintedColour(favorite, out Vector2 painted, out double paintedScore) &&
            paintedScore < best)
        {
            point = painted;
            found = true;
        }

        return found;
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

    private void ResolveRuntime()
    {
        if (_sandbox is not null && !GodotObject.IsInstanceValid(_sandbox))
        {
            _sandbox = null;
            _layer = null;
            _background = null;
        }

        _sandbox ??= FindFirst<SandboxRoot>(GetTree().Root);
        _layer ??= FindFirst<EnvironmentDecorationLayer>(GetTree().Root);
        _background ??= FindFirst<EnvironmentBackgroundPresenter>(GetTree().Root);
    }

    private static double ColourDistanceSquared(Rgba32 favorite, Color authored) =>
        ColourDistanceSquared(favorite, authored.R, authored.G, authored.B);

    private static double ColourDistanceSquared(Rgba32 favorite, double r, double g, double b)
    {
        double dr = (favorite.R / 255.0) - r;
        double dg = (favorite.G / 255.0) - g;
        double db = (favorite.B / 255.0) - b;
        return (dr * dr) + (dg * dg) + (db * db);
    }

    private static T? FindFirst<T>(Node root) where T : Node
    {
        if (root is T match)
            return match;
        foreach (Node child in root.GetChildren())
        {
            T? descendant = FindFirst<T>(child);
            if (descendant is not null)
                return descendant;
        }
        return null;
    }
}
