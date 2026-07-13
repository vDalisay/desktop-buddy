using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using DesktopBuddy.App;
using DesktopBuddy.Buddy.Physics;
using DesktopBuddy.Domain.Buddy;
using DesktopBuddy.Domain.Damage;
using DesktopBuddy.Domain.Economy;
using DesktopBuddy.Interaction;
using Godot;

namespace DesktopBuddy.Testing;

/// <summary>
/// Strikes one representative part per payout region through authoritative
/// contacts and checks the ledger formula against each semantic event.
/// </summary>
public sealed class PayoutByRegionScenario : IScenario
{
    public string Id => "payout_by_region";

    public async Task<ScenarioResult> RunAsync(SceneTree tree, ulong seed)
    {
        var checks = new List<StartupCheck>();
        var messages = new List<string> { $"seed={seed}" };
        var cases = new[]
        {
            (BuddyPart.Head, PayoutRegion.Head),
            (BuddyPart.Torso, PayoutRegion.Torso),
            (BuddyPart.LeftHand, PayoutRegion.Arms),
            (BuddyPart.LeftFoot, PayoutRegion.Legs),
        };

        foreach ((BuddyPart partId, PayoutRegion expectedRegion) in cases)
        {
            BuddyLab? lab = await ScenarioSteps.CreateControlledImpactLab(tree, maximumPain: 10.0f);
            if (lab is null)
            {
                checks.Add(new StartupCheck("payout_scene_loadable", false, "res://scenes/buddy_lab.tscn"));
                break;
            }

            var target = lab.Buddy.Rig.GetPart((BuddyPartId)(int)partId);
            AcceptedImpact? impact = await ScenarioSteps.StrikePart(tree, lab, target);
            long expectedMilli = impact is AcceptedImpact hit
                ? (long)Math.Round(
                    hit.Pain * PayoutMultipliers.Region(expectedRegion) * RewardLedger.MilliCreditsPerCredit,
                    MidpointRounding.AwayFromZero)
                : -1;
            bool passed = impact is AcceptedImpact accepted &&
                          accepted.Part == partId &&
                          accepted.Region == expectedRegion &&
                          accepted.ConsciousnessAtAcceptance == DamageConsciousness.Conscious &&
                          accepted.MilliCredits == expectedMilli &&
                          lab.Pipeline.BalanceMilliCredits == expectedMilli;
            checks.Add(new StartupCheck($"payout_{expectedRegion.ToString().ToLowerInvariant()}", passed,
                impact is AcceptedImpact value
                    ? $"part={value.Part} pain={value.Pain:F1} milli={value.MilliCredits} expected={expectedMilli}"
                    : "no accepted impact"));
            lab.QueueFree();
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        }

        return new ScenarioResult(AllPassed(checks), checks, messages);
    }

    private static bool AllPassed(IReadOnlyList<StartupCheck> checks)
    {
        if (checks.Count != 4) return false;
        foreach (StartupCheck check in checks)
        {
            if (!check.Passed) return false;
        }

        return true;
    }
}
