using System;
using DesktopBuddy.Buddy.Physics;
using DesktopBuddy.Domain.Autonomy;
using DesktopBuddy.Domain.Buddy;
using DesktopBuddy.Domain.Physics;
using Godot;

namespace DesktopBuddy.Buddy.Behavior;

/// <summary>
/// Selects seeded ambient idle/walk/jump intent from physical support state.
/// It never applies forces and is suppressed by consciousness or recovery.
/// </summary>
[GlobalClass]
public partial class AutonomousMotionComponent : Node
{
    private AutonomousMotionPlanner? _planner;

    [Export] public StandingDetector Standing { get; set; } = null!;
    [Export] public AutonomousMotionProfile Profile { get; set; } = null!;

    public AutonomousMotionIntent Intent { get; private set; }
    public ulong Seed { get; private set; }
    public int JumpRequestCount { get; private set; }
    public bool IsInitialized { get; private set; }

    public void Initialize(ulong seed)
    {
        if (!GodotObject.IsInstanceValid(Standing) || !Standing.IsInitialized ||
            !GodotObject.IsInstanceValid(Profile) || Profile.Validate().Count > 0)
        {
            throw new InvalidOperationException(
                "AutonomousMotionComponent requires an initialized standing detector and valid profile.");
        }

        Reseed(seed);
        IsInitialized = true;
    }

    public void Reseed(ulong seed)
    {
        Seed = seed;
        _planner = new AutonomousMotionPlanner(new SeededRandomSource(seed), Profile.ToTuning());
        Intent = default;
        JumpRequestCount = 0;
    }

    public void PhysicsTick(Consciousness consciousness, RecoveryClockState recovery)
    {
        if (_planner is null)
        {
            throw new InvalidOperationException("AutonomousMotionComponent was ticked before initialization.");
        }

        StandingSnapshot standing = Standing.Snapshot;
        bool enabled = consciousness == Consciousness.Conscious && !recovery.AssistanceActive;
        bool canWalk = standing.SupportContactCount > 0;
        bool canJump = standing.IsStable;
        Intent = _planner.Tick(enabled, canWalk, canJump);
        if (Intent.JumpRequested)
        {
            JumpRequestCount++;
        }
    }
}
