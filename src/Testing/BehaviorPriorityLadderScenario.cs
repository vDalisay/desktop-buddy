using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using DesktopBuddy.App;
using DesktopBuddy.Buddy.Behavior;
using DesktopBuddy.Buddy.Physics;
using DesktopBuddy.Domain.Autonomy;
using DesktopBuddy.Domain.Buddy;
using DesktopBuddy.Domain.Mood;
using Godot;

namespace DesktopBuddy.Testing;

/// <summary>M4 Task 3 gate for the complete §4 ladder and runtime preemption seams.</summary>
public sealed class BehaviorPriorityLadderScenario : IScenario
{
    public string Id => "behavior_priority_ladder";

    public async Task<ScenarioResult> RunAsync(SceneTree tree, ulong seed)
    {
        var checks = new List<StartupCheck>();
        var messages = new List<string> { $"seed={seed}" };
        BuddyLab? lab = await M4ObjectScenarioSupport.LoadLab(tree, seed);
        if (lab is null)
        {
            checks.Add(new StartupCheck("arbiter_lab_loadable", false, "buddy_lab"));
            return new ScenarioResult(false, checks, messages);
        }

        bool fullDomainLadder = VerifyDomainLadder(out string ladderDetail);
        checks.Add(new StartupCheck(
            "behavior_priority_full_ladder",
            fullDomainLadder,
            ladderDetail));
        checks.Add(BenchmarkArbiterTick());

        lab.Buddy.Arbiter.Reset();
        lab.Buddy.PhysicsTick();
        bool ambient = lab.Buddy.Arbiter.Diagnostics.Owner == BehaviorPriority.Ambient;

        lab.Progress.ApplyCareMood(-100.0f);
        Vector2 nearCursor = lab.Buddy.Rig.Torso.GlobalPosition + new Vector2(40.0f, 0.0f);
        lab.Buddy.Arbiter.Reset();
        lab.Buddy.PhysicsTick(cursorWorldPosition: nearCursor, socialTargetValid: true);
        bool social = lab.Buddy.Arbiter.Diagnostics.Owner == BehaviorPriority.Social;

        lab.Progress.ApplyCareMood(130.0f);
        lab.Buddy.Arbiter.Reset();
        _ = M4ObjectScenarioSupport.SpawnCatchCandidate(lab);
        bool objectAction = await M4ObjectScenarioSupport.WaitFor(
            tree,
            () => lab.Buddy.Arbiter.Diagnostics.Owner == BehaviorPriority.ObjectAction,
            240);

        lab.Buddy.ObjectInteraction.CancelActiveInteraction();
        lab.Progress.ApplyCareMood(-130.0f);
        lab.Reactions.PhysicsTick();
        bool grabbed = lab.Grab.TryGrab(
            lab.Buddy.Rig.Torso,
            lab.Buddy.Rig.Torso.GlobalPosition);
        Vector2 anchor = lab.Buddy.Rig.Torso.GlobalPosition + new Vector2(-24.0f, 0.0f);
        lab.Grab.MoveCursor(anchor);
        lab.Buddy.GrabResistance.SetGrabContext(grabbed, anchor);
        lab.Buddy.Arbiter.Reset();
        lab.Buddy.PhysicsTick(
            BuddyPartId.Torso,
            anchor);
        bool resistance =
            lab.Buddy.Arbiter.Diagnostics.Owner == BehaviorPriority.GrabResistance &&
            lab.Buddy.CurrentDriveIntent.PanicLeftHandActive &&
            lab.Buddy.CurrentDriveIntent.PanicRightHandActive;
        if (lab.Grab.IsGrabbing)
            lab.Grab.Release(countsAsThrow: false);
        lab.Buddy.GrabResistance.SetGrabContext(false, Vector2.Zero);

        var hazardIntent = new ToolReactionIntent(
            Active: true,
            WalkDirection: -1.0f,
            LocomotionScale: 0.75f,
            JumpRequested: false,
            JumpDirection: 0.0f,
            JumpScale: 1.0f,
            JumpHorizontalRatio: 0.0f,
            GuardActive: true,
            LeftGuardTarget: lab.Buddy.Rig.LeftHand.GlobalPosition,
            RightGuardTarget: lab.Buddy.Rig.RightHand.GlobalPosition,
            GuardStiffness: 1_000.0f,
            GuardDamping: 50.0f,
            GuardMaximumForce: 10_000.0f,
            GuardAbsorption: 0.5f);
        lab.Buddy.SetToolReactionIntent(hazardIntent);
        lab.Buddy.Arbiter.Reset();
        lab.Buddy.PhysicsTick();
        bool hazard =
            lab.Buddy.Arbiter.Diagnostics.Owner == BehaviorPriority.Hazard &&
            lab.Buddy.CurrentDriveIntent.GuardActive;
        lab.Buddy.SetToolReactionIntent(default);

        lab.Buddy.SetConsciousness(Consciousness.Unconscious);
        lab.Buddy.Arbiter.Reset();
        lab.Buddy.PhysicsTick();
        bool unconscious =
            lab.Buddy.Arbiter.Diagnostics.Owner == BehaviorPriority.Unconscious &&
            !lab.Buddy.ActiveDrive.ActiveOutputsEnabled;
        lab.Buddy.SetConsciousness(Consciousness.Conscious);

        lab.Buddy.Rig.Torso.GlobalPosition =
            new Vector2(lab.Buddy.Recovery.SafeBounds.End.X + 100.0f, 100.0f);
        lab.Buddy.Arbiter.Reset();
        lab.Buddy.PhysicsTick();
        bool failsafe =
            lab.Buddy.Arbiter.Diagnostics.Owner == BehaviorPriority.Failsafe &&
            lab.Buddy.Recovery.LastHardRecoveryReason is not null;

        checks.Add(new StartupCheck(
            "runtime_arbiter_routes_layers",
            ambient && social && objectAction && resistance && hazard && unconscious && failsafe,
            $"p7={ambient} p6={social} p5={objectAction} p4={resistance} " +
            $"p3={hazard} p1={unconscious} p0={failsafe}"));
        checks.Add(new StartupCheck(
            "higher_priority_preempts_same_tick",
            social && hazard && unconscious && failsafe,
            $"social={social} hazard={hazard} unconscious={unconscious} failsafe={failsafe}"));

        await M4ObjectScenarioSupport.Cleanup(tree, lab);
        bool passed = true;
        foreach (StartupCheck check in checks) passed &= check.Passed;
        return new ScenarioResult(passed, checks, messages);
    }

    private static bool VerifyDomainLadder(out string detail)
    {
        var model = new BehaviorArbiterModel(new BehaviorArbiterTuning(600, 35));
        var traits = BuddyTraits.Default;
        BehaviorPriority[] priorities =
        [
            BehaviorPriority.Ambient,
            BehaviorPriority.Social,
            BehaviorPriority.ObjectAction,
            BehaviorPriority.GrabResistance,
            BehaviorPriority.Hazard,
            BehaviorPriority.SelfRighting,
            BehaviorPriority.Unconscious,
            BehaviorPriority.Failsafe,
        ];

        bool passed = true;
        for (int index = 0; index < priorities.Length; index++)
        {
            BehaviorPriority expected = priorities[index];
            ActuationIntent intent = model.Resolve(SnapshotFor(expected, index), traits);
            passed &= intent.Owner == expected;
        }
        detail = $"resolved={model.Owner} expected_sequence={string.Join(",", priorities)}";
        return passed;
    }

    private static StartupCheck BenchmarkArbiterTick()
    {
        const int iterations = 100_000;
        const double physicsBudgetMilliseconds = 1000.0 / 120.0;
        var model = new BehaviorArbiterModel();
        var traits = BuddyTraits.Default;
        BehaviorSnapshot snapshot = SnapshotFor(BehaviorPriority.Ambient, 0);
        _ = model.Resolve(snapshot, traits);

        long started = Stopwatch.GetTimestamp();
        for (int tick = 0; tick < iterations; tick++)
        {
            snapshot = snapshot with { Tick = tick };
            _ = model.Resolve(snapshot, traits);
        }
        long ended = Stopwatch.GetTimestamp();
        double averageMilliseconds =
            (ended - started) * 1000.0 / Stopwatch.Frequency / iterations;
        return new StartupCheck(
            "arbiter_tick_inside_120hz_step_budget",
            averageMilliseconds < physicsBudgetMilliseconds,
            $"average_ms={averageMilliseconds:F6} budget_ms={physicsBudgetMilliseconds:F3}");
    }

    private static BehaviorSnapshot SnapshotFor(BehaviorPriority priority, int tick)
    {
        var snapshot = new BehaviorSnapshot(
            tick,
            Consciousness.Conscious,
            false,
            false,
            false,
            0.0f,
            false,
            false,
            0.0f,
            true,
            false,
            false,
            MoodBand.Neutral,
            false,
            0.0f,
            false,
            1.0f,
            40.0f,
            true,
            1.0f,
            1.0f,
            false);

        return priority switch
        {
            BehaviorPriority.Failsafe => snapshot with { RequiresFailsafeReposition = true },
            BehaviorPriority.Unconscious => snapshot with { Consciousness = Consciousness.Unconscious },
            BehaviorPriority.SelfRighting => snapshot with { SelfRightingEligible = true },
            BehaviorPriority.Hazard => snapshot with { HazardPresent = true, HazardFleeDirection = -1.0f },
            BehaviorPriority.GrabResistance => snapshot with
            {
                Grabbed = true,
                AfraidOfGrab = true,
                GrabFleeDirection = -1.0f,
            },
            BehaviorPriority.ObjectAction => snapshot with
            {
                ObjectActionCommitted = true,
                ObjectApproachDirection = 1.0f,
            },
            BehaviorPriority.Social => snapshot with { SocialReactionPresent = true },
            _ => snapshot,
        };
    }
}
