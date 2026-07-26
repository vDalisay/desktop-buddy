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
    [Export] public BoxingGloveController Glove { get; set; } = null!;
    [Export] public ToolReactionProfile Profile { get; set; } = null!;

    private Vector2 _guardDirection = Vector2.Right;
    private Vector2 _guardAimPoint;
    private bool _guardAimInitialized;
    private bool _gloveDefenseLatched;

    public ToolReactionIntent Intent { get; private set; }
    public bool IsInitialized { get; private set; }
    public bool IsDefending => Intent.GuardActive;
    public bool IsTickleFleeing => Intent.Active && !Intent.GuardActive && Intent.WalkDirection != 0.0f;
    /// <summary>
    /// True while a learned-harm glove is an immediate on-screen threat. Persistent
    /// harmful memory remains owned by the damage/mood pipeline; presentation uses
    /// this narrower semantic so a selected but off-screen glove cannot pin the face.
    /// </summary>
    public bool IsLearnedGloveThreatActive =>
        Pipeline.SelectedTool == ToolId.BoxingGlove &&
        Pipeline.IsToolHarmful(ToolId.BoxingGlove) &&
        Glove.HasCursor;
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
            !GodotObject.IsInstanceValid(Glove) || !Glove.IsInitialized ||
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

        Intent = canReact ? ResolveIntent(delta) : default;
        if (!Intent.GuardActive)
            _guardAimInitialized = false;
        Buddy.SetToolReactionIntent(Intent);
    }

    private ToolReactionIntent ResolveIntent(double delta)
    {
        if (Pipeline.SelectedTool == ToolId.Tickle)
            return ResolveTickle();
        if (Pipeline.SelectedTool == ToolId.BoxingGlove)
            return ResolveGloveDefense(delta);
        return default;
    }

    private ToolReactionIntent ResolveTickle()
    {
        bool angry = CareStroke.TickleDisposition == TickleDisposition.Angry;
        bool active = angry || CareStroke.TickleHopRequested;
        if (!active)
            return default;

        float away = AwayFrom(CareStroke.Cursor);
        return new ToolReactionIntent(
            true,
            angry ? away : 0.0f,
            angry ? Profile.AngryFleeScale : 0.0f,
            CareStroke.TickleHopRequested,
            away,
            angry ? Profile.AngryJumpScale : Profile.FriendlyJumpScale,
            Profile.TickleJumpHorizontalRatio,
            false,
            Vector2.Zero,
            Vector2.Zero,
            0.0f,
            0.0f,
            0.0f,
            1.0f);
    }

    private ToolReactionIntent ResolveGloveDefense(double delta)
    {
        BoxingGloveBody? glove = Glove.Glove;
        if (glove is null || !Pipeline.IsToolHarmful(ToolId.BoxingGlove))
        {
            _gloveDefenseLatched = false;
            return default;
        }

        Vector2 protectedCenter = (Buddy.Rig.Head.GlobalPosition + Buddy.Rig.Torso.GlobalPosition) * 0.5f;
        Vector2 towardGlove = glove.GlobalPosition - protectedCenter;
        float distance = towardGlove.Length();
        if (_gloveDefenseLatched)
        {
            if (distance > Profile.DefenseReleaseRange)
            {
                _gloveDefenseLatched = false;
                return default;
            }
        }
        else
        {
            if (distance > Profile.DefenseRange)
                return default;
            _gloveDefenseLatched = true;
        }

        Vector2 threatPoint = Glove.HasCursor ? Glove.Cursor : glove.GlobalPosition;
        if (!_guardAimInitialized)
        {
            _guardAimPoint = threatPoint;
            _guardAimInitialized = true;
        }
        else
        {
            float lag = Mathf.Max(0.01f, Profile.GuardAimLagSeconds);
            float alpha = 1.0f - Mathf.Exp(-(float)delta / lag);
            _guardAimPoint = _guardAimPoint.Lerp(threatPoint, alpha);
        }

        Vector2 laggedDirection = _guardAimPoint - protectedCenter;
        if (laggedDirection.IsZeroApprox())
            laggedDirection = distance > 0.001f ? towardGlove : Vector2.Right;
        _guardDirection = laggedDirection.Normalized();

        Vector2 perpendicular = new(-_guardDirection.Y, _guardDirection.X);
        // Targets stay at a fixed reach from the buddy. They rotate toward the
        // pointer with lag but never become anchors on the physical glove body.
        Vector2 guardCenter = protectedCenter + _guardDirection * Profile.GuardReach;
        Vector2 halfSeparation = perpendicular * (Profile.GuardHandSeparation * 0.5f);
        float away = AwayFrom(threatPoint);

        return new ToolReactionIntent(
            true,
            away,
            Profile.DefenseFleeScale,
            false,
            0.0f,
            1.0f,
            0.0f,
            true,
            guardCenter + halfSeparation,
            guardCenter - halfSeparation,
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
