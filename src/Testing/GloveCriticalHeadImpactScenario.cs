using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DesktopBuddy.App;
using DesktopBuddy.Buddy.Physics;
using DesktopBuddy.Domain.Buddy;
using DesktopBuddy.Domain.Content;
using DesktopBuddy.Domain.Tools;
using DesktopBuddy.Interaction;
using DesktopBuddy.Tools;
using Godot;

namespace DesktopBuddy.Testing;

/// <summary>
/// Focused CAP-2/CAP-9 regression gate for the short boxing-glove critical-head pause and its
/// replacement-ready audio lane. Every probe uses a real PhysicalTools rigid body and production
/// contact pipeline; no synthetic damage, direct hit-lag invocation, or direct audio playback is
/// allowed. Hard torso and sub-threshold head strikes are negative controls.
/// </summary>
public sealed class GloveCriticalHeadImpactScenario : IScenario
{
    private const float HardStrikeSpeed = 3200.0f;
    private const float HardStrikeMass = 1.0f;
    private const float WeakStrikeSpeed = 900.0f;
    private const float WeakStrikeMass = 0.25f;
    private const int ContactTimeoutTicks = 120;

    public string Id => "glove_critical_head_impact";

    public async Task<ScenarioResult> RunAsync(SceneTree tree, ulong seed)
    {
        var checks = new List<StartupCheck>();
        var messages = new List<string>();

        ProbeResult strongHead = await RunProbe(tree, BuddyPart.Head, HardStrikeSpeed, HardStrikeMass, awaitCompletion: true);
        checks.Add(new StartupCheck(
            "critical_head_strike_enters_real_damage_pipeline",
            strongHead.Impact is { ContentId: ContentIds.ToolBoxingGlove, Part: BuddyPart.Head },
            strongHead.Impact is null
                ? "No accepted boxing-glove head impact was observed."
                : $"content={strongHead.Impact.Value.ContentId} part={strongHead.Impact.Value.Part} raw={strongHead.Impact.Value.RawImpulse:0.0}"));
        checks.Add(new StartupCheck(
            "critical_head_strike_meets_shared_impulse_anchor",
            strongHead.Impact is { RawImpulse: >= SwingHitLagComponent.GloveCriticalHeadImpulse },
            strongHead.Impact is null
                ? "No accepted impact."
                : $"raw={strongHead.Impact.Value.RawImpulse:0.0} threshold={SwingHitLagComponent.GloveCriticalHeadImpulse:0.0}"));
        checks.Add(new StartupCheck(
            "critical_head_hitlag_uses_six_tick_pseudo_epoch",
            strongHead.Started &&
            strongHead.StartedState.SwingEpoch == SwingHitLagComponent.GloveCriticalPseudoEpoch &&
            strongHead.StartedState.DurationTicks == SwingHitLagComponent.GloveCriticalHeadHitLagTicks &&
            strongHead.StartedState.StruckPart == BuddyPart.Head &&
            !strongHead.StartedState.IsLooseObjectHit,
            strongHead.Started
                ? $"epoch={strongHead.StartedState.SwingEpoch} ticks={strongHead.StartedState.DurationTicks} part={strongHead.StartedState.StruckPart} loose={strongHead.StartedState.IsLooseObjectHit}"
                : "Critical head strike never started hit-lag."));
        checks.Add(new StartupCheck(
            "critical_head_hitlag_freezes_exactly_six_frames_and_recovers",
            strongHead.FrozenFrames == SwingHitLagComponent.GloveCriticalHeadHitLagTicks &&
            strongHead.Completed && !strongHead.ActiveAfterCompletion,
            $"frozen={strongHead.FrozenFrames} expected={SwingHitLagComponent.GloveCriticalHeadHitLagTicks} completed={strongHead.Completed} active_after={strongHead.ActiveAfterCompletion}"));
        checks.Add(new StartupCheck(
            "critical_head_uses_one_glove_audio_route_and_one_critical_marker",
            strongHead.GloveAudioCount == 1 && strongHead.CriticalAudioCount == 1,
            $"glove_audio={strongHead.GloveAudioCount} critical_audio={strongHead.CriticalAudioCount}; critical cue replaces rather than layers over the ordinary glove cue"));

        ProbeResult hardTorso = await RunProbe(tree, BuddyPart.Torso, HardStrikeSpeed, HardStrikeMass, awaitCompletion: false);
        checks.Add(new StartupCheck(
            "hard_torso_glove_hit_does_not_trigger_critical_head_pause",
            hardTorso.Impact is { Part: BuddyPart.Torso } &&
            hardTorso.Impact.Value.RawImpulse >= SwingHitLagComponent.GloveCriticalHeadImpulse &&
            !hardTorso.Started,
            hardTorso.Impact is null
                ? "No hard torso impact was observed."
                : $"raw={hardTorso.Impact.Value.RawImpulse:0.0} threshold={SwingHitLagComponent.GloveCriticalHeadImpulse:0.0} started={hardTorso.Started}"));
        checks.Add(new StartupCheck(
            "hard_torso_keeps_ordinary_glove_audio_route",
            hardTorso.GloveAudioCount == 1 && hardTorso.CriticalAudioCount == 0,
            $"glove_audio={hardTorso.GloveAudioCount} critical_audio={hardTorso.CriticalAudioCount}"));

        ProbeResult weakHead = await RunProbe(tree, BuddyPart.Head, WeakStrikeSpeed, WeakStrikeMass, awaitCompletion: false);
        checks.Add(new StartupCheck(
            "sub_threshold_head_glove_hit_does_not_trigger_pause",
            weakHead.Impact is { Part: BuddyPart.Head } &&
            weakHead.Impact.Value.RawImpulse < SwingHitLagComponent.GloveCriticalHeadImpulse &&
            !weakHead.Started,
            weakHead.Impact is null
                ? "No weak head impact was observed."
                : $"raw={weakHead.Impact.Value.RawImpulse:0.0} threshold={SwingHitLagComponent.GloveCriticalHeadImpulse:0.0} started={weakHead.Started}"));
        checks.Add(new StartupCheck(
            "sub_threshold_head_keeps_ordinary_glove_audio_route",
            weakHead.GloveAudioCount == 1 && weakHead.CriticalAudioCount == 0,
            $"glove_audio={weakHead.GloveAudioCount} critical_audio={weakHead.CriticalAudioCount}"));

        checks.Add(new StartupCheck(
            "critical_glove_pause_is_shorter_than_home_run_maximum",
            SwingHitLagComponent.GloveCriticalHeadHitLagTicks < 60,
            $"glove={SwingHitLagComponent.GloveCriticalHeadHitLagTicks} home_run_max=60"));

        bool passed = checks.All(check => check.Passed);
        messages.Add($"seed={seed}");
        messages.Add("critical glove punctuation is contact-driven: boxing glove + Head + shared 1500 raw-impulse anchor only");
        messages.Add("critical glove audio is replacement-ready and mutually exclusive with the ordinary glove cue for the same accepted impact");
        return new ScenarioResult(passed, checks, messages);
    }

    private static async Task<ProbeResult> RunProbe(
        SceneTree tree,
        BuddyPart targetPart,
        float speed,
        float mass,
        bool awaitCompletion)
    {
        BuddyLab? lab = await ScenarioSteps.CreateControlledImpactLab(
            tree,
            maximumPain: 100.0f,
            maximumImpulse: 5000.0f,
            curveFloorImpulse: 10.0f);
        if (lab is null)
            return default;

        PuppetPartBody target = targetPart == BuddyPart.Head ? lab.Buddy.Rig.Head : lab.Buddy.Rig.Torso;
        int frozenBefore = lab.SwingHitLag.FrozenFrameCount;
        int completionBefore = lab.SwingHitLag.CompletionCount;
        int gloveAudioBefore = lab.ReactionAudio.GloveImpactCount;
        int criticalAudioBefore = lab.ReactionAudio.GloveCriticalHeadImpactCount;
        SwingHitLagStarted startedState = default;
        bool started = false;
        void OnStarted(SwingHitLagStarted state)
        {
            if (!started)
            {
                started = true;
                startedState = state;
            }
        }
        lab.SwingHitLag.Started += OnStarted;

        AcceptedImpact? impact = await Strike(tree, lab, target, speed, mass);
        bool activeAtContact = lab.SwingHitLag.IsActive;

        if (awaitCompletion && lab.SwingHitLag.IsActive)
        {
            for (int frame = 0; frame < SwingHitLagComponent.GloveCriticalHeadHitLagTicks + 4 && lab.SwingHitLag.IsActive; frame++)
                await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
        }

        int frozenFrames = lab.SwingHitLag.FrozenFrameCount - frozenBefore;
        bool completed = lab.SwingHitLag.CompletionCount > completionBefore;
        bool activeAfter = lab.SwingHitLag.IsActive;
        int gloveAudioCount = lab.ReactionAudio.GloveImpactCount - gloveAudioBefore;
        int criticalAudioCount = lab.ReactionAudio.GloveCriticalHeadImpactCount - criticalAudioBefore;
        lab.SwingHitLag.Started -= OnStarted;

        lab.QueueFree();
        await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);

        return new ProbeResult(
            impact,
            started || activeAtContact,
            startedState,
            frozenFrames,
            completed,
            activeAfter,
            gloveAudioCount,
            criticalAudioCount);
    }

    private static async Task<AcceptedImpact?> Strike(
        SceneTree tree,
        BuddyLab lab,
        PuppetPartBody target,
        float speed,
        float mass)
    {
        const float sourceRadius = 10.0f;
        var source = new ScenarioImpactBody();
        source.Configure(ContentIds.ToolBoxingGlove, sourceRadius, mass);
        // This focused probe intentionally runs faster than one collider diameter per
        // 120 Hz tick. Shape casting prevents tunnelling while the target isolation below
        // makes the assertion about one exact Buddy part rather than whichever overlapping
        // limb happens to intercept the probe first.
        source.ContinuousCd = RigidBody2D.CcdMode.CastShape;

        var isolatedLayers = new List<(PuppetPartBody Body, uint Layer)>();
        foreach (PuppetPartBody part in lab.Buddy.Rig.Parts)
        {
            if (ReferenceEquals(part, target))
                continue;

            isolatedLayers.Add((part, part.CollisionLayer));
            part.CollisionLayer = 0;
        }

        Vector2 direction = target == lab.Buddy.Rig.Torso
            ? Vector2.Right
            : (lab.Buddy.Rig.Torso.GlobalPosition - target.GlobalPosition).Normalized();
        if (direction.IsZeroApprox())
            direction = Vector2.Down;

        source.Position = target.GlobalPosition - direction * (target.Radius + sourceRadius + 2.0f);
        source.LinearVelocity = direction * speed;

        AcceptedImpact? accepted = null;
        void OnAccepted(AcceptedImpact candidate)
        {
            if (candidate.InteractionId == source.InteractionId && accepted is null)
                accepted = candidate;
        }

        lab.Pipeline.ImpactAccepted += OnAccepted;
        try
        {
            lab.AddChild(source);
            for (int tick = 0; tick < ContactTimeoutTicks && accepted is null; tick++)
                await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
        }
        finally
        {
            lab.Pipeline.ImpactAccepted -= OnAccepted;
            foreach ((PuppetPartBody body, uint layer) in isolatedLayers)
            {
                if (GodotObject.IsInstanceValid(body))
                    body.CollisionLayer = layer;
            }

            if (GodotObject.IsInstanceValid(source))
                source.QueueFree();
        }

        return accepted;
    }

    private readonly record struct ProbeResult(
        AcceptedImpact? Impact,
        bool Started,
        SwingHitLagStarted StartedState,
        int FrozenFrames,
        bool Completed,
        bool ActiveAfterCompletion,
        int GloveAudioCount,
        int CriticalAudioCount);
}
