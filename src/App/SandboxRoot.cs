using System;
using System.Collections.Generic;
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
using DesktopBuddy.Domain.Platform;
using DesktopBuddy.Domain.Tools;
using DesktopBuddy.Economy;
using DesktopBuddy.Grab;
using DesktopBuddy.Interaction;
using DesktopBuddy.Laboratory;
using DesktopBuddy.Objects;
using DesktopBuddy.Platform;
using DesktopBuddy.Persistence;
using DesktopBuddy.Persistence.Characters;
using DesktopBuddy.Domain.Presentation;
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
    private readonly Rect2I[] _buddyWorkModeHitRegions =
        new Rect2I[PuppetRigProfile.RequiredPartCount];
    private readonly Rect2[] _buddyWorkModeWorldRegions =
        new Rect2[PuppetRigProfile.RequiredPartCount];
    private IReadOnlyList<Rect2> _overlayWorkModeHitRegions = [];

    private LooseObjectBody? _shownGrenade;

    [Export] public DesktopWindowController Window { get; set; } = null!;
    [Export] public DesktopShellController Shell { get; set; } = null!;
    [Export] public BoundaryController Boundaries { get; set; } = null!;
    [Export] public BuddyRoot Buddy { get; set; } = null!;
    [Export] public GrabTetherController Grab { get; set; } = null!;
    [Export] public RopeSuspensionComponent Ropes { get; set; } = null!;
    [Export] public LabPointerGrabComponent Pointer { get; set; } = null!;
    [Export] public PuppetRoomContainmentComponent Containment { get; set; } = null!;
    [Export] public InteractionDamageComponent Pipeline { get; set; } = null!;
    [Export] public LooseObjectRegistry Objects { get; set; } = null!;
    [Export] public PullbackLauncherComponent Launcher { get; set; } = null!;

    /// <summary>The single per-run persistent semantic state (ARCHITECTURE §12).</summary>
    public BuddyProgressState Progress { get; private set; } = null!;

    /// <summary>The sole currency/unlock mutator for this run (ARCHITECTURE §11).</summary>
    public EconomyService Economy { get; private set; } = null!;
    public SaveCoordinator Saves { get; private set; } = null!;
    public LocalSettingsSave Settings { get; private set; } = null!;
    private RunContext? _runContext;
    private bool _quitSaveStarted;
    [Export] public CursorToolController CursorTools { get; set; } = null!;
    [Export] public CursorGunComponent CursorGuns { get; set; } = null!;
    [Export] public CursorGunVisual3D CursorGunVisual { get; set; } = null!;
    [Export] public GrenadeComponent Grenades { get; set; } = null!;
    [Export] public GrenadeVisual3D GrenadeVisual { get; set; } = null!;

    /// <summary>Draws the loose objects whose profiles author a 3D shape.</summary>
    [Export] public LooseObjectVisual3D LooseObjectVisual { get; set; } = null!;
    [Export] public GrenadeVisual2D GrenadeVisualLegacy { get; set; } = null!;
    [Export] public GrenadeAudioComponent GrenadeAudio { get; set; } = null!;
    [Export] public FireSprayerComponent FireSprayer { get; set; } = null!;
    [Export] public FireVisual2D FireVisualLegacy { get; set; } = null!;
    [Export] public FireVisual3D FireVisual { get; set; } = null!;
    [Export] public KnockoutStarsVisual3D KnockoutStars { get; set; } = null!;
    [Export] public TreatSparklesVisual3D TreatSparkles { get; set; } = null!;
    [Export] public FireAudioComponent FireAudio { get; set; } = null!;
    [Export] public CursorSprayerVisual3D SprayerVisual { get; set; } = null!;
    [Export] public ScorchPresenter Scorch { get; set; } = null!;
    [Export] public CameraKickComponent CameraKick { get; set; } = null!;
    [Export] public CareStrokeComponent CareStroke { get; set; } = null!;
    [Export] public ToolReactionComponent ToolReactions { get; set; } = null!;
    [Export] public ToolCursorPresenter CareCursor { get; set; } = null!;
    [Export] public CareToolVisual3D CareCursorVisual { get; set; } = null!;
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
    [Export] public MoodEconomyProfile MoodEconomy { get; set; } = null!;
    public LifecycleCoordinator Lifecycle { get; private set; } = null!;

    /// <summary>The minimal M4 Show/Hide + Save &amp; Quit command surface.</summary>
    public TrayCommandComponent TrayCommands { get; private set; } = null!;
    // Mii3D is the shipping default since the M3.5 Task 8 owner gate (2026-07-18); the
    // legacy circles remain behind the V toggle / --presentation=legacy as a dev view.
    [Export] public PresentationMode Mode { get; set; } = PresentationMode.Mii3D;

    /// <summary>Inject the normal-run context before adding this scene to the tree.</summary>
    public void Configure(RunContext context)
    {
        if (IsInsideTree())
            throw new InvalidOperationException("Sandbox run context must be configured before _Ready.");
        _runContext = context ?? throw new ArgumentNullException(nameof(context));
    }

    public override void _Ready()
    {
        if (!GodotObject.IsInstanceValid(Window) ||
            !GodotObject.IsInstanceValid(Shell) ||
            !GodotObject.IsInstanceValid(Boundaries) || !GodotObject.IsInstanceValid(Buddy) ||
            !GodotObject.IsInstanceValid(Grab) ||
            !GodotObject.IsInstanceValid(Ropes) || !GodotObject.IsInstanceValid(Pointer) ||
            !GodotObject.IsInstanceValid(Containment) || !GodotObject.IsInstanceValid(Pipeline) ||
            !GodotObject.IsInstanceValid(Objects) ||
            !GodotObject.IsInstanceValid(Launcher) ||
            !GodotObject.IsInstanceValid(CursorTools) ||
            !GodotObject.IsInstanceValid(CursorGuns) ||
            !GodotObject.IsInstanceValid(CursorGunVisual) ||
            !GodotObject.IsInstanceValid(Grenades) ||
            !GodotObject.IsInstanceValid(GrenadeVisual) ||
            !GodotObject.IsInstanceValid(LooseObjectVisual) ||
            !GodotObject.IsInstanceValid(GrenadeVisualLegacy) ||
            !GodotObject.IsInstanceValid(GrenadeAudio) ||
            !GodotObject.IsInstanceValid(FireSprayer) ||
            !GodotObject.IsInstanceValid(FireVisualLegacy) ||
            !GodotObject.IsInstanceValid(FireVisual) ||
            !GodotObject.IsInstanceValid(KnockoutStars) ||
            !GodotObject.IsInstanceValid(TreatSparkles) ||
            !GodotObject.IsInstanceValid(FireAudio) ||
            !GodotObject.IsInstanceValid(SprayerVisual) ||
            !GodotObject.IsInstanceValid(Scorch) ||
            !GodotObject.IsInstanceValid(CameraKick) ||
            !GodotObject.IsInstanceValid(CareStroke) ||
            !GodotObject.IsInstanceValid(ToolReactions) || !GodotObject.IsInstanceValid(CareCursor) || !GodotObject.IsInstanceValid(CareCursorVisual) ||
            !GodotObject.IsInstanceValid(Reactions) || !GodotObject.IsInstanceValid(ReactionAudio) ||
            !GodotObject.IsInstanceValid(ImpactFeedback) ||
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
            !GodotObject.IsInstanceValid(CursorToolVisual) ||
            !GodotObject.IsInstanceValid(MoodEconomy))
        {
            throw new InvalidOperationException(
                "SandboxRoot requires an injected window controller, shell controller, and boundary.");
        }

        Grab.Initialize();
        Ropes.Initialize();
        Pointer.Initialize(developmentOnly: false);
        // Direct scene runs and scenario fixtures deliberately stay saveless.
        // Normal boot injects a disk-backed context from Bootstrap.
        _runContext ??= CreateInMemoryRunContext();
        Progress = _runContext.Progress;
        Economy = _runContext.Economy;
        Saves = _runContext.Saves;
        Settings = _runContext.Settings;
        Pipeline.Initialize(Progress, Economy);
        Objects.Initialize();
        Launcher.Initialize(ClearLooseObjectsForReplacement);
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
        LooseObjectVisual.Initialize(Objects);
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
        KnockoutStars.Initialize(Buddy, FireSprayer.Profile.VisualDepthOffset + 2.0f);
        FireAudio.Initialize();
        SprayerVisual.Initialize(FireSprayer, FireSprayer.Profile);
        // The shipped sandbox has real machine-local settings, so the seam is fed from them.
        ApplyEffectsSettings(EffectsSettings.FromSave(Settings));
        Grenades.PinPulled += OnGrenadePinPulled;
        Grenades.Detonated += OnGrenadeDetonated;
        // Everything the rest of the sandbox can do to a grenade meets it here: a swung tool,
        // a round from any gun, and the sprayer's flame (owner instruction 2026-08-21). The
        // grenade owns the rules; the tools only report that they connected. BuddyLab wires
        // the same three lines — this is the root the shipped game actually runs.
        Grenades.Flame = FireSprayer;
        CursorTools.LooseObjectStruck += OnToolStruckLooseObject;
        CursorGuns.LooseObjectStruck += OnShotStruckLooseObject;
        CareStroke.Initialize();
        CareCursor.Initialize();
        CareCursorVisual.Initialize(CareStroke);
        // The squirm under the feather reads off the same contact the burn does.
        ImpactVisualOffset.Care = CareStroke;
        ToolReactions.Initialize();
        Reactions.Initialize();
        // Behind the head rather than in front of it, which is the whole look: the glisten
        // comes out from around the buddy (owner instruction 2026-08-22).
        TreatSparkles.Initialize(Buddy, Reactions, FireSprayer.Profile.VisualDepthOffset - 4.0f);
        ReactionAudio.Initialize();
        SwingHitLag.Initialize();
        ImpactVisualOffset.Initialize();
        SwingAudio.Initialize();
        ImpactFeedback.Initialize();
        MoneyHud.Initialize(Economy);
        VisualPresenter.Initialize();
        // After the visual presenter: the scorch driver writes through that presenter's own
        // per-part materials, so it cannot be composed before they exist.
        Scorch.Initialize();
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
        // The shell already applied the opening layout from its own _Ready, before these
        // handlers existed. Replay it so nothing is left mirroring a room that is not the
        // room the physics is using.
        Boundaries.RepublishLayout();
        Buddy.AutonomousMotion.SetWalkableBounds(Boundaries.InnerBounds);
        RefreshWorkModeHitRegions();
        Buddy.Recovery.HardRecovered += OnHardRecovered;
        Buddy.Recovery.SessionResumed += OnSessionResumed;
        Pipeline.ToolChanged += OnToolChanged;
        Grab.Released += OnGrabReleased;
        Buddy.ObjectInteraction.ConsumeSucceeded += OnCareItemTaken;
        CursorTools.BodySpawned += OnCursorToolSpawned;
        CursorTools.BodyDespawned += OnCursorToolDespawned;
        Window.WindowFocusLost += OnWindowFocusLost;
        Lifecycle = new LifecycleCoordinator { Name = nameof(LifecycleCoordinator) };
        Lifecycle.Configure(
            Progress,
            Economy,
            Saves,
            MoodEconomy,
            () => Grab.IsGrabbing || CursorTools.IsActive || CursorGuns.IsActive || FireSprayer.IsActive ||
                  CareStroke.IsHeld || Buddy.ObjectInteraction.IsHolding,
            _runContext.TimeSource,
            ResetPresentationInterpolation,
            Window.Adapter.SetWindowVisible,
            // Work mode is the player getting on with something else; the buddy idles on
            // their desktop and barely works up an appetite.
            () => Shell.Mode == DesktopBuddy.Domain.Platform.InputMode.Work,
            () => Window.Adapter.ForegroundAppIsFullscreen);
        Lifecycle.BackgroundMaxFps = Settings.BackgroundMaxFps;
        Lifecycle.HideForFullscreenApps = Settings.HideForFullscreenApps;
        AddChild(Lifecycle);

        TrayCommands = new TrayCommandComponent { Name = nameof(TrayCommandComponent) };
        TrayCommands.HideShowToggled += OnTrayHideShowToggled;
        TrayCommands.SaveAndQuitRequested += RequestSaveAndQuit;
        TrayCommands.ResetProgressConfirmed += OnResetProgressConfirmed;
        AddChild(TrayCommands);

        // §24 power/session stimuli reach the lifecycle clock through the platform
        // adapter, so the emulated adapter can drive them deterministically headless
        // and the native adapter binds the same seam when its message hooks land.
        IWindowsDesktopAdapter adapter = Window.Adapter;
        adapter.SystemSuspending += OnSystemSuspending;
        adapter.SystemResumed += OnSystemResumed;
        adapter.SessionLockChanged += OnSessionLockChanged;
        if (DisplayServer.GetName() != "headless")
        {
            GetTree().AutoAcceptQuit = false;
            GetWindow().CloseRequested += OnCloseRequested;
        }

        // Save data never contains pose or transient gameplay state. Every launch,
        // including backup/default recovery, begins from this safe reset seam.
        Buddy.Recovery.ResetForSessionResume();

        ApplyRunnerPresentationOverride();
        SetPresentationMode(Mode);

        Log.Info("Sandbox", "SandboxRoot composed with desktop shell.");
    }

    public override void _PhysicsProcess(double delta)
    {
        Pointer.ResolvePendingInput();
        VisualPresenter.CaptureTickSnapshot();
        CursorToolVisual.CaptureTickSnapshot();
        GrenadeVisual.CaptureTickSnapshot();
        LooseObjectVisual.CaptureTickSnapshot();
        CursorTools.RoutePendingImpactEvents();
        if (SwingHitLag.ConsumeFrozenPhysicsFrame())
        {
            return;
        }

        // Shell drains a queued resize into a boundary request; the boundary
        // applies pending layout changes on this physics boundary.
        Shell.PhysicsTick();
        Boundaries.PhysicsTick();
        Grab.PhysicsTick(delta);
        Ropes.PhysicsTick(delta);
        Launcher.PhysicsTick();
        GrabState grab = Grab.CurrentGrab;
        Objects.PhysicsTick(grab, Boundaries.InnerBounds);
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
        Pipeline.PhysicsTick();
        // After the pipeline, so a blast is scored against the same simulation clock
        // every contact this tick was scored against.
        Grenades.PhysicsTick();
        FireSprayer.PhysicsTick();
        Pipeline.SetFireUnconsciousness(FireSprayer.FullBodyBurnKnockoutActive);
        // Burning is an immediate hazard in its own right (RAGDOLL §4 priority 3): one
        // snapshot bool, and the existing ladder does the panic and the drop.
        Buddy.Arbiter.SetStatusHazard(FireSprayer.IsBurning, FireSprayer.HazardFleeDirection);
        FireVisual.PhysicsTick();
        FireVisualLegacy.PhysicsTick();
        SprayerVisual.PhysicsTick();
        Scorch.PhysicsTick();
        SyncGrenadeVisuals();
        GrenadeVisual.PhysicsTick();
            LooseObjectVisual.PhysicsTick();
        GrenadeVisualLegacy.PhysicsTick();
        RefreshWorkModeHitRegions();
    }

    public override void _ExitTree()
    {
        if (GodotObject.IsInstanceValid(CursorGuns))
        {
            CursorGuns.ShotFired -= OnGunShotFired;
            CursorGuns.LooseObjectStruck -= OnShotStruckLooseObject;
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

        if (GodotObject.IsInstanceValid(Boundaries) && GodotObject.IsInstanceValid(Containment))
        {
            Boundaries.LayoutApplied -= Containment.ApplyLayout;
            Boundaries.LayoutApplied -= OnBoundaryLayoutApplied;
            Boundaries.LayoutApplied -= OnLayoutMovedTheCameras;
        }
        if (GodotObject.IsInstanceValid(Buddy) && GodotObject.IsInstanceValid(Buddy.Recovery))
        {
            Buddy.Recovery.HardRecovered -= OnHardRecovered;
            Buddy.Recovery.SessionResumed -= OnSessionResumed;
        }
        if (GodotObject.IsInstanceValid(Pipeline)) Pipeline.ToolChanged -= OnToolChanged;
        if (GodotObject.IsInstanceValid(Grab)) Grab.Released -= OnGrabReleased;
        if (GodotObject.IsInstanceValid(Buddy) &&
            GodotObject.IsInstanceValid(Buddy.ObjectInteraction))
        {
            Buddy.ObjectInteraction.ConsumeSucceeded -= OnCareItemTaken;
        }
        if (GodotObject.IsInstanceValid(CursorTools))
        {
            CursorTools.BodySpawned -= OnCursorToolSpawned;
            CursorTools.BodyDespawned -= OnCursorToolDespawned;
            CursorTools.LooseObjectStruck -= OnToolStruckLooseObject;
        }
        if (GodotObject.IsInstanceValid(Window))
        {
            Window.WindowFocusLost -= OnWindowFocusLost;
            IWindowsDesktopAdapter adapter = Window.Adapter;
            adapter.SystemSuspending -= OnSystemSuspending;
            adapter.SystemResumed -= OnSystemResumed;
            adapter.SessionLockChanged -= OnSessionLockChanged;
        }
        if (GodotObject.IsInstanceValid(TrayCommands))
        {
            TrayCommands.HideShowToggled -= OnTrayHideShowToggled;
            TrayCommands.SaveAndQuitRequested -= RequestSaveAndQuit;
            TrayCommands.ResetProgressConfirmed -= OnResetProgressConfirmed;
        }
        if (DisplayServer.GetName() != "headless" && IsInsideTree())
            GetWindow().CloseRequested -= OnCloseRequested;

        // CloseRequested normally performs the awaited save before Quit. This blocking
        // fallback covers other clean tree exits (runner shutdown, host-initiated quit)
        // so a dirty final revision is never abandoned by a fire-and-forget task.
        if (Saves is not null && Saves.IsDirty)
        {
            if (GodotObject.IsInstanceValid(Lifecycle))
            {
                try
                {
                    Lifecycle.BeginShutdown();
                }
                catch (Exception exception)
                {
                    Log.Error(
                        "Persistence",
                        $"Lifecycle shutdown settle failed; attempting final save: {exception.Message}");
                }
            }

            try
            {
                Saves.FlushProgressAsync(force: true).GetAwaiter().GetResult();
            }
            catch (Exception exception)
            {
                Log.Error(
                    "Persistence",
                    $"Exit save failed; progress remains dirty: {exception.Message}");
            }
        }
    }

    private RunContext CreateInMemoryRunContext()
    {
        var progress = new BuddyProgressState(Pipeline.RequirePainProfile().CashPerPain);
        var economy = new EconomyService(progress, CatalogueLoader.Catalogue);
        var store = new InMemoryProgressStore();
        return new RunContext(
            progress,
            economy,
            store,
            new SaveCoordinator(progress, store),
            new LocalSettingsSave(),
            SaveLoadStatus.NewSave);
    }

    private void OnWindowFocusLost() =>
        _ = ObserveSaveAsync(Saves.FlushProgressAsync(), "Focus-loss save");

    /// <summary>Tray/UI command seam for the minimal M4 Save &amp; Quit surface.</summary>
    public void RequestSaveAndQuit() => OnCloseRequested();
    public void SetHiddenToTray(bool hidden) => Lifecycle.SetHiddenToTray(hidden);

    private void OnTrayHideShowToggled() => SetHiddenToTray(!Lifecycle.IsHiddenToTray);

    /// <summary>
    /// The confirmed reset. The state is rewritten in place, so nothing is re-bound here; the
    /// live pose, grab, and held object are dropped through the same seam a resumed session
    /// uses, because a fresh save must not resume mid-interaction.
    /// </summary>
    private async void OnResetProgressConfirmed()
    {
        CharacterStore? characters = _runContext?.Characters;
        bool reset = await ProgressReset.ResetAsync(
            Progress,
            Saves,
            Economy,
            deleteCharacters: characters is null
                ? null
                : token => characters.DeleteAllAsync(token));
        if (!reset)
        {
            Log.Error("Persistence", "Reset Progress failed to write; progress is unchanged.");
            return;
        }

        if (GetTree().Root.FindChild(nameof(DesktopBuddy.Environment.EnvironmentCustomizationBootstrap), true, false)
            is DesktopBuddy.Environment.EnvironmentCustomizationBootstrap environment)
        {
            environment.ClearPaintedBackground();
        }
        // The characters are gone, so the rig must stop wearing one.
        if (GetTree().Root.FindChild(nameof(CharacterSelectionRuntime), true, false)
            is CharacterSelectionRuntime runtime && runtime.Coordinator is not null)
        {
            runtime.Coordinator.RevertToBuiltIn();
        }

        OnSessionResumed();
        Buddy.Recovery.ResetForSessionResume();
        Log.Info(
            "Persistence",
            $"Progress reset to a first run; {ProgressReset.DeletedCharacterCount} character(s) " +
            "removed, settings untouched.");
    }

    private void OnSystemSuspending() => Lifecycle.NotifySuspended();

    private void OnSystemResumed() => Lifecycle.NotifyResumed(Lifecycle.IsHiddenToTray);

    private void OnSessionLockChanged(bool locked) => Lifecycle.NotifySessionLock(locked);

    /// <summary>
    /// Re-anchors every interpolated body to its current transform. Called when the render
    /// loop restarts after hidden mode so nothing tweens from a pre-hide pose (FR-015.10).
    /// </summary>
    private void ResetPresentationInterpolation()
    {
        Buddy.Rig.ResetInterpolation();
        Objects.ResetInterpolation();
    }

    private async void OnCloseRequested()
    {
        if (_quitSaveStarted)
            return;
        _quitSaveStarted = true;
        SetPhysicsProcess(false);
        try
        {
            Lifecycle.BeginShutdown();
            // Forced: this is the last chance to write, so a mutation that landed during
            // the flush must not be abandoned.
            await Saves.FlushProgressAsync(force: true);
            Shell.CaptureWindowStateForSave();
            await Saves.SaveSettingsAsync(Settings);
        }
        catch (Exception exception)
        {
            // A failed write remains dirty and the prior primary/backup survive.
            // Closing is still honored so the app cannot trap the user.
            Log.Error("Persistence", $"Save & Quit failed: {exception.Message}");
        }
        GodotInteropShutdown.PrepareForQuit();
        GetTree().Quit();
    }

    private static async System.Threading.Tasks.Task ObserveSaveAsync(
        System.Threading.Tasks.Task operation,
        string label)
    {
        try
        {
            await operation;
        }
        catch (Exception exception)
        {
            Log.Error("Persistence", $"{label} failed; progress remains dirty: {exception.Message}");
        }
    }

    /// <summary>A room resize repositions both cameras, so any live kick is abandoned.</summary>
    private void OnLayoutMovedTheCameras(RoomLayout _layout, Rect2 _bounds) =>
        CameraKick.NotifyLayoutChanged();

    /// <summary>A round left the barrel: kick the camera by whatever that gun authors.</summary>
    private void OnGunShotFired(GunProfile profile) =>
        CameraKick.Kick(profile.FireShakeAmplitudePx, profile.FireShakeDecayTicks);

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
        CareCursorVisual.ApplyEffectsSettings(settings.ReducedParticles);
        FireVisual.ApplyEffectsSettings(settings);
        KnockoutStars.ApplyEffectsSettings(settings);
        TreatSparkles.ApplyEffectsSettings(settings);
        SprayerVisual.ApplyEffectsSettings(settings);
        FireVisualLegacy.ApplyEffectsSettings(settings);
        CameraKick.ApplyEffectsSettings(settings);
        ImpactFeedback.ApplyEffectsSettings(settings);
    }

    /// <summary>The effect settings currently in force.</summary>
    public EffectsSettings Effects { get; private set; } = EffectsSettings.Default;

    private void OnBoundaryLayoutApplied(RoomLayout _layout, Rect2 innerBounds) =>
        Buddy.AutonomousMotion.SetWalkableBounds(innerBounds);

    private void RefreshWorkModeHitRegions()
    {
        double zoom = Shell.EffectiveZoom;
        IReadOnlyList<PuppetPartBody> parts = Buddy.Rig.Parts;
        for (int index = 0; index < parts.Count; index++)
        {
            PuppetPartBody part = parts[index];
            float diameter = part.Radius * 2.0f;
            _buddyWorkModeWorldRegions[index] = new Rect2(
                part.GlobalPosition - Vector2.One * part.Radius,
                Vector2.One * diameter);
            PixelRect projected = SandboxProjection.SandboxRectToClient(
                part.GlobalPosition.X - part.Radius,
                part.GlobalPosition.Y - part.Radius,
                diameter,
                diameter,
                zoom);
            _buddyWorkModeHitRegions[index] =
                new Rect2I(projected.X, projected.Y, projected.Width, projected.Height);
        }

        if (_overlayWorkModeHitRegions.Count == 0)
        {
            Shell.UpdateWorkModeHitRegions(
                _buddyWorkModeWorldRegions,
                _buddyWorkModeHitRegions);
            return;
        }

        var world = new List<Rect2>(_buddyWorkModeWorldRegions);
        var client = new List<Rect2I>(_buddyWorkModeHitRegions);
        Transform2D canvas = GetViewport().GetCanvasTransform().AffineInverse();
        foreach (Rect2 overlay in _overlayWorkModeHitRegions)
        {
            world.Add(new Rect2(canvas * overlay.Position, canvas.BasisXform(overlay.Size)));
            client.Add(new Rect2I(
                (Vector2I)overlay.Position.Floor(),
                (Vector2I)overlay.Size.Ceil()));
        }
        Shell.UpdateWorkModeHitRegions(world, client);
    }

    /// <summary>
    /// Work-Mode regions owned by same-window overlay UI (the dock and its menus), in
    /// viewport pixels. They are appended to the per-frame buddy regions rather than
    /// replacing them, so an open menu never makes the buddy itself pass through.
    /// </summary>
    public void SetOverlayWorkModeHitRegions(IReadOnlyList<Rect2> regions)
    {
        _overlayWorkModeHitRegions = regions ?? throw new ArgumentNullException(nameof(regions));
        RefreshWorkModeHitRegions();
    }

    /// <summary>
    /// The healing, applied identically however the item was taken — eaten or thrown. Only a
    /// profile that authors <c>ClearsHarmfulStatuses</c> reaches past its mood gain, so food
    /// stays food. The knockout end time is untouched by construction (FR-008.7).
    /// </summary>
    private void OnCareItemTaken(LooseObjectBody item)
    {
        if (!GodotObject.IsInstanceValid(item) ||
            !GodotObject.IsInstanceValid(item.Profile) ||
            !item.Profile!.ClearsHarmfulStatuses)
        {
            return;
        }

        Pipeline.ClearRollingPain();
        FireSprayer.ClearBurning();
        Buddy.Arbiter.SetStatusHazard(false, 0.0f);
    }

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
        if (Grab.IsGrabbing) Grab.Release(countsAsThrow: false);
    }

    private void OnSessionResumed()
    {
        SwingHitLag.Cancel();
        Buddy.ObjectInteraction.Reset();
        if (Grab.IsGrabbing)
            Grab.Release(countsAsThrow: false);
    }

    private void OnToolChanged(ToolId previous, ToolId selected)
    {
        SwingHitLag.Cancel();
        // By category: switching away from either grab variant drops what is held, and the
        // drop is never a powered throw.
        if (ToolCatalog.CategoryOf(previous) == ToolCategory.Grab && Grab.IsGrabbing)
            Grab.Release(countsAsThrow: false);
    }

    private void OnGrabReleased(RigidBody2D body, bool countsAsThrow)
    {
        if (body is not LooseObjectBody loose || loose.RuntimeId == 0)
            return;

        if (countsAsThrow)
        {
            // Attribute to the tool that actually threw it: the per-tool statistics
            // dictionaries are keyed by tool, so filing a Power throw under Normal would put
            // a real event under the wrong key.
            ToolId thrower = Pipeline is not null && GodotObject.IsInstanceValid(Pipeline)
                ? Pipeline.SelectedTool
                : ToolId.Grab;
            Objects.MarkPlayerThrown(
                loose,
                ContentIds.ForTool(
                    ToolCatalog.CategoryOf(thrower) == ToolCategory.Grab
                        ? thrower
                        : ToolId.Grab));
        }
        else
            Objects.MarkBuddyReleased(loose);
    }

    /// <summary>Root-owned one-ball replacement policy used by the Baseball launcher.</summary>
    private void ClearLooseObjectsForReplacement()
    {
        for (int index = GetChildCount() - 1; index >= 0; index--)
        {
            if (GetChild(index) is not LooseObjectBody body)
                continue;
            // Replacement drops whatever the player is holding, buddy part included —
            // unchanged behaviour, and broader than the targeted release below.
            if (Grab.IsGrabbing)
                Grab.Release(countsAsThrow: false);
            RemoveLooseObject(body);
        }
    }

    /// <summary>
    /// Takes one loose object out of the world, releasing whoever had hold of it first.
    /// Shared by the replacement policy and by a detonating grenade, because "this object
    /// is gone" has the same three consequences either way.
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

    private void OnToolStruckLooseObject(LooseObjectStrike strike) =>
        Grenades.NotifyStruck(strike.Body, strike.ContentId);

    private void OnShotStruckLooseObject(ProjectileStrike strike) =>
        Grenades.NotifyStruck(strike.Body, strike.ContentId);

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
    /// nothing announces — eviction, a spawn that replaced it, a clear.
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
        CareCursorVisual.SetPresentationActive(show3D);
        CareCursor.SetLegacyVisualEnabled(!show3D);
        // One gun per cursor: the 3D presenter and the legacy 2D drawing are the same
        // weapon seen two ways, never both at once.
        CursorGunVisual.SetPresentationActive(show3D);
        CursorGuns.SetLegacyVisualEnabled(!show3D);
        // Same rule for the grenade: one silhouette per mode, never both at once.
        GrenadeVisual.SetPresentationActive(show3D);
        LooseObjectVisual.SetPresentationActive(show3D);
        GrenadeVisualLegacy.SetPresentationActive(!show3D);
        // One fire per burning buddy: the frontal flame and the flat one are the same
        // fire seen two ways, never both at once.
        FireVisual.SetPresentationActive(show3D);
        KnockoutStars.SetPresentationActive(show3D);
        TreatSparkles.SetPresentationActive(show3D);
        FireVisualLegacy.SetPresentationActive(!show3D);
        // One flamethrower per cursor: the frontal model and the flat silhouette are the
        // same weapon seen two ways, never both at once.
        SprayerVisual.SetPresentationActive(show3D);
        FireSprayer.SetLegacyVisualEnabled(!show3D);
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
}
