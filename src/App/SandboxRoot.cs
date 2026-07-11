using DesktopBuddy.Diagnostics;
using Godot;

namespace DesktopBuddy.App;

/// <summary>
/// Composition root of the play sandbox (the normal-boot target). Per
/// ARCHITECTURE.md Section 3 this node only composes and routes: it owns the
/// single gameplay <c>_PhysicsProcess</c> that drives the fixed-tick order for
/// its children, and holds nothing else. The boundary controller, buddy,
/// tools, loose-object registry, and overlay UI attach here as focused
/// components/services in later milestones. Milestone 0 keeps it empty but real
/// so the boot path composes and the boot smoke check can assert its presence.
/// </summary>
public partial class SandboxRoot : Node2D
{
    public override void _Ready()
    {
        Log.Info("Sandbox", "SandboxRoot composed.");
    }
}
