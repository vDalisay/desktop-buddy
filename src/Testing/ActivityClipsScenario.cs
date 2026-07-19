using System.Collections.Generic;
using System.Threading.Tasks;
using DesktopBuddy.App;
using DesktopBuddy.Buddy.Presentation3D;
using DesktopBuddy.Domain.Presentation;
using Godot;

namespace DesktopBuddy.Testing;

/// <summary>
/// M3.6 Task 3 gate: every activity resolves to a real clip in the animation library
/// (`activity_clip_mapping`), walk-dressing phase advances proportionally to MEASURED
/// travel and freezes outside the walk (`walk_cycle_speed_match`), and the eat clip is
/// item-agnostic — two different item visuals ride the same hand ItemSocket through the
/// same clip (`eat_clip_item_agnostic`). Semantic assertions only, never pixels.
/// </summary>
public sealed class ActivityClipsScenario : IScenario
{
    private const double FixedDelta = 1.0 / 120.0;

    public string Id => "activity_clips";

    public async Task<ScenarioResult> RunAsync(SceneTree tree, ulong seed)
    {
        var checks = new List<StartupCheck>();
        var messages = new List<string> { $"seed={seed}" };

        PackedScene? packed = GD.Load<PackedScene>("res://scenes/buddy_lab.tscn");
        if (packed is null)
        {
            checks.Add(new StartupCheck("activity_scene_loadable", false, "buddy_lab"));
            return new ScenarioResult(false, checks, messages);
        }

        BuddyLab lab = packed.Instantiate<BuddyLab>();
        tree.Root.AddChild(lab);
        await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        lab.Controls.Reseed(seed);

        checks.Add(CheckClipMapping(lab, messages));
        checks.Add(await CheckWalkCycleSpeedMatch(tree, lab, messages));
        checks.Add(await CheckEatClipItemAgnostic(tree, lab, messages));

        lab.QueueFree();
        await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);

        bool passed = true;
        foreach (StartupCheck check in checks)
        {
            passed &= check.Passed;
        }

        return new ScenarioResult(passed, checks, messages);
    }

    private static StartupCheck CheckClipMapping(BuddyLab lab, List<string> messages)
    {
        ActivityAnimator animator = lab.Activities;
        var names = new List<string>();
        bool passed = true;
        foreach (ActivityId activity in new[]
        {
            ActivityId.IdleBreathe, ActivityId.WalkCycle, ActivityId.JumpAnticipation,
            ActivityId.Wave, ActivityId.Eat,
        })
        {
            string name = ActivityAnimator.ClipNameFor(activity);
            bool resolved = !string.IsNullOrEmpty(name) && animator.HasClip(activity);
            passed &= resolved;
            names.Add($"{activity}:{name}:{resolved}");
        }

        messages.Add($"clip_mapping {string.Join(" ", names)}");
        return new StartupCheck("activity_clip_mapping", passed, string.Join(",", names));
    }

    private static async Task<StartupCheck> CheckWalkCycleSpeedMatch(
        SceneTree tree, BuddyLab lab, List<string> messages)
    {
        ActivityAnimator animator = lab.Activities;
        float pixelsPerCycle = animator.Profile.ActivityWalkCyclePixels;
        await ScenarioSteps.WaitForStanding(tree, lab, 1800);

        // Accumulate measured travel and unwrapped phase over real walk-dressing frames;
        // outside those frames the phase must not move at all.
        double expectedCycles = 0.0;
        double actualCycles = 0.0;
        bool frozenOutsideWalk = true;
        float previousPhase = animator.WalkPhase;
        int walkFrames = 0;
        for (int frame = 0; frame < 14400 && expectedCycles < 0.75; frame++)
        {
            float speedBefore = Mathf.Abs(lab.Buddy.Rig.Torso.LinearVelocity.X);
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            float phase = animator.WalkPhase;
            float delta = phase - previousPhase;
            if (delta < -0.5f)
            {
                delta += 1.0f;
            }

            if (animator.Current == ActivityId.WalkCycle)
            {
                walkFrames++;
                expectedCycles += speedBefore * FixedDelta / pixelsPerCycle;
                actualCycles += delta;
            }
            else
            {
                frozenOutsideWalk &= Mathf.Abs(delta) < 0.000001f;
            }

            previousPhase = phase;
        }

        bool enoughTravel = expectedCycles >= 0.25;
        double ratio = enoughTravel ? actualCycles / expectedCycles : 0.0;
        bool proportional = enoughTravel && ratio > 0.85 && ratio < 1.15;

        bool passed = enoughTravel && proportional && frozenOutsideWalk;
        messages.Add($"walk_cycle frames={walkFrames} expected={expectedCycles:F3} " +
            $"actual={actualCycles:F3} ratio={ratio:F3} frozen={frozenOutsideWalk}");
        return new StartupCheck("walk_cycle_speed_match", passed,
            $"walk_frames={walkFrames} expected_cycles={expectedCycles:F3} " +
            $"actual_cycles={actualCycles:F3} ratio={ratio:F3} frozen_outside={frozenOutsideWalk}");
    }

    private static async Task<StartupCheck> CheckEatClipItemAgnostic(
        SceneTree tree, BuddyLab lab, List<string> messages)
    {
        ActivityAnimator animator = lab.Activities;

        (bool ate, bool rode, string clip) first = await EatWithItem(
            tree, lab, new SphereMesh { Radius = 3.0f, Height = 6.0f });
        (bool ate, bool rode, string clip) second = await EatWithItem(
            tree, lab, new BoxMesh { Size = new Vector3(5.0f, 5.0f, 5.0f) });
        animator.ClearItemVisual();

        bool sameClip = first.clip == "eat" && second.clip == "eat";
        bool passed = first.ate && first.rode && second.ate && second.rode && sameClip;
        messages.Add($"eat_item_agnostic sphere=({first.ate},{first.rode}) " +
            $"box=({second.ate},{second.rode}) clip={first.clip}/{second.clip}");
        return new StartupCheck("eat_clip_item_agnostic", passed,
            $"sphere_ate={first.ate} sphere_rode={first.rode} box_ate={second.ate} " +
            $"box_rode={second.rode} same_clip={sameClip}");
    }

    private static async Task<(bool ate, bool rode, string clip)> EatWithItem(
        SceneTree tree, BuddyLab lab, Mesh mesh)
    {
        ActivityAnimator animator = lab.Activities;
        var visual = new MeshInstance3D { Name = "ItemVisual", Mesh = mesh };
        animator.AttachItemVisual(visual);
        animator.SetActivity(ActivityId.Eat, 2.0);

        bool ate = false;
        bool rode = true;
        string clip = string.Empty;
        int validSamples = 0;
        Node3D handSocket = lab.VisualPresenter.GetPartSocket(
            DesktopBuddy.Buddy.Physics.BuddyPartId.RightHand);
        for (int frame = 0; frame < 600 && validSamples < 20; frame++)
        {
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            if (animator.Current != ActivityId.Eat)
            {
                continue;
            }

            validSamples++;
            ate = true;
            clip = animator.CurrentClipName;
            // The item visual rides the hand: its global position stays pinned to the
            // ItemSocket under the hand socket while the clip moves that hand.
            rode &= visual.GlobalPosition.DistanceTo(animator.ItemSocket.GlobalPosition) < 0.01f &&
                animator.ItemSocket.GlobalPosition.DistanceTo(handSocket.GlobalPosition) < 0.01f;
        }

        animator.SetActivity(ActivityId.None);
        return (ate && validSamples >= 20, rode, clip);
    }
}
