using System;
using DesktopBuddy.Buddy;
using DesktopBuddy.Buddy.Physics;
using DesktopBuddy.Domain.Automation;
using DesktopBuddy.Grab;
using Godot;

namespace DesktopBuddy.Laboratory;

/// <summary>Development-only side-by-side profile comparison with one routed fixed tick.</summary>
public partial class DualProfileLab : Node2D
{
    [Export] public BuddyRoot BuddyA { get; set; } = null!;
    [Export] public BuddyRoot BuddyB { get; set; } = null!;
    [Export] public Label MetricsA { get; set; } = null!;
    [Export] public Label MetricsB { get; set; } = null!;
    [Export] public GrabTetherController Grab { get; set; } = null!;
    [Export] public LabPointerGrabComponent Pointer { get; set; } = null!;
    public int InteractiveBuddyIndex { get; private set; }
    private TelemetryRecorder? _recorderA;
    private TelemetryRecorder? _recorderB;
    private long _tick;
    private RunnerArguments _args = new();
    private BuddyRoot ActiveBuddy => InteractiveBuddyIndex == 0 ? BuddyA : BuddyB;

    public override void _EnterTree()
    {
        _args = RunnerArguments.Parse(OS.GetCmdlineUserArgs());
        ApplyProfile(BuddyA, _args.ProfileA);
        ApplyProfile(BuddyB, _args.ProfileB);
        ApplyDriveProfile(BuddyA, _args.DriveA);
        ApplyDriveProfile(BuddyB, _args.DriveB);
    }

    public override void _Ready()
    {
        if (!GodotObject.IsInstanceValid(BuddyA) || !GodotObject.IsInstanceValid(BuddyB) ||
            !GodotObject.IsInstanceValid(Grab) || !GodotObject.IsInstanceValid(Pointer))
            throw new InvalidOperationException("DualProfileLab requires two buddy compositions, one grab controller, and one pointer component.");
        Grab.Initialize();
        Pointer.Initialize();
        BuddyA.Arbiter.InitializeSaveless();
        BuddyB.Arbiter.InitializeSaveless();
        Pointer.PickFilter = body => body is PuppetPartBody part && ActiveBuddy.IsAncestorOf(part);
        ulong seed = _args.Seed ?? 1;
        BuddyA.ReseedAutonomy(seed); BuddyB.ReseedAutonomy(seed);
        if (!string.IsNullOrEmpty(_args.ArtifactsDir))
        {
            _recorderA = new TelemetryRecorder { Name = "TelemetryRecorderA" };
            _recorderB = new TelemetryRecorder { Name = "TelemetryRecorderB" };
            AddChild(_recorderA); AddChild(_recorderB);
            _recorderA.Initialize(BuddyA, Grab, _args.ArtifactsDir, "dual_profile_a");
            _recorderB.Initialize(BuddyB, Grab, _args.ArtifactsDir, "dual_profile_b");
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        Pointer.ResolvePendingInput();
        Grab.PhysicsTick(delta);
        GrabState grab = Grab.CurrentGrab;
        BuddyRoot active = ActiveBuddy;
        BuddyRoot inactive = InteractiveBuddyIndex == 0 ? BuddyB : BuddyA;
        PuppetPartBody? grabbedBody = grab.Active ? grab.Target as PuppetPartBody : null;
        bool activePartGrabbed = grabbedBody is not null;
        active.GrabResistance.SetGrabContext(activePartGrabbed, grab.CursorAnchor);
        inactive.GrabResistance.SetGrabContext(false, Vector2.Zero);
        BuddyA.PhysicsTick(
            ReferenceEquals(active, BuddyA) ? grabbedBody?.PartId : null,
            ReferenceEquals(active, BuddyA) ? grab.CursorAnchor : default,
            Pointer.WorldCursor,
            Pointer.HasPointerInput);
        BuddyB.PhysicsTick(
            ReferenceEquals(active, BuddyB) ? grabbedBody?.PartId : null,
            ReferenceEquals(active, BuddyB) ? grab.CursorAnchor : default,
            Pointer.WorldCursor,
            Pointer.HasPointerInput);
        _recorderA?.Capture(_tick); _recorderB?.Capture(_tick); _tick++;
    }

    public override void _Process(double delta)
    {
        if (!GodotObject.IsInstanceValid(MetricsA) || !GodotObject.IsInstanceValid(MetricsB)) return;
        MetricsA.Text = FormatMetrics("A", BuddyA);
        MetricsB.Text = FormatMetrics("B", BuddyB);
    }

    public override void _ExitTree()
    {
        CompleteTelemetry();
    }

    public void CompleteTelemetry()
    {
        _recorderA?.Complete();
        _recorderB?.Complete();
    }

    public override void _Input(InputEvent @event)
    {
        if (@event is InputEventKey { Pressed: true, Echo: false, PhysicalKeycode: Key.Tab })
            InteractiveBuddyIndex = 1 - InteractiveBuddyIndex;
    }

    private static void ApplyProfile(BuddyRoot buddy, string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        var profile = GD.Load<DesktopBuddy.Buddy.Physics.PuppetRigProfile>(path);
        if (profile is null) throw new InvalidOperationException($"Unable to load profile '{path}'.");
        buddy.Rig.Profile = profile;
    }

    private static string FormatMetrics(string id, BuddyRoot buddy) =>
        $"{id}  support {buddy.Standing.Snapshot.SupportContactCount}  speed {buddy.Standing.Snapshot.MaximumBodySpeed:F1}\n" +
        $"standing {buddy.Standing.Snapshot.IsStable}  drive {buddy.ActiveDrive.LastLocomotionForce.Length():F0}";

    private static void ApplyDriveProfile(BuddyRoot buddy, string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        var profile = GD.Load<ActiveDriveProfile>(path);
        if (profile is null) throw new InvalidOperationException($"Unable to load drive profile '{path}'.");
        // _EnterTree runs before child _Ready initialization, so the replacement
        // is validated by ActiveDriveComponent.Initialize.
        buddy.ActiveDrive.Profile = profile;
    }
}
