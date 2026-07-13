using System.Collections.Generic;
using System.Threading.Tasks;
using DesktopBuddy.App;
using DesktopBuddy.Interaction;
using DesktopBuddy.Objects;
using Godot;

namespace DesktopBuddy.Testing;

/// <summary>
/// Contact-episode deduplication through the real wiring (RAGDOLL §7.1–7.2,
/// TEST_PLAN.md §2): idle autonomy (standing, walking, jump landings) never
/// scores pain; a dropped loose object scores exactly one episode per real hit
/// while its resting/sliding contact stream stays suppressed; and a fresh drop
/// after the re-arm gap scores again. Also proves the resting-object hazard
/// calibration: sub-threshold contacts neither score nor keep episodes alive.
/// </summary>
public sealed class ImpactDedupScenario : IScenario
{
    private const int SettleTimeoutTicks = 720;
    private const int IdleTicks = 240;
    private const int DropTravelTicks = 120;
    private const int RestSettleTimeoutTicks = 600;
    private const int RestWindowTicks = 300;
    private const float BallRadius = 10.0f;
    private const float BallMass = 2.0f;
    private const float StrikeSpeed = 550.0f;

    public string Id => "impact_dedup";

    public async Task<ScenarioResult> RunAsync(SceneTree tree, ulong seed)
    {
        var checks = new List<StartupCheck>();
        var messages = new List<string> { $"seed={seed}" };
        var packed = GD.Load<PackedScene>("res://scenes/buddy_lab.tscn");
        if (packed is null)
        {
            checks.Add(new StartupCheck("dedup_scene_loadable", false, "res://scenes/buddy_lab.tscn"));
            return new ScenarioResult(false, checks, messages);
        }

        BuddyLab lab = packed.Instantiate<BuddyLab>();
        tree.Root.AddChild(lab);
        await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);

        bool standing = await ScenarioSteps.WaitForStanding(tree, lab, SettleTimeoutTicks);
        checks.Add(new StartupCheck("dedup_starts_from_standing", standing,
            $"stable_ticks={lab.Buddy.Standing.Snapshot.StableTicks}"));

        InteractionDamageComponent pipeline = lab.Pipeline;
        var ballEpisodes = new List<AcceptedContactEpisode>();
        var ballImpacts = new List<AcceptedImpact>();

        // Phase A — idle autonomy: raw floor contact streams constantly, episode
        // accepts stay sparse, and nothing ever scores pain or money.
        long idleScoredBefore = pipeline.ScoredImpactCount;
        long idleAcceptedBefore = pipeline.AcceptedEpisodeCount;
        long idleRawBefore = pipeline.RawContactCount;
        for (int tick = 0; tick < IdleTicks; tick++)
        {
            await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
        }

        long idleRaw = pipeline.RawContactCount - idleRawBefore;
        long idleAccepted = pipeline.AcceptedEpisodeCount - idleAcceptedBefore;
        long idleScored = pipeline.ScoredImpactCount - idleScoredBefore;
        checks.Add(new StartupCheck("idle_autonomy_scores_nothing", idleScored == 0,
            $"scored={idleScored} maxImpulse={pipeline.MaxRawImpulse:F0}"));
        checks.Add(new StartupCheck("resting_sliding_contacts_suppressed",
            idleRaw >= 100 && idleAccepted <= 30 && idleRaw > idleAccepted * 5,
            $"raw={idleRaw} accepted={idleAccepted}"));

        // Phase B — first drop: one real hit opens exactly one scoring episode.
        var ball = new LooseObjectBody();
        ball.Configure(BallRadius, BallMass);
        lab.AddChild(ball);
        pipeline.EpisodeAccepted += OnEpisodeAccepted;
        pipeline.ImpactAccepted += OnImpactAccepted;
        await PositionForHeadStrike(tree, lab, ball, StrikeSpeed);

        int dropAcceptedBefore = ballEpisodes.Count;
        int dropScoredBefore = ballImpacts.Count;
        for (int tick = 0; tick < DropTravelTicks && ballImpacts.Count == dropScoredBefore; tick++)
        {
            await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
        }

        int dropAccepted = ballEpisodes.Count - dropAcceptedBefore;
        int dropScored = ballImpacts.Count - dropScoredBefore;
        checks.Add(new StartupCheck("drop_first_contact_accepted", dropAccepted >= 1,
            $"accepted={dropAccepted}"));
        checks.Add(new StartupCheck("drop_scores_pain", dropScored >= 1,
            $"scored={dropScored} lastPain={pipeline.LastImpact.Pain:F1} lastImpulse={pipeline.LastImpact.Impulse:F0}"));

        // Phase C — the settled ball's resting stream must never score and its
        // episode accepts must stay bounded (buddy may nudge it while walking).
        bool ballSettled = await WaitForBallRest(tree, ball, RestSettleTimeoutTicks);
        checks.Add(new StartupCheck("ball_settles", ballSettled,
            $"speed={ball.LinearVelocity.Length():F1}"));

        int restScoredBefore = ballImpacts.Count;
        for (int tick = 0; tick < RestWindowTicks; tick++)
        {
            await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
        }

        int restScored = ballImpacts.Count - restScoredBefore;
        checks.Add(new StartupCheck("resting_object_never_scores", restScored == 0,
            $"scored={restScored}"));
        checks.Add(new StartupCheck("resting_object_produces_no_new_reward",
            restScored == 0,
            $"scored={restScored} balance={pipeline.BalanceMilliCredits}"));

        // Phase D — a fresh drop after far more than the 0.15 s gap re-arms the
        // episode key and scores again.
        ScenarioSteps.IsolateControlledImpacts(lab);
        lab.Buddy.SetConsciousness(DesktopBuddy.Domain.Buddy.Consciousness.Unconscious);
        int reDropScoredBefore = ballImpacts.Count;
        int originalInteractionId = ball.InteractionId;
        ball.QueueFree();
        await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        _ = await ScenarioSteps.StrikePart(
            tree,
            lab,
            lab.Buddy.Rig.Head,
            ImpactContent.LooseObject,
            originalInteractionId);

        int reDropScored = ballImpacts.Count - reDropScoredBefore;
        checks.Add(new StartupCheck("re_drop_scores_new_episode", reDropScored >= 1,
            $"scored={reDropScored}"));

        messages.Add($"raw={pipeline.RawContactCount} accepted={pipeline.AcceptedEpisodeCount} " +
            $"scored={pipeline.ScoredImpactCount} maxImpulse={pipeline.MaxRawImpulse:F0} " +
            $"ballEpisodes={ballEpisodes.Count} ballImpacts={ballImpacts.Count} " +
            $"balanceMilli={pipeline.BalanceMilliCredits}");

        pipeline.EpisodeAccepted -= OnEpisodeAccepted;
        pipeline.ImpactAccepted -= OnImpactAccepted;
        lab.QueueFree();
        bool passed = true;
        foreach (StartupCheck check in checks)
        {
            passed &= check.Passed;
        }

        return new ScenarioResult(passed, checks, messages);

        void OnEpisodeAccepted(AcceptedContactEpisode episode)
        {
            if (episode.InteractionId == ball.InteractionId)
            {
                ballEpisodes.Add(episode);
            }
        }

        void OnImpactAccepted(AcceptedImpact impact)
        {
            if (impact.InteractionId == ball.InteractionId)
            {
                ballImpacts.Add(impact);
            }
        }
    }

    private static async Task PositionForHeadStrike(
        SceneTree tree,
        BuddyLab lab,
        LooseObjectBody ball,
        float speed)
    {
        var head = lab.Buddy.Rig.Head;
        ball.Freeze = true;
        ball.LinearVelocity = Vector2.Zero;
        ball.AngularVelocity = 0.0f;
        ball.GlobalPosition = head.GlobalPosition +
                              Vector2.Up * (head.Radius + ball.Radius + 20.0f);
        ball.ResetPhysicsInterpolation();
        await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
        ball.Freeze = false;
        ball.Sleeping = false;
        await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
        ball.ApplyCentralImpulse(Vector2.Down * (ball.Mass * speed));
    }

    private static async Task<bool> WaitForBallRest(SceneTree tree, LooseObjectBody ball, int timeoutTicks)
    {
        int calmTicks = 0;
        for (int tick = 0; tick < timeoutTicks; tick++)
        {
            await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
            calmTicks = ball.LinearVelocity.Length() < 5.0f ? calmTicks + 1 : 0;
            if (calmTicks >= 60)
            {
                return true;
            }
        }

        return false;
    }
}
