using System;
using DesktopBuddy.App;
using DesktopBuddy.Domain.Characters;
using DesktopBuddy.Domain.Environment;
using DesktopBuddy.Environment;
using Godot;

namespace DesktopBuddy.Buddy.Behavior;

/// <summary>
/// Lightweight room-awareness personality for the Demo. The buddy occasionally notices the
/// placed decoration whose authored colour is closest to its own torso colour and submits a
/// temporary point of interest to ambient autonomy. It never edits the room, awards currency,
/// persists personality state, or drives physics independently of BehaviorArbiter.
/// </summary>
public partial class RoomInterestBootstrap : Node
{
    private const double FirstConsiderationSeconds = 8.0;
    private const double ConsiderationSeconds = 18.0;
    private const int InterestDurationTicks = 6 * 120;

    private SandboxRoot? _sandbox;
    private EnvironmentDecorationLayer? _layer;
    private EnvironmentDecorationRegistry? _registry;
    private double _secondsUntilConsideration = FirstConsiderationSeconds;

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
        if (!GodotObject.IsInstanceValid(_sandbox) || !GodotObject.IsInstanceValid(_layer) ||
            _registry is null || _sandbox!.Window.WorkCompanionActive)
        {
            return;
        }

        _secondsUntilConsideration -= Math.Max(0.0, delta);
        if (_secondsUntilConsideration > 0.0)
            return;
        _secondsUntilConsideration = ConsiderationSeconds;

        if (_sandbox.Buddy.AutonomousMotion.HasRoomInterest)
            return;

        CompiledCharacterAppearance? appearance = _sandbox.VisualPresenter.RigView.ActiveAppearance;
        if (appearance is null)
            return;

        if (!TryFindClosestColourDecoration(
                appearance.PartColors.Torso,
                _layer!.VisibleLayout,
                out PlacedDecoration placed))
        {
            return;
        }

        Rect2 room = _sandbox.Boundaries.InnerBounds;
        if (room.Size.X <= 0.0f || room.Size.Y <= 0.0f)
            return;
        Vector2 point = EnvironmentPlacement.ToScreen(
            placed.Position,
            new RoomScreenBounds(room.Position.X, room.Position.Y, room.Size.X, room.Size.Y));
        _sandbox.Buddy.AutonomousMotion.SuggestRoomInterest(point.X, InterestDurationTicks);
    }

    private void ResolveRuntime()
    {
        if (_sandbox is not null && !GodotObject.IsInstanceValid(_sandbox))
        {
            _sandbox = null;
            _layer = null;
            _registry = null;
        }

        _sandbox ??= FindFirst<SandboxRoot>(GetTree().Root);
        _layer ??= FindFirst<EnvironmentDecorationLayer>(GetTree().Root);
        if (_registry is null && GodotObject.IsInstanceValid(_layer))
        {
            try
            {
                _registry = EnvironmentDecorationRegistry.LoadDefault();
            }
            catch (Exception exception)
            {
                GD.PushWarning($"Room-interest decoration catalogue unavailable: {exception.Message}");
                _secondsUntilConsideration = ConsiderationSeconds;
            }
        }
    }

    private bool TryFindClosestColourDecoration(
        Rgba32 favorite,
        EnvironmentLayout layout,
        out PlacedDecoration selected)
    {
        selected = default;
        if (_registry is null || layout.Decorations.Count == 0)
            return false;

        double bestScore = double.PositiveInfinity;
        bool found = false;
        foreach (PlacedDecoration placed in layout.Decorations)
        {
            EnvironmentDecorationResource? resource = _registry.Find(placed.DefinitionId);
            if (resource is null || resource.Category == DecorationCategory.Wallpapers)
                continue;

            double score = Math.Min(
                ColourDistanceSquared(favorite, resource.FillColor),
                ColourDistanceSquared(favorite, resource.SecondaryColor));
            if (score >= bestScore)
                continue;

            bestScore = score;
            selected = placed;
            found = true;
        }
        return found;
    }

    private static double ColourDistanceSquared(Rgba32 favorite, Color authored)
    {
        double r = (favorite.R / 255.0) - authored.R;
        double g = (favorite.G / 255.0) - authored.G;
        double b = (favorite.B / 255.0) - authored.B;
        return (r * r) + (g * g) + (b * b);
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
