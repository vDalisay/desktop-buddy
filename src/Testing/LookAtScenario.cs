using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using DesktopBuddy.App;
using DesktopBuddy.Buddy.Physics;
using DesktopBuddy.Buddy.Presentation3D;
using DesktopBuddy.Domain.Presentation;
using DesktopBuddy.Domain.Tools;
using DesktopBuddy.Interaction;
using Godot;

namespace DesktopBuddy.Testing;

/// <summary>
/// M3.6 Task 4 gate (`lookat_priority_and_cone`): interest targets resolve by the plan's
/// priority order (engaged cursor in range, item, impact memory, ambient glance, rest),
/// every angle stays inside the profile cone, ambient glances are deterministic per seed,
/// and the suppression states — forced Tracking after a strike, and a high-priority
/// reaction face — hold the head still. The expected angles are recomputed here with
/// independent atan2 math against the same gaze depth, never read back from the model.
/// All assertions are semantic (source, angles, pupil quanta); never pixels.
/// </summary>
public sealed class LookAtScenario : IScenario
{
    public string Id => "lookat_priority_and_cone";

    public async Task<ScenarioResult> RunAsync(SceneTree tree, ulong seed)
    {
        var checks = new List<StartupCheck>();
        var messages = new List<string> { $"seed={seed}" };

        PackedScene? packed = GD.Load<PackedScene>("res://scenes/buddy_lab.tscn");
        if (packed is null)
        {
            checks.Add(new StartupCheck("lookat_scene_loadable", false, "buddy_lab"));
            return new ScenarioResult(false, checks, messages);
        }

        BuddyLab lab = packed.Instantiate<BuddyLab>();
        tree.Root.AddChild(lab);
        await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        lab.Controls.Reseed(seed);
        await ScenarioSteps.WaitForStanding(tree, lab, 1800);

        checks.Add(await CheckEngagedCursorTracked(tree, lab, messages));
        checks.Add(await CheckBeyondRangeReleasesTheCursor(tree, lab, messages));
        checks.Add(await CheckItemTargetWins(tree, lab, messages));
        checks.Add(await CheckAmbientGlanceDeterminism(tree, lab, seed, messages));
        // Last: the strike leaves real pain, fear, and harmful memory behind.
        checks.Add(await CheckImpactSuppressionAndMemory(tree, lab, messages));

        lab.QueueFree();
        await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);

        bool passed = true;
        foreach (StartupCheck check in checks)
        {
            passed &= check.Passed;
        }

        return new ScenarioResult(passed, checks, messages);
    }

    /// <summary>The scenario's own target-to-angle oracle (plan Task 4 convention).</summary>
    private static float ExpectedAngleDegrees(float delta, float gazeDepth) =>
        Mathf.RadToDeg(Mathf.Atan2(delta, gazeDepth));

    private static bool InsideCone(HeadLookAtComponent look) =>
        Mathf.Abs(look.CurrentYawDegrees) <= look.Profile.LookConeYawDegrees + 0.001f &&
        Mathf.Abs(look.CurrentPitchDegrees) <= look.Profile.LookConePitchDegrees + 0.001f;

    /// <summary>
    /// An engaged pet stroke over a foot: the head must acquire the cursor, aim at it with
    /// the oracle's angles, and stay inside the cone the whole time. A stroke over the
    /// opposite foot must move the gaze the other way.
    /// </summary>
    private static async Task<StartupCheck> CheckEngagedCursorTracked(
        SceneTree tree, BuddyLab lab, List<string> messages)
    {
        HeadLookAtComponent look = lab.HeadLookAt;
        float depth = look.Profile.LookGazeDepthPixels;
        lab.Pipeline.SelectTool(ToolId.Pet);

        (bool acquired, bool matched, bool coned, float yaw) left =
            await StrokeAndWatch(tree, lab, () => lab.Buddy.Rig.LeftFoot.GlobalPosition);
        (bool acquired, bool matched, bool coned, float yaw) right =
            await StrokeAndWatch(tree, lab, () => lab.Buddy.Rig.RightFoot.GlobalPosition);

        lab.CareStroke.SetStroke(false, Vector2.Zero);
        lab.Pipeline.SelectTool(ToolId.Grab);

        bool passed = left.acquired && left.matched && left.coned &&
            right.acquired && right.matched && right.coned;
        messages.Add($"engaged_cursor depth={depth} left_yaw={left.yaw:F3} right_yaw={right.yaw:F3}");
        return new StartupCheck("lookat_engaged_cursor_tracked", passed,
            $"left=({left.acquired},{left.matched},{left.coned}) " +
            $"right=({right.acquired},{right.matched},{right.coned})");
    }

    private static async Task<(bool acquired, bool matched, bool coned, float yaw)> StrokeAndWatch(
        SceneTree tree, BuddyLab lab, Func<Vector2> target)
    {
        HeadLookAtComponent look = lab.HeadLookAt;
        float depth = look.Profile.LookGazeDepthPixels;
        bool acquired = false;
        bool coned = true;
        bool matched = false;
        for (int frame = 0; frame < 360; frame++)
        {
            lab.CareStroke.SetStroke(true, target());
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            coned &= InsideCone(look);
            if (look.CurrentSource != LookAtSource.Cursor)
            {
                continue;
            }

            acquired = true;
            // Once the acquisition ease has settled, the model must sit exactly on the
            // independently recomputed target angles (clamped into the cone).
            Vector2 head = lab.Buddy.Rig.Head.GlobalPosition;
            Vector2 cursor = lab.CareStroke.Cursor;
            float expectedYaw = Mathf.Clamp(
                ExpectedAngleDegrees(cursor.X - head.X, depth),
                -look.Profile.LookConeYawDegrees, look.Profile.LookConeYawDegrees);
            float expectedPitch = Mathf.Clamp(
                ExpectedAngleDegrees(cursor.Y - head.Y, depth),
                -look.Profile.LookConePitchDegrees, look.Profile.LookConePitchDegrees);
            matched = Mathf.Abs(look.CurrentYawDegrees - expectedYaw) < 0.5f &&
                Mathf.Abs(look.CurrentPitchDegrees - expectedPitch) < 0.5f;
            if (matched)
            {
                break;
            }
        }

        return (acquired, matched, coned, look.CurrentYawDegrees);
    }

    /// <summary>
    /// The engagement-range cutoff: an engaged stroke whose cursor sits far from the head
    /// is not watched, and ambient behaviour resumes (the cursor is never the source).
    /// </summary>
    private static async Task<StartupCheck> CheckBeyondRangeReleasesTheCursor(
        SceneTree tree, BuddyLab lab, List<string> messages)
    {
        HeadLookAtComponent look = lab.HeadLookAt;
        float range = look.Profile.LookEngagementRangePixels;
        lab.Pipeline.SelectTool(ToolId.Pet);

        bool everCursor = false;
        bool coned = true;
        int frames = 0;
        for (int frame = 0; frame < 180; frame++)
        {
            // Held, but far outside the engagement range: too distant to be worth a look.
            lab.CareStroke.SetStroke(
                true, lab.Buddy.Rig.Head.GlobalPosition + new Vector2(range * 3.0f, 0.0f));
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            frames++;
            everCursor |= look.CurrentSource == LookAtSource.Cursor;
            coned &= InsideCone(look);
        }

        lab.CareStroke.SetStroke(false, Vector2.Zero);
        lab.Pipeline.SelectTool(ToolId.Grab);

        bool passed = !everCursor && coned && frames > 0;
        messages.Add($"range_cutoff range={range} ever_cursor={everCursor} source={look.CurrentSource}");
        return new StartupCheck("lookat_engagement_range_cutoff", passed,
            $"ever_cursor={everCursor} coned={coned} source={look.CurrentSource}");
    }

    /// <summary>
    /// A socketed item during the eat activity outranks ambient idling, and the gaze aims
    /// at the item socket's own world position through the oracle.
    /// </summary>
    private static async Task<StartupCheck> CheckItemTargetWins(
        SceneTree tree, BuddyLab lab, List<string> messages)
    {
        HeadLookAtComponent look = lab.HeadLookAt;
        float depth = look.Profile.LookGazeDepthPixels;
        var visual = new MeshInstance3D
        {
            Name = "LookItemVisual",
            Mesh = new SphereMesh { Radius = 3.0f, Height = 6.0f },
        };
        lab.Activities.AttachItemVisual(visual);

        bool acquired = false;
        bool matched = false;
        bool coned = true;
        for (int frame = 0; frame < 600 && !matched; frame++)
        {
            lab.Activities.SetActivity(ActivityId.Eat, 1.0);
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            coned &= InsideCone(look);
            if (look.CurrentSource != LookAtSource.Item)
            {
                continue;
            }

            acquired = true;
            Vector2 head = lab.Buddy.Rig.Head.GlobalPosition;
            Vector2 item = DesktopBuddy.Presentation3D.WorldPlaneMapping.To2D(
                lab.Activities.ItemSocket.GlobalPosition);
            float expectedYaw = Mathf.Clamp(
                ExpectedAngleDegrees(item.X - head.X, depth),
                -look.Profile.LookConeYawDegrees, look.Profile.LookConeYawDegrees);
            matched = Mathf.Abs(look.CurrentYawDegrees - expectedYaw) < 1.0f;
        }

        lab.Activities.SetActivity(ActivityId.None);
        lab.Activities.ClearItemVisual();

        bool passed = acquired && matched && coned;
        messages.Add($"item_target acquired={acquired} matched={matched} yaw={look.CurrentYawDegrees:F3}");
        return new StartupCheck("lookat_item_target_wins", passed,
            $"acquired={acquired} matched={matched} coned={coned}");
    }

    /// <summary>
    /// Ambient glances come from the component's own salted stream: reseeding with the
    /// same run seed replays the same glance angles. The cadence is compressed to a
    /// scenario-local one for the observation and restored afterwards, so the check costs
    /// seconds instead of minutes without touching the arbitration under test.
    /// </summary>
    private static async Task<StartupCheck> CheckAmbientGlanceDeterminism(
        SceneTree tree, BuddyLab lab, ulong seed, List<string> messages)
    {
        HeadLookAtComponent look = lab.HeadLookAt;
        BuddyExpressionProfile profile = look.Profile;
        int intervalMinimum = profile.LookGlanceIntervalMinimumTicks;
        int intervalMaximum = profile.LookGlanceIntervalMaximumTicks;
        int holdMinimum = profile.LookGlanceHoldMinimumTicks;
        int holdMaximum = profile.LookGlanceHoldMaximumTicks;
        profile.LookGlanceIntervalMinimumTicks = 24;
        profile.LookGlanceIntervalMaximumTicks = 60;
        profile.LookGlanceHoldMinimumTicks = 24;
        profile.LookGlanceHoldMaximumTicks = 48;

        (List<float> angles, bool coned, bool quantized) first = await ObserveGlances(tree, lab, seed);
        (List<float> angles, bool coned, bool quantized) second = await ObserveGlances(tree, lab, seed);

        profile.LookGlanceIntervalMinimumTicks = intervalMinimum;
        profile.LookGlanceIntervalMaximumTicks = intervalMaximum;
        profile.LookGlanceHoldMinimumTicks = holdMinimum;
        profile.LookGlanceHoldMaximumTicks = holdMaximum;
        look.Reseed(seed);

        bool observed = first.angles.Count >= 2;
        bool repeatable = first.angles.Count == second.angles.Count;
        for (int index = 0; repeatable && index < first.angles.Count; index++)
        {
            repeatable = Mathf.Abs(first.angles[index] - second.angles[index]) < 0.0001f;
        }

        bool passed = observed && repeatable && first.coned && second.coned &&
            first.quantized && second.quantized;
        messages.Add($"glance_determinism count={first.angles.Count}/{second.angles.Count} " +
            $"repeatable={repeatable}");
        return new StartupCheck("lookat_ambient_glances_deterministic", passed,
            $"observed={observed} repeatable={repeatable} coned={first.coned && second.coned} " +
            $"quantized={first.quantized && second.quantized}");
    }

    private static async Task<(List<float> angles, bool coned, bool quantized)> ObserveGlances(
        SceneTree tree, BuddyLab lab, ulong seed)
    {
        HeadLookAtComponent look = lab.HeadLookAt;
        int steps = look.Profile.LookPupilQuantizationSteps;
        look.Reseed(seed);

        var angles = new List<float>();
        bool coned = true;
        bool quantized = true;
        LookAtSource previous = look.CurrentSource;
        for (int frame = 0; frame < 900 && angles.Count < 4; frame++)
        {
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            coned &= InsideCone(look);
            // The pupil seam Task 5 consumes: always on a profile step boundary.
            Vector2 pupil = look.PupilOffset;
            quantized &= Mathf.Abs(pupil.X * steps - Mathf.Round(pupil.X * steps)) < 0.0001f &&
                Mathf.Abs(pupil.Y * steps - Mathf.Round(pupil.Y * steps)) < 0.0001f &&
                Mathf.Abs(pupil.X) <= 1.0f && Mathf.Abs(pupil.Y) <= 1.0f;

            LookAtSource source = look.CurrentSource;
            if (source == LookAtSource.Glance && previous != LookAtSource.Glance)
            {
                // Sample the glance target one settled frame later; the ease starts at the
                // previous angle, so the acquisition frame itself is not the glance angle.
                await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
                angles.Add(look.CurrentYawDegrees);
                source = look.CurrentSource;
            }

            previous = source;
        }

        return (angles, coned, quantized);
    }

    /// <summary>
    /// One controlled strike exercises three suppressions at once: the pain face holds the
    /// gaze at rest, the post-impact cooldown forces Tracking so the APPLIED head angles
    /// are exactly zero, and once both clear the impact point is watched until the profile
    /// memory expires.
    /// </summary>
    private static async Task<StartupCheck> CheckImpactSuppressionAndMemory(
        SceneTree tree, BuddyLab lab, List<string> messages)
    {
        HeadLookAtComponent look = lab.HeadLookAt;
        float depth = look.Profile.LookGazeDepthPixels;
        int memoryTicks = look.Profile.LookImpactMemoryTicks;

        AcceptedImpact? impact =
            await ScenarioSteps.StrikePartAtSpeed(tree, lab, lab.Buddy.Rig.Torso, 2000.0f);
        await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);

        bool painFaceSeen = false;
        bool restWhileSuppressed = true;
        bool zeroWhileTracking = true;
        bool watchedImpact = false;
        bool impactAimMatched = false;
        bool coned = true;
        bool decayed = false;
        for (int frame = 0; frame < memoryTicks + 600; frame++)
        {
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            coned &= InsideCone(look);

            if (look.Profile.SuppressesLookAt(lab.Reactions.CurrentFace))
            {
                painFaceSeen = true;
                restWhileSuppressed &= look.CurrentSource == LookAtSource.Rest;
            }

            if (lab.PosePipeline.Mode == PresentationPoseMode.Tracking)
            {
                zeroWhileTracking &=
                    Mathf.Abs(lab.VisualPresenter.AppliedHeadYawDegrees) < 0.0001f &&
                    Mathf.Abs(lab.VisualPresenter.AppliedHeadPitchDegrees) < 0.0001f;
            }

            if (look.CurrentSource == LookAtSource.Impact)
            {
                watchedImpact = true;
                Vector2 head = lab.Buddy.Rig.Head.GlobalPosition;
                Vector2 point = impact?.Point ?? Vector2.Zero;
                float expectedYaw = Mathf.Clamp(
                    ExpectedAngleDegrees(point.X - head.X, depth),
                    -look.Profile.LookConeYawDegrees, look.Profile.LookConeYawDegrees);
                impactAimMatched |= Mathf.Abs(look.CurrentYawDegrees - expectedYaw) < 1.0f;
            }
            else if (watchedImpact)
            {
                decayed = true;
                break;
            }
        }

        bool passed = impact is not null && painFaceSeen && restWhileSuppressed &&
            zeroWhileTracking && watchedImpact && impactAimMatched && decayed && coned;
        messages.Add($"impact_lookat watched={watchedImpact} aim_matched={impactAimMatched} " +
            $"decayed={decayed} pain_face={painFaceSeen}");
        return new StartupCheck("lookat_impact_memory_and_suppression", passed,
            $"impact={impact is not null} pain_face={painFaceSeen} " +
            $"rest_while_suppressed={restWhileSuppressed} zero_while_tracking={zeroWhileTracking} " +
            $"watched={watchedImpact} aim_matched={impactAimMatched} decayed={decayed} coned={coned}");
    }
}
