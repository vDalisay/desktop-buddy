using System.Collections.Generic;
using System.Threading.Tasks;
using DesktopBuddy.App;
using DesktopBuddy.Domain.Autonomy;
using DesktopBuddy.Objects;
using Godot;

namespace DesktopBuddy.Testing;

/// <summary>
/// Owner instruction 2026-07-27: the buddy laughs when it catches a thrown ball out of the
/// air, interest in catch fades with repetition, and a recharge timer brings it back.
/// </summary>
public sealed class FunCatchLaughScenario : IScenario
{
    /// <summary>A known taste so the interest arithmetic below is exact rather than assumed.</summary>
    private const int CatchDrain = 4;

    public string Id => "fun_catch_laugh";

    public async Task<ScenarioResult> RunAsync(SceneTree tree, ulong seed)
    {
        var checks = new List<StartupCheck>();
        var messages = new List<string> { $"seed={seed}" };

        BuddyLab? lab = await M4ObjectScenarioSupport.LoadLab(tree, seed);
        if (lab is null)
            return new ScenarioResult(false, checks, ["fun lab failed to load"]);

        lab.Progress.SeedTraits(new BuddyTraits(
            50, new FunPreferences(CatchDrain, 5, 5, 5)));

        // Catch phases first, ground detection last: the ground check puts a ball in the room
        // that the buddy is free to commit to, and a stolen catch there would spend the very
        // novelty the phases below are measuring.
        checks.Add(await CheckCleanCatchLaughs(tree, lab, messages));
        checks.Add(await CheckBoredBuddyDoesNotLaugh(tree, lab, messages));
        checks.Add(CheckRechargeRestoresInterest(lab, messages));
        checks.Add(await CheckGroundContactIsDetected(tree, lab));

        await M4ObjectScenarioSupport.Cleanup(tree, lab);
        bool passed = true;
        foreach (StartupCheck check in checks)
            passed &= check.Passed;
        return new ScenarioResult(passed, checks, messages);
    }

    /// <summary>
    /// The mechanism the whole feature rests on: a ball that reaches the floor is marked, and
    /// a freshly thrown one is not. Without this the "without touching the ground" clause is
    /// unenforceable and every catch would read as clean.
    /// </summary>
    private static async Task<StartupCheck> CheckGroundContactIsDetected(
        SceneTree tree, BuddyLab lab)
    {
        // Dropped at the far edge of the room, well outside the buddy's sense radius, so this
        // is a test of the floor detector and not an accidental second catch.
        Rect2 bounds = lab.Boundaries.InnerBounds;
        float buddyX = lab.Buddy.Rig.Torso.GlobalPosition.X;
        float farX = buddyX - bounds.Position.X > bounds.End.X - buddyX
            ? bounds.Position.X + lab.SafeObjectProfile.Radius + 4.0f
            : bounds.End.X - lab.SafeObjectProfile.Radius - 4.0f;
        LooseObjectBody? falling = lab.SpawnLooseObject(
            lab.SafeObjectProfile,
            new Vector2(farX, bounds.Position.Y + 20.0f),
            Vector2.Zero,
            playerThrown: true);
        if (falling is null)
            return new StartupCheck("ground_contact_detected", false, "spawn refused");

        bool airborneStartsClean =
            lab.Objects.TryGetSnapshot(falling.RuntimeId, out LooseObjectSnapshot spawned) &&
            !spawned.TouchedGroundSinceThrow;

        bool grounded = await M4ObjectScenarioSupport.WaitFor(
            tree,
            () => lab.Objects.TryGetSnapshot(falling.RuntimeId, out LooseObjectSnapshot live) &&
                live.TouchedGroundSinceThrow,
            900);

        RemoveObject(lab, falling);
        return new StartupCheck(
            "ground_contact_detected",
            airborneStartsClean && grounded,
            $"airborne_starts_clean={airborneStartsClean} grounded_after_fall={grounded}");
    }

    private static async Task<StartupCheck> CheckCleanCatchLaughs(
        SceneTree tree, BuddyLab lab, List<string> messages)
    {
        float interestBefore = lab.Progress.InterestIn(FunActivityId.Catch);
        int laughsBefore = lab.Reactions.LaughCount;

        LooseObjectBody? ball = M4ObjectScenarioSupport.SpawnCleanThrow(lab);

        // The laugh is a timed hold that starts on the catch tick, so it is latched across
        // the whole window rather than sampled at one instant — which tick the face lands on
        // depends on component ordering within the frame and is not what this asserts.
        bool held = false;
        bool laughing = false;
        bool laughFaceShown = false;
        for (int tick = 0; tick < 360; tick++)
        {
            await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
            held |= lab.Buddy.ObjectInteraction.Phase == ObjectPhase.Hold;
            laughing |= lab.Reactions.LaughTicksRemaining > 0;
            laughFaceShown |= lab.Reactions.CurrentFace == "^_^";
            if (held && laughing && laughFaceShown)
                break;
        }

        float interestAfter = lab.Progress.InterestIn(FunActivityId.Catch);

        bool cleanCounted = lab.Buddy.ObjectInteraction.CleanCatchCount == 1;
        bool funCounted = lab.Buddy.ObjectInteraction.FunCatchCount == 1;
        bool laughed = lab.Reactions.LaughCount == laughsBefore + 1;
        bool interestSpent = Mathf.IsEqualApprox(
            interestBefore - interestAfter, CatchDrain);

        string detail =
            $"held={held} care={lab.Buddy.ObjectInteraction.CatchCareCount} " +
            $"token={(ball is not null && lab.Objects.TryGetSnapshot(ball.RuntimeId, out LooseObjectSnapshot s) ? $"{s.ThrowToken}/grounded={s.TouchedGroundSinceThrow}" : "gone")} " +
            $"clean={lab.Buddy.ObjectInteraction.CleanCatchCount} " +
            $"fun={lab.Buddy.ObjectInteraction.FunCatchCount} " +
            $"laughs={lab.Reactions.LaughCount} laughing={laughing} " +
            $"laugh_face={laughFaceShown} " +
            $"interest={interestBefore:F0}->{interestAfter:F0} drain={CatchDrain}";
        messages.Add($"clean_catch {detail}");

        if (ball is not null)
            RemoveObject(lab, ball);
        return new StartupCheck(
            "clean_catch_makes_the_buddy_laugh",
            held && laughing && laughFaceShown && laughed && cleanCounted &&
                funCounted && interestSpent,
            detail);
    }

    /// <summary>
    /// The same throw, once the buddy has had enough of catch: still caught, still cleanly,
    /// but no longer funny. This is the fade the owner asked for.
    /// </summary>
    private static async Task<StartupCheck> CheckBoredBuddyDoesNotLaugh(
        SceneTree tree, BuddyLab lab, List<string> messages)
    {
        // Spend the meter through the real seam rather than reaching into the model.
        int spent = 0;
        while (lab.Progress.IsFun(FunActivityId.Catch) && spent < 200)
        {
            lab.Progress.EngageFun(FunActivityId.Catch);
            spent++;
        }

        int cleanBefore = lab.Buddy.ObjectInteraction.CleanCatchCount;
        int funBefore = lab.Buddy.ObjectInteraction.FunCatchCount;
        int laughsBefore = lab.Reactions.LaughCount;

        // Let the previous phase's cancelled hold unwind, and let its laugh finish, before
        // throwing again: the earlier laugh is a timed hold that would otherwise still be
        // counting down and would read as this catch having been fun.
        await M4ObjectScenarioSupport.WaitFor(
            tree,
            () => lab.Buddy.ObjectInteraction.Phase == ObjectPhase.Idle &&
                lab.Reactions.LaughTicksRemaining == 0,
            600);

        LooseObjectBody? ball = M4ObjectScenarioSupport.SpawnCleanThrow(lab);
        // Latched across the window: if boredom were broken, a laugh would fire somewhere in
        // here, and a single end-of-phase sample could miss it.
        bool held = false;
        bool everLaughed = false;
        for (int tick = 0; tick < 360 && !(held && everLaughed); tick++)
        {
            await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
            held |= lab.Buddy.ObjectInteraction.Phase == ObjectPhase.Hold;
            everLaughed |= lab.Reactions.LaughTicksRemaining > 0;
        }

        bool stillNotLaughing = !everLaughed;
        bool caughtAgain = lab.Buddy.ObjectInteraction.CleanCatchCount == cleanBefore + 1;
        bool noFun = lab.Buddy.ObjectInteraction.FunCatchCount == funBefore;
        bool noLaugh = lab.Reactions.LaughCount == laughsBefore;

        string detail =
            $"held={held} spent_engagements={spent} " +
            $"clean={cleanBefore}->{lab.Buddy.ObjectInteraction.CleanCatchCount} " +
            $"fun={funBefore}->{lab.Buddy.ObjectInteraction.FunCatchCount} " +
            $"laughs={laughsBefore}->{lab.Reactions.LaughCount} " +
            $"interest={lab.Progress.InterestIn(FunActivityId.Catch):F0} " +
            $"never_laughed={stillNotLaughing}";
        messages.Add($"bored {detail}");

        if (ball is not null)
            RemoveObject(lab, ball);
        return new StartupCheck(
            "a_bored_buddy_catches_without_laughing",
            held && caughtAgain && noFun && noLaugh && stillNotLaughing,
            detail);
    }

    /// <summary>Leaving the toy alone for a while is what makes it interesting again.</summary>
    private static StartupCheck CheckRechargeRestoresInterest(
        BuddyLab lab, List<string> messages)
    {
        bool boredFirst = !lab.Progress.IsFun(FunActivityId.Catch);

        // A sliver of recharge must not be enough. Without the comeback gate the meter would
        // tick back above zero immediately and boredom would last a single frame.
        lab.Progress.RechargeFun(2.0);
        bool stillBored = !lab.Progress.IsFun(FunActivityId.Catch) &&
            lab.Progress.InterestIn(FunActivityId.Catch) > 0.0f;

        lab.Progress.RechargeFun(60.0);
        bool funAgain = lab.Progress.IsFun(FunActivityId.Catch);

        string detail = $"bored_first={boredFirst} still_bored_while_recovering={stillBored} " +
            $"fun_after_a_minute={funAgain} " +
            $"interest={lab.Progress.InterestIn(FunActivityId.Catch):F0}";
        messages.Add($"recharge {detail}");
        return new StartupCheck(
            "interest_recharges_over_time",
            boredFirst && stillBored && funAgain,
            detail);
    }

    private static void RemoveObject(BuddyLab lab, LooseObjectBody body)
    {
        if (lab.Buddy.ObjectInteraction.IsHolding &&
            lab.Buddy.ObjectInteraction.TrackedRuntimeId == body.RuntimeId)
        {
            lab.Buddy.ObjectInteraction.CancelActiveInteraction();
        }
        if (lab.Grab.IsGrabbing)
            lab.Grab.Release(countsAsThrow: false);
        lab.Objects.Unregister(body);
        body.QueueFree();
    }
}
