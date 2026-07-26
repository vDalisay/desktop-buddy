using System;
using DesktopBuddy.Buddy;
using DesktopBuddy.Buddy.Behavior;
using DesktopBuddy.Buddy.Physics;
using DesktopBuddy.Buddy.Presentation;
using DesktopBuddy.Buddy.Presentation3D;
using DesktopBuddy.Diagnostics;
using DesktopBuddy.Domain.Automation;
using DesktopBuddy.Domain.Content;
using DesktopBuddy.Domain.Persistence;
using DesktopBuddy.Domain.Physics;
using DesktopBuddy.Domain.Presentation;
using DesktopBuddy.Domain.Tools;
using DesktopBuddy.Economy;
using DesktopBuddy.Grab;
using DesktopBuddy.Interaction;
using DesktopBuddy.Laboratory;
using DesktopBuddy.Objects;
using DesktopBuddy.Presentation3D;
using DesktopBuddy.Persistence;
using DesktopBuddy.Sandbox;
using DesktopBuddy.Tools;
using DesktopBuddy.UI;
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
    private bool _allocationProbeEnabled;

    [Export] public BuddyRoot Buddy { get; set; } = null!;
    [Export] public LaboratoryControlComponent Controls { get; set; } = null!;
    [Export] public GrabTetherController Grab { get; set; } = null!;
    [Export] public LabPointerGrabComponent Pointer { get; set; } = null!;
    [Export] public BoundaryController Boundaries { get; set; } = null!;
    [Export] public PuppetRoomContainmentComponent Containment { get; set; } = null!;
    [Export] public LaboratoryTelemetryPanel TelemetryPanel { get; set; } = null!;
    [Export] public LaboratoryBoundaryVisualizer BoundaryVisualizer { get; set; } = null!;
    [Export] public InteractionDamageComponent Pipeline { get; set; } = null!;
    [Export] public LooseObjectRegistry Objects { get; set; } = null!;
    [Export] public LooseObjectProfile LabFoodProfile { get; set; } = null!;
    [Export] public LooseObjectProfile SafeObjectProfile { get; set; } = null!;

    /// <summary>The single per-run persistent semantic state (ARCHITECTURE §12).</summary>
    public BuddyProgressState Progress { get; private set; } = null!;

    /// <summary>The sole currency/unlock mutator for this run (ARCHITECTURE §11).</summary>
    public EconomyService Economy { get; private set; } = null!;
    public SaveCoordinator Saves { get; private set; } = null!;
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
    public TelemetryRecorder? TelemetryRecorder { get; private set; }

    public override void _Ready()
    {
        if (!GodotObject.IsInstanceValid(Buddy) || !GodotObject.IsInstanceValid(Controls) ||
            !GodotObject.IsInstanceValid(Grab) || !GodotObject.IsInstanceValid(Pointer) ||
            !GodotObject.IsInstanceValid(Boundaries) || !GodotObject.IsInstanceValid(Containment) ||
            !GodotObject.IsInstanceValid(TelemetryPanel) ||
            !GodotObject.IsInstanceValid(BoundaryVisualizer) ||
            !GodotObject.IsInstanceValid(Pipeline) ||
            !GodotObject.IsInstanceValid(Objects) ||
            !GodotObject.IsInstanceValid(LabFoodProfile) ||
            !GodotObject.IsInstanceValid(SafeObjectProfile) ||
            !GodotObject.IsInstanceValid(Glove) ||
            !GodotObject.IsInstanceValid(CareStroke) || !GodotObject.IsInstanceValid(ToolReactions) ||
            !GodotObject.IsInstanceValid(CareCursor) || !GodotObject.IsInstanceValid(Reactions) ||
            !GodotObject.IsInstanceValid(ReactionAudio) || !GodotObject.IsInstanceValid(ImpactFeedback) ||
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
                "BuddyLab requires injected buddy, controls, grab, pointer, boundaries, containment, telemetry, boundary visualization, and the interaction pipeline/tools.");
        }

        Controls.Initialize();
        Grab.Initialize();
        Pointer.Initialize();
        // Labs and scenarios are hermetic: never resolve or mutate user:// progress.
        Progress = new BuddyProgressState(Pipeline.RequirePainProfile().CashPerPain);
        Economy = new EconomyService(Progress);
        var progressStore = new InMemoryProgressStore();
        Saves = new SaveCoordinator(Progress, progressStore);
        Pipeline.Initialize(Progress, Economy);
        Objects.Initialize();
        Buddy.Arbiter.Initialize(Progress);
        Buddy.ObjectInteraction.Initialize(Objects, Progress, Buddy.Arbiter.SocialTuning);
        Glove.Initialize();
        CareStroke.Initialize();
        CareCursor.Initialize();
        ToolReactions.Initialize();
        Reactions.Initialize();
        ReactionAudio.Initialize();
        ImpactFeedback.Initialize();
        MoneyHud.Initialize(Economy);
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
        Vector2 viewportSize = GetViewport().GetVisibleRect().Size;
        var clientSize = new Vector2I((int)viewportSize.X, (int)viewportSize.Y);
        if (clientSize.X < RoomLayoutPolicy.MinimumRoomWidth ||
            clientSize.Y < RoomLayoutPolicy.MinimumRoomHeight)
        {
            // Headless scenario viewports can report zero before their first
            // render frame; use the confirmed default client size in that case.
            clientSize = new Vector2I(
                RoomLayoutPolicy.DefaultClientWidth,
                RoomLayoutPolicy.DefaultClientHeight);
        }

        Boundaries.Initialize(clientSize, 1.0);
        Buddy.AutonomousMotion.SetWalkableBounds(Boundaries.InnerBounds);
        BoundaryVisualizer.Initialize();
        TelemetryPanel.Initialize();

        // DECISIONS.md "Fail-safe cleanup": a hard recovery releases the active
        // grab as part of clearing transient state. The tether lives at lab level
        // and recovery at buddy level, so the lab bridges the two.
        Buddy.Recovery.HardRecovered += OnHardRecovered;

        // Leaving Grab drops the current interaction without changing selection
        // rules (RAGDOLL §9.1); the pipeline owns selection, the lab owns the tether.
        Pipeline.ToolChanged += OnToolChanged;
        Grab.Released += OnGrabReleased;
        Controls.PresentationToggleRequested += OnPresentationToggleRequested;
        Controls.EatToggleRequested += OnEatToggleRequested;
        Controls.LooseObjectSpawnRequested += OnLooseObjectSpawnRequested;
        Controls.LooseObjectClearRequested += OnLooseObjectClearRequested;
        Buddy.ObjectInteraction.ConsumeStarted += OnObjectConsumeStarted;
        Buddy.ObjectInteraction.ConsumeCancelled += OnObjectConsumeCancelled;
        Glove.BodySpawned += OnGloveBodySpawned;
        Glove.BodyDespawned += OnGloveBodyDespawned;

        ApplyRunnerPresentationOverride();
        SetPresentationMode(Mode);

        Log.Info("BuddyLab", "BuddyLab composed with seeded six-body active puppet.");
    }

    public override void _PhysicsProcess(double delta)
    {
        // Pointer acquisition/cursor tracking stays responsive even while paused;
        // the tether only integrates force on a routed tick. Inert when headless.
        Pointer.ResolvePendingInput();
        // Capture every engine tick, including paused lab ticks, so the manual
        // 3D interpolation pair stays adjacent and cannot shimmer while frozen.
        VisualPresenter.CaptureTickSnapshot();
        GloveVisual.CaptureTickSnapshot();

        if (Controls.BeginPhysicsTick())
        {
            // Window/zoom requests rebuild containment at a physics boundary.
            Boundaries.PhysicsTick();

            // Grab/tool forces and buddy drive/constraint forces accumulate into
            // the same physics step; ordering between them does not matter.
            Grab.PhysicsTick(delta);
            GrabState grab = Grab.CurrentGrab;
            long registryAllocationBefore = _allocationProbeEnabled
                ? GC.GetAllocatedBytesForCurrentThread()
                : 0;
            Objects.PhysicsTick(grab);
            if (_allocationProbeEnabled)
            {
                PhysicsRegistryAllocationSamples++;
                PhysicsRegistryAllocatedBytes +=
                    GC.GetAllocatedBytesForCurrentThread() - registryAllocationBefore;
            }
            PuppetPartBody? grabbedBody = grab.Active ? grab.Target as PuppetPartBody : null;
            bool buddyPartGrabbed = grabbedBody is not null;
            Buddy.GrabResistance.SetGrabContext(buddyPartGrabbed, grab.CursorAnchor);

            Glove.PhysicsTick(delta);
            CareStroke.PhysicsTick(delta);
            ToolReactions.PhysicsTick(delta);
            Reactions.PhysicsTick();

            Buddy.PhysicsTick(
                grabbedBody?.PartId,
                grab.CursorAnchor,
                Pointer.WorldCursor,
                Pointer.HasPointerInput);

            // ARCHITECTURE §7 steps 7-8: the pipeline consumes the previous
            // step's authoritative contacts after the buddy routed its tick.
            Pipeline.PhysicsTick();

            TelemetryRecorder?.Capture(Controls.RoutedPhysicsTicks);
            Controls.NotifyPhysicsTickRouted();
        }
    }

    public int PhysicsRegistryAllocationSamples { get; private set; }
    public long PhysicsRegistryAllocatedBytes { get; private set; }

    public void BeginPhysicsAllocationProbe()
    {
        PhysicsRegistryAllocationSamples = 0;
        PhysicsRegistryAllocatedBytes = 0;
        Buddy.Arbiter.BeginAllocationProbe();
        _allocationProbeEnabled = true;
    }

    public void EndPhysicsAllocationProbe()
    {
        _allocationProbeEnabled = false;
        Buddy.Arbiter.EndAllocationProbe();
    }

    public void EnableTelemetry(string artifactsDirectory, string id)
    {
        if (TelemetryRecorder is not null)
        {
            throw new InvalidOperationException("Telemetry is already enabled for this lab.");
        }

        TelemetryRecorder = new TelemetryRecorder { Name = nameof(TelemetryRecorder) };
        AddChild(TelemetryRecorder);
        TelemetryRecorder.Initialize(Buddy, Grab, artifactsDirectory, id);
    }

    public override void _ExitTree()
    {
        if (GodotObject.IsInstanceValid(Buddy) && GodotObject.IsInstanceValid(Buddy.Recovery))
        {
            Buddy.Recovery.HardRecovered -= OnHardRecovered;
        }

        if (GodotObject.IsInstanceValid(Boundaries) && GodotObject.IsInstanceValid(Containment))
        {
            Boundaries.LayoutApplied -= Containment.ApplyLayout;
            Boundaries.LayoutApplied -= OnBoundaryLayoutApplied;
        }

        if (GodotObject.IsInstanceValid(Pipeline))
        {
            Pipeline.ToolChanged -= OnToolChanged;
        }
        if (GodotObject.IsInstanceValid(Grab))
        {
            Grab.Released -= OnGrabReleased;
        }

        if (GodotObject.IsInstanceValid(Controls))
        {
            Controls.PresentationToggleRequested -= OnPresentationToggleRequested;
            Controls.EatToggleRequested -= OnEatToggleRequested;
            Controls.LooseObjectSpawnRequested -= OnLooseObjectSpawnRequested;
            Controls.LooseObjectClearRequested -= OnLooseObjectClearRequested;
        }
        if (GodotObject.IsInstanceValid(Buddy) &&
            GodotObject.IsInstanceValid(Buddy.ObjectInteraction))
        {
            Buddy.ObjectInteraction.ConsumeStarted -= OnObjectConsumeStarted;
            Buddy.ObjectInteraction.ConsumeCancelled -= OnObjectConsumeCancelled;
        }
        if (GodotObject.IsInstanceValid(Glove))
        {
            Glove.BodySpawned -= OnGloveBodySpawned;
            Glove.BodyDespawned -= OnGloveBodyDespawned;
        }

        TelemetryRecorder?.Complete();
    }

    private void OnBoundaryLayoutApplied(RoomLayout _layout, Rect2 innerBounds) =>
        Buddy.AutonomousMotion.SetWalkableBounds(innerBounds);

    private void OnHardRecovered(HardRecoveryReason reason)
    {
        Buddy.ObjectInteraction.Reset();
        if (Grab.IsGrabbing)
        {
            Grab.Release(countsAsThrow: false);
        }
    }

    private void OnToolChanged(ToolId previous, ToolId selected)
    {
        if (previous == ToolId.Grab && Grab.IsGrabbing)
        {
            Grab.Release(countsAsThrow: false);
        }
    }

    private void OnGrabReleased(RigidBody2D body, bool countsAsThrow)
    {
        if (body is not LooseObjectBody loose || loose.RuntimeId == 0)
            return;

        if (countsAsThrow)
            Objects.MarkPlayerThrown(loose, ContentIds.ToolGrab);
        else
            Objects.MarkBuddyReleased(loose);
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

    private void OnPresentationToggleRequested() => SetPresentationMode(
        Mode == PresentationMode.LegacyCircles
            ? PresentationMode.Mii3D
            : PresentationMode.LegacyCircles);

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

    /// <summary>Root-owned loose-object factory used by the lab and scenarios.</summary>
    public LooseObjectBody? SpawnLooseObject(
        LooseObjectProfile profile,
        Vector2 worldPosition,
        Vector2 velocity = default,
        bool playerThrown = false)
    {
        bool profileValid = GodotObject.IsInstanceValid(profile) && profile.IsRuntimeValid;
        if (!profileValid)
            return null;

        var body = new LooseObjectBody
        {
            Name = $"LooseObject_{Objects.Count + 1}",
            GlobalPosition = worldPosition,
            LinearVelocity = velocity,
        };
        body.Configure(profile);
        AddChild(body);
        if (!Objects.TryRegister(body, profile, out _))
        {
            body.QueueFree();
            return null;
        }

        if (playerThrown)
            Objects.MarkPlayerThrown(body);
        return body;
    }

    /// <summary>
    /// Drops a safe loose object at the cursor, or on the floor ahead of the buddy when
    /// the pointer has not been used yet. The owner needs objects in the room to judge
    /// catching, tossing, and obstacle hops at all; the Eat key only ever puts food
    /// directly into the hand.
    /// </summary>
    private void OnLooseObjectSpawnRequested()
    {
        float floorY = Boundaries.InnerBounds.End.Y - SafeObjectProfile.Radius - 1.0f;
        Vector2 spawn = Pointer.HasPointerInput
            ? Pointer.WorldCursor
            : new Vector2(Buddy.Rig.Torso.GlobalPosition.X + 70.0f, floorY);
        spawn = new Vector2(
            Mathf.Clamp(
                spawn.X,
                Boundaries.InnerBounds.Position.X + SafeObjectProfile.Radius,
                Boundaries.InnerBounds.End.X - SafeObjectProfile.Radius),
            Mathf.Min(spawn.Y, floorY));
        if (SpawnLooseObject(SafeObjectProfile, spawn) is null)
            Log.Warn("Laboratory", "Loose-object spawn refused; registry is full of protected objects.");
    }

    private void OnLooseObjectClearRequested()
    {
        for (int index = GetChildCount() - 1; index >= 0; index--)
        {
            if (GetChild(index) is not LooseObjectBody body)
                continue;
            if (Buddy.ObjectInteraction.IsHolding &&
                Buddy.ObjectInteraction.TrackedRuntimeId == body.RuntimeId)
            {
                Buddy.ObjectInteraction.CancelActiveInteraction();
            }
            if (Grab.IsGrabbing)
                Grab.Release(countsAsThrow: false);
            Objects.Unregister(body);
            body.QueueFree();
        }
    }

    private void OnEatToggleRequested()
    {
        if (Buddy.Activity.Current == ActivityId.Eat)
        {
            LooseObjectBody? cancelled = Objects.FindBody(Buddy.ObjectInteraction.TrackedRuntimeId);
            Buddy.ObjectInteraction.CancelActiveInteraction();
            Buddy.SetBehaviorActivity(ActivityId.None);
            Activities.ClearItemVisual();
            // The E key spawned this food, so the E key removes it. Leaving a dropped
            // consumable in the room means a neutral-or-better buddy walks straight back
            // to pick it up, which overrides whatever the operator does next.
            if (GodotObject.IsInstanceValid(cancelled) &&
                cancelled!.SemanticContentId == ContentIds.CareLabFood)
            {
                Objects.Unregister(cancelled);
                cancelled.QueueFree();
            }
            return;
        }

        Vector2 spawn = (Buddy.Rig.LeftHand.GlobalPosition + Buddy.Rig.RightHand.GlobalPosition) * 0.5f;
        LooseObjectBody? food = SpawnLooseObject(LabFoodProfile, spawn);
        if (food is null || !Buddy.ObjectInteraction.TryBeginLaboratoryFoodConsume(food))
        {
            if (GodotObject.IsInstanceValid(food))
            {
                Objects.Unregister(food!);
                food!.QueueFree();
            }
        }
    }

    private void OnObjectConsumeStarted(LooseObjectBody body)
    {
        Activities.AttachItemVisual(new MeshInstance3D
        {
            Name = "LabEatItemVisual",
            Mesh = new SphereMesh
            {
                Radius = Mathf.Max(2.0f, body.Radius * 0.25f),
                Height = Mathf.Max(4.0f, body.Radius * 0.5f),
            },
        });
    }

    private void OnObjectConsumeCancelled(LooseObjectBody _body) =>
        Activities.ClearItemVisual();
}
