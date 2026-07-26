using System.Collections.Generic;
using System.Threading.Tasks;
using DesktopBuddy.App;
using DesktopBuddy.Domain.Autonomy;
using DesktopBuddy.Domain.Damage;
using DesktopBuddy.Domain.Economy;
using DesktopBuddy.Objects;
using Godot;

namespace DesktopBuddy.Testing;

/// <summary>M4 Task 2 gate for one-shot toss, low-energy discard, and collision cleanup.</summary>
public sealed class ObjectTossDiscardScenario : IScenario
{
    public string Id => "object_toss_discard";

    public async Task<ScenarioResult> RunAsync(SceneTree tree, ulong seed)
    {
        var checks = new List<StartupCheck>();
        var messages = new List<string> { $"seed={seed}" };

        (bool tossPassed, string tossDetail) = await RunToss(tree, seed);
        checks.Add(new StartupCheck("object_toss_one_shot", tossPassed, tossDetail));

        (bool discardPassed, string discardDetail) = await RunDiscard(tree, seed);
        checks.Add(new StartupCheck("harmful_object_discard", discardPassed, discardDetail));

        messages.Add(tossDetail);
        messages.Add(discardDetail);
        return new ScenarioResult(tossPassed && discardPassed, checks, messages);
    }

    private static async Task<(bool, string)> RunToss(SceneTree tree, ulong seed)
    {
        BuddyLab? lab = await M4ObjectScenarioSupport.LoadLab(tree, seed);
        if (lab is null) return (false, "toss lab failed to load");
        lab.Progress.ApplyCareMood(30.0f);
        LooseObjectBody? body = M4ObjectScenarioSupport.SpawnCatchCandidate(lab);
        bool held = await M4ObjectScenarioSupport.WaitForPhase(tree, lab, ObjectPhase.Hold, 240);
        bool tossed = await M4ObjectScenarioSupport.WaitFor(
            tree, () => lab.Buddy.ObjectInteraction.TossCount == 1, 420);

        Vector2 impulse = lab.Buddy.ObjectInteraction.LastReleaseImpulse;
        // The collision exception is held a little past the release on purpose, so a thrown
        // ball does not immediately collide with the hand that just threw it. It must clear
        // once the gesture ends, so assert that it does rather than reading a single frame.
        bool exceptionsCleared = await M4ObjectScenarioSupport.WaitFor(
            tree, () => !lab.Buddy.ObjectInteraction.CollisionExceptionsActive, 240);
        bool released = body is not null &&
            lab.Objects.TryGetSnapshot(body.RuntimeId, out LooseObjectSnapshot snapshot) &&
            !snapshot.BuddyHeld && snapshot.ThrowToken == 0 &&
            exceptionsCleared;
        int driveCount = lab.Buddy.ActiveDrive.ObjectTossCount;
        // The return throw goes *toward* the player, reversing the earlier away-from-cursor
        // policy (owner instruction 2026-07-26). Discard keeps the away release.
        float towardCursor = Mathf.Sign(
            lab.Pointer.WorldCursor.X - lab.Buddy.Rig.Torso.GlobalPosition.X);
        bool aimedAtCursor = Mathf.IsZeroApprox(towardCursor) ||
            Mathf.Sign(impulse.X) == towardCursor;
        bool passed = held && tossed && released && impulse.Length() > 0.0f &&
            driveCount == 1 && aimedAtCursor;
        string detail = $"toss held={held} tossed={tossed} released={released} " +
            $"impulse={impulse.Length():F1} drive_count={driveCount} aimed_at_cursor={aimedAtCursor} " +
            $"impulse_x={impulse.X:F1} toward={towardCursor:F0} " +
            $"phase={lab.Buddy.ObjectInteraction.Phase} drops={lab.Buddy.ObjectInteraction.DropCount} " +
            $"attached={lab.Buddy.ObjectInteraction.IsAttached} mood={lab.Progress.Mood:F1}";
        await M4ObjectScenarioSupport.Cleanup(tree, lab);
        return (passed, detail);
    }

    private static async Task<(bool, string)> RunDiscard(SceneTree tree, ulong seed)
    {
        BuddyLab? lab = await M4ObjectScenarioSupport.LoadLab(tree, seed);
        if (lab is null) return (false, "discard lab failed to load");
        lab.Progress.ApplyCareMood(30.0f);
        LooseObjectBody? body = M4ObjectScenarioSupport.SpawnCatchCandidate(lab);
        bool held = await M4ObjectScenarioSupport.WaitForPhase(tree, lab, ObjectPhase.Hold, 240);
        if (held)
        {
            lab.Economy.AcceptDamage(
                lab.SafeObjectProfile.ContentId,
                1.0f,
                PayoutRegion.Arms,
                DamageConsciousness.Conscious,
                lab.Pipeline.NowSeconds);
        }

        bool discarded = await M4ObjectScenarioSupport.WaitFor(
            tree, () => lab.Buddy.ObjectInteraction.DiscardCount == 1, 30);
        Vector2 impulse = lab.Buddy.ObjectInteraction.LastReleaseImpulse;
        bool lowEnergy = impulse.Length() > 0.0f &&
            impulse.Length() < lab.Buddy.ObjectInteraction.Profile.TossImpulse;
        // Flee bias is a single-tick request, so latch it before advancing any further.
        bool fleeBias = lab.Buddy.ObjectInteraction.FleeBiasRequested;
        bool exceptionsCleared = await M4ObjectScenarioSupport.WaitFor(
            tree, () => !lab.Buddy.ObjectInteraction.CollisionExceptionsActive, 240);
        bool released = body is not null &&
            lab.Objects.TryGetSnapshot(body.RuntimeId, out LooseObjectSnapshot snapshot) &&
            !snapshot.BuddyHeld &&
            exceptionsCleared;
        bool passed = held && discarded && lowEnergy && released &&
            fleeBias &&
            lab.Buddy.ActiveDrive.ObjectDiscardCount == 1;
        string detail = $"discard held={held} discarded={discarded} released={released} " +
            $"low_energy={lowEnergy} flee_bias={fleeBias} " +
            $"impulse={impulse.Length():F1} drive_count={lab.Buddy.ActiveDrive.ObjectDiscardCount}";
        await M4ObjectScenarioSupport.Cleanup(tree, lab);
        return (passed, detail);
    }
}
