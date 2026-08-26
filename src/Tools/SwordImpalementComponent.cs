using System;
using DesktopBuddy.Buddy;
using DesktopBuddy.Buddy.Physics;
using DesktopBuddy.Domain.Buddy;
using DesktopBuddy.Domain.Content;
using DesktopBuddy.Domain.Physics;
using DesktopBuddy.Grab;
using DesktopBuddy.Interaction;
using Godot;
using NumericsVector2 = System.Numerics.Vector2;

namespace DesktopBuddy.Tools;

/// <summary>
/// Runs the Sword through the buddy and leaves it there.
///
/// <para><b>Driven by the blade's own tip, not by the damage pipeline.</b> The first version
/// waited for an <c>ImpactAccepted</c> carrying enough impulse, and in practice that never
/// arrived: the capsule bounced off before it could build one, so the owner could not impale
/// the buddy at all (report 2026-08-25). Now the component watches where the point actually
/// is each tick. If it has crossed into a part while the player is driving it, that is a
/// skewer — no impulse threshold, because a sword going in is a matter of geometry rather
/// than of how hard it was swung.</para>
///
/// <para><b>Three stages.</b> The tip enters a part; the blade is excepted from colliding
/// with every part so it passes <i>through</i> rather than shoving him; and the part is held
/// on the blade by the same bounded spring the Grab tether uses, so the buddy hangs off the
/// sword and can be carried on it. Let go and the blade stays in him, as an ordinary dropped
/// tool pinned to the part, and the wound it opened goes on bleeding.</para>
///
/// <para><b>A wielded blade does not collide with him at all.</b> It is excepted from every
/// part for as long as it is being wielded, not just once it is in — a point that shoves him
/// away before it can enter is the "too much knockback ... no knockback should be in the
/// pointy part" the owner reported (2026-08-25). Because there is then no solver contact to
/// score, the stab reports its own pain through
/// <see cref="InteractionDamageComponent.ApplyBlastImpulse"/>, the same entry the grenade
/// uses for a source with no contact of its own. It goes through the shared curve, so a stab
/// pays what its depth is worth and there is still no per-tool multiplier anywhere.</para>
///
/// <para><b>Impaling is the Sword's mechanic, not Gore Mode's.</b> It is deliberately not
/// behind the Gore toggle: holding the buddy on the blade moves him, and anything gated on
/// <see cref="Domain.Presentation.EffectsSettings"/> must not be able to change what a run
/// simulates (FR-004.3a). So the blade goes in whatever the build and whatever the setting;
/// only the <b>blood</b> it draws is Gore Mode's business.</para>
/// </summary>
[GlobalClass]
public partial class SwordImpalementComponent : Node2D
{
    /// <summary>
    /// How fast the point must be travelling to go in, in px/s. Deliberately tiny: this is a
    /// sharp blade, so almost any deliberate push should bury it (owner instruction
    /// 2026-08-25). All the threshold still rules out is a blade resting against him while
    /// the cursor is not moving at all.
    /// </summary>
    private const float MinimumEntrySpeed = 45.0f;

    /// <summary>
    /// How far into a part the tip must reach, as a fraction of the part's radius. Nearly
    /// the whole radius: touching the surface with a point is enough for a blade.
    /// </summary>
    private const float EntryDepthFraction = 0.95f;

    /// <summary>
    /// Impulse a fully driven stab reports, in the units the solver would have produced.
    /// Fed through the shared pain curve like everything else, so this sets what a stab is
    /// worth without inventing a second scoring path.
    /// </summary>
    private const float StabImpulse = 1600.0f;

    // The spring that holds a skewered part on the blade. Firm enough to carry the buddy's
    // weight, bounded so the solver can never be handed an impulse it cannot integrate.
    private const float HoldStiffness = 2600.0f;
    private const float HoldDamping = 90.0f;
    private const float HoldMaximumForce = 90000.0f;

    /// <summary>Closest to the hilt he may slide, so he never ends up inside the grip.</summary>
    private const float MinimumDepthFromHilt = 18.0f;

    /// <summary>
    /// The depth lane a buried blade is drawn in: <b>behind the whole buddy</b>, between him
    /// and the room behind him (owner instruction 2026-08-25).
    ///
    /// <para>His parts occupy -48 to 96 and the room's paint sits at -70, so -60 is the gap
    /// between the two. Every part therefore occludes the length of blade inside his
    /// silhouette and only what sticks out past it is drawn — from any angle, in any pose.
    /// The previous attempt put the blade in the struck part's own lane, which meant a blade
    /// in the head still drew in front of the torso and the result was, in the owner's word,
    /// inconsistent. There is no per-pixel depth inside the buddy to do better with; putting
    /// the blade behind all of him sidesteps needing one.</para>
    /// </summary>
    private const float BuriedBladeDepth = -60.0f;

    private DroppedCursorToolBody? _embedded;

    /// <summary>Where the embedded blade sits in the part it is in, in that part's space.</summary>
    private Transform2D _embeddedOffset = Transform2D.Identity;
    private BuddyPartId _skewered;
    private bool _isSkewered;

    private Vector2 _previousTip;
    private bool _hasPreviousTip;

    // Composed in code rather than authored: the dropped-tool component this depends on is
    // itself built by DroppedToolInputBootstrap once the sandbox has finished composing, so
    // there is no scene-time node for an [Export] to point at.
    private InteractionDamageComponent _pipeline = null!;
    private LooseObjectVisual3D? _looseVisual;
    private BuddyRoot _buddy = null!;
    private CursorToolController _cursorTools = null!;
    private CursorToolVisual3D? _visual;
    private DroppedToolInteractionComponent _droppedTools = null!;
    private GrabTetherController _grab = null!;

    /// <summary>True while the live blade is excepted from colliding with the buddy.</summary>
    private bool _passingThrough;

    public bool IsInitialized { get; private set; }

    /// <summary>How many times a blade has gone into the buddy this run.</summary>
    public int ImpalementCount { get; private set; }

    /// <summary>True while the player is holding the buddy on the blade.</summary>
    public bool IsSkewered => _isSkewered;

    /// <summary>True while a blade has been left in him.</summary>
    public bool IsEmbedded => GodotObject.IsInstanceValid(_embedded);

    /// <summary>Which part currently holds the blade. Only meaningful while skewered.</summary>
    public BuddyPart SkeweredPart => (BuddyPart)(int)_skewered;

    /// <param name="visual">
    /// The live tool's 3D slot, so a buried blade can be sunk into the buddy's depth range
    /// and occluded by the part it is in. Optional: without it the blade still goes in, it
    /// just draws in front of him.
    /// </param>
    /// <param name="looseVisual">
    /// The dropped-object slots, for the same reason once the blade has been let go of. A
    /// blade left in him is a dropped tool, and dropped tools draw at their authored depth
    /// — which is why letting go used to pop it back on top of him.
    /// </param>
    public void Initialize(
        InteractionDamageComponent pipeline,
        BuddyRoot buddy,
        CursorToolController cursorTools,
        DroppedToolInteractionComponent droppedTools,
        GrabTetherController grab,
        CursorToolVisual3D? visual = null,
        LooseObjectVisual3D? looseVisual = null)
    {
        if (!GodotObject.IsInstanceValid(pipeline) || !pipeline.IsInitialized ||
            !GodotObject.IsInstanceValid(buddy) ||
            !GodotObject.IsInstanceValid(cursorTools) ||
            !GodotObject.IsInstanceValid(droppedTools) ||
            !GodotObject.IsInstanceValid(grab))
        {
            throw new InvalidOperationException("SwordImpalementComponent dependencies are incomplete.");
        }

        _pipeline = pipeline;
        _buddy = buddy;
        _cursorTools = cursorTools;
        _droppedTools = droppedTools;
        _grab = grab;
        _visual = visual;
        _looseVisual = looseVisual;
        IsInitialized = true;
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!IsInitialized)
            return;

        ReleaseEmbeddedIfTakenHold();

        CursorToolProfile? profile = _cursorTools.ActiveProfile;
        CursorToolBody? blade = _cursorTools.Body;
        bool live =
            profile is not null && blade is not null &&
            GodotObject.IsInstanceValid(profile) && GodotObject.IsInstanceValid(blade) &&
            profile!.ContentId == ContentIds.ToolSword && profile.IsElongated;

        if (!live)
        {
            // The sword went away without being let go of deliberately — tool switched, or
            // despawned. Nothing stays skewered on a blade that is not there.
            _isSkewered = false;
            _hasPreviousTip = false;
            _passingThrough = false;
            return;
        }

        Vector2 hilt = blade!.ToGlobal(profile!.HandleLocalOffset);
        Vector2 tip = blade.ToGlobal(-profile.HandleLocalOffset);
        float step = (float)Math.Max(0.0001, delta);
        Vector2 tipVelocity = _hasPreviousTip ? (tip - _previousTip) / step : Vector2.Zero;
        _previousTip = tip;
        _hasPreviousTip = true;

        if (!_cursorTools.IsWieldingPointFirst)
        {
            // Letting go of the button is how a blade is left in him.
            if (_isSkewered)
                LeaveItIn(blade);
            else
                SetPassThrough(blade, false);

            return;
        }

        // A wielded blade never collides with him. This is set every tick it is wielded
        // rather than only on entry, so the point cannot shove him away before it goes in.
        SetPassThrough(blade, true);

        if (_isSkewered)
            HoldOnBlade(hilt, tip, blade);
        else
            TryEnter(tip, tipVelocity, hilt, blade);
    }

    /// <summary>
    /// Whether the point has crossed into a part hard enough to go in. Geometry plus
    /// intent: the tip must be inside the part and travelling into it, which is what stops
    /// a blade being dragged sideways past him from skewering anything.
    /// </summary>
    private void TryEnter(Vector2 tip, Vector2 tipVelocity, Vector2 hilt, CursorToolBody blade)
    {
        if (tipVelocity.Length() < MinimumEntrySpeed)
            return;

        for (int index = 0; index < 6; index++)
        {
            if (!TryPart((BuddyPart)index, out PuppetPartBody? part))
                continue;

            Vector2 toCentre = part!.GlobalPosition - tip;
            if (toCentre.Length() > part.Radius * EntryDepthFraction)
                continue;

            // Travelling into the part, not away from it or across it.
            if (tipVelocity.Normalized().Dot(toCentre.Normalized()) < 0.0f)
                continue;

            Enter((BuddyPartId)index, part, hilt, tip, blade);
            return;
        }
    }

    private void Enter(
        BuddyPartId partId,
        PuppetPartBody part,
        Vector2 hilt,
        Vector2 tip,
        CursorToolBody blade)
    {
        _skewered = partId;
        _isSkewered = true;
        ImpalementCount++;

        // The stab reports its own pain, because there is no solver contact to report one:
        // the blade has been excepted from him since the moment it was wielded. Everything
        // downstream — the curve, the payout, harmful memory, and Gore Mode's wound — keys
        // off this one event exactly as it would off a bullet.
        _pipeline.ApplyBlastImpulse(
            blade.InteractionId,
            ContentIds.ToolSword,
            (BuddyPart)(int)partId,
            StabImpulse,
            tip);

        SinkIntoBuddy();
    }

    /// <summary>
    /// Drops the blade's drawing into the depth lane of the part it is in, so that part's
    /// mesh occludes the length that went in and only what is still outside him is drawn.
    /// How much disappears therefore follows how far it was pushed, with no extra state.
    /// </summary>
    private void SinkIntoBuddy()
    {
        if (_visual is not null && GodotObject.IsInstanceValid(_visual))
            _visual.SetDepthOverride(BuriedBladeDepth);
    }

    /// <summary>Turns the pass-through exception on or off, and only when it changes.</summary>
    private void SetPassThrough(PhysicsBody2D blade, bool through)
    {
        if (_passingThrough == through)
            return;

        _passingThrough = through;
        PassThroughParts(blade, through);
        if (!through)
            _visual?.SetDepthOverride(null);
    }

    /// <summary>
    /// Holds the skewered part at its depth along the blade with a bounded spring — the
    /// same shape as the Grab tether, and bounded for the same reason: a hard transform
    /// write would fight the ragdoll solver and could throw him across the room.
    /// </summary>
    private void HoldOnBlade(Vector2 hilt, Vector2 tip, CursorToolBody blade)
    {
        if (!TryPart(SkeweredPart, out PuppetPartBody? part))
        {
            _isSkewered = false;
            return;
        }

        float bladeLength = hilt.DistanceTo(tip);
        if (bladeLength < 0.01f)
            return;

        Vector2 along = (tip - hilt) / bladeLength;

        // He is threaded onto the blade, not pinned to a point on it. The anchor is
        // wherever he currently sits <b>along</b> the blade, so the correction below is
        // purely perpendicular and he is free to slide up and down it — which is what lets
        // the player keep pushing the blade further in once it is already through him
        // (owner report 2026-08-25). Pinning a fixed depth made a push drag him along
        // instead of running him further onto the point.
        float depth = (part!.GlobalPosition - hilt).Dot(along);

        // Off the end of the point and he is free. Withdrawing the blade is therefore how
        // it comes back out, with no separate gesture.
        if (depth > bladeLength + part.Radius)
        {
            _isSkewered = false;
            _visual?.SetDepthOverride(null);
            return;
        }

        // Never into the hand: the hilt end stops at the guard.
        depth = Mathf.Max(depth, MinimumDepthFromHilt);

        Vector2 anchor = hilt + (along * depth);
        Vector2 error = anchor - part.GlobalPosition;
        Vector2 relativeVelocity = part.LinearVelocity - blade.LinearVelocity;

        GrabTetherResult result = GrabTether.Evaluate(new GrabTetherInput(
            new NumericsVector2(error.X, error.Y),
            new NumericsVector2(relativeVelocity.X, relativeVelocity.Y),
            HoldStiffness,
            HoldDamping,
            HoldMaximumForce));
        var force = new Vector2(result.Force.X, result.Force.Y);
        if (force.IsFinite())
            part.ApplyForce(force);
    }

    /// <summary>
    /// The player let go with the blade still in him. It becomes an ordinary dropped tool
    /// pinned to the part it went into, so the existing Grab picker can pull it back out
    /// and the registry owns its identity, cap and eviction unchanged.
    /// </summary>
    private void LeaveItIn(CursorToolBody blade)
    {
        _isSkewered = false;
        if (!TryPart(SkeweredPart, out PuppetPartBody? part))
            return;

        Transform2D bladeTransform = blade.GlobalTransform;
        if (!_droppedTools.TryDropSelected())
        {
            PassThroughParts(blade, except: false);
            return;
        }

        DroppedCursorToolBody? dropped = _droppedTools.FindDropped(ContentIds.ToolSword);
        if (dropped is null || !GodotObject.IsInstanceValid(dropped))
            return;

        dropped.GlobalTransform = bladeTransform;
        dropped.LinearVelocity = Vector2.Zero;
        dropped.AngularVelocity = 0.0f;
        dropped.FreezeMode = RigidBody2D.FreezeModeEnum.Kinematic;
        dropped.Freeze = true;
        PassThroughParts(dropped, except: true);

        // The live blade is gone; its exception and its sunken depth go with it.
        _passingThrough = false;
        _visual?.SetDepthOverride(null);

        _embeddedOffset = part!.GlobalTransform.AffineInverse() * bladeTransform;
        _embedded = dropped;
    }

    public override void _Process(double _delta)
    {
        // The embedded blade rides the part it is in. Driven kinematically rather than
        // jointed, so its mass never fights the ragdoll solver for how the buddy falls.
        if (!IsEmbedded || !TryPart(SkeweredPart, out PuppetPartBody? part))
            return;

        _embedded!.GlobalTransform = part!.GlobalTransform * _embeddedOffset;

        // Re-applied every frame rather than once on release: the dropped-object visual
        // pools its slots and re-authors their depth whenever one is re-attached, so a
        // one-shot override would be lost the first time the blade left and re-entered view.
        if (_looseVisual is not null && GodotObject.IsInstanceValid(_looseVisual))
            _looseVisual.TrySetDepthOverride(_embedded.RuntimeId, BuriedBladeDepth);
    }

    private void ReleaseEmbeddedIfTakenHold()
    {
        if (!IsEmbedded)
            return;

        GrabState grab = _grab.CurrentGrab;
        if (grab.Active && ReferenceEquals(grab.Target, _embedded))
            PullOut();
    }

    /// <summary>
    /// Pulls the blade back out: it becomes an ordinary loose object again, wherever it is.
    /// Also the entry point for the Repair Kit and for a hard reposition.
    /// </summary>
    public void PullOut()
    {
        _isSkewered = false;
        if (!GodotObject.IsInstanceValid(_embedded))
        {
            _embedded = null;
            return;
        }

        DroppedCursorToolBody blade = _embedded!;
        PassThroughParts(blade, except: false);
        blade.Freeze = false;
        blade.Sleeping = false;
        _embedded = null;
    }

    /// <summary>
    /// Turns collision between the blade and every buddy part on or off. Every part, not
    /// just the skewered one, so a blade run through the chest does not catch on an arm on
    /// its way through.
    /// </summary>
    private void PassThroughParts(PhysicsBody2D blade, bool except)
    {
        if (!GodotObject.IsInstanceValid(blade))
            return;

        for (int index = 0; index < 6; index++)
        {
            if (!TryPart((BuddyPart)index, out PuppetPartBody? part))
                continue;

            if (except)
                blade.AddCollisionExceptionWith(part);
            else
                blade.RemoveCollisionExceptionWith(part);
        }
    }

    private bool TryPart(BuddyPart part, out PuppetPartBody? body)
    {
        body = null;
        if (!GodotObject.IsInstanceValid(_buddy) || !GodotObject.IsInstanceValid(_buddy.Rig) ||
            !_buddy.Rig.IsInitialized)
        {
            return false;
        }

        body = _buddy.Rig.GetPart((BuddyPartId)(int)part);
        return GodotObject.IsInstanceValid(body);
    }
}
