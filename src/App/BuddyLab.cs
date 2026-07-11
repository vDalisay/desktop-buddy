using System;
using DesktopBuddy.Buddy;
using DesktopBuddy.Buddy.Physics;
using DesktopBuddy.Diagnostics;
using DesktopBuddy.Grab;
using DesktopBuddy.Laboratory;
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
    [Export] public BuddyRoot Buddy { get; set; } = null!;
    [Export] public LaboratoryControlComponent Controls { get; set; } = null!;
    [Export] public GrabTetherController Grab { get; set; } = null!;
    [Export] public LabPointerGrabComponent Pointer { get; set; } = null!;

    public override void _Ready()
    {
        if (!GodotObject.IsInstanceValid(Buddy) || !GodotObject.IsInstanceValid(Controls) ||
            !GodotObject.IsInstanceValid(Grab) || !GodotObject.IsInstanceValid(Pointer))
        {
            throw new InvalidOperationException(
                "BuddyLab requires injected buddy, laboratory controls, grab tether, and pointer harness.");
        }

        Controls.Initialize();
        Grab.Initialize();
        Pointer.Initialize();

        // DECISIONS.md "Fail-safe cleanup": a hard recovery releases the active
        // grab as part of clearing transient state. The tether lives at lab level
        // and recovery at buddy level, so the lab bridges the two.
        Buddy.Recovery.HardRecovered += OnHardRecovered;

        Log.Info("BuddyLab", "BuddyLab composed with seeded six-body active puppet.");
    }

    public override void _PhysicsProcess(double delta)
    {
        // Pointer acquisition/cursor tracking stays responsive even while paused;
        // the tether only integrates force on a routed tick. Inert when headless.
        Pointer.ResolvePendingInput();

        if (Controls.BeginPhysicsTick())
        {
            // Grab force and buddy drive/constraint forces accumulate into the
            // same physics step; ordering between them does not matter.
            Grab.PhysicsTick(delta);
            GrabState grab = Grab.CurrentGrab;
            bool buddyPartGrabbed = grab.Active && grab.Target is PuppetPartBody;
            Buddy.GrabResistance.SetGrabContext(buddyPartGrabbed, grab.CursorAnchor);

            Buddy.PhysicsTick();
            Controls.NotifyPhysicsTickRouted();
        }
    }

    public override void _ExitTree()
    {
        if (GodotObject.IsInstanceValid(Buddy) && GodotObject.IsInstanceValid(Buddy.Recovery))
        {
            Buddy.Recovery.HardRecovered -= OnHardRecovered;
        }
    }

    private void OnHardRecovered(HardRecoveryReason reason)
    {
        if (Grab.IsGrabbing)
        {
            Grab.Release();
        }
    }
}
