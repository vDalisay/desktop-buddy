using DesktopBuddy.Diagnostics;
using Godot;

namespace DesktopBuddy.App;

/// <summary>
/// Composition root of the physics laboratory scene (ROADMAP.md Milestone 1).
/// The laboratory runs the real production rig/components at a fixed 120 Hz with
/// seeded scenarios and development-only telemetry/controls; it is not a shipped
/// scene and is excluded from release exports. Milestone 0 provides only the
/// empty composition root so the scene exists and imports; the six-body puppet,
/// drive, grab, and telemetry land in Milestone 1.
/// </summary>
public partial class BuddyLab : Node2D
{
    public override void _Ready()
    {
        Log.Info("BuddyLab", "BuddyLab composed (Milestone 1 fills the rig).");
    }
}
