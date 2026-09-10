using System;
using DesktopBuddy.Buddy;
using DesktopBuddy.Buddy.Behavior;
using DesktopBuddy.Buddy.Physics;
using DesktopBuddy.Buddy.Presentation;
using DesktopBuddy.Buddy.Presentation3D;
using DesktopBuddy.Domain.Persistence;
using DesktopBuddy.Domain.Scenes;
using DesktopBuddy.Interaction;
using DesktopBuddy.Tools;
using Godot;

namespace DesktopBuddy.Scenes;

/// <summary>
/// Per-Buddy inputs prepared by the one authoritative Scene/Sandbox fixed tick. The actor has no
/// engine callback of its own: the owning root supplies this context once, in stable Scene order.
/// </summary>
public readonly record struct BuddyActorTickContext(
    double Delta,
    BuddyPartId? GrabbedPart,
    Vector2 GrabWorldAnchor,
    Vector2 CursorWorldPosition,
    bool SocialTargetValid,
    bool RopeSuspended);

/// <summary>
/// Focused runtime binding for one live Buddy actor. It groups the already-existing components that
/// semantically belong to one Buddy and exposes the per-Buddy slice of the current SandboxRoot tick.
/// It deliberately does not inherit Node and must never own <c>_PhysicsProcess</c>.
///
/// This first extraction still wraps the legacy <see cref="InteractionDamageComponent"/> binding.
/// The split <see cref="BuddyProgressCoordinator"/> is the migration target for that component in the
/// next packet; this class does not pretend the new BuddyIdentityState is live before that rebind lands.
/// </summary>
public sealed class BuddyActorRuntime
{
    public BuddyActorRuntime(
        BuddyPlacementId placementId,
        BuddyIdentityId buddyIdentityId,
        BuddyRoot buddy,
        InteractionDamageComponent damage,
        CareStrokeComponent careStroke,
        ToolReactionComponent toolReaction,
        BuddyReactionComponent reaction,
        BuddyVisualPresenter visualPresenter)
    {
        if (!placementId.IsValid)
            throw new ArgumentException("Buddy actor requires a stable Scene placement ID.", nameof(placementId));
        if (!buddyIdentityId.IsValid)
            throw new ArgumentException("Buddy actor requires a stable Buddy identity ID.", nameof(buddyIdentityId));

        Buddy = RequireValid(buddy, nameof(buddy));
        Damage = RequireValid(damage, nameof(damage));
        CareStroke = RequireValid(careStroke, nameof(careStroke));
        ToolReaction = RequireValid(toolReaction, nameof(toolReaction));
        Reaction = RequireValid(reaction, nameof(reaction));
        VisualPresenter = RequireValid(visualPresenter, nameof(visualPresenter));

        if (!Buddy.IsInitialized || !Damage.IsInitialized || !CareStroke.IsInitialized ||
            !ToolReaction.IsInitialized || !Reaction.IsInitialized || !VisualPresenter.IsInitialized)
        {
            throw new InvalidOperationException(
                "BuddyActorRuntime may bind only after the existing per-Buddy components are initialized.");
        }

        if (!ReferenceEquals(Damage.Buddy, Buddy) ||
            !ReferenceEquals(CareStroke.Pipeline, Damage) ||
            !ReferenceEquals(ToolReaction.Buddy, Buddy) ||
            !ReferenceEquals(ToolReaction.Pipeline, Damage) ||
            !ReferenceEquals(ToolReaction.CareStroke, CareStroke) ||
            !ReferenceEquals(Reaction.Buddy, Buddy) ||
            !ReferenceEquals(Reaction.Pipeline, Damage) ||
            !ReferenceEquals(Reaction.CareStroke, CareStroke) ||
            !ReferenceEquals(Reaction.ToolReaction, ToolReaction) ||
            !ReferenceEquals(VisualPresenter.Buddy, Buddy))
        {
            throw new ArgumentException(
                "Buddy actor components cross-bind different Buddy/pipeline instances; one actor must be internally isolated.");
        }

        PlacementId = placementId;
        BuddyIdentityId = buddyIdentityId;
    }

    public BuddyPlacementId PlacementId { get; }
    public BuddyIdentityId BuddyIdentityId { get; }
    public SceneActorBindingKey BindingKey => new(PlacementId, BuddyIdentityId);

    public BuddyRoot Buddy { get; }
    public InteractionDamageComponent Damage { get; }
    public CareStrokeComponent CareStroke { get; }
    public ToolReactionComponent ToolReaction { get; }
    public BuddyReactionComponent Reaction { get; }
    public BuddyVisualPresenter VisualPresenter { get; }

    /// <summary>Samples this actor before the solver advances, matching the existing presenter lane.</summary>
    public void CaptureTickSnapshot() => VisualPresenter.CaptureTickSnapshot();

    /// <summary>
    /// Routes exactly the current per-Buddy tick slice. Shared cursor tools, guns, room objects,
    /// grenades and fire remain owned by the Scene/Sandbox root and are deliberately absent here.
    /// </summary>
    public void PhysicsTick(in BuddyActorTickContext context)
    {
        if (context.Delta < 0.0 || !double.IsFinite(context.Delta))
            throw new ArgumentOutOfRangeException(nameof(context), "Buddy actor delta must be finite and non-negative.");

        Buddy.GrabResistance.SetGrabContext(context.GrabbedPart is not null, context.GrabWorldAnchor);
        CareStroke.PhysicsTick(context.Delta);
        ToolReaction.PhysicsTick(context.Delta);
        Reaction.PhysicsTick();
        Buddy.PhysicsTick(
            context.GrabbedPart,
            context.GrabWorldAnchor,
            context.CursorWorldPosition,
            context.SocialTargetValid,
            context.RopeSuspended);
        Damage.PhysicsTick();
    }

    public bool OwnsPart(PuppetPartBody? part)
        => Buddy.Rig.OwnsPart(part);

    private static T RequireValid<T>(T value, string parameterName) where T : GodotObject
    {
        ArgumentNullException.ThrowIfNull(value, parameterName);
        if (!GodotObject.IsInstanceValid(value))
            throw new ArgumentException("Buddy actor component is not a valid Godot object.", parameterName);
        return value;
    }
}
