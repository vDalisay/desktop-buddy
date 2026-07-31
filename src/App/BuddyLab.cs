using System;
using DesktopBuddy.Buddy;
using DesktopBuddy.Buddy.Behavior;
using DesktopBuddy.Buddy.Physics;
using DesktopBuddy.Buddy.Presentation;
using DesktopBuddy.Buddy.Presentation3D;
using DesktopBuddy.Content;
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
using DesktopBuddy.Platform;
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
    private IWindowsDesktopAdapter _windowAdapter = null!;
    private RunContext? _runContext;
    private LooseObjectBody? _shownGrenade;

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
    [Export] public MoodEconomyProfile MoodEconomy { get; set; } = null!;
    [Export] public PullbackLauncherComponent Launcher { get; set; } = null!;

    /// <summary>The single per-run persistent semantic state (ARCHITECTURE §12).</summary>
    public BuddyProgressState Progress { get; private set; } = null!;

    /// <summary>The sole currency/unlock mutator for this run (ARCHITECTURE §11).</summary>
    public EconomyService Economy { get; private set; } = null!;
    public SaveCoordinator Saves { get; private set; } = null!;
    public LifecycleCoordinator Lifecycle { get; private set; } = null!;
    public TrayCommandComponent TrayCommands { get; private set; } = null!;
    public bool WindowAdapterVisibleForTests => _windowAdapter?.IsWindowVisible ?? false;
    [Export] public CursorToolController CursorTools { get; set; } = null!;
    [Export] public CursorGunComponent CursorGuns { get; set; } = null!;
    [Export] public CursorGunVisual3D CursorGunVisual { get; set; } = null!;
    [Export] public GrenadeComponent Grenades { get; set; } = null!;
    [Export] public GrenadeVisual3D GrenadeVisual { get; set; } = null!;
    [Export] public GrenadeVisual2D GrenadeVisualLegacy { get; set; } = null!;
    [Export] public GrenadeAudioComponent GrenadeAudio { get; set; } = null!;
    [Export] public FireSprayerComponent FireSprayer { get; set; } = null!;
    [Export] public FireVisual2D FireVisualLegacy { get; set; } = null!;
    [Export] public FireVisual3D FireVisual { get; set; } = null!;
    [Export] public FireAudioComponent FireAudio { get; set; } = null!;
    [Export] public CameraKickComponent CameraKick { get; set; } = null!;
    [Export] public CareStrokeComponent CareStroke { get; set; } = null!;
    [Export] public ToolReactionComponent ToolReactions { get; set; } = null!;
    [Export] public ToolCursorPresenter CareCursor { get; set; } = null!;
    [Export] public BuddyReactionComponent Reactions { get; set; } = null!;
    [Export] public ReactionAudioPresenter ReactionAudio { get; set; } = null!;
    [Export] public ImpactFeedbackPresenter ImpactFeedback { get; set; } = null!;
    [Export] public SwingHitLagComponent SwingHitLag { get; set; } = null!;
    [Export] public ImpactVisualOffsetComponent ImpactVisualOffset { get; set; } = null!;
    [Export] public SwingAudioComponent SwingAudio { get; set; } = null!;
    [Export] public MoneyHudPresenter MoneyHud { get; set; } = null!;
    [Export] public BuddyVisualPresenter VisualPresenter { get; set; } = null!;
    [Export] public BuddyLookLightingRig LightingRig { get; set; } = null!;
    [Export] public BuddyPosePipeline PosePipeline { get; set; } = null!;
    [Export] public FacingController Facing { get; set; } = null!;
    [Export] public ActivityAnimator Activities { get; set; } = null!;
    [Export] public HeadLookAtComponent HeadLookAt { get; set; } = null!;
    [Export] public FaceCompositor Face { get; set; } = null!;
    [Export] public CursorToolVisual3D CursorToolVisual { get; set; } = null!;
    // Mii3D is the shipping default since the M3.5 Task 8 owner gate (2026-07-18); the
    // legacy circles remain behind the V toggle / --presentation=legacy as a dev view.
    [Export] public PresentationMode Mode { get; set; } = PresentationMode.Mii3D;
    public TelemetryRecorder? TelemetryRecorder { get; private set; }

    /// <summary>
    /// Injects a journey-owned persistence context before composition. Ordinary lab and
    /// scenario runs omit this and remain hermetic in memory.
    /// </summary>
    public void Configure(RunContext context)
    {
        if (IsInsideTree())
            throw new InvalidOperationException("Buddy Lab context must be configured before _Ready.");
        _runContext = context ?? throw new ArgumentNullException(nameof(context));
    }

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
            !GodotObject.IsInstanceValid(MoodEconomy) ||
            !GodotObject.IsInstanceValid(Launcher) ||
            !GodotObject.IsInstanceValid(CursorTools) ||
            !GodotObject.IsInstanceValid(CursorGuns) ||
            !GodotObject.IsInstanceValid(CursorGunVisual) ||
            !GodotObject.IsInstanceValid(Grenades) ||
            !GodotObject.IsInstanceValid(GrenadeVisual) ||
            !GodotObject.IsInstanceValid(GrenadeVisualLegacy) ||
            !GodotObject.IsInstanceValid(GrenadeAudio) ||
            !GodotObject.IsInstanceValid(FireSprayer) ||
            !GodotObject.IsInstanceValid(FireVisualLegacy) ||
            !GodotObject.IsInstanceValid(FireVisual) ||
            !GodotObject.IsInstanceValid(FireAudio) ||
            !GodotObject.IsInstanceValid(CameraKick) ||
            !GodotObject.IsInstanceValid(CareStroke) || !GodotObject.IsInstanceValid(ToolReactions) ||
            !GodotObject.IsInstanceValid(CareCursor) || !GodotObject.IsInstanceValid(Reactions) ||
            !GodotObject.IsInstanceValid(ReactionAudio) || !GodotObject.IsInstanceValid(ImpactFeedback) ||
            !GodotObject.IsInstanceValid(SwingHitLag) ||
            !GodotObject.IsInstanceValid(ImpactVisualOffset) ||
            !GodotObject.IsInstanceValid(SwingAudio) ||
            !GodotObject.IsInstanceValid(MoneyHud) ||
            !GodotObject.IsInstanceValid(VisualPresenter) ||
            !GodotObject.IsInstanceValid(LightingRig) ||
            !GodotObject.IsInstanceValid(PosePipeline) ||
            !GodotObject.IsInstanceValid(Facing) ||
            !GodotObject.IsInstanceValid(Activities) ||
            !GodotObject.IsInstanceValid(HeadLookAt) ||
            !GodotObject.IsInstanceValid(Face) ||
            !GodotObject.IsInstanceValid(CursorToolVisual))
        {
            throw new InvalidOperationException(
                "BuddyLab requires injected buddy, controls, grab, pointer, boundaries, containment, telemetry, boundary visualization, interaction pipeline, launcher, and tools.");
        }

        Controls.Initialize();
        Grab.Initialize();
        Pointer.Initialize();
        // Labs and scenarios are hermetic by default. The phased persistence journey may
        // inject its own fixture-backed context; it never resolves or mutates user://.
        if (_runContext is null)
        {
            Progress = new BuddyProgressState(Pipeline.RequirePainProfile().CashPerPain);
            Economy = new EconomyService(Progress, CatalogueLoader.Catalogue);
            var progressStore = new InMemoryProgressStore();
            Saves = new SaveCoordinator(Progress, progressStore);
        }
        else
        {
            Progress = _runContext.Progress;
            Economy = _runContext.Economy;
            Saves = _runContext.Saves;
        }
        // Development laboratory catalogue: implemented M5 tools are available for
        // mechanical tuning without granting them on a real new save.
        if (_runContext is null)
        {
            Economy.Unlock(ContentIds.ToolBaseball);
            Economy.Unlock(ContentIds.ToolMeal);
            Economy.Unlock(ContentIds.ToolBaseballBat);
            Economy.Unlock(ContentIds.ToolNerfBlaster);
            Economy.Unlock(ContentIds.ToolPistol);
            Economy.Unlock(ContentIds.ToolGrenade);
            Economy.Unlock(ContentIds.ToolFireSprayer);
        }
        Pipeline.Initialize(Progress, Economy);
        Objects.Initialize();
        Launcher.Initialize(OnLooseObjectClearRequested);
        Buddy.Arbiter.Initialize(Progress);
        Buddy.ObjectInteraction.Initialize(Objects, Progress, Buddy.Arbiter.SocialTuning);
        CursorTools.Initialize();
        CursorGuns.Initialize();
        CursorGunVisual.Initialize(CursorGuns);
        // The gun does not own the camera: it says a shot left the barrel, and the
        // camera's own offset lane decides what that looks like.
        CursorGuns.ShotFired += OnGunShotFired;
        // Taking a detonated grenade out of the world also has to release the player's
        // grab and cancel a buddy interaction, so removal stays the root's job.
        Grenades.Initialize(RemoveLooseObject);
        GrenadeVisual.Initialize(Grenades.Profile);
        // The pooled pins exist only after the component has built them, and the 3D
        // presenter takes their flat drawing over the moment it adopts them.
        GrenadeVisual.TrackPins(Grenades.Pins);
        GrenadeVisualLegacy.Initialize(Grenades.Profile);
        GrenadeAudio.Initialize();
        // The sprayer is a sibling of the guns on the same thin-driver shape; its burn
        // keeps running whatever tool is selected, so it is composed unconditionally.
        FireSprayer.Initialize();
        FireVisualLegacy.Initialize(FireSprayer, FireSprayer.Profile);
        FireVisual.Initialize(FireSprayer, FireSprayer.Profile);
        FireAudio.Initialize();
        ApplyEffectsSettings(EffectsSettings.FromSave(null));
        Grenades.PinPulled += OnGrenadePinPulled;
        Grenades.Detonated += OnGrenadeDetonated;
        CareStroke.Initialize();
        CareCursor.Initialize();
        ToolReactions.Initialize();
        Reactions.Initialize();
        ReactionAudio.Initialize();
        SwingHitLag.Initialize();
        ImpactVisualOffset.Initialize();
        SwingAudio.Initialize();
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
        // The slot is shaped per spawn, because which collider attaches depends on
        // which cursor tool is selected; the first authored profile is only the
        // resting default before anything has been picked up.
        CursorToolVisual.Initialize(CursorTools.Profiles[0]!);
        Containment.Initialize();
        Boundaries.LayoutApplied += Containment.ApplyLayout;
        Boundaries.LayoutApplied += OnBoundaryLayoutApplied;
        Boundaries.LayoutApplied += OnLayoutMovedTheCameras;
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

        // The M4 owner gate runs this laboratory, not the normal sandbox. Compose the
        // same focused lifecycle/command workers here so Ctrl+Shift+H exercises the
        // shipped hidden-mode path while the lab remains saveless (in-memory store).
        _windowAdapter = WindowsDesktopAdapterFactory.Create();
        Lifecycle = new LifecycleCoordinator { Name = nameof(LifecycleCoordinator) };
        Lifecycle.Configure(
            Progress,
            Economy,
            Saves,
            MoodEconomy,
            () => Grab.IsGrabbing || CursorTools.IsActive || CursorGuns.IsActive || FireSprayer.IsActive ||
                  CareStroke.IsHeld || Buddy.ObjectInteraction.IsHolding,
            _runContext?.TimeSource,
            resumePresentation: ResetPresentationInterpolation,
            setWindowVisibility: _windowAdapter.SetWindowVisible);
        AddChild(Lifecycle);

        TrayCommands = new TrayCommandComponent { Name = nameof(TrayCommandComponent) };
        TrayCommands.HideShowToggled += OnTrayHideShowToggled;
        TrayCommands.SaveAndQuitRequested += RequestSaveAndQuit;
        AddChild(TrayCommands);

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
        CursorTools.BodySpawned += OnCursorToolSpawned;
        CursorTools.BodyDespawned += OnCursorToolDespawned;

        ApplyRunnerPresentationOverride();
        SetPresentationMode(Mode);

        // The fixture-backed persistence journey must enter through the same safe-pose
        // resume seam as the normal sandbox. Pose and other transient simulation state
        // are intentionally absent from the save.
        if (_runContext is not null)
            Buddy.Recovery.ResetForSessionResume();

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
        CursorToolVisual.CaptureTickSnapshot();
        GrenadeVisual.CaptureTickSnapshot();
        // Solver contacts are semantic input to the gate, so drain them before
        // deciding whether this engine frame may advance any gameplay.
        CursorTools.RoutePendingImpactEvents();
        if (SwingHitLag.ConsumeFrozenPhysicsFrame())
        {
            return;
        }

        if (Controls.BeginPhysicsTick())
        {
            // Window/zoom requests rebuild containment at a physics boundary.
            Boundaries.PhysicsTick();

            // Grab/tool forces and buddy drive/constraint forces accumulate into
            // the same physics step; ordering between them does not matter.
            Grab.PhysicsTick(delta);
            Launcher.PhysicsTick();
            GrabState grab = Grab.CurrentGrab;
            long registryAllocationBefore = _allocationProbeEnabled
                ? GC.GetAllocatedBytesForCurrentThread()
                : 0;
            Objects.PhysicsTick(grab, Boundaries.InnerBounds.End.Y);
            if (_allocationProbeEnabled)
            {
                PhysicsRegistryAllocationSamples++;
                PhysicsRegistryAllocatedBytes +=
                    GC.GetAllocatedBytesForCurrentThread() - registryAllocationBefore;
            }
            PuppetPartBody? grabbedBody = grab.Active ? grab.Target as PuppetPartBody : null;
            bool buddyPartGrabbed = grabbedBody is not null;
            Buddy.GrabResistance.SetGrabContext(buddyPartGrabbed, grab.CursorAnchor);

            CursorTools.PhysicsTick(delta);
            CursorGuns.PhysicsTick();
            CameraKick.PhysicsTick();
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

            // After the pipeline, so a blast is scored against the same simulation
            // clock every contact this tick was scored against.
            Grenades.PhysicsTick();
            FireSprayer.PhysicsTick();
            // Burning is an immediate hazard in its own right (RAGDOLL §4 priority 3): one
            // snapshot bool, and the existing ladder does the panic and the drop.
            Buddy.Arbiter.SetStatusHazard(
                FireSprayer.IsBurning, FireSprayer.HazardFleeDirection);
            FireVisual.PhysicsTick();
            FireVisualLegacy.PhysicsTick();
            SyncGrenadeVisuals();
            GrenadeVisual.PhysicsTick();
            GrenadeVisualLegacy.PhysicsTick();

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
        if (GodotObject.IsInstanceValid(CursorGuns))
        {
            CursorGuns.ShotFired -= OnGunShotFired;
        }

        if (GodotObject.IsInstanceValid(Grenades))
        {
            Grenades.PinPulled -= OnGrenadePinPulled;
            Grenades.Detonated -= OnGrenadeDetonated;
        }

        if (GodotObject.IsInstanceValid(SwingHitLag))
        {
            SwingHitLag.Cancel();
        }

        if (GodotObject.IsInstanceValid(Buddy) && GodotObject.IsInstanceValid(Buddy.Recovery))
        {
            Buddy.Recovery.HardRecovered -= OnHardRecovered;
        }

        if (GodotObject.IsInstanceValid(Boundaries) && GodotObject.IsInstanceValid(Containment))
        {
            Boundaries.LayoutApplied -= Containment.ApplyLayout;
            Boundaries.LayoutApplied -= OnBoundaryLayoutApplied;
            Boundaries.LayoutApplied -= OnLayoutMovedTheCameras;
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
        if (GodotObject.IsInstanceValid(CursorTools))
        {
            CursorTools.BodySpawned -= OnCursorToolSpawned;
            CursorTools.BodyDespawned -= OnCursorToolDespawned;
        }
        if (GodotObject.IsInstanceValid(TrayCommands))
        {
            TrayCommands.HideShowToggled -= OnTrayHideShowToggled;
            TrayCommands.SaveAndQuitRequested -= RequestSaveAndQuit;
        }
        _windowAdapter?.Shutdown();

        TelemetryRecorder?.Complete();
    }

    public void SetHiddenToTray(bool hidden) => Lifecycle.SetHiddenToTray(hidden);

    private void OnTrayHideShowToggled() =>
        SetHiddenToTray(!Lifecycle.IsHiddenToTray);

    private async void RequestSaveAndQuit()
    {
        try
        {
            await Saves.FlushProgressAsync(force: true);
        }
        catch (Exception exception)
        {
            Log.Error("Laboratory", $"In-memory Save & Quit flush failed: {exception.Message}");
        }

        GodotInteropShutdown.PrepareForQuit();
        GetTree().Quit(0);
    }

    private void ResetPresentationInterpolation()
    {
        Buddy.Rig.ResetInterpolation();
        Objects.ResetInterpolation();
    }

    /// <summary>A room resize repositions both cameras, so any live kick is abandoned.</summary>
    private void OnLayoutMovedTheCameras(RoomLayout _layout, Rect2 _bounds) =>
        CameraKick.NotifyLayoutChanged();

    /// <summary>A round left the barrel: kick the camera by whatever that gun authors.</summary>
    private void OnGunShotFired(GunProfile profile) =>
        CameraKick.Kick(profile.FireShakeAmplitudePx, profile.FireShakeDecayTicks);

    private void OnGrenadePinPulled(Vector2 _position)
    {
        GrenadeVisual.NotifyPinPulled();
        GrenadeVisualLegacy.NotifyPinPulled();
    }

    /// <summary>
    /// A grenade went off. The same offset lane the pistol kicks, with the grenade's own
    /// bigger numbers — no new camera code, and non-stacking by the component's design.
    /// </summary>
    private void OnGrenadeDetonated(Vector2 center)
    {
        CameraKick.Kick(Grenades.Profile.KickAmplitudePx, Grenades.Profile.KickDecayTicks);
        GrenadeVisual.NotifyDetonated(center);
        GrenadeVisualLegacy.NotifyDetonated(center);
    }

    /// <summary>
    /// Keeps both grenade presenters attached to whatever the grenade component is
    /// following. Polled rather than event-driven because a grenade can leave for reasons
    /// nothing announces — eviction, a spawn that replaced it, a lab clear.
    /// </summary>
    private void SyncGrenadeVisuals()
    {
        LooseObjectBody? tracked = Grenades.Tracked;
        if (_shownGrenade == tracked)
            return;

        if (GodotObject.IsInstanceValid(_shownGrenade))
        {
            GrenadeVisual.Detach(_shownGrenade!);
            GrenadeVisualLegacy.Detach(_shownGrenade!);
        }

        _shownGrenade = tracked;
        if (tracked is not null)
        {
            GrenadeVisual.Attach(tracked, !Grenades.PinIsOut);
            GrenadeVisualLegacy.Attach(tracked, !Grenades.PinIsOut);
        }
    }

    /// <summary>
    /// Hands the four accessibility effect settings to every presenter that honours one
    /// (FR-017.3). <b>Gameplay never sees them</b>: this reaches presentation components
    /// only, so flipping every toggle changes what a run looks and sounds like and cannot
    /// change one tick of what it simulates.
    /// </summary>
    public void ApplyEffectsSettings(EffectsSettings settings)
    {
        Effects = settings;
        FireSprayer.ApplyEffectsSettings(settings);
        FireVisual.ApplyEffectsSettings(settings);
        FireVisualLegacy.ApplyEffectsSettings(settings);
        CameraKick.ApplyEffectsSettings(settings);
    }

    /// <summary>The effect settings currently in force.</summary>
    public EffectsSettings Effects { get; private set; } = EffectsSettings.Default;

    private void OnBoundaryLayoutApplied(RoomLayout _layout, Rect2 innerBounds) =>
        Buddy.AutonomousMotion.SetWalkableBounds(innerBounds);

    private void OnHardRecovered(HardRecoveryReason reason)
    {
        SwingHitLag.Cancel();
        Buddy.ObjectInteraction.Reset();
        Launcher.CancelImmediately();
        Grenades.CancelImmediately();
        // DECISIONS "Fail-safe cleanup" already promises a hard reposition clears Burning;
        // this is the one call that makes that sentence true.
        FireSprayer.ClearBurning();
        Buddy.Arbiter.SetStatusHazard(false, 0.0f);
        if (Grab.IsGrabbing)
        {
            Grab.Release(countsAsThrow: false);
        }
    }

    private void OnToolChanged(ToolId previous, ToolId selected)
    {
        SwingHitLag.Cancel();
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
        CursorToolVisual.SetPresentationActive(show3D);
        // One gun per cursor: the 3D presenter and the legacy 2D drawing are the same
        // weapon seen two ways, never both at once.
        CursorGunVisual.SetPresentationActive(show3D);
        CursorGuns.SetLegacyVisualEnabled(!show3D);
        // Same rule for the grenade: one silhouette per mode, never both at once.
        GrenadeVisual.SetPresentationActive(show3D);
        GrenadeVisualLegacy.SetPresentationActive(!show3D);
        // One fire per burning buddy: the frontal flame and the flat one are the same
        // fire seen two ways, never both at once.
        FireVisual.SetPresentationActive(show3D);
        FireVisualLegacy.SetPresentationActive(!show3D);
        FireSprayer.SetLegacyVisualEnabled(!show3D);
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

    private void OnCursorToolSpawned(CursorToolBody body)
    {
        CursorToolProfile profile = CursorTools.ActiveProfile!;
        CursorToolVisual.SetProfile(profile);
        CursorToolVisual.Attach(body);
    }

    private void OnCursorToolDespawned(CursorToolBody body)
    {
        SwingHitLag.Cancel();
        CursorToolVisual.Detach(body);
    }

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
        // A grenade is a grenade however it reached the room; the component decides
        // whether this one is one of its own.
        Grenades.NotifySpawned(body);
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
        // One ball at a time (owner instruction 2026-07-27): a new drop replaces the old one
        // rather than littering the room. The registry keeps its full capacity and eviction
        // rules — this is a spawn policy, not a cap.
        OnLooseObjectClearRequested();

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
            // The clear key drops whatever the player is holding, buddy part included —
            // unchanged behaviour, and broader than the targeted release below.
            if (Grab.IsGrabbing)
                Grab.Release(countsAsThrow: false);
            RemoveLooseObject(body);
        }
    }

    /// <summary>
    /// Takes one loose object out of the world, releasing whoever had hold of it first.
    /// Shared by the lab's clear key and by a detonating grenade, because "this object is
    /// gone" has the same three consequences either way.
    /// </summary>
    private void RemoveLooseObject(LooseObjectBody body)
    {
        if (!GodotObject.IsInstanceValid(body))
            return;

        if (Buddy.ObjectInteraction.IsHolding &&
            Buddy.ObjectInteraction.TrackedRuntimeId == body.RuntimeId)
        {
            Buddy.ObjectInteraction.CancelActiveInteraction();
        }

        if (Grab.IsGrabbing && Grab.CurrentGrab.Target == body)
            Grab.Release(countsAsThrow: false);
        Objects.Unregister(body);
        body.QueueFree();
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
