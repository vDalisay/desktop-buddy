using System;
using DesktopBuddy.Buddy.Behavior;
using DesktopBuddy.Buddy.Physics;
using DesktopBuddy.Buddy.Presentation3D;
using DesktopBuddy.Domain.Autonomy;
using DesktopBuddy.Domain.Buddy;
using DesktopBuddy.Domain.Presentation;
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
    [Export] public BehaviorActivityComponent Activity { get; set; } = null!;

    public event Action<Consciousness>? ConsciousnessChanged;
    /// <summary>Raised on every autonomy reseed so seeded presentation streams
    /// (facing idle variety) can re-derive their own stream from the same seed.</summary>
    public event Action<ulong>? AutonomyReseeded;
    public event Action<ActivityId>? BehaviorActivityChanged;

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
            !GodotObject.IsInstanceValid(GrabResistance) ||
            !GodotObject.IsInstanceValid(Activity))
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
        Activity.Initialize();
        Activity.ActivityChanged += OnBehaviorActivityChanged;
        Recovery.HardRecovered += _ => SetConsciousness(Consciousness.Conscious);
        IsInitialized = true;
    }

    public void PhysicsTick(bool buddyPartGrabbed, bool headGrabbed = false)
    {
        if (!IsInitialized)
        {
            return;
        }

        RoutedTicks++;
        Standing.PhysicsTick();
        bool dangled = buddyPartGrabbed && Standing.Snapshot.SupportContactCount == 0;
        // An airborne grab is the same passive body state as unconsciousness while
        // leaving the buddy's awareness intact. Ground contact keeps normal drive.
        Recovery.PhysicsTick(CurrentConsciousness == Consciousness.Conscious && !dangled);
        Activity.PhysicsTick();
        AutonomousMotion.PhysicsTick(
            CurrentConsciousness,
            Recovery.State,
            behaviorEnabled: !Activity.IsStationary && !dangled);
        GrabResistance.PhysicsTick(CurrentConsciousness);
        CurrentDriveIntent = BuildDriveIntent();
        if (headGrabbed)
            ActiveDrive.NotifyHeadDisturbed();
        ActiveDrive.PhysicsTick(CurrentConsciousness, CurrentDriveIntent, buddyPartGrabbed);
        // Passive structure never turns off: unconsciousness and airborne grabs
        // disable active drive, not the springs that preserve the six-part topology.
        Constraints.PhysicsTick(airborneGrab: dangled);
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
                false, Vector2.Zero, Vector2.Zero, 0.0f, 0.0f, 0.0f, 1.0f,
                false, false, 0.0f, Vector2.Zero, Vector2.Zero);
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
                reaction.GuardAbsorption,
                false,
                false,
                0.0f,
                Vector2.Zero,
                Vector2.Zero);
        }

        AutonomousMotionIntent motion = AutonomousMotion.Intent;
        bool reach = Activity.EatReachActive;
        Vector2 chestCenter = Rig.Torso.GlobalPosition + ActiveDrive.Profile.EatChestTargetOffset;
        Vector2 finalLowerCenter = Rig.Torso.GlobalPosition +
            ActiveDrive.Profile.EatFinalLowerTargetOffset;
        Vector2 returnCenter = chestCenter.Lerp(finalLowerCenter, Activity.EatFinalLowering);
        Vector2 mouthCenter = Rig.Head.GlobalPosition + ActiveDrive.Profile.EatMouthTargetOffset;
        Vector2 reachCenter = reach
            ? returnCenter.Lerp(mouthCenter, Activity.EatLift)
            : Vector2.Zero;
        Vector2 handSeparation = new(ActiveDrive.Profile.EatHandHalfSeparation, 0.0f);
        return new DriveIntent(
            motion.WalkDirection, 1.0f, motion.JumpRequested, 0.0f, 1.0f, 0.0f,
            0.0f, 0.0f, false, Vector2.Zero, Vector2.Zero, 0.0f, 0.0f, 0.0f, 1.0f,
            Activity.IsStationary || AutonomousMotion.IsWallStopping,
            reach,
            Activity.EatLift,
            reachCenter - handSeparation,
            reachCenter + handSeparation);
    }

    /// <summary>Pushed by the focused tool-reaction worker before this buddy ticks.</summary>
    public void SetToolReactionIntent(ToolReactionIntent intent) => CurrentToolReactionIntent = intent;

    /// <summary>Gameplay-owned Class B activity command; presentation observes the event.</summary>
    public void SetBehaviorActivity(ActivityId activity) => Activity.SetActivity(activity);

    /// <summary>Accepted physical disruption cancels a behavior-backed gesture immediately.</summary>
    public void InterruptBehaviorActivity() => Activity.Interrupt();

    private void OnBehaviorActivityChanged(ActivityId activity) =>
        BehaviorActivityChanged?.Invoke(activity);

    public override void _ExitTree()
    {
        if (GodotObject.IsInstanceValid(Activity))
            Activity.ActivityChanged -= OnBehaviorActivityChanged;
    }

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
