using System;
using DesktopBuddy.Buddy;
using DesktopBuddy.Buddy.Behavior;
using DesktopBuddy.Buddy.Physics;
using DesktopBuddy.Buddy.Presentation;
using DesktopBuddy.Buddy.Presentation3D;
using DesktopBuddy.Diagnostics;
using DesktopBuddy.Domain.Automation;
using DesktopBuddy.Domain.Physics;
using DesktopBuddy.Domain.Tools;
using DesktopBuddy.Grab;
using DesktopBuddy.Interaction;
using DesktopBuddy.Laboratory;
using DesktopBuddy.Platform;
using DesktopBuddy.Presentation3D;
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
    [Export] public BuddyVisualPresenter VisualPresenter { get; set; } = null!;
    [Export] public BuddyLookLightingRig LightingRig { get; set; } = null!;
    [Export] public BuddyPosePipeline PosePipeline { get; set; } = null!;
    [Export] public FacingController Facing { get; set; } = null!;
    [Export] public ActivityAnimator Activities { get; set; } = null!;
    [Export] public HeadLookAtComponent HeadLookAt { get; set; } = null!;
    [Export] public FaceCompositor Face { get; set; } = null!;
    [Export] public Body2DVisual3D GloveVisual { get; set; } = null!;
    // Mii3D is the shipping default since the M3.5 Task 8 owner gate (2026-07-18); the
    // legacy circles remain behind the V toggle / --presentation=legacy as a dev view.
    [Export] public PresentationMode Mode { get; set; } = PresentationMode.Mii3D;

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
            !GodotObject.IsInstanceValid(MoneyHud) ||
            !GodotObject.IsInstanceValid(VisualPresenter) ||
            !GodotObject.IsInstanceValid(LightingRig) ||
            !GodotObject.IsInstanceValid(PosePipeline) ||
            !GodotObject.IsInstanceValid(Facing) ||
            !GodotObject.IsInstanceValid(Activities) ||
            !GodotObject.IsInstanceValid(HeadLookAt) ||
            !GodotObject.IsInstanceValid(Face) ||
            !GodotObject.IsInstanceValid(GloveVisual))
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
        VisualPresenter.Initialize();
        // Same Resource the presenter renders with: lights and materials share one look truth.
        LightingRig.Initialize(VisualPresenter.Profile.Look);
        PosePipeline.Initialize();
        Facing.Initialize();
        Activities.Initialize();
        // After the animator: look-at reads the eat activity and its item socket.
        HeadLookAt.Initialize();
        // Last of the expressive chain: the face reads reactions, the eat activity, and
        // the look-at pupils.
        Face.Initialize();
        GloveVisual.Initialize(
            Glove.Profile.Radius,
            Glove.Profile.VisualColor,
            Glove.Profile.VisualDepthOffset);
        Containment.Initialize();
        Boundaries.LayoutApplied += Containment.ApplyLayout;
        Boundaries.LayoutApplied += OnBoundaryLayoutApplied;
        Buddy.AutonomousMotion.SetWalkableBounds(Boundaries.InnerBounds);
        Buddy.Recovery.HardRecovered += OnHardRecovered;
        Pipeline.ToolChanged += OnToolChanged;
        Glove.BodySpawned += OnGloveBodySpawned;
        Glove.BodyDespawned += OnGloveBodyDespawned;

        ApplyRunnerPresentationOverride();
        SetPresentationMode(Mode);

        Log.Info("Sandbox", "SandboxRoot composed with desktop shell.");
    }

    public override void _PhysicsProcess(double delta)
    {
        Pointer.ResolvePendingInput();
        VisualPresenter.CaptureTickSnapshot();
        GloveVisual.CaptureTickSnapshot();
        // Shell drains a queued resize into a boundary request; the boundary
        // applies pending layout changes on this physics boundary.
        Shell.PhysicsTick();
        Boundaries.PhysicsTick();
        Grab.PhysicsTick(delta);
        GrabState grab = Grab.CurrentGrab;
        bool buddyPartGrabbed = grab.Active && grab.Target is PuppetPartBody;
        Buddy.GrabResistance.SetGrabContext(buddyPartGrabbed, grab.CursorAnchor);
        Glove.PhysicsTick(delta);
        CareStroke.PhysicsTick(delta);
        ToolReactions.PhysicsTick(delta);
        Reactions.PhysicsTick();
        Buddy.PhysicsTick(buddyPartGrabbed, grab.Active && grab.Target == Buddy.Rig.Head);
        Pipeline.PhysicsTick();
    }

    public override void _ExitTree()
    {
        if (GodotObject.IsInstanceValid(Boundaries) && GodotObject.IsInstanceValid(Containment))
        {
            Boundaries.LayoutApplied -= Containment.ApplyLayout;
            Boundaries.LayoutApplied -= OnBoundaryLayoutApplied;
        }
        if (GodotObject.IsInstanceValid(Buddy) && GodotObject.IsInstanceValid(Buddy.Recovery))
            Buddy.Recovery.HardRecovered -= OnHardRecovered;
        if (GodotObject.IsInstanceValid(Pipeline)) Pipeline.ToolChanged -= OnToolChanged;
        if (GodotObject.IsInstanceValid(Glove))
        {
            Glove.BodySpawned -= OnGloveBodySpawned;
            Glove.BodyDespawned -= OnGloveBodyDespawned;
        }
    }

    private void OnBoundaryLayoutApplied(RoomLayout _layout, Rect2 innerBounds) =>
        Buddy.AutonomousMotion.SetWalkableBounds(innerBounds);

    private void OnHardRecovered(HardRecoveryReason reason)
    {
        if (Grab.IsGrabbing) Grab.Release();
    }

    private void OnToolChanged(ToolId previous, ToolId selected)
    {
        if (previous == ToolId.Grab && Grab.IsGrabbing) Grab.Release();
    }

    public void SetPresentationMode(PresentationMode mode)
    {
        Mode = mode;
        bool show3D = mode == PresentationMode.Mii3D;
        foreach (PuppetPartBody part in Buddy.Rig.Parts)
        {
            part.Visible = !show3D;
        }

        VisualPresenter.Visible = show3D;
        GloveVisual.SetPresentationActive(show3D);
    }

    private void ApplyRunnerPresentationOverride()
    {
        RunnerPresentation? presentation = RunnerArguments.Parse(OS.GetCmdlineUserArgs()).Presentation;
        if (presentation is not null)
        {
            Mode = presentation == RunnerPresentation.Mii3D
                ? PresentationMode.Mii3D
                : PresentationMode.LegacyCircles;
        }
    }

    private void OnGloveBodySpawned(BoxingGloveBody body) => GloveVisual.Attach(body);

    private void OnGloveBodyDespawned(BoxingGloveBody body) => GloveVisual.Detach(body);
}
