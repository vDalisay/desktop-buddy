using System;
using DesktopBuddy.Buddy.Behavior;
using DesktopBuddy.Buddy.Physics;
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
    [Export] public PuppetConstraintComponent Constraints { get; set; } = null!;
    [Export] public StandingDetector Standing { get; set; } = null!;
    [Export] public RecoveryComponent Recovery { get; set; } = null!;
    [Export] public AutonomousMotionComponent AutonomousMotion { get; set; } = null!;
    [Export] public ActiveDriveComponent ActiveDrive { get; set; } = null!;

    public event Action<Consciousness>? ConsciousnessChanged;

    public Consciousness CurrentConsciousness { get; private set; } = Consciousness.Conscious;
    public bool IsInitialized { get; private set; }

    public override void _Ready()
    {
        if (!GodotObject.IsInstanceValid(Rig) || !GodotObject.IsInstanceValid(Constraints) ||
            !GodotObject.IsInstanceValid(Standing) || !GodotObject.IsInstanceValid(Recovery) ||
            !GodotObject.IsInstanceValid(AutonomousMotion) ||
            !GodotObject.IsInstanceValid(ActiveDrive))
        {
            throw new InvalidOperationException("BuddyRoot requires every injected physics and behavior component.");
        }

        Rig.Initialize(GlobalPosition);
        Constraints.Initialize();
        Standing.Initialize();
        Recovery.Initialize();
        AutonomousMotion.Initialize(DefaultAutonomySeed);
        ActiveDrive.Initialize();
        Recovery.HardRecovered += _ => SetConsciousness(Consciousness.Conscious);
        IsInitialized = true;
    }

    public void PhysicsTick()
    {
        if (!IsInitialized)
        {
            return;
        }

        Standing.PhysicsTick();
        Recovery.PhysicsTick(CurrentConsciousness == Consciousness.Conscious);
        AutonomousMotion.PhysicsTick(CurrentConsciousness, Recovery.State);
        ActiveDrive.PhysicsTick(CurrentConsciousness, AutonomousMotion.Intent);
        Constraints.PhysicsTick();
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

    public void ReseedAutonomy(ulong seed) => AutonomousMotion.Reseed(seed);
}
