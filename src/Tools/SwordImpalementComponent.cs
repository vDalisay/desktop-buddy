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

    /// <summary>Severity of the wound a skewer opens, before Gore Mode decides to draw it.</summary>
    private const float SkewerWoundSeverity = 0.85f;

    // The spring that holds a skewered part on the blade. Firm enough to carry the buddy's
    // weight, bounded so the solver can never be handed an impulse it cannot integrate.
    private const float HoldStiffness = 2600.0f;
    private const float HoldDamping = 90.0f;
    private const float HoldMaximumForce = 90000.0f;

    private DroppedCursorToolBody? _embedded;

    /// <summary>Where the embedded blade sits in the part it is in, in that part's space.</summary>
    private Transform2D _embeddedOffset = Transform2D.Identity;
    private BuddyPartId _skewered;
    private bool _isSkewered;

    /// <summary>Distance from the hilt along the blade at which the part sits.</summary>
    private float _bladeDepth;

    private Vector2 _previousTip;
    private bool _hasPreviousTip;

    // Composed in code rather than authored: the dropped-tool component this depends on is
    // itself built by DroppedToolInputBootstrap once the sandbox has finished composing, so
    // there is no scene-time node for an [Export] to point at.
    private BuddyRoot _buddy = null!;
    private CursorToolController _cursorTools = null!;
    private DroppedToolInteractionComponent _droppedTools = null!;
    private GrabTetherController _grab = null!;
    private GoreComponent _gore = null!;

    public bool IsInitialized { get; private set; }

    /// <summary>How many times a blade has gone into the buddy this run.</summary>
    public int ImpalementCount { get; private set; }

    /// <summary>True while the player is holding the buddy on the blade.</summary>
    public bool IsSkewered => _isSkewered;

    /// <summary>True while a blade has been left in him.</summary>
    public bool IsEmbedded => GodotObject.IsInstanceValid(_embedded);

    /// <summary>Which part currently holds the blade. Only meaningful while skewered.</summary>
    public BuddyPart SkeweredPart => (BuddyPart)(int)_skewered;

    public void Initialize(
        BuddyRoot buddy,
        CursorToolController cursorTools,
        DroppedToolInteractionComponent droppedTools,
        GrabTetherController grab,
        GoreComponent gore)
    {
        if (!GodotObject.IsInstanceValid(buddy) ||
            !GodotObject.IsInstanceValid(cursorTools) ||
            !GodotObject.IsInstanceValid(droppedTools) ||
            !GodotObject.IsInstanceValid(grab) ||
            !GodotObject.IsInstanceValid(gore))
        {
            throw new InvalidOperationException("SwordImpalementComponent dependencies are incomplete.");
        }

        _buddy = buddy;
        _cursorTools = cursorTools;
        _droppedTools = droppedTools;
        _grab = grab;
        _gore = gore;
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

            return;
        }

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

        // Where along the blade he ends up: his centre projected onto the blade axis, so he
        // is threaded on at the depth the point actually reached him rather than snapping to
        // the tip. Kept off the hilt itself so he cannot end up inside the player's hand.
        float bladeLength = hilt.DistanceTo(tip);
        Vector2 along = bladeLength > 0.01f ? (tip - hilt) / bladeLength : Vector2.Right;
        float depth = (part.GlobalPosition - hilt).Dot(along);
        _bladeDepth = Mathf.Clamp(depth, part.Radius, bladeLength);

        // Through, not into: without this the capsule keeps colliding with him and shoves
        // him off the point instead of running him onto it.
        PassThroughParts(blade, except: true);

        // The blade opened him up. Gore Mode decides whether that is drawn.
        _gore.OpenWound((BuddyPart)(int)partId, SkewerWoundSeverity, tip);
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

        Vector2 along = (tip - hilt).Normalized();
        Vector2 anchor = hilt + (along * _bladeDepth);
        Vector2 error = anchor - part!.GlobalPosition;
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
