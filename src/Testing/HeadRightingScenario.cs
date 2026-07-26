using System.Collections.Generic;
using System.Threading.Tasks;
using DesktopBuddy.App;
using DesktopBuddy.Buddy.Physics;
using DesktopBuddy.Domain.Buddy;
using DesktopBuddy.Domain.Content;
using DesktopBuddy.Domain.Tools;
using DesktopBuddy.Interaction;
using Godot;

namespace DesktopBuddy.Testing;

/// <summary>Owner-fix B1: a real head grab stamps two calm seconds, then bounded
/// conscious torque restores the head; accepted impacts re-arm and knockout disables it.</summary>
public sealed class HeadRightingScenario : IScenario
{
    public string Id => "head_rights_after_disturbance";

    public async Task<ScenarioResult> RunAsync(SceneTree tree, ulong seed)
    {
        var checks = new List<StartupCheck>();
        var messages = new List<string> { $"seed={seed}" };
        PackedScene? packed = GD.Load<PackedScene>("res://scenes/buddy_lab.tscn");
        if (packed is null)
            return new ScenarioResult(false,
                new[] { new StartupCheck("head_righting_scene_loadable", false, "buddy_lab") }, messages);

        BuddyLab lab = packed.Instantiate<BuddyLab>();
        tree.Root.AddChild(lab);
        await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        await ScenarioSteps.WaitForStanding(tree, lab, 1200);

        PuppetPartBody head = lab.Buddy.Rig.Head;
        Vector2 grabPoint = head.GlobalPosition + new Vector2(head.Radius * 0.75f, 0.0f);
        bool grabbed = lab.Grab.TryGrab(head, grabPoint);
        float peakGrabAngle = 0.0f;
        for (int tick = 0; tick < 240 && peakGrabAngle < 1.8f; tick++)
        {
            lab.Grab.MoveCursor(head.GlobalPosition + new Vector2(0.0f, -90.0f));
            await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
            peakGrabAngle = Mathf.Max(peakGrabAngle, AbsoluteWrapped(head.GlobalRotation));
        }
        lab.Grab.Release();
        head.AngularVelocity = 0.0f;
        float releaseAngle = AbsoluteWrapped(head.GlobalRotation);

        bool quietWindow = true;
        float minimumDelayAngle = releaseAngle;
        int quietTicks = Mathf.Max(1, lab.Buddy.ActiveDrive.Profile.HeadRightingDelayTicks - 24);
        for (int tick = 0; tick < quietTicks; tick++)
        {
            await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
            quietWindow &= Mathf.IsZeroApprox(lab.Buddy.ActiveDrive.LastHeadUprightTorque);
            minimumDelayAngle = Mathf.Min(minimumDelayAngle, AbsoluteWrapped(head.GlobalRotation));
        }

        bool torqueObserved = false;
        int rightingTicks = 0;
        while (rightingTicks < 960 && AbsoluteWrapped(head.GlobalRotation) > 0.35f)
        {
            await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
            torqueObserved |= Mathf.Abs(lab.Buddy.ActiveDrive.LastHeadUprightTorque) > 0.01f;
            rightingTicks++;
        }
        float finalAngle = AbsoluteWrapped(head.GlobalRotation);

        lab.Pipeline.SelectTool(ToolId.BoxingGlove);
        AcceptedImpact? impact = await ScenarioSteps.StrikePart(
            tree, lab, head, ContentIds.ToolBoxingGlove, 730_001);
        bool impactRearmed = impact is { Part: BuddyPart.Head } &&
            lab.Buddy.ActiveDrive.HeadRightingDelayTicksRemaining > 0;

        lab.Buddy.SetConsciousness(Consciousness.Unconscious);
        head.GlobalRotation = Mathf.Pi * 0.9f;
        head.AngularVelocity = 0.0f;
        bool unconsciousTorqueZero = true;
        for (int tick = 0; tick < 300; tick++)
        {
            await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
            unconsciousTorqueZero &= Mathf.IsZeroApprox(lab.Buddy.ActiveDrive.LastHeadUprightTorque);
        }

        checks.Add(new StartupCheck("head_grab_creates_rotation",
            grabbed && peakGrabAngle >= 1.0f && releaseAngle >= 0.8f,
            $"grabbed={grabbed} peak={peakGrabAngle:F2} release={releaseAngle:F2}"));
        checks.Add(new StartupCheck("head_righting_waits_for_calm_window",
            quietWindow && minimumDelayAngle >= 0.5f,
            $"quiet={quietWindow} min_delay_angle={minimumDelayAngle:F2} delay_ticks={quietTicks}"));
        checks.Add(new StartupCheck("head_rights_after_disturbance",
            torqueObserved && finalAngle <= 0.35f && rightingTicks <= 60,
            $"torque={torqueObserved} final={finalAngle:F2} righting_ticks={rightingTicks}"));
        checks.Add(new StartupCheck("head_impact_rearms_delay", impactRearmed,
            $"impact={impact is not null} remaining={lab.Buddy.ActiveDrive.HeadRightingDelayTicksRemaining}"));
        checks.Add(new StartupCheck("unconscious_head_stays_passive", unconsciousTorqueZero,
            $"torque_zero={unconsciousTorqueZero}"));
        messages.Add($"head_righting peak={peakGrabAngle:F2} release={releaseAngle:F2} " +
            $"delay_min={minimumDelayAngle:F2} final={finalAngle:F2} ticks={rightingTicks}");

        lab.QueueFree();
        bool passed = true;
        foreach (StartupCheck check in checks) passed &= check.Passed;
        return new ScenarioResult(passed, checks, messages);
    }

    private static float AbsoluteWrapped(float angle) =>
        Mathf.Abs(Mathf.Wrap(angle, -Mathf.Pi, Mathf.Pi));
}
