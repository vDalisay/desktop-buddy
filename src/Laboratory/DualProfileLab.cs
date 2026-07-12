using System;
using DesktopBuddy.Buddy;
using DesktopBuddy.Domain.Automation;
using Godot;

namespace DesktopBuddy.Laboratory;

/// <summary>Development-only side-by-side profile comparison with one routed fixed tick.</summary>
public partial class DualProfileLab : Node2D
{
    [Export] public BuddyRoot BuddyA { get; set; } = null!;
    [Export] public BuddyRoot BuddyB { get; set; } = null!;
    [Export] public Label MetricsA { get; set; } = null!;
    [Export] public Label MetricsB { get; set; } = null!;
    public int InteractiveBuddyIndex { get; private set; }
    private TelemetryRecorder? _recorderA;
    private TelemetryRecorder? _recorderB;
    private long _tick;

    public override void _EnterTree()
    {
        RunnerArguments args = RunnerArguments.Parse(OS.GetCmdlineUserArgs());
        ApplyProfile(BuddyA, args.ProfileA);
        ApplyProfile(BuddyB, args.ProfileB);
    }

    public override void _Ready()
    {
        if (!GodotObject.IsInstanceValid(BuddyA) || !GodotObject.IsInstanceValid(BuddyB))
            throw new InvalidOperationException("DualProfileLab requires two injected buddy compositions.");
        BuddyA.ReseedAutonomy(1); BuddyB.ReseedAutonomy(1);
        RunnerArguments args = RunnerArguments.Parse(OS.GetCmdlineUserArgs());
        if (!string.IsNullOrEmpty(args.ArtifactsDir))
        {
            _recorderA = new TelemetryRecorder { Name = "TelemetryRecorderA" };
            _recorderB = new TelemetryRecorder { Name = "TelemetryRecorderB" };
            AddChild(_recorderA); AddChild(_recorderB);
            _recorderA.Initialize(BuddyA, null, args.ArtifactsDir, "dual_profile_a");
            _recorderB.Initialize(BuddyB, null, args.ArtifactsDir, "dual_profile_b");
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        BuddyA.PhysicsTick();
        BuddyB.PhysicsTick();
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
        _recorderA?.Complete(); _recorderB?.Complete();
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
}
