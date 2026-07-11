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
            room {snapshot.RoomWidth:F0}x{snapshot.RoomHeight:F0} | zoom {snapshot.EffectiveZoom * 100.0:F0}%
            """);
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
