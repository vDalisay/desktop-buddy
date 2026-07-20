using System;
using DesktopBuddy.Buddy.Physics;
using DesktopBuddy.Domain.Buddy;
using DesktopBuddy.Domain.Presentation;
using DesktopBuddy.Grab;
using DesktopBuddy.Interaction;
using Godot;

namespace DesktopBuddy.Buddy.Presentation3D;

/// <summary>
/// M3.6 Task 1 pose-mode arbitration between the M3.5 snapshot layer and the sockets
/// (M3_6_EXPRESSIVE_PRESENTATION_PLAN.md). Reads ONLY existing gameplay semantics on the
/// rendered frame — consciousness, recovery assistance, the live grab, the tool-reaction
/// window, standing stability, and a post-impact cooldown — and answers two questions for
/// the presenter: which mode is active (<see cref="PresentationPoseMode"/>) and the current
/// tracking-to-performance blend weight. Presentation never writes gameplay state; the
/// arbitration rules themselves live engine-free in <see cref="PoseModeArbiter"/>.
/// </summary>
[GlobalClass]
public partial class BuddyPosePipeline : Node
{
    [Export] public BuddyRoot Buddy { get; set; } = null!;
    [Export] public GrabTetherController Grab { get; set; } = null!;
    [Export] public InteractionDamageComponent DamagePipeline { get; set; } = null!;
    [Export] public BuddyExpressionProfile Profile { get; set; } = null!;

    private PerformanceBlend _blend = null!;
    // Routed-tick stamp of the last accepted impact; long.MinValue = never hit.
    private long _lastImpactTick = long.MinValue;

    public PresentationPoseMode Mode { get; private set; } = PresentationPoseMode.Tracking;
    public float PerformanceWeight => IsInitialized ? _blend.Weight : 0.0f;
    public bool IsInitialized { get; private set; }

    public void Initialize()
    {
        if (IsInitialized)
        {
            return;
        }

        if (!GodotObject.IsInstanceValid(Buddy) || !Buddy.IsInitialized ||
            !GodotObject.IsInstanceValid(Grab) ||
            !GodotObject.IsInstanceValid(DamagePipeline) || !DamagePipeline.IsInitialized ||
            !GodotObject.IsInstanceValid(Profile))
        {
            throw new InvalidOperationException("BuddyPosePipeline dependencies are incomplete.");
        }

        Godot.Collections.Array<string> errors = Profile.Validate();
        if (errors.Count > 0)
        {
            throw new InvalidOperationException(
                $"Invalid buddy expression profile: {string.Join("; ", errors)}");
        }

        _blend = new PerformanceBlend(Profile.PerformanceBlendSeconds);
        DamagePipeline.ImpactAccepted += OnImpactAccepted;
        IsInitialized = true;
    }

    public override void _ExitTree()
    {
        if (IsInitialized && GodotObject.IsInstanceValid(DamagePipeline))
        {
            DamagePipeline.ImpactAccepted -= OnImpactAccepted;
        }
    }

    /// <summary>
    /// Arbitrates the mode from current semantics and advances the blend. Called by the
    /// presenter once per rendered frame; allocation-free.
    /// </summary>
    public float Evaluate(double deltaSeconds)
    {
        if (!IsInitialized)
        {
            throw new InvalidOperationException("BuddyPosePipeline used before initialization.");
        }

        // Routed ticks, not engine frames: a paused lab must not burn the cooldown.
        long ticksSinceImpact = _lastImpactTick == long.MinValue
            ? long.MaxValue
            : Buddy.RoutedTicks - _lastImpactTick;
        var inputs = new PoseModeInputs(
            Buddy.CurrentConsciousness == Consciousness.Unconscious,
            Buddy.Recovery.State.AssistanceActive,
            Grab.CurrentGrab.Active && Grab.CurrentGrab.Target is PuppetPartBody,
            Buddy.CurrentToolReactionIntent.Active,
            Buddy.Standing.Snapshot.IsStable,
            (int)Math.Clamp(ticksSinceImpact, 0, int.MaxValue));
        Mode = PoseModeArbiter.Evaluate(inputs, Profile.PostImpactCooldownTicks);
        return _blend.Update(deltaSeconds, Mode);
    }

    private void OnImpactAccepted(AcceptedImpact impact) =>
        _lastImpactTick = Buddy.RoutedTicks;
}
