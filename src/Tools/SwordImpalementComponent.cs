using System;
using DesktopBuddy.Buddy;
using DesktopBuddy.Buddy.Physics;
using DesktopBuddy.Domain.Buddy;
using DesktopBuddy.Domain.Content;
using DesktopBuddy.Grab;
using DesktopBuddy.Interaction;
using Godot;

namespace DesktopBuddy.Tools;

/// <summary>
/// Leaves the Sword buried in whatever it was driven into.
///
/// <para><b>It reuses the drop path rather than inventing a held state.</b> An impaled
/// sword is an ordinary <see cref="DroppedCursorToolBody"/> — the same world form the D key
/// produces — that happens to be pinned to a part. That is what makes pulling it back out
/// free: the existing Grab picker already picks up dropped tools, the registry already owns
/// its identity, cap and eviction, and double-clicking it already re-equips it. A bespoke
/// "impaled" object would have had to re-earn every one of those.</para>
///
/// <para><b>Why it is driven and not jointed.</b> A <see cref="PinJoint2D"/> would let the
/// sword's mass fight the ragdoll solver, so a blade left in the chest would change how the
/// buddy falls — a presentation feature altering the simulation, which is exactly what
/// Gore Mode is not allowed to do. Instead the blade is frozen, driven kinematically from
/// the part's own transform, and excepted from collision with every part, so it rides the
/// buddy and exerts nothing on him. This is the same reason blood droplets are drawn rather
/// than simulated.</para>
///
/// <para>Gated with the rest of Gore Mode: a build or a player without it gets a sword that
/// swings and hurts exactly as it does here, and simply bounces off instead of sticking.</para>
/// </summary>
[GlobalClass]
public partial class SwordImpalementComponent : Node2D
{
    /// <summary>
    /// How close to the point of the blade a contact must land to count as a stab, as a
    /// fraction of the blade's half-length. A hit further down is the flat of the sword and
    /// swats rather than pierces.
    /// </summary>
    private const float TipFraction = 0.42f;

    /// <summary>
    /// Closing speed the tip must carry to bury itself, in px/s. Well above a careless
    /// nudge: resting the blade against the buddy must never impale him.
    /// </summary>
    private const float MinimumStabSpeed = 900.0f;

    /// <summary>Pain floor for a stab, so a glancing tip contact does not stick.</summary>
    private const float MinimumStabPain = 6.0f;

    private DroppedCursorToolBody? _impaled;
    private BuddyPartId _anchorPart;
    private Vector2 _anchorOffset;
    private float _anchorRotation;

    // Composed in code rather than authored: the dropped-tool component this depends on is
    // itself built by DroppedToolInputBootstrap once the sandbox has finished composing, so
    // there is no scene-time node for an [Export] to point at.
    private InteractionDamageComponent _pipeline = null!;
    private BuddyRoot _buddy = null!;
    private CursorToolController _cursorTools = null!;
    private DroppedToolInteractionComponent _droppedTools = null!;
    private GrabTetherController _grab = null!;
    private GoreComponent _gore = null!;

    public bool IsInitialized { get; private set; }

    /// <summary>How many times a blade has been left in the buddy this run.</summary>
    public int ImpalementCount { get; private set; }

    /// <summary>True while a sword is buried in the buddy.</summary>
    public bool IsImpaled => GodotObject.IsInstanceValid(_impaled);

    /// <summary>Which part currently holds a blade. Only meaningful while impaled.</summary>
    public BuddyPart ImpaledPart => (BuddyPart)(int)_anchorPart;

    public void Initialize(
        InteractionDamageComponent pipeline,
        BuddyRoot buddy,
        CursorToolController cursorTools,
        DroppedToolInteractionComponent droppedTools,
        GrabTetherController grab,
        GoreComponent gore)
    {
        if (!GodotObject.IsInstanceValid(pipeline) || !pipeline.IsInitialized ||
            !GodotObject.IsInstanceValid(buddy) ||
            !GodotObject.IsInstanceValid(cursorTools) ||
            !GodotObject.IsInstanceValid(droppedTools) ||
            !GodotObject.IsInstanceValid(grab) ||
            !GodotObject.IsInstanceValid(gore) || !gore.IsInitialized)
        {
            throw new InvalidOperationException("SwordImpalementComponent dependencies are incomplete.");
        }

        _pipeline = pipeline;
        _buddy = buddy;
        _cursorTools = cursorTools;
        _droppedTools = droppedTools;
        _grab = grab;
        _gore = gore;
        _pipeline.ImpactAccepted += OnImpactAccepted;
        IsInitialized = true;
    }

    public override void _ExitTree()
    {
        if (GodotObject.IsInstanceValid(_pipeline))
            _pipeline.ImpactAccepted -= OnImpactAccepted;
    }

    private void OnImpactAccepted(AcceptedImpact impact)
    {
        if (!_gore.IsActive || IsImpaled ||
            impact.ContentId != ContentIds.ToolSword ||
            impact.Pain < MinimumStabPain ||
            impact.RelativeSpeed < MinimumStabSpeed)
        {
            return;
        }

        CursorToolProfile? profile = _cursorTools.ActiveProfile;
        CursorToolBody? blade = _cursorTools.Body;
        if (profile is null || blade is null ||
            !GodotObject.IsInstanceValid(profile) || !GodotObject.IsInstanceValid(blade) ||
            profile.ContentId != ContentIds.ToolSword || !profile.IsElongated)
        {
            return;
        }

        if (!StruckWithTheTip(profile, blade, impact.Point))
            return;

        Impale(impact.Part, blade);
    }

    /// <summary>
    /// Whether the contact landed near the point. The tip is the end opposite the authored
    /// handle offset, which is the same end the swing glint and the collider cap use.
    /// </summary>
    private static bool StruckWithTheTip(CursorToolProfile profile, CursorToolBody blade, Vector2 worldPoint)
    {
        Vector2 tipLocal = -profile.HandleLocalOffset;
        float distance = blade.ToLocal(worldPoint).DistanceTo(tipLocal);
        return distance <= MathF.Max(profile.Radius * 2.0f, profile.Length * TipFraction);
    }

    /// <summary>
    /// Turns the held blade into a dropped one pinned to the struck part, capturing the
    /// pose first so the sword stays exactly where it landed rather than snapping.
    /// </summary>
    private void Impale(BuddyPart part, CursorToolBody blade)
    {
        if (!TryPart(part, out PuppetPartBody? body))
            return;

        Transform2D bladeTransform = blade.GlobalTransform;
        if (!_droppedTools.TryDropSelected())
            return;

        DroppedCursorToolBody? dropped = _droppedTools.FindDropped(ContentIds.ToolSword);
        if (dropped is null || !GodotObject.IsInstanceValid(dropped))
            return;

        // The drop lands the body at the held pose already; this re-states it so the anchor
        // below is taken from exactly the transform the player saw the blade in.
        dropped.GlobalTransform = bladeTransform;

        _anchorPart = (BuddyPartId)(int)part;
        Transform2D partToBlade = body!.GlobalTransform.AffineInverse() * bladeTransform;
        _anchorOffset = partToBlade.Origin;
        _anchorRotation = partToBlade.Rotation;

        dropped.LinearVelocity = Vector2.Zero;
        dropped.AngularVelocity = 0.0f;
        dropped.FreezeMode = RigidBody2D.FreezeModeEnum.Kinematic;
        dropped.Freeze = true;

        // The blade must not push the body it is buried in, nor catch on the other limbs on
        // the way past. Excepting every part is what keeps an impaled sword inert.
        for (int index = 0; index < 6; index++)
        {
            if (TryPart((BuddyPart)index, out PuppetPartBody? other))
                dropped.AddCollisionExceptionWith(other);
        }

        _impaled = dropped;
        ImpalementCount++;
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!IsImpaled)
            return;

        // Released the moment anything takes hold of it — the Grab tether, an eviction, or
        // Gore Mode being switched off underneath it.
        if (!_gore.IsActive || WasTakenHold())
        {
            Release();
            return;
        }

        if (!TryPart(ImpaledPart, out PuppetPartBody? body))
        {
            Release();
            return;
        }

        _impaled!.GlobalTransform =
            body!.GlobalTransform * new Transform2D(_anchorRotation, _anchorOffset);
    }

    private bool WasTakenHold()
    {
        GrabState grab = _grab.CurrentGrab;
        return grab.Active && ReferenceEquals(grab.Target, _impaled);
    }

    /// <summary>
    /// Pulls the blade out: it becomes an ordinary loose object again, wherever it was. Also
    /// the entry point for the Repair Kit and for a hard reposition.
    /// </summary>
    public void Release()
    {
        if (!GodotObject.IsInstanceValid(_impaled))
        {
            _impaled = null;
            return;
        }

        DroppedCursorToolBody blade = _impaled!;
        for (int index = 0; index < 6; index++)
        {
            if (TryPart((BuddyPart)index, out PuppetPartBody? other))
                blade.RemoveCollisionExceptionWith(other);
        }

        blade.Freeze = false;
        blade.Sleeping = false;
        _impaled = null;
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
