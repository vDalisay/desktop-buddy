using System;
using DesktopBuddy.Buddy;
using DesktopBuddy.Buddy.Behavior;
using DesktopBuddy.Buddy.Physics;
using DesktopBuddy.Buddy.Presentation;
using DesktopBuddy.Diagnostics;
using DesktopBuddy.Domain.Tools;
using DesktopBuddy.Grab;
using DesktopBuddy.Interaction;
using DesktopBuddy.Laboratory;
using DesktopBuddy.Platform;
using DesktopBuddy.Sandbox;
using DesktopBuddy.Tools;
using DesktopBuddy.UI;
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
    [Export] public BuddyRoot Buddy { get; set; } = null!;
    [Export] public GrabTetherController Grab { get; set; } = null!;
    [Export] public LabPointerGrabComponent Pointer { get; set; } = null!;
    [Export] public PuppetRoomContainmentComponent Containment { get; set; } = null!;
    [Export] public InteractionDamageComponent Pipeline { get; set; } = null!;
    [Export] public BoxingGloveController Glove { get; set; } = null!;
    [Export] public CareStrokeComponent CareStroke { get; set; } = null!;
    [Export] public ToolReactionComponent ToolReactions { get; set; } = null!;
    [Export] public ToolCursorPresenter CareCursor { get; set; } = null!;
    [Export] public BuddyReactionComponent Reactions { get; set; } = null!;
    [Export] public ReactionAudioPresenter ReactionAudio { get; set; } = null!;
    [Export] public ImpactFeedbackPresenter ImpactFeedback { get; set; } = null!;
    [Export] public MoneyHudPresenter MoneyHud { get; set; } = null!;

    public override void _Ready()
    {
        if (!GodotObject.IsInstanceValid(Window) ||
            !GodotObject.IsInstanceValid(Shell) ||
            !GodotObject.IsInstanceValid(Boundaries) || !GodotObject.IsInstanceValid(Buddy) ||
            !GodotObject.IsInstanceValid(Grab) || !GodotObject.IsInstanceValid(Pointer) ||
            !GodotObject.IsInstanceValid(Containment) || !GodotObject.IsInstanceValid(Pipeline) ||
            !GodotObject.IsInstanceValid(Glove) || !GodotObject.IsInstanceValid(CareStroke) ||
            !GodotObject.IsInstanceValid(ToolReactions) || !GodotObject.IsInstanceValid(CareCursor) ||
            !GodotObject.IsInstanceValid(Reactions) || !GodotObject.IsInstanceValid(ReactionAudio) ||
            !GodotObject.IsInstanceValid(ImpactFeedback) ||
            !GodotObject.IsInstanceValid(MoneyHud))
        {
            throw new InvalidOperationException(
                "SandboxRoot requires an injected window controller, shell controller, and boundary.");
        }

        Grab.Initialize();
        Pointer.Initialize(developmentOnly: false);
        Pipeline.Initialize();
        Glove.Initialize();
        CareStroke.Initialize();
        CareCursor.Initialize();
        ToolReactions.Initialize();
        Reactions.Initialize();
        ReactionAudio.Initialize();
        ImpactFeedback.Initialize();
        MoneyHud.Initialize();
        Containment.Initialize();
        Boundaries.LayoutApplied += Containment.ApplyLayout;
        Buddy.Recovery.HardRecovered += OnHardRecovered;
        Pipeline.ToolChanged += OnToolChanged;

        Log.Info("Sandbox", "SandboxRoot composed with desktop shell.");
    }

    public override void _PhysicsProcess(double delta)
    {
        Pointer.ResolvePendingInput();
        // Shell drains a queued resize into a boundary request; the boundary
        // applies pending layout changes on this physics boundary.
        Shell.PhysicsTick();
        Boundaries.PhysicsTick();
        Grab.PhysicsTick(delta);
        GrabState grab = Grab.CurrentGrab;
        Buddy.GrabResistance.SetGrabContext(grab.Active && grab.Target is PuppetPartBody, grab.CursorAnchor);
        Glove.PhysicsTick(delta);
        CareStroke.PhysicsTick(delta);
        ToolReactions.PhysicsTick(delta);
        Reactions.PhysicsTick();
        Buddy.PhysicsTick();
        Pipeline.PhysicsTick();
    }

    public override void _ExitTree()
    {
        if (GodotObject.IsInstanceValid(Boundaries) && GodotObject.IsInstanceValid(Containment))
            Boundaries.LayoutApplied -= Containment.ApplyLayout;
        if (GodotObject.IsInstanceValid(Buddy) && GodotObject.IsInstanceValid(Buddy.Recovery))
            Buddy.Recovery.HardRecovered -= OnHardRecovered;
        if (GodotObject.IsInstanceValid(Pipeline)) Pipeline.ToolChanged -= OnToolChanged;
    }

    private void OnHardRecovered(HardRecoveryReason reason)
    {
        if (Grab.IsGrabbing) Grab.Release();
    }

    private void OnToolChanged(ToolId previous, ToolId selected)
    {
        if (previous == ToolId.Grab && Grab.IsGrabbing) Grab.Release();
    }
}
