using System.Collections.Generic;
using System.Threading.Tasks;
using DesktopBuddy.App;
using DesktopBuddy.Buddy.Physics;
using DesktopBuddy.Buddy.Presentation3D;
using DesktopBuddy.Domain.Buddy;
using DesktopBuddy.Domain.Presentation;
using DesktopBuddy.Domain.Tools;
using DesktopBuddy.Interaction;
using DesktopBuddy.Presentation3D;
using Godot;

namespace DesktopBuddy.Testing;

/// <summary>
/// M3.6 Task 1 gate: pose-mode arbitration driven through real gameplay semantics,
/// the bounded-offset invariant, and pain invariance across Tracking/Performance/
/// mid-blend strikes (M3_6_EXPRESSIVE_PRESENTATION_PLAN.md Task 1). Runs on a
/// normal-gravity laboratory; the injected pain profile saturates well below the
/// controlled strike impulse so accepted pain is exactly the anchor maximum in every
/// mode and equality is robust to gait noise.
/// </summary>
public sealed class PosePipelineScenario : IScenario
{
    private const float SaturatedPain = 10.0f;
    private const int StandingBudgetTicks = 1800;
    private const int ModeBudgetFrames = 1200;

    public string Id => "pose_pipeline";

    public async Task<ScenarioResult> RunAsync(SceneTree tree, ulong seed)
    {
        var checks = new List<StartupCheck>();
        var messages = new List<string> { $"seed={seed}" };

        PackedScene? packed = GD.Load<PackedScene>("res://scenes/buddy_lab.tscn");
        if (packed is null)
        {
            checks.Add(new StartupCheck("pose_scene_loadable", false, "buddy_lab"));
            return new ScenarioResult(false, checks, messages);
        }

        BuddyLab lab = packed.Instantiate<BuddyLab>();
        // Saturating conversion: any controlled strike impulse beyond 4 yields exactly
        // SaturatedPain, so pain equality across modes cannot be broken by pose noise —
        // even a strike into an already-recoiling torso (roughly half the relative
        // speed) lands far above the saturation anchor.
        lab.Pipeline.Profile = new PainConversionProfile
        {
            ResourceName = "ScenarioSaturatingPainConversion",
            ImpulseAnchors = new[] { 2.0f, 4.0f },
            PainAnchors = new[] { 0.0f, SaturatedPain },
            MinimumImpulse = 2.0f,
            CashPerPain = 1.0,
        };
        tree.Root.AddChild(lab);
        await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        lab.Controls.Reseed(seed);

        // Order matters: the saturating profile makes every glove-hover contact episode
        // in the arbitration check worth maximum pain, which legitimately knocks the
        // buddy out through the real rolling window. Run the strike-equality and offset
        // checks on a fresh pain window first; hover the guard-raising glove last.
        checks.Add(await CheckPauseHoldsPresentation(tree, lab, messages));
        checks.Add(await CheckBlendPhysicsInvariant(tree, lab, messages));
        checks.Add(await CheckOffsetBounded(tree, lab, messages));
        checks.Add(await CheckModeArbitration(tree, lab, messages));

        lab.QueueFree();
        await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);

        bool passed = true;
        foreach (StartupCheck check in checks)
        {
            passed &= check.Passed;
        }

        return new ScenarioResult(passed, checks, messages);
    }

    /// <summary>
    /// A paused laboratory must be visually still. The performance layer is driven by the
    /// RENDERED frame, which keeps arriving while the routed gameplay tick does not, so
    /// without an explicit hold the buddy kept breathing, turning, and glancing behind a
    /// frozen ragdoll — the "frozen but its head still moves" report from 2026-07-20.
    /// </summary>
    private static async Task<StartupCheck> CheckPauseHoldsPresentation(
        SceneTree tree, BuddyLab lab, List<string> messages)
    {
        await ScenarioSteps.WaitForStanding(tree, lab, 1800);
        for (int frame = 0; frame < 240 && lab.PosePipeline.PerformanceWeight < 1.0f; frame++)
        {
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        }

        lab.Controls.SetPaused(true);
        await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);

        long ticksAtPause = lab.Buddy.RoutedTicks;
        float yaw = lab.VisualPresenter.AppliedYawDegrees;
        float headYaw = lab.VisualPresenter.AppliedHeadYawDegrees;
        float headPitch = lab.VisualPresenter.AppliedHeadPitchDegrees;
        Vector3 offset = lab.Activities.OffsetFor((int)BuddyPartId.Torso);

        // Long enough that the seeded glance and idle-flip timers would both have fired.
        bool stillYaw = true;
        bool stillHead = true;
        bool stillOffset = true;
        for (int frame = 0; frame < 600; frame++)
        {
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            stillYaw &= Mathf.Abs(lab.VisualPresenter.AppliedYawDegrees - yaw) < 0.0001f;
            stillHead &=
                Mathf.Abs(lab.VisualPresenter.AppliedHeadYawDegrees - headYaw) < 0.0001f &&
                Mathf.Abs(lab.VisualPresenter.AppliedHeadPitchDegrees - headPitch) < 0.0001f;
            stillOffset &= lab.Activities.OffsetFor((int)BuddyPartId.Torso)
                .DistanceTo(offset) < 0.0001f;
        }

        bool simulationHeld = lab.Buddy.RoutedTicks == ticksAtPause;

        // Releasing must restore motion rather than latch the hold.
        lab.Controls.SetPaused(false);
        bool resumed = false;
        for (int frame = 0; frame < 600 && !resumed; frame++)
        {
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            resumed = lab.Activities.OffsetFor((int)BuddyPartId.Torso).DistanceTo(offset) > 0.0001f ||
                Mathf.Abs(lab.VisualPresenter.AppliedYawDegrees - yaw) > 0.0001f;
        }

        bool passed = simulationHeld && stillYaw && stillHead && stillOffset && resumed;
        messages.Add($"pause_hold yaw_still={stillYaw} head_still={stillHead} " +
            $"offset_still={stillOffset} resumed={resumed}");
        return new StartupCheck("pause_holds_presentation", passed,
            $"simulation_held={simulationHeld} yaw_still={stillYaw} head_still={stillHead} " +
            $"offset_still={stillOffset} resumed_after_release={resumed}");
    }

    /// <summary>
    /// Awaits rendered frames until the pipeline reports the wanted mode. Always awaits
    /// before checking so a stale pre-await mode from an earlier frame cannot satisfy
    /// the wait (signal resumption order vs. _Process is not guaranteed).
    /// </summary>
    private static async Task<bool> WaitForMode(
        SceneTree tree, BuddyLab lab, PresentationPoseMode wanted, int budgetFrames)
    {
        for (int frame = 0; frame < budgetFrames; frame++)
        {
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            if (lab.PosePipeline.Mode == wanted)
            {
                return true;
            }
        }

        return false;
    }

    private static async Task<StartupCheck> CheckModeArbitration(
        SceneTree tree, BuddyLab lab, List<string> messages)
    {
        bool stood = await ScenarioSteps.WaitForStanding(tree, lab, StandingBudgetTicks);
        bool calmPerformance = await WaitForMode(
            tree, lab, PresentationPoseMode.Performance, ModeBudgetFrames);

        // A live buddy-part grab forces Tracking through the real tether.
        bool grabForces = false;
        PuppetPartBody torso = lab.Buddy.Rig.Torso;
        if (lab.Grab.TryGrab(torso, torso.GlobalPosition))
        {
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            grabForces = lab.PosePipeline.Mode == PresentationPoseMode.Tracking;
            lab.Grab.Release();
        }

        // Unconsciousness forces Tracking instantly; recovery then has to stand back up.
        lab.Buddy.SetConsciousness(Consciousness.Unconscious);
        await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        bool unconsciousForces = lab.PosePipeline.Mode == PresentationPoseMode.Tracking;
        lab.Buddy.SetConsciousness(Consciousness.Conscious);
        await ScenarioSteps.WaitForStanding(tree, lab, StandingBudgetTicks);

        // A controlled strike stamps the post-impact cooldown: Tracking immediately
        // after the accepted impact, Performance again once the cooldown and any
        // physical disturbance have passed.
        AcceptedImpact? impact = await ScenarioSteps.StrikePart(tree, lab, torso);
        await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        bool impactForces = impact is not null &&
            lab.PosePipeline.Mode == PresentationPoseMode.Tracking;
        bool cooldownRecovers = await WaitForMode(
            tree, lab, PresentationPoseMode.Performance, ModeBudgetFrames);

        // The learned-harm hand guard is the real tool-reaction window (same drive as
        // tool_feel_reactions): glove cursor near the protected band raises the guard.
        lab.Pipeline.SelectTool(ToolId.BoxingGlove);
        Vector2 protectedCenter =
            (lab.Buddy.Rig.Head.GlobalPosition + torso.GlobalPosition) * 0.5f;
        lab.CursorTools.MoveCursor(protectedCenter);
        for (int tick = 0; tick < 60 && !lab.ToolReactions.IsDefending; tick++)
        {
            await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
        }

        await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        bool reactionForces = lab.ToolReactions.IsDefending &&
            lab.PosePipeline.Mode == PresentationPoseMode.Tracking;
        lab.Pointer.NotifyPointerExitedPlayArea();
        lab.Pipeline.SelectTool(ToolId.Grab);

        bool passed = stood && calmPerformance && grabForces && unconsciousForces &&
            impactForces && cooldownRecovers && reactionForces;
        messages.Add($"arbitration calm={calmPerformance} grab={grabForces} " +
            $"unconscious={unconsciousForces} impact={impactForces} " +
            $"cooldown_recovers={cooldownRecovers} reaction={reactionForces}");
        return new StartupCheck("pose_mode_arbitration", passed,
            $"stood={stood} calm={calmPerformance} grab={grabForces} " +
            $"unconscious={unconsciousForces} impact={impactForces} " +
            $"cooldown_recovers={cooldownRecovers} reaction={reactionForces}");
    }

    private static async Task<StartupCheck> CheckOffsetBounded(
        SceneTree tree, BuddyLab lab, List<string> messages)
    {
        BuddyVisualPresenter presenter = lab.VisualPresenter;
        // Request offsets far beyond every cap; the clamp must hold the applied offset
        // at exactly the cap once the blend is full, and never beyond it.
        for (int index = 0; index < PuppetRigProfile.RequiredPartCount; index++)
        {
            presenter.SetDevelopmentOffset((BuddyPartId)index, new Vector3(50.0f, 40.0f, 30.0f));
        }

        bool reachedFull = false;
        for (int frame = 0; frame < ModeBudgetFrames && !reachedFull; frame++)
        {
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            reachedFull = lab.PosePipeline.Mode == PresentationPoseMode.Performance &&
                lab.PosePipeline.PerformanceWeight >= 0.999f;
        }

        float capFraction = lab.PosePipeline.Profile.OffsetCapRadiusFraction;
        int validSamples = 0;
        bool bounded = true;
        bool nonVacuous = true;
        float worstRatio = 0.0f;
        for (int frame = 0; frame < 600 && validSamples < 10; frame++)
        {
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            if (lab.PosePipeline.PerformanceWeight < 0.999f)
            {
                continue;
            }

            validSamples++;
            // The facing controller may have committed a three-quarter yaw, so the
            // no-offset expectation applies the same independent yaw math the look
            // scenario uses (torso pivot, Up axis) at the presenter's applied yaw;
            // rotation preserves the offset magnitude, so the distance to that
            // expectation is exactly the applied clamped offset.
            var yawBasis = new Basis(Vector3.Up, Mathf.DegToRad(presenter.AppliedYawDegrees));
            Vector3 pivot = WorldPlaneMapping.To3D(
                presenter.RenderedPosition2D(BuddyPartId.Torso));
            for (int index = 0; index < PuppetRigProfile.RequiredPartCount; index++)
            {
                var id = (BuddyPartId)index;
                float depth = lab.Buddy.VisualProfile.FindPart(id)!.DepthOffset;
                Vector3 mapped = WorldPlaneMapping.To3D(presenter.RenderedPosition2D(id));
                Vector3 tracked = pivot + (yawBasis * (mapped - pivot)) +
                    new Vector3(0.0f, 0.0f, depth);
                float offset = presenter.GetPartSocket(id).GlobalPosition.DistanceTo(tracked);
                float cap = capFraction * presenter.PartMeshRadius(id);
                bounded &= offset <= cap * 1.01f;
                nonVacuous &= offset >= cap * 0.99f;
                worstRatio = Mathf.Max(worstRatio, cap > 0.0f ? offset / cap : 0.0f);
            }
        }

        for (int index = 0; index < PuppetRigProfile.RequiredPartCount; index++)
        {
            presenter.SetDevelopmentOffset((BuddyPartId)index, Vector3.Zero);
        }

        bool passed = reachedFull && validSamples >= 10 && bounded && nonVacuous;
        messages.Add($"offset_bounded samples={validSamples} worst_ratio={worstRatio:F4}");
        return new StartupCheck("pose_offset_bounded", passed,
            $"full_blend={reachedFull} samples={validSamples} bounded={bounded} " +
            $"at_cap={nonVacuous} worst_ratio={worstRatio:F4}");
    }

    private static async Task<StartupCheck> CheckBlendPhysicsInvariant(
        SceneTree tree, BuddyLab lab, List<string> messages)
    {
        PuppetPartBody torso = lab.Buddy.Rig.Torso;

        // 2000 px/s strikes: even a torso already recoiling from the previous hit keeps
        // the relative contact speed far above the saturation anchor, so all three
        // accepted pains are exactly SaturatedPain when the pipeline is invariant.
        const float strikeSpeed = 2000.0f;
        await ScenarioSteps.WaitForStanding(tree, lab, StandingBudgetTicks);
        bool performanceAtFirst = await WaitForMode(
            tree, lab, PresentationPoseMode.Performance, ModeBudgetFrames);
        AcceptedImpact? performanceHit =
            await ScenarioSteps.StrikePartAtSpeed(tree, lab, torso, strikeSpeed);

        // Still inside the post-impact cooldown: the next strike launches in Tracking.
        await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        bool trackingAtSecond = lab.PosePipeline.Mode == PresentationPoseMode.Tracking;
        AcceptedImpact? trackingHit =
            await ScenarioSteps.StrikePartAtSpeed(tree, lab, torso, strikeSpeed);

        // Strike again the moment Performance is re-allowed, while the blend is easing.
        bool performanceAtThird = await WaitForMode(
            tree, lab, PresentationPoseMode.Performance, ModeBudgetFrames);
        string forcing = $"stable={lab.Buddy.Standing.Snapshot.IsStable} " +
            $"assist={lab.Buddy.Recovery.State.AssistanceActive} " +
            $"react={lab.Buddy.CurrentToolReactionIntent.Active} " +
            $"grab={lab.Grab.CurrentGrab.Active} " +
            $"conscious={lab.Buddy.CurrentConsciousness}";
        float weightAtLaunch = lab.PosePipeline.PerformanceWeight;
        AcceptedImpact? blendHit =
            await ScenarioSteps.StrikePartAtSpeed(tree, lab, torso, strikeSpeed);

        bool allLanded = performanceHit is not null && trackingHit is not null &&
            blendHit is not null;
        bool equalPain = allLanded &&
            Mathf.IsEqualApprox((float)performanceHit!.Value.Pain, (float)trackingHit!.Value.Pain) &&
            Mathf.IsEqualApprox((float)trackingHit.Value.Pain, (float)blendHit!.Value.Pain) &&
            Mathf.IsEqualApprox((float)performanceHit.Value.Pain, SaturatedPain);

        bool passed = performanceAtFirst && trackingAtSecond && performanceAtThird &&
            allLanded && equalPain;
        messages.Add($"blend_invariant perf={performanceHit?.Pain:F3} " +
            $"track={trackingHit?.Pain:F3} blend={blendHit?.Pain:F3} " +
            $"blend_launch_weight={weightAtLaunch:F3}");
        return new StartupCheck("mode_blend_physics_invariant", passed,
            $"perf_at_first={performanceAtFirst} track_at_second={trackingAtSecond} " +
            $"perf_at_third={performanceAtThird} landed={allLanded} equal_pain={equalPain} " +
            $"blend_launch_weight={weightAtLaunch:F3} third_wait_state[{forcing}]");
    }
}
