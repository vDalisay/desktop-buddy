using System;
using System.Collections.Generic;
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
using DesktopBuddy.Domain.Platform;
using DesktopBuddy.Domain.Tools;
using DesktopBuddy.Economy;
using DesktopBuddy.Grab;
using DesktopBuddy.Interaction;
using DesktopBuddy.Laboratory;
using DesktopBuddy.Objects;
using DesktopBuddy.Platform;
using DesktopBuddy.Persistence;
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

    [Export] public DesktopWindowController Window { get; set; } = null!;
    [Export] public DesktopShellController Shell { get; set; } = null!;
    [Export] public BoundaryController Boundaries { get; set; } = null!;
    [Export] public BuddyRoot Buddy { get; set; } = null!;
    [Export] public GrabTetherController Grab { get; set; } = null!;
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
            !GodotObject.IsInstanceValid(Grab) || !GodotObject.IsInstanceValid(Pointer) ||
            !GodotObject.IsInstanceValid(Containment) || !GodotObject.IsInstanceValid(Pipeline) ||
            !GodotObject.IsInstanceValid(Objects) ||
            !GodotObject.IsInstanceValid(Launcher) ||
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
            !GodotObject.IsInstanceValid(GloveVisual) ||
            !GodotObject.IsInstanceValid(MoodEconomy))
        {
            throw new InvalidOperationException(
                "SandboxRoot requires an injected window controller, shell controller, and boundary.");
        }

        Grab.Initialize();
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
        Buddy.AutonomousMotion.SetWalkableBounds(Boundaries.InnerBounds);
        RefreshWorkModeHitRegions();
        Buddy.Recovery.HardRecovered += OnHardRecovered;
        Buddy.Recovery.SessionResumed += OnSessionResumed;
        Pipeline.ToolChanged += OnToolChanged;
        Grab.Released += OnGrabReleased;
        Glove.BodySpawned += OnGloveBodySpawned;
        Glove.BodyDespawned += OnGloveBodyDespawned;
        Window.WindowFocusLost += OnWindowFocusLost;
        Lifecycle = new LifecycleCoordinator { Name = nameof(LifecycleCoordinator) };
        Lifecycle.Configure(
            Progress,
            Economy,
            Saves,
            MoodEconomy,
            () => Grab.IsGrabbing || Glove.IsActive || CareStroke.IsHeld ||
                  Buddy.ObjectInteraction.IsHolding,
            _runContext.TimeSource,
            ResetPresentationInterpolation,
            Window.Adapter.SetWindowVisible);
        AddChild(Lifecycle);

        TrayCommands = new TrayCommandComponent { Name = nameof(TrayCommandComponent) };
        TrayCommands.HideShowToggled += OnTrayHideShowToggled;
        TrayCommands.SaveAndQuitRequested += RequestSaveAndQuit;
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
        GloveVisual.CaptureTickSnapshot();
        // Shell drains a queued resize into a boundary request; the boundary
        // applies pending layout changes on this physics boundary.
        Shell.PhysicsTick();
        Boundaries.PhysicsTick();
        Grab.PhysicsTick(delta);
        Launcher.PhysicsTick();
        GrabState grab = Grab.CurrentGrab;
        Objects.PhysicsTick(grab, Boundaries.InnerBounds.End.Y);
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
        Pipeline.PhysicsTick();
        RefreshWorkModeHitRegions();
    }

    public override void _ExitTree()
    {
        if (GodotObject.IsInstanceValid(Boundaries) && GodotObject.IsInstanceValid(Containment))
        {
            Boundaries.LayoutApplied -= Containment.ApplyLayout;
            Boundaries.LayoutApplied -= OnBoundaryLayoutApplied;
        }
        if (GodotObject.IsInstanceValid(Buddy) && GodotObject.IsInstanceValid(Buddy.Recovery))
        {
            Buddy.Recovery.HardRecovered -= OnHardRecovered;
            Buddy.Recovery.SessionResumed -= OnSessionResumed;
        }
        if (GodotObject.IsInstanceValid(Pipeline)) Pipeline.ToolChanged -= OnToolChanged;
        if (GodotObject.IsInstanceValid(Grab)) Grab.Released -= OnGrabReleased;
        if (GodotObject.IsInstanceValid(Glove))
        {
            Glove.BodySpawned -= OnGloveBodySpawned;
            Glove.BodyDespawned -= OnGloveBodyDespawned;
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
        }
        if (DisplayServer.GetName() != "headless" && IsInsideTree())
            GetWindow().CloseRequested -= OnCloseRequested;

        if (Saves is not null && Saves.IsDirty)
            _ = ObserveSaveAsync(Saves.FlushProgressAsync(force: true), "Exit save");
    }

    private RunContext CreateInMemoryRunContext()
    {
        var progress = new BuddyProgressState(Pipeline.RequirePainProfile().CashPerPain);
        var economy = new EconomyService(progress);
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
            // Forced: this is the last chance to write, so a mutation that landed during
            // the flush must not be abandoned.
            await Saves.FlushProgressAsync(force: true);
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

        Shell.UpdateWorkModeHitRegions(
            _buddyWorkModeWorldRegions,
            _buddyWorkModeHitRegions);
    }

    private void OnHardRecovered(HardRecoveryReason reason)
    {
        Buddy.ObjectInteraction.Reset();
        Launcher.CancelImmediately();
        if (Grab.IsGrabbing) Grab.Release(countsAsThrow: false);
    }

    private void OnSessionResumed()
    {
        Buddy.ObjectInteraction.Reset();
        if (Grab.IsGrabbing)
            Grab.Release(countsAsThrow: false);
    }

    private void OnToolChanged(ToolId previous, ToolId selected)
    {
        if (previous == ToolId.Grab && Grab.IsGrabbing) Grab.Release(countsAsThrow: false);
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

    /// <summary>Root-owned one-ball replacement policy used by the Baseball launcher.</summary>
    private void ClearLooseObjectsForReplacement()
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
