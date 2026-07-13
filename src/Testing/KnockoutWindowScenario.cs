using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using DesktopBuddy.App;
using DesktopBuddy.Domain.Buddy;
using DesktopBuddy.Domain.Damage;
using DesktopBuddy.Interaction;
using Godot;

namespace DesktopBuddy.Testing;

/// <summary>
/// Drives the fixed four-second knockout through real physical contacts and
/// asserts semantic consciousness, payout, and timer events (RAGDOLL §7.3).
/// </summary>
public sealed class KnockoutWindowScenario : IScenario
{
    private const int OneSecondTicks = 120;
    private const int WakeTimeoutTicks = 600;

    public string Id => "knockout_window";

    public async Task<ScenarioResult> RunAsync(SceneTree tree, ulong seed)
    {
        var checks = new List<StartupCheck>();
        var messages = new List<string> { $"seed={seed}" };
        BuddyLab? lab = await ScenarioSteps.CreateControlledImpactLab(tree, maximumPain: 100.0f);
        if (lab is null)
        {
            checks.Add(new StartupCheck("knockout_scene_loadable", false, "res://scenes/buddy_lab.tscn"));
            return new ScenarioResult(false, checks, messages);
        }

        int starts = 0;
        int ends = 0;
        double startedAt = -1.0;
        double endedAt = -1.0;
        lab.Pipeline.KnockoutStarted += OnStarted;
        lab.Pipeline.KnockoutEnded += OnEnded;

        AcceptedImpact? trigger = await ScenarioSteps.StrikePart(tree, lab, lab.Buddy.Rig.Head);
        checks.Add(new StartupCheck("threshold_hit_is_physical", trigger is not null,
            trigger is AcceptedImpact triggerHit ? $"impulse={triggerHit.Impulse:F1} pain={triggerHit.Pain:F1}" : "no accepted impact"));
        checks.Add(new StartupCheck("threshold_hit_triggers_once",
            trigger is { KnockoutTriggered: true, ConsciousnessAtAcceptance: DamageConsciousness.Conscious } &&
            starts == 1 && lab.Pipeline.KnockoutCount == 1,
            $"starts={starts} count={lab.Pipeline.KnockoutCount}"));
        checks.Add(new StartupCheck("buddy_drive_enters_unconscious",
            lab.Buddy.CurrentConsciousness == Consciousness.Unconscious,
            $"state={lab.Buddy.CurrentConsciousness}"));

        for (int tick = 0; tick < OneSecondTicks; tick++)
        {
            await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
        }

        AcceptedImpact? during = await ScenarioSteps.StrikePart(tree, lab, lab.Buddy.Rig.Torso);
        checks.Add(new StartupCheck("unconscious_hit_pays_half",
            during is { ConsciousnessAtAcceptance: DamageConsciousness.Unconscious, MilliCredits: 50_000 },
            during is AcceptedImpact duringHit ? $"milli={duringHit.MilliCredits} state={duringHit.ConsciousnessAtAcceptance}" : "no accepted impact"));
        checks.Add(new StartupCheck("unconscious_hit_does_not_retrigger",
            during is { KnockoutTriggered: false } && starts == 1 && lab.Pipeline.KnockoutCount == 1,
            $"starts={starts} count={lab.Pipeline.KnockoutCount}"));

        for (int tick = 0; tick < WakeTimeoutTicks && ends == 0; tick++)
        {
            await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
        }

        double duration = endedAt - startedAt;
        double tickSeconds = 1.0 / Engine.PhysicsTicksPerSecond;
        checks.Add(new StartupCheck("knockout_ends_once_at_four_seconds",
            ends == 1 && Math.Abs(duration - PainKnockoutModel.KnockoutSeconds) <= tickSeconds,
            $"ends={ends} duration={duration:F6}"));
        checks.Add(new StartupCheck("buddy_drive_wakes_naturally",
            lab.Buddy.CurrentConsciousness == Consciousness.Conscious,
            $"state={lab.Buddy.CurrentConsciousness}"));

        messages.Add($"started={startedAt:F6} ended={endedAt:F6} duration={duration:F6} balance={lab.Pipeline.BalanceMilliCredits}");
        lab.Pipeline.KnockoutStarted -= OnStarted;
        lab.Pipeline.KnockoutEnded -= OnEnded;
        lab.QueueFree();
        return new ScenarioResult(AllPassed(checks), checks, messages);

        void OnStarted(double time)
        {
            starts++;
            startedAt = time;
        }

        void OnEnded(double time)
        {
            ends++;
            endedAt = time;
        }
    }

    private static bool AllPassed(IReadOnlyList<StartupCheck> checks)
    {
        foreach (StartupCheck check in checks)
        {
            if (!check.Passed) return false;
        }

        return true;
    }
}
