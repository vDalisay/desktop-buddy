using System;
using DesktopBuddy.Buddy.Behavior;
using DesktopBuddy.Buddy.Physics;
using DesktopBuddy.Buddy.Presentation3D;
using DesktopBuddy.Domain.Autonomy;
using DesktopBuddy.Domain.Buddy;
using Godot;

namespace DesktopBuddy.Buddy;

/// <summary>
/// Thin buddy composition router. The owning sandbox/laboratory calls the
/// single fixed-tick entry; this root delegates to focused components.
/// </summary>
[GlobalClass]
public partial class BuddyRoot : Node2D
{
    private const ulong DefaultAutonomySeed = 1;

    [Export] public PuppetRig Rig { get; set; } = null!;
    [Export] public BuddyVisualProfile VisualProfile { get; set; } = null!;
    [Export] public PuppetConstraintComponent Constraints { get; set; } = null!;
    [Export] public StandingDetector Standing { get; set; } = null!;
    [Export] public RecoveryComponent Recovery { get; set; } = null!;
    [Export] public AutonomousMotionComponent AutonomousMotion { get; set; } = null!;
    [Export] public ActiveDriveComponent ActiveDrive { get; set; } = null!;
    [Export] public GrabResistanceComponent GrabResistance { get; set; } = null!;

    public event Action<Consciousness>? ConsciousnessChanged;
    /// <summary>Raised on every autonomy reseed so seeded presentation streams
    /// (facing idle variety) can re-derive their own stream from the same seed.</summary>
    public event Action<ulong>? AutonomyReseeded;

    public Consciousness CurrentConsciousness { get; private set; } = Consciousness.Conscious;
    public bool IsInitialized { get; private set; }
    public DriveIntent CurrentDriveIntent { get; private set; }

    /// <summary>
    /// Gameplay ticks actually routed into this buddy — the simulation's own clock, which
    /// is NOT the engine's physics-frame counter: a paused laboratory keeps ticking engine
    /// frames while routing none of them. Presentation timers (facing hysteresis, look-at
    /// glances and impact memory, the post-impact cooldown) count in this clock so they
    /// hold still exactly when the simulation they decorate holds still, and advance by
    /// exactly one on a single step. Read-only for everyone but this root.
    /// </summary>
    public long RoutedTicks { get; private set; }
    public ToolReactionIntent CurrentToolReactionIntent { get; private set; }

    public override void _Ready()
    {
        if (!GodotObject.IsInstanceValid(Rig) || !GodotObject.IsInstanceValid(VisualProfile) ||
            !GodotObject.IsInstanceValid(Constraints) ||
            !GodotObject.IsInstanceValid(Standing) || !GodotObject.IsInstanceValid(Recovery) ||
            !GodotObject.IsInstanceValid(AutonomousMotion) ||
            !GodotObject.IsInstanceValid(ActiveDrive) ||
            !GodotObject.IsInstanceValid(GrabResistance))
        {
            throw new InvalidOperationException(
                "BuddyRoot requires its visual profile and every injected physics and behavior component.");
        }

        Godot.Collections.Array<string> visualErrors = VisualProfile.Validate();
        if (visualErrors.Count > 0)
        {
            throw new InvalidOperationException($"Invalid buddy visual profile: {string.Join("; ", visualErrors)}");
        }

        var fillColors = new Color[PuppetRigProfile.RequiredPartCount];
        for (int index = 0; index < fillColors.Length; index++)
        {
            PartVisualDefinition part = VisualProfile.FindPart((BuddyPartId)index)
                ?? throw new InvalidOperationException($"Missing visual definition for {(BuddyPartId)index}.");
            fillColors[index] = part.Color;
        }

        Rig.Initialize(GlobalPosition, fillColors);
        Constraints.Initialize();
        Standing.Initialize();
        Recovery.Initialize();
        AutonomousMotion.Initialize(DefaultAutonomySeed);
        ActiveDrive.Initialize();
        GrabResistance.Initialize();
        Recovery.HardRecovered += _ => SetConsciousness(Consciousness.Conscious);
        IsInitialized = true;
    }

    public void PhysicsTick()
    {
        if (!IsInitialized)
        {
            return;
        }

        RoutedTicks++;
        Standing.PhysicsTick();
        Recovery.PhysicsTick(CurrentConsciousness == Consciousness.Conscious);
        AutonomousMotion.PhysicsTick(CurrentConsciousness, Recovery.State);
        GrabResistance.PhysicsTick(CurrentConsciousness);
        CurrentDriveIntent = BuildDriveIntent();
        ActiveDrive.PhysicsTick(CurrentConsciousness, CurrentDriveIntent);
        Constraints.PhysicsTick();
    }

    // Minimal actuation arbitration until the full BehaviorArbiter lands: a
    // player-constraint fear response supersedes ambient autonomy.
    private DriveIntent BuildDriveIntent()
    {
        GrabResistanceIntent resistance = GrabResistance.Intent;
        if (resistance.Active)
        {
            return new DriveIntent(
                0.0f, 0.0f, false, 0.0f, 1.0f, 0.0f,
                resistance.Direction, resistance.Strength,
                false, Vector2.Zero, Vector2.Zero, 0.0f, 0.0f, 0.0f, 1.0f);
        }

        ToolReactionIntent reaction = CurrentToolReactionIntent;
        if (reaction.Active)
        {
            return new DriveIntent(
                reaction.WalkDirection,
                reaction.LocomotionScale,
                reaction.JumpRequested,
                reaction.JumpDirection,
                reaction.JumpScale,
                reaction.JumpHorizontalRatio,
                0.0f,
                0.0f,
                reaction.GuardActive,
                reaction.LeftGuardTarget,
                reaction.RightGuardTarget,
                reaction.GuardStiffness,
                reaction.GuardDamping,
                reaction.GuardMaximumForce,
                reaction.GuardAbsorption);
        }

        AutonomousMotionIntent motion = AutonomousMotion.Intent;
        return new DriveIntent(
            motion.WalkDirection, 1.0f, motion.JumpRequested, 0.0f, 1.0f, 0.0f,
            0.0f, 0.0f, false, Vector2.Zero, Vector2.Zero, 0.0f, 0.0f, 0.0f, 1.0f);
    }

    /// <summary>Pushed by the focused tool-reaction worker before this buddy ticks.</summary>
    public void SetToolReactionIntent(ToolReactionIntent intent) => CurrentToolReactionIntent = intent;

    public void SetConsciousness(Consciousness consciousness)
    {
        if (CurrentConsciousness == consciousness)
        {
            return;
        }

        CurrentConsciousness = consciousness;
        ConsciousnessChanged?.Invoke(consciousness);
    }

    public void ReseedAutonomy(ulong seed)
    {
        AutonomousMotion.Reseed(seed);
        AutonomyReseeded?.Invoke(seed);
    }
}
