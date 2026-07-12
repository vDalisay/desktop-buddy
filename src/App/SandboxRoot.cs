using System;
using DesktopBuddy.Diagnostics;
using DesktopBuddy.Platform;
using DesktopBuddy.Sandbox;
using Godot;

namespace DesktopBuddy.App;

/// <summary>
/// Composition root of the play sandbox (the normal-boot target). Per
/// ARCHITECTURE.md Section 3 this node only composes and routes: it owns the
/// single gameplay <c>_PhysicsProcess</c> that drives the fixed-tick order for
/// its children. Milestone 2 wires the desktop shell (transparent box window,
/// Work/Play modes, resize→boundary rebuild); the buddy, tools, loose-object
/// registry, and overlay UI attach here as focused components in later milestones.
/// </summary>
public partial class SandboxRoot : Node2D
{
    [Export] public DesktopWindowController Window { get; set; } = null!;
    [Export] public DesktopShellController Shell { get; set; } = null!;
    [Export] public BoundaryController Boundaries { get; set; } = null!;

    public override void _Ready()
    {
        if (!GodotObject.IsInstanceValid(Window) ||
            !GodotObject.IsInstanceValid(Shell) ||
            !GodotObject.IsInstanceValid(Boundaries))
        {
            throw new InvalidOperationException(
                "SandboxRoot requires an injected window controller, shell controller, and boundary.");
        }

        Log.Info("Sandbox", "SandboxRoot composed with desktop shell.");
    }

    public override void _PhysicsProcess(double delta)
    {
        // Shell drains a queued resize into a boundary request; the boundary
        // applies pending layout changes on this physics boundary.
        Shell.PhysicsTick();
        Boundaries.PhysicsTick();
    }
}
