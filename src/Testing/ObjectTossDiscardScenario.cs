using System.Collections.Generic;
using System.Threading.Tasks;
using DesktopBuddy.App;
using DesktopBuddy.Domain.Autonomy;
using DesktopBuddy.Domain.Damage;
using DesktopBuddy.Domain.Economy;
using DesktopBuddy.Domain.Presentation;
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
        // Deliberately no mood boost: a fresh buddy sits at mood 0 (neutral), and the +30
        // boost here is exactly what hid the content-band-only toss gate — the buddy threw in
        // this scenario and never in the real app (owner report 2026-07-27).
        LooseObjectBody? body = M4ObjectScenarioSupport.SpawnCatchCandidate(lab);
        bool held = await M4ObjectScenarioSupport.WaitForPhase(tree, lab, ObjectPhase.Hold, 240);

        // Aim off to one side while the buddy holds it, so the throw is judged on a real
        // lateral shot rather than the near-vertical one a parked headless cursor produces.
        Vector2 cursorTarget = M4ObjectScenarioSupport.LateralCursorTarget(lab);
        await M4ObjectScenarioSupport.AimCursorAt(tree, lab, cursorTarget);

        // Holding turns the buddy toward the player (owner instruction 2026-07-27), which is
        // also the side it is about to throw toward.
        FacingSide wantedSide = cursorTarget.X > lab.Buddy.Rig.Torso.GlobalPosition.X
            ? FacingSide.Right
            : FacingSide.Left;
        bool facedCursor = await M4ObjectScenarioSupport.WaitFor(
            tree,
            () => lab.Buddy.ObjectInteraction.IsHolding &&
                lab.Facing.CommittedSide == wantedSide,
            180);

        bool tossed = await M4ObjectScenarioSupport.WaitFor(
            tree, () => lab.Buddy.ObjectInteraction.TossCount == 1, 420);

        // The ball must actually leave. Asserting only that a release was *recorded* is what
        // let a throw that dropped straight down pass every gate (owner report 2026-07-27).
        Vector2 releaseOrigin = lab.Buddy.ObjectInteraction.LastReleaseOrigin;
        Vector2 cursor = lab.Pointer.WorldCursor;
        float flightSpeed = 0.0f;
        float flightDistance = 0.0f;
        float apexRise = 0.0f;
        float closestToCursor = float.MaxValue;
        // Long enough to cover the whole solved flight (ThrowFlightSeconds at 120 Hz) plus
        // slack, so the landing itself is observed rather than just the launch.
        int flightTicks = Mathf.CeilToInt(
            lab.Buddy.ObjectInteraction.Profile.ThrowFlightSeconds * 120.0f) + 20;
        for (int tick = 0; tick < flightTicks && body is not null; tick++)
        {
            await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
            if (!GodotObject.IsInstanceValid(body))
                break;
            flightSpeed = Mathf.Max(flightSpeed, body.LinearVelocity.Length());
            // Straight-line displacement, not horizontal: the throw follows the cursor, which
            // may be well above the buddy, and a high arc is still a throw.
            flightDistance = Mathf.Max(
                flightDistance,
                body.GlobalPosition.DistanceTo(releaseOrigin));
            closestToCursor = Mathf.Min(closestToCursor, body.GlobalPosition.DistanceTo(cursor));
            // Screen axes: rising means a smaller Y than the release point.
            apexRise = Mathf.Max(apexRise, releaseOrigin.Y - body.GlobalPosition.Y);
        }
        bool flew = flightSpeed > 200.0f && flightDistance > 40.0f;
        // The throw is aimed at the cursor, so it must actually get there. Direction alone was
        // never enough of a gate — a flat launch that merely pointed the right way passed it.
        bool reachedCursor = closestToCursor < 32.0f;
        // A throw, not a slingshot: the ball must leave along a rising arc.
        bool arced = apexRise > 8.0f;

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
        // policy (owner instruction 2026-07-26). Aim is judged from the throwing hand, which is
        // where the release actually happens — it sits a whole arm plus a forward swing away
        // from the torso, so a torso-relative comparison flips sign near the buddy.
        float towardCursor = Mathf.Sign(
            lab.Pointer.WorldCursor.X - lab.Buddy.ObjectInteraction.LastReleaseOrigin.X);
        bool aimedAtCursor = Mathf.IsZeroApprox(towardCursor) ||
            Mathf.Sign(impulse.X) == towardCursor;
        bool passed = held && tossed && released && impulse.Length() > 0.0f &&
            driveCount == 1 && aimedAtCursor && flew && reachedCursor && arced && facedCursor;
        string detail = $"toss held={held} tossed={tossed} released={released} flew={flew} " +
            $"faced_cursor={facedCursor} side={lab.Facing.CommittedSide} " +
            $"reached_cursor={reachedCursor} closest={closestToCursor:F0} arced={arced} " +
            $"apex_rise={apexRise:F0} " +
            $"flight_speed={flightSpeed:F0} flight_travel={flightDistance:F0} " +
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
            impulse.Length() < lab.Buddy.ObjectInteraction.Profile.TossSpeed;
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
