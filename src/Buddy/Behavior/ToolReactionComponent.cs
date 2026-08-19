using System;
using DesktopBuddy.Buddy.Physics;
using DesktopBuddy.Domain.Buddy;
using DesktopBuddy.Domain.Mood;
using DesktopBuddy.Domain.Tools;
using DesktopBuddy.Interaction;
using DesktopBuddy.Tools;
using Godot;

namespace DesktopBuddy.Buddy.Behavior;

/// <summary>Typed social/tool reaction intent consumed by the buddy arbiter.</summary>
public readonly record struct ToolReactionIntent(
    bool Active,
    float WalkDirection,
    float LocomotionScale,
    bool JumpRequested,
    float JumpDirection,
    float JumpScale,
    float JumpHorizontalRatio,
    bool GuardActive,
    Vector2 LeftGuardTarget,
    Vector2 RightGuardTarget,
    float GuardStiffness,
    float GuardDamping,
    float GuardMaximumForce,
    float GuardAbsorption);

/// <summary>
/// Chooses the narrow M3 social reactions for Tickle and learned Boxing Glove
/// harm. The buddy root arbitrates this intent and the active drive translates
/// it into bounded forces; guarded accepted impacts are delegated back to that
/// same physics component for the confirmed counter-impulse.
/// </summary>
[GlobalClass]
public partial class ToolReactionComponent : Node
{
    [Export] public BuddyRoot Buddy { get; set; } = null!;
    [Export] public InteractionDamageComponent Pipeline { get; set; } = null!;
    [Export] public CareStrokeComponent CareStroke { get; set; } = null!;
    [Export] public CursorToolController CursorTools { get; set; } = null!;
    [Export] public ToolReactionProfile Profile { get; set; } = null!;

    private Vector2 _guardDirection = Vector2.Right;
    private Vector2 _guardAimPoint;
    private bool _guardAimInitialized;
    private bool _gloveDefenseLatched;
    private bool _tickleReachLatched;

    public ToolReactionIntent Intent { get; private set; }
    public bool IsInitialized { get; private set; }
    /// <summary>
    /// Guarding specifically against the glove. The tickle feather now uses the same reach
    /// (owner instruction 2026-08-19), so the tool — not the raw guard flag — is what separates
    /// "defending myself" from "reaching for the feather"; the angry face keys off this.
    /// </summary>
    public bool IsDefending => Intent.GuardActive && Pipeline.SelectedTool == ToolId.BoxingGlove;
    public bool IsTickleFleeing =>
        Intent.Active && Pipeline.SelectedTool == ToolId.Tickle && Intent.WalkDirection != 0.0f;
    /// <summary>True while the buddy is reaching its hands toward the tickle feather.</summary>
    public bool IsReachingForFeather => Intent.GuardActive && Pipeline.SelectedTool == ToolId.Tickle;
    /// <summary>
    /// True while a learned-harm glove is an immediate on-screen threat. Persistent
    /// harmful memory remains owned by the damage/mood pipeline; presentation uses
    /// this narrower semantic so a selected but off-screen glove cannot pin the face.
    /// </summary>
    public bool IsLearnedGloveThreatActive =>
        Pipeline.SelectedTool == ToolId.BoxingGlove &&
        Pipeline.IsToolHarmful(ToolId.BoxingGlove) &&
        CursorTools.HasCursor;
    public Vector2 GuardDirection => _guardDirection;
    public Vector2 GuardAimPoint => _guardAimPoint;
    public Vector2 GuardCenter => Intent.GuardActive
        ? (Intent.LeftGuardTarget + Intent.RightGuardTarget) * 0.5f
        : Vector2.Zero;

    public void Initialize()
    {
        if (!GodotObject.IsInstanceValid(Buddy) || !Buddy.IsInitialized ||
            !GodotObject.IsInstanceValid(Pipeline) || !Pipeline.IsInitialized ||
            !GodotObject.IsInstanceValid(CareStroke) || !CareStroke.IsInitialized ||
            !GodotObject.IsInstanceValid(CursorTools) || !CursorTools.IsInitialized ||
            !GodotObject.IsInstanceValid(Profile) || Profile.Validate().Count > 0)
        {
            throw new InvalidOperationException("ToolReactionComponent dependencies are incomplete or invalid.");
        }

        Pipeline.ImpactAccepted += OnImpactAccepted;
        IsInitialized = true;
    }

    public override void _ExitTree()
    {
        if (IsInitialized && GodotObject.IsInstanceValid(Pipeline))
            Pipeline.ImpactAccepted -= OnImpactAccepted;
    }

    public void PhysicsTick(double delta)
    {
        if (!IsInitialized)
            throw new InvalidOperationException("ToolReactionComponent used before initialization.");

        bool canReact = Buddy.CurrentConsciousness == Consciousness.Conscious;
        if (!canReact || Pipeline.SelectedTool != ToolId.BoxingGlove)
            _gloveDefenseLatched = false;
        if (!canReact || Pipeline.SelectedTool != ToolId.Tickle)
            _tickleReachLatched = false;

        Intent = canReact ? ResolveIntent(delta) : default;
        if (!Intent.GuardActive)
            _guardAimInitialized = false;
        Buddy.SetToolReactionIntent(Intent);
    }

    private ToolReactionIntent ResolveIntent(double delta)
    {
        if (Pipeline.SelectedTool == ToolId.Tickle)
            return ResolveTickle(delta);
        if (Pipeline.SelectedTool == ToolId.BoxingGlove)
            return ResolveGloveDefense(delta);
        return default;
    }

    private ToolReactionIntent ResolveTickle(double delta)
    {
        bool angry = CareStroke.TickleDisposition == TickleDisposition.Angry;
        bool hopping = angry || CareStroke.TickleHopRequested;

        // The feather gets the same hands-out reach the glove gets: same targeting, same lag,
        // same spring (owner instruction 2026-08-19). Only the reason differs — the buddy is
        // fending off a tickle, not a punch.
        Vector2 leftTarget = Vector2.Zero;
        Vector2 rightTarget = Vector2.Zero;
        bool reaching = false;
        if (CareStroke.IsHeld)
        {
            reaching = TryResolveReach(
                CareStroke.ContactPoint,
                CareStroke.ContactPoint,
                delta,
                ref _tickleReachLatched,
                out leftTarget,
                out rightTarget);
        }
        else
        {
            _tickleReachLatched = false;
        }

        if (!hopping && !reaching)
            return default;

        float away = AwayFrom(CareStroke.ContactPoint);
        return new ToolReactionIntent(
            true,
            angry ? away : 0.0f,
            angry ? Profile.AngryFleeScale : 0.0f,
            CareStroke.TickleHopRequested,
            away,
            angry ? Profile.AngryJumpScale : Profile.FriendlyJumpScale,
            Profile.TickleJumpHorizontalRatio,
            reaching,
            leftTarget,
            rightTarget,
            reaching ? Profile.GuardStiffness : 0.0f,
            reaching ? Profile.GuardDamping : 0.0f,
            reaching ? Profile.GuardMaximumForce : 0.0f,
            reaching ? Profile.GuardAbsorption : 1.0f);
    }

    /// <summary>
    /// Range-latched, lag-aimed hand targets pointed at <paramref name="aimPoint"/>. Shared by the
    /// glove guard and the tickle reach so both feel identical; returns false when the threat body
    /// is out of range, which also drops the latch.
    /// </summary>
    private bool TryResolveReach(
        Vector2 bodyPoint,
        Vector2 aimPoint,
        double delta,
        ref bool latched,
        out Vector2 leftTarget,
        out Vector2 rightTarget)
    {
        leftTarget = Vector2.Zero;
        rightTarget = Vector2.Zero;

        Vector2 protectedCenter = (Buddy.Rig.Head.GlobalPosition + Buddy.Rig.Torso.GlobalPosition) * 0.5f;
        Vector2 towardBody = bodyPoint - protectedCenter;
        float distance = towardBody.Length();
        if (latched)
        {
            if (distance > Profile.DefenseReleaseRange)
            {
                latched = false;
                return false;
            }
        }
        else
        {
            if (distance > Profile.DefenseRange)
                return false;
            latched = true;
        }

        if (!_guardAimInitialized)
        {
            _guardAimPoint = aimPoint;
            _guardAimInitialized = true;
        }
        else
        {
            float lag = Mathf.Max(0.01f, Profile.GuardAimLagSeconds);
            float alpha = 1.0f - Mathf.Exp(-(float)delta / lag);
            _guardAimPoint = _guardAimPoint.Lerp(aimPoint, alpha);
        }

        Vector2 laggedDirection = _guardAimPoint - protectedCenter;
        if (laggedDirection.IsZeroApprox())
            laggedDirection = distance > 0.001f ? towardBody : Vector2.Right;
        _guardDirection = laggedDirection.Normalized();

        Vector2 perpendicular = new(-_guardDirection.Y, _guardDirection.X);
        // Targets stay at a fixed reach from the buddy. They rotate toward the
        // pointer with lag but never become anchors on the physical tool body.
        Vector2 guardCenter = protectedCenter + _guardDirection * Profile.GuardReach;
        Vector2 halfSeparation = perpendicular * (Profile.GuardHandSeparation * 0.5f);
        leftTarget = guardCenter + halfSeparation;
        rightTarget = guardCenter - halfSeparation;
        return true;
    }

    private ToolReactionIntent ResolveGloveDefense(double delta)
    {
        CursorToolBody? glove = CursorTools.Body;
        if (glove is null || !Pipeline.IsToolHarmful(ToolId.BoxingGlove))
        {
            _gloveDefenseLatched = false;
            return default;
        }

        Vector2 threatPoint = CursorTools.HasCursor ? CursorTools.Cursor : glove.GlobalPosition;
        if (!TryResolveReach(
                glove.GlobalPosition,
                threatPoint,
                delta,
                ref _gloveDefenseLatched,
                out Vector2 leftTarget,
                out Vector2 rightTarget))
            return default;

        return new ToolReactionIntent(
            true,
            AwayFrom(threatPoint),
            Profile.DefenseFleeScale,
            false,
            0.0f,
            1.0f,
            0.0f,
            true,
            leftTarget,
            rightTarget,
            Profile.GuardStiffness,
            Profile.GuardDamping,
            Profile.GuardMaximumForce,
            Profile.GuardAbsorption);
    }

    private float AwayFrom(Vector2 threat) =>
        Buddy.Rig.Torso.GlobalPosition.X >= threat.X ? 1.0f : -1.0f;

    private void OnImpactAccepted(AcceptedImpact impact)
    {
        if (!impact.Guarded)
            return;
        Buddy.ActiveDrive.AbsorbGuardedImpact(
            impact.Part,
            impact.Normal,
            impact.RawImpulse,
            Intent.GuardAbsorption);
    }
}
