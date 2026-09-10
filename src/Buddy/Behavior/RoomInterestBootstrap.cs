using System;
using System.Collections.Generic;
using System.Linq;
using DesktopBuddy.App;
using DesktopBuddy.Buddy.Presentation;
using DesktopBuddy.Buddy.Presentation3D;
using DesktopBuddy.Domain.Scenes;
using DesktopBuddy.Interaction;
using DesktopBuddy.Scenes;
using DesktopBuddy.Domain.Characters;
using DesktopBuddy.Domain.Environment;
using DesktopBuddy.Domain.Physics;
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

    private readonly Dictionary<BuddyPlacementId, ActorInterest> _interests = [];
    private SandboxRoot? _sandbox;

    /// <summary>Per-Buddy consideration timer and pending arrival. Every cast member looks for its
    /// own favourite colour on its own schedule.</summary>
    private sealed class ActorInterest
    {
        public double SecondsUntilConsideration = FirstConsiderationSeconds;
        public int AwaitedArrivalCount = -1;
    }

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

        if (_sandbox.ActiveSceneRuntime is { } runtime && runtime.Actors.Count > 0)
        {
            foreach (BuddyActorRuntime actor in runtime.Actors)
            {
                ConsiderActor(
                    actor.PlacementId,
                    actor.Buddy,
                    actor.VisualPresenter,
                    actor.Reaction,
                    actor.Damage,
                    delta);
            }
            return;
        }

        ConsiderActor(
            default,
            _sandbox.Buddy,
            _sandbox.VisualPresenter,
            _sandbox.Reactions,
            _sandbox.Pipeline,
            delta);
    }

    private void ConsiderActor(
        BuddyPlacementId placementId,
        BuddyRoot buddy,
        BuddyVisualPresenter presenter,
        BuddyReactionComponent? reactions,
        InteractionDamageComponent? pipeline,
        double delta)
    {
        if (!GodotObject.IsInstanceValid(buddy) || !GodotObject.IsInstanceValid(presenter))
            return;
        if (!_interests.TryGetValue(placementId, out ActorInterest? interest))
        {
            interest = new ActorInterest();
            _interests[placementId] = interest;
        }

        ObserveArrival(interest, buddy, reactions, pipeline);

        interest.SecondsUntilConsideration -= Math.Max(0.0, delta);
        if (interest.SecondsUntilConsideration > 0.0)
            return;
        interest.SecondsUntilConsideration = NextConsiderationSeconds();

        if (buddy.AutonomousMotion.HasRoomInterest)
            return;

        CompiledCharacterAppearance? appearance = presenter.RigView.ActiveAppearance;
        if (appearance is null)
            return;

        if (!TryFindFavouriteColour(appearance.FavoriteColor, out Vector2 point))
            return;

        interest.AwaitedArrivalCount = buddy.AutonomousMotion.RoomInterestArrivals + 1;
        buddy.AutonomousMotion.SuggestRoomInterest(point, InterestDurationTicks, ArrivalGazeTicks);
    }

    /// <summary>
    /// The buddy reached the colour it set out for: the gaze is already held by the motion
    /// component, so this only performs the smile and the small mood gain.
    /// </summary>
    private static void ObserveArrival(
        ActorInterest interest,
        BuddyRoot buddy,
        BuddyReactionComponent? reactions,
        InteractionDamageComponent? pipeline)
    {
        if (interest.AwaitedArrivalCount < 0 ||
            buddy.AutonomousMotion.RoomInterestArrivals < interest.AwaitedArrivalCount)
        {
            return;
        }

        interest.AwaitedArrivalCount = -1;
        if (GodotObject.IsInstanceValid(reactions))
            reactions!.PlayColourSmile();
        if (GodotObject.IsInstanceValid(pipeline))
            pipeline!.ProgressBinding.ApplyCareMood(1.0f);
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

    private void ResolveRuntime()
    {
        if (_sandbox is not null && !GodotObject.IsInstanceValid(_sandbox))
        {
            _sandbox = null;
            ForgetRoomSurfaces();
        }

        SandboxRoot? previous = _sandbox;
        _sandbox ??= FindFirst<SandboxRoot>(GetTree().Root);
        if (!ReferenceEquals(previous, _sandbox) && _sandbox is not null)
        {
            // Placements that leave the room take their pending interest with them.
            _sandbox.SceneRosterChanged += ForgetDepartedActors;
        }
        ResolveRoomSurfaces();
    }

    private void ForgetDepartedActors()
    {
        if (_sandbox?.ActiveSceneRuntime is not { } runtime)
            return;
        var live = new HashSet<BuddyPlacementId>();
        foreach (BuddyActorRuntime actor in runtime.Actors)
            live.Add(actor.PlacementId);
        foreach (BuddyPlacementId placementId in _interests.Keys.ToArray())
        {
            if (!live.Contains(placementId))
                _interests.Remove(placementId);
        }
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
