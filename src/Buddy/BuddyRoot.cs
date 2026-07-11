using System;
using DesktopBuddy.Buddy.Physics;
using Godot;

namespace DesktopBuddy.Buddy;

/// <summary>
/// Thin buddy composition router. The owning sandbox/laboratory calls the
/// single fixed-tick entry; this root delegates to focused components.
/// </summary>
[GlobalClass]
public partial class BuddyRoot : Node2D
{
    [Export] public PuppetRig Rig { get; set; } = null!;
    [Export] public PuppetConstraintComponent Constraints { get; set; } = null!;

    public bool IsInitialized { get; private set; }

    public override void _Ready()
    {
        if (!GodotObject.IsInstanceValid(Rig) || !GodotObject.IsInstanceValid(Constraints))
        {
            throw new InvalidOperationException("BuddyRoot requires injected rig and constraint components.");
        }

        Rig.Initialize();
        Constraints.Initialize();
        IsInitialized = true;
    }

    public void PhysicsTick()
    {
        if (!IsInitialized)
        {
            return;
        }

        Constraints.PhysicsTick();
    }
}
