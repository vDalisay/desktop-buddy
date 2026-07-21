using System;
using System.Globalization;
using DesktopBuddy.Buddy;
using DesktopBuddy.Buddy.Physics;
using DesktopBuddy.Domain.Buddy;
using DesktopBuddy.Domain.Physics;
using DesktopBuddy.Grab;
using DesktopBuddy.Sandbox;
using Godot;

namespace DesktopBuddy.Laboratory;

/// <summary>Read-only live measurements rendered by the development lab panel.</summary>
public readonly record struct LaboratoryTelemetrySnapshot(
    Consciousness Consciousness,
    bool IsPaused,
    double TimeScale,
    ulong AutonomySeed,
    long RoutedPhysicsTicks,
    bool IsStanding,
    int SupportContacts,
    float TorsoTilt,
    float MaximumBodySpeed,
    int UnableTicks,
    bool AssistanceActive,
    float AssistanceRamp,
    float MaximumLinkStrain,
    float MaximumLinkForce,
    bool GrabActive,
    float GrabExtension,
    float GrabForce,
    float LastReleaseSpeed,
    double EffectiveZoom,
    double RoomWidth,
    double RoomHeight,
    int LastContainmentCorrections);

/// <summary>
/// Development-only lab instructions and telemetry. It reads component
/// snapshots and never mutates gameplay state apart from its own visibility.
/// </summary>
[GlobalClass]
public partial class LaboratoryTelemetryPanel : PanelContainer
{
    private const double RefreshIntervalSeconds = 0.1;
    private double _refreshCountdown;

    [Export] public BuddyRoot Buddy { get; set; } = null!;
    [Export] public LaboratoryControlComponent Controls { get; set; } = null!;
    [Export] public GrabTetherController Grab { get; set; } = null!;
    [Export] public BoundaryController Boundaries { get; set; } = null!;
    [Export] public PuppetRoomContainmentComponent Containment { get; set; } = null!;
    [Export] public Label InstructionsLabel { get; set; } = null!;
    [Export] public Label TelemetryLabel { get; set; } = null!;

    // Optional M3 interaction pipeline readout; the dual-profile lab leaves it unset.
    [Export] public Interaction.InteractionDamageComponent? Pipeline { get; set; }

    // Optional M3.6 expressive-presentation readout. Head look-at is rotation-only on a
    // sphere behind a screen-upright placeholder face, so until the Task 5 composed face
    // lands this panel is the only way to SEE the gaze arbitration working.
    [Export] public Buddy.Presentation3D.BuddyPosePipeline? PosePipeline { get; set; }
    [Export] public Buddy.Presentation3D.FacingController? Facing { get; set; }
    [Export] public Buddy.Presentation3D.ActivityAnimator? Activities { get; set; }
    [Export] public Buddy.Presentation3D.HeadLookAtComponent? HeadLookAt { get; set; }
    [Export] public Buddy.Presentation3D.BuddyVisualPresenter? Presenter { get; set; }

    public bool IsInitialized { get; private set; }
    public LaboratoryTelemetrySnapshot Snapshot { get; private set; }

    public void Initialize()
    {
        if (!GodotObject.IsInstanceValid(Buddy) || !Buddy.IsInitialized ||
            !GodotObject.IsInstanceValid(Controls) || !Controls.IsInitialized ||
            !GodotObject.IsInstanceValid(Grab) || !Grab.IsInitialized ||
            !GodotObject.IsInstanceValid(Boundaries) || !Boundaries.IsInitialized ||
            !GodotObject.IsInstanceValid(Containment) || !Containment.IsInitialized ||
            !GodotObject.IsInstanceValid(InstructionsLabel) ||
            !GodotObject.IsInstanceValid(TelemetryLabel))
        {
            throw new InvalidOperationException("LaboratoryTelemetryPanel dependencies are incomplete.");
        }

        ProcessMode = ProcessModeEnum.Always;
        InstructionsLabel.Text =
            "PHYSICS LAB — run this scene (F6)\n" +
            "Left-drag: grab / throw   Right-click: drop\n" +
            "P: pause   .: single tick   U: limp/wake\n" +
            "Shift+U: reseed   1/2/3/4: simulation speed\n" +
            "G: grab  B: glove  F: pet  T: tickle\n" +
            "V: presentation  E: eat item  Q: wave\n" +
            "Z/X: face left/right  C: release facing\n" +
            "H: hide/show this panel";
        IsInitialized = true;
        RefreshNow();
    }

    public override void _Process(double delta)
    {
        if (!IsInitialized)
        {
            return;
        }

        _refreshCountdown -= delta;
        if (_refreshCountdown <= 0.0)
        {
            RefreshNow();
            _refreshCountdown = RefreshIntervalSeconds;
        }
    }

    public override void _Input(InputEvent @event)
    {
        if (!IsInitialized || @event is not InputEventKey
            {
                PhysicalKeycode: Key.H,
                Pressed: true,
                Echo: false,
            })
        {
            return;
        }

        Visible = !Visible;
        GetViewport().SetInputAsHandled();
    }

    public void RefreshNow()
    {
        if (!IsInitialized)
        {
            return;
        }

        Snapshot = CaptureSnapshot();
        LaboratoryTelemetrySnapshot snapshot = Snapshot;
        string runState = snapshot.IsPaused ? "PAUSED" : "RUNNING";
        string standing = snapshot.IsStanding ? "yes" : "no";
        string assistance = snapshot.AssistanceActive
            ? $"{snapshot.AssistanceRamp * 100.0f:F0}%"
            : "off";
        string grab = snapshot.GrabActive ? "active" : "off";

        TelemetryLabel.Text = string.Create(
            CultureInfo.InvariantCulture,
            $"""
            {snapshot.Consciousness} | {runState} x{snapshot.TimeScale:F2} | seed {snapshot.AutonomySeed}
            tick {snapshot.RoutedPhysicsTicks} | standing {standing} | supports {snapshot.SupportContacts}
            tilt {snapshot.TorsoTilt:F3} | max speed {snapshot.MaximumBodySpeed:F1}
            recovery {snapshot.UnableTicks} ticks | assist {assistance}
            links strain {snapshot.MaximumLinkStrain:F3} | force {snapshot.MaximumLinkForce:F0}
            grab {grab} | stretch {snapshot.GrabExtension:F1} | force {snapshot.GrabForce:F0}
            release {snapshot.LastReleaseSpeed:F1} | corrections {snapshot.LastContainmentCorrections}
            room {snapshot.RoomWidth:F0}x{snapshot.RoomHeight:F0} | zoom {snapshot.EffectiveZoom * 100.0:F0}%{PipelineLine()}{PresentationLine()}
            """);
    }

    /// <summary>Live M3.6 mode/facing/activity/gaze readout; empty when unwired.</summary>
    private string PresentationLine()
    {
        if (PosePipeline is not { IsInitialized: true })
        {
            return string.Empty;
        }

        string facing = Facing is { IsInitialized: true }
            ? $"{Facing.CommittedSide} {Facing.CurrentYawDegrees:F1}"
            : "-";
        string activity = Activities is { IsInitialized: true }
            ? Activities.Current.ToString()
            : "-";
        string line = string.Create(
            CultureInfo.InvariantCulture,
            $"\nmode {PosePipeline.Mode} w{PosePipeline.PerformanceWeight:F2} | " +
            $"facing {facing} | act {activity}");

        if (HeadLookAt is not { IsInitialized: true })
        {
            return line;
        }

        // Model angles are what look-at WANTS; applied angles are what the presenter put on
        // the head socket after the performance weight — they diverge exactly when a
        // suppression (Tracking, unconsciousness) is doing its job.
        string applied = Presenter is { IsInitialized: true }
            ? $" | applied y{Presenter.AppliedHeadYawDegrees:F1} p{Presenter.AppliedHeadPitchDegrees:F1}"
            : string.Empty;
        return line + string.Create(
            CultureInfo.InvariantCulture,
            $"\ngaze {HeadLookAt.CurrentSource} y{HeadLookAt.CurrentYawDegrees:F1} " +
            $"p{HeadLookAt.CurrentPitchDegrees:F1} | pupil " +
            $"({HeadLookAt.PupilOffset.X:F2},{HeadLookAt.PupilOffset.Y:F2}){applied}");
    }

    private string PipelineLine()
    {
        if (Pipeline is null || !GodotObject.IsInstanceValid(Pipeline) || !Pipeline.IsInitialized)
        {
            return string.Empty;
        }

        return string.Create(
            CultureInfo.InvariantCulture,
            $"\ntool {Pipeline.SelectedTool} | mood {Pipeline.Mood:F1} {Pipeline.MoodBand} | ${Pipeline.BalanceCredits}" +
            $"\npain {Pipeline.LastKnockoutState.RollingPain:F0} | KO {Pipeline.KnockoutCount} | hits {Pipeline.ScoredImpactCount}");
    }

    private LaboratoryTelemetrySnapshot CaptureSnapshot()
    {
        StandingSnapshot standing = Buddy.Standing.Snapshot;
        RecoveryClockState recovery = Buddy.Recovery.State;
        GrabTelemetry grab = Grab.Telemetry;
        float maximumStrain = 0.0f;
        float maximumForce = 0.0f;
        foreach (LinkTelemetry link in Buddy.Constraints.Telemetry)
        {
            maximumStrain = Mathf.Max(maximumStrain, link.Strain);
            maximumForce = Mathf.Max(maximumForce, link.ForceOnA.Length());
        }

        RoomLayout room = Boundaries.CurrentLayout;
        return new LaboratoryTelemetrySnapshot(
            Buddy.CurrentConsciousness,
            Controls.IsPaused,
            Controls.TimeScale,
            Controls.AutonomySeed,
            Controls.RoutedPhysicsTicks,
            standing.IsStable,
            standing.SupportContactCount,
            standing.TorsoTilt,
            standing.MaximumBodySpeed,
            recovery.UnableTicks,
            recovery.AssistanceActive,
            recovery.AssistanceRamp,
            maximumStrain,
            maximumForce,
            grab.Active,
            grab.Extension,
            grab.Force.Length(),
            grab.LastReleaseSpeed,
            room.EffectiveZoom,
            room.RoomWidth,
            room.RoomHeight,
            Containment.LastCorrectionCount);
    }
}
