using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using DesktopBuddy.App;
using DesktopBuddy.Buddy.Physics;
using Godot;

namespace DesktopBuddy.Testing;

/// <summary>
/// A horizontally driven foot grab must move as a loose pendulum: the mass
/// follows with measurable phase lag, the spring frame flexes, and every link
/// remains anchored by its configured distance cap.
/// </summary>
public sealed class GrabSwingPendulumScenario : IScenario
{
    private const int PreSwingTicks = 240;
    private const int SwingTicks = 480;
    private const int PeriodTicks = 120;
    private const int MaximumCorrelationLagTicks = 45;
    private const int RecoveryBudgetTicks = 1200;
    private const float CursorAmplitude = 60.0f;
    private const float MinimumComAmplitudeFraction = 0.25f;
    private const float MaximumLinkMargin = 10.0f;
    private const float MinimumTransientExtension = 4.0f;
    private const float MinimumPeakCorrelation = 0.5f;
    private const int MinimumLagTicks = 2;

    public string Id => "grab_swing_pendulum";

    public async Task<ScenarioResult> RunAsync(SceneTree tree, ulong seed)
    {
        var checks = new List<StartupCheck>();
        var messages = new List<string> { $"seed={seed}" };
        PackedScene? packed = GD.Load<PackedScene>("res://scenes/buddy_lab.tscn");
        if (packed is null)
        {
            return new ScenarioResult(false,
                new[] { new StartupCheck("grab_swing_scene_loadable", false, "buddy_lab") },
                messages);
        }

        BuddyLab lab = packed.Instantiate<BuddyLab>();
        tree.Root.AddChild(lab);
        await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        lab.Buddy.ReseedAutonomy(seed);
        lab.Buddy.ActiveDrive.SuppressLocomotion = true;
        lab.Buddy.Rig.ResetToSafePose(new Vector2(240.0f, 240.0f));
        lab.Buddy.Standing.Reset();
        await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);

        PuppetPartBody foot = lab.Buddy.Rig.LeftFoot;
        bool grabbed = lab.Grab.TryGrab(foot, foot.GlobalPosition);
        var cursorSamples = new float[SwingTicks];
        var comSamples = new float[SwingTicks];
        bool passive = grabbed;
        bool finite = grabbed;
        int unsupportedTicks = 0;
        float maximumDistanceExcess = float.NegativeInfinity;
        float maximumTransientExtension = 0.0f;

        for (int tick = 0; tick < PreSwingTicks; tick++)
        {
            lab.Grab.MoveCursor(new Vector2(240.0f, 145.0f));
            await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
            ObservePhysics(lab, ref passive, ref finite, ref unsupportedTicks,
                ref maximumDistanceExcess, ref maximumTransientExtension);
        }

        for (int tick = 0; tick < SwingTicks; tick++)
        {
            float phase = MathF.Tau * tick / PeriodTicks;
            float cursorX = 240.0f + (CursorAmplitude * MathF.Sin(phase));
            lab.Grab.MoveCursor(new Vector2(cursorX, 145.0f));
            await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);

            cursorSamples[tick] = cursorX;
            comSamples[tick] = CenterOfMass(lab).X;
            ObservePhysics(lab, ref passive, ref finite, ref unsupportedTicks,
                ref maximumDistanceExcess, ref maximumTransientExtension);
        }

        float comAmplitude = (Maximum(comSamples) - Minimum(comSamples)) * 0.5f;
        (int bestLag, float peakCorrelation) = FindBestPositiveLag(cursorSamples, comSamples);
        bool anchored = maximumDistanceExcess <= MaximumLinkMargin;
        bool flexed = maximumTransientExtension >= MinimumTransientExtension;

        lab.Grab.Release();
        bool driveResumed = false;
        bool standingRecovered = false;
        for (int tick = 0; tick < RecoveryBudgetTicks && !standingRecovered; tick++)
        {
            await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
            driveResumed |= lab.Buddy.ActiveDrive.ActiveOutputsEnabled;
            standingRecovered = lab.Buddy.Standing.Snapshot.IsStable;
            finite &= lab.Buddy.Rig.AllBodiesFinite();
        }

        checks.Add(new StartupCheck("grab_swing_foot_acquired", grabbed,
            $"grabbed={grabbed}"));
        checks.Add(new StartupCheck("grab_swing_com_follows_cursor",
            comAmplitude >= CursorAmplitude * MinimumComAmplitudeFraction,
            $"com_amplitude={comAmplitude:F1}px cursor_amplitude={CursorAmplitude:F1}px " +
            $"minimum_fraction={MinimumComAmplitudeFraction:F2}"));
        checks.Add(new StartupCheck("grab_swing_mass_lags_cursor",
            bestLag >= MinimumLagTicks && peakCorrelation >= MinimumPeakCorrelation,
            $"best_lag={bestLag}ticks peak_correlation={peakCorrelation:F3}"));
        checks.Add(new StartupCheck("grab_swing_links_remain_anchored", anchored,
            $"maximum_distance_excess={maximumDistanceExcess:F1}px margin={MaximumLinkMargin:F1}px"));
        checks.Add(new StartupCheck("grab_swing_structure_flexes", flexed,
            $"maximum_transient_extension={maximumTransientExtension:F1}px " +
            $"minimum={MinimumTransientExtension:F1}px"));
        checks.Add(new StartupCheck("grab_swing_drive_remains_passive",
            passive && unsupportedTicks >= SwingTicks,
            $"passive={passive} unsupported_ticks={unsupportedTicks}"));
        checks.Add(new StartupCheck("grab_swing_bodies_stay_finite", finite,
            $"finite={finite}"));
        checks.Add(new StartupCheck("grab_swing_release_recovers_standing",
            driveResumed && standingRecovered,
            $"drive_resumed={driveResumed} standing={standingRecovered} " +
            $"budget={RecoveryBudgetTicks}ticks"));

        messages.Add($"swing=com_amplitude:{comAmplitude:F2},best_lag:{bestLag}," +
            $"correlation:{peakCorrelation:F4},distance_excess:{maximumDistanceExcess:F2}," +
            $"extension:{maximumTransientExtension:F2}");
        lab.QueueFree();

        bool passed = true;
        foreach (StartupCheck check in checks)
            passed &= check.Passed;
        return new ScenarioResult(passed, checks, messages);
    }

    private static void ObservePhysics(
        BuddyLab lab,
        ref bool passive,
        ref bool finite,
        ref int unsupportedTicks,
        ref float maximumDistanceExcess,
        ref float maximumTransientExtension)
    {
        if (lab.Buddy.Standing.Snapshot.SupportContactCount == 0)
        {
            unsupportedTicks++;
            passive &= !lab.Buddy.ActiveDrive.ActiveOutputsEnabled &&
                lab.Buddy.ActiveDrive.LastUprightTorque == 0.0f &&
                lab.Buddy.ActiveDrive.LastHeadUprightTorque == 0.0f &&
                lab.Buddy.ActiveDrive.LastBalanceForce.IsZeroApprox() &&
                lab.Buddy.ActiveDrive.LastLocomotionForce.IsZeroApprox() &&
                lab.Buddy.ActiveDrive.LastResistanceForce.IsZeroApprox();
        }

        finite &= lab.Buddy.Rig.AllBodiesFinite();
        foreach (PuppetLinkDefinition link in lab.Buddy.Rig.Profile.Links)
        {
            Vector2 offset = lab.Buddy.Rig.GetPart(link.PartB).GlobalPosition -
                             lab.Buddy.Rig.GetPart(link.PartA).GlobalPosition;
            float separation = offset.Length();
            maximumDistanceExcess = Mathf.Max(
                maximumDistanceExcess,
                separation - link.MaximumDistance);
            maximumTransientExtension = Mathf.Max(
                maximumTransientExtension,
                separation - link.RestOffset.Length());
        }
    }

    private static Vector2 CenterOfMass(BuddyLab lab)
    {
        Vector2 weighted = Vector2.Zero;
        float totalMass = 0.0f;
        foreach (PuppetPartBody body in lab.Buddy.Rig.Parts)
        {
            weighted += body.GlobalPosition * body.Mass;
            totalMass += body.Mass;
        }

        return weighted / totalMass;
    }

    private static (int Lag, float Correlation) FindBestPositiveLag(
        IReadOnlyList<float> cursor,
        IReadOnlyList<float> body)
    {
        int bestLag = 0;
        float bestCorrelation = float.NegativeInfinity;
        for (int lag = 0; lag <= MaximumCorrelationLagTicks; lag++)
        {
            float correlation = CorrelationAtLag(cursor, body, lag);
            if (correlation > bestCorrelation)
            {
                bestCorrelation = correlation;
                bestLag = lag;
            }
        }

        return (bestLag, bestCorrelation);
    }

    private static float CorrelationAtLag(
        IReadOnlyList<float> cursor,
        IReadOnlyList<float> body,
        int lag)
    {
        int count = cursor.Count - lag;
        float cursorMean = 0.0f;
        float bodyMean = 0.0f;
        for (int index = lag; index < cursor.Count; index++)
        {
            cursorMean += cursor[index - lag];
            bodyMean += body[index];
        }
        cursorMean /= count;
        bodyMean /= count;

        float covariance = 0.0f;
        float cursorVariance = 0.0f;
        float bodyVariance = 0.0f;
        for (int index = lag; index < cursor.Count; index++)
        {
            float cursorDelta = cursor[index - lag] - cursorMean;
            float bodyDelta = body[index] - bodyMean;
            covariance += cursorDelta * bodyDelta;
            cursorVariance += cursorDelta * cursorDelta;
            bodyVariance += bodyDelta * bodyDelta;
        }

        float denominator = MathF.Sqrt(cursorVariance * bodyVariance);
        return denominator > 0.0001f ? covariance / denominator : 0.0f;
    }

    private static float Minimum(IReadOnlyList<float> values)
    {
        float minimum = float.PositiveInfinity;
        for (int index = 0; index < values.Count; index++)
            minimum = Mathf.Min(minimum, values[index]);
        return minimum;
    }

    private static float Maximum(IReadOnlyList<float> values)
    {
        float maximum = float.NegativeInfinity;
        for (int index = 0; index < values.Count; index++)
            maximum = Mathf.Max(maximum, values[index]);
        return maximum;
    }
}
