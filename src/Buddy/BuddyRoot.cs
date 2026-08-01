using System;
using DesktopBuddy.Buddy.Behavior;
using DesktopBuddy.Buddy.Physics;
using DesktopBuddy.Buddy.Presentation3D;
using DesktopBuddy.Domain.Buddy;
using DesktopBuddy.Domain.Physics;
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
    [Export] public ObjectInteractionComponent ObjectInteraction { get; set; } = null!;
    [Export] public BehaviorArbiter Arbiter { get; set; } = null!;

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
            !GodotObject.IsInstanceValid(Activity) ||
            !GodotObject.IsInstanceValid(ObjectInteraction) ||
            !GodotObject.IsInstanceValid(Arbiter))
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

    public void PhysicsTick(
        BuddyPartId? grabbedPart = null,
        Vector2 grabWorldAnchor = default,
        Vector2 cursorWorldPosition = default,
        bool socialTargetValid = false)
    {
        if (!IsInitialized)
        {
            return;
        }

        RoutedTicks++;
        Standing.PhysicsTick();
        bool buddyPartGrabbed = grabbedPart is not null;
        bool dangled = buddyPartGrabbed && Standing.Snapshot.SupportContactCount == 0;
        // An airborne grab is the same passive body state as unconsciousness while
        // leaving the buddy's awareness intact. Ground contact keeps normal drive.
        int hardRecoveryCountBefore = Recovery.HardRecoveryCount;
        Recovery.PhysicsTick(CurrentConsciousness == Consciousness.Conscious && !dangled);
        bool hardRecoveredThisTick = Recovery.HardRecoveryCount != hardRecoveryCountBefore;
        Activity.PhysicsTick();
        GrabResistance.PhysicsTick(CurrentConsciousness);
        CurrentDriveIntent = Arbiter.PhysicsTick(
            RoutedTicks,
            CurrentConsciousness,
            CurrentToolReactionIntent,
            grabbedPart,
            dangled,
            hardRecoveredThisTick,
            cursorWorldPosition,
            socialTargetValid);
        if (grabbedPart == BuddyPartId.Head)
            ActiveDrive.NotifyHeadDisturbed();
        ActiveDrive.PhysicsTick(
            CurrentConsciousness,
            CurrentDriveIntent,
            grabbedPart,
            grabWorldAnchor);
        // Passive structure never turns off: unconsciousness and airborne grabs
        // disable active drive, not the springs that preserve the six-part topology.
        Constraints.PhysicsTick(airborneGrab: dangled);
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
        // The soccer kick picks its angle off its own salted stream from the same seed, so a
        // reseeded scenario replays the same sequence of straight and angled kicks.
        ObjectInteraction.Reseed(seed);
        AutonomyReseeded?.Invoke(seed);
    }
}
