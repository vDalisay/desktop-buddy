using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using DesktopBuddy.App;
using DesktopBuddy.Buddy.Physics;
using DesktopBuddy.Domain.Presentation;
using Godot;

namespace DesktopBuddy.Testing;

/// <summary>Windowed evidence capture for the owner-feedback Eat and airborne-grab poses.
/// Screenshots aid human review; semantic scenarios remain the pass/fail authority.</summary>
public sealed class OwnerFeedbackVisualScenario : IScenario
{
    public string Id => "owner_feedback_visual";

    public async Task<ScenarioResult> RunAsync(SceneTree tree, ulong seed)
    {
        var checks = new List<StartupCheck>();
        var messages = new List<string> { $"seed={seed}" };
        PackedScene? packed = GD.Load<PackedScene>("res://scenes/buddy_lab.tscn");
        if (packed is null)
            return new ScenarioResult(false,
                new[] { new StartupCheck("owner_visual_scene_loadable", false, "buddy_lab") }, messages);

        BuddyLab lab = packed.Instantiate<BuddyLab>();
        tree.Root.AddChild(lab);
        await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        lab.GetNode<CanvasLayer>("LabUi").Visible = false;
        lab.BoundaryVisualizer.Visible = false;
        await ScenarioSteps.WaitForStanding(tree, lab, 1200);

        string directory = Path.GetFullPath(ScenarioArtifacts.Directory ?? ".artifacts/owner_review");
        Directory.CreateDirectory(directory);

        lab.Activities.AttachItemVisual(new MeshInstance3D
        {
            Name = "OwnerReviewFood",
            Mesh = new SphereMesh { Radius = 3.0f, Height = 6.0f },
        });
        lab.Facing.SetDevelopmentSide(1);
        for (int frame = 0; frame < 90; frame++)
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        lab.Buddy.SetBehaviorActivity(ActivityId.Eat);
        int mouthPoseTick = lab.Buddy.Activity.Profile.EatChestHoldTicks +
            Mathf.RoundToInt(0.55f * lab.Buddy.Activity.Profile.EatBiteCycleTicks);
        for (int tick = 0; tick < mouthPoseTick; tick++)
            await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
        await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        string eatPath = Path.Combine(directory, "eat_hands_face_front.png");
        Error eatSaved = tree.Root.GetTexture().GetImage().SavePng(eatPath);

        for (int tick = 0; tick < 480 &&
            lab.Buddy.Activity.Current == ActivityId.Eat &&
            lab.Buddy.Activity.RemainingTicks > 6; tick++)
            await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
        await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        string finalLowerPath = Path.Combine(directory, "eat_final_hand_lower.png");
        Error finalLowerSaved = tree.Root.GetTexture().GetImage().SavePng(finalLowerPath);

        lab.Buddy.SetBehaviorActivity(ActivityId.None);
        lab.Facing.SetDevelopmentSide(0);
        lab.Activities.ClearItemVisual();
        lab.Buddy.Rig.ResetToSafePose(new Vector2(240.0f, 240.0f));
        lab.Buddy.Standing.Reset();
        PuppetPartBody hand = lab.Buddy.Rig.LeftHand;
        bool grabbed = lab.Grab.TryGrab(hand, hand.GlobalPosition);
        for (int tick = 0; tick < 180; tick++)
        {
            lab.Grab.MoveCursor(new Vector2(240.0f, 150.0f));
            await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
        }
        await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        string grabPath = Path.Combine(directory, "grab_hand_topology.png");
        Error grabSaved = tree.Root.GetTexture().GetImage().SavePng(grabPath);
        lab.Grab.Release();

        bool screenshotsSaved = eatSaved == Error.Ok && finalLowerSaved == Error.Ok &&
            grabSaved == Error.Ok && File.Exists(eatPath) &&
            File.Exists(finalLowerPath) && File.Exists(grabPath);
        checks.Add(new StartupCheck("owner_review_screenshots_saved", screenshotsSaved,
            $"eat={eatSaved}:{eatPath} final_lower={finalLowerSaved}:{finalLowerPath} " +
            $"grab={grabSaved}:{grabPath}"));
        checks.Add(new StartupCheck("owner_review_hand_grab_acquired", grabbed,
            $"grabbed={grabbed}"));
        messages.Add($"eat_screenshot={eatPath}");
        messages.Add($"final_lower_screenshot={finalLowerPath}");
        messages.Add($"grab_screenshot={grabPath}");

        lab.QueueFree();
        bool passed = true;
        foreach (StartupCheck check in checks) passed &= check.Passed;
        return new ScenarioResult(passed, checks, messages);
    }
}
