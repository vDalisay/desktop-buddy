using System.Collections.Generic;
using System.Threading.Tasks;
using DesktopBuddy.App;
using DesktopBuddy.Domain.Autonomy;
using DesktopBuddy.Objects;
using Godot;

namespace DesktopBuddy.Testing;

/// <summary>
/// A ball resting hard against a wall must still be picked up (owner report, 2026-07-29).
/// The ambient wall-avoid margin stops the buddy roughly `40 px` from the wall, which left a
/// cornered ball permanently outside the scoop gate: the buddy committed, walked up, and then
/// stood next to it forever. A committed object approach may close all the way to the wall.
///
/// <para>Both corners are exercised, because the two directions go through different blocked
/// flags and the buddy's start position favours neither.</para>
/// </summary>
public sealed class CornerScoopScenario : IScenario
{
    public string Id => "corner_scoop";

    public async Task<ScenarioResult> RunAsync(SceneTree tree, ulong seed)
    {
        var checks = new List<StartupCheck>();
        var messages = new List<string> { $"seed={seed}" };

        foreach (float side in new[] { -1.0f, 1.0f })
        {
            string corner = side < 0.0f ? "left" : "right";
            (bool held, string detail) = await RunCorner(tree, seed, side);
            checks.Add(new StartupCheck($"ball_in_the_{corner}_corner_is_picked_up", held, detail));
            messages.Add($"{corner}: {detail}");
        }

        bool passed = true;
        foreach (StartupCheck check in checks) passed &= check.Passed;
        return new ScenarioResult(passed, checks, messages);
    }

    private static async Task<(bool, string)> RunCorner(SceneTree tree, ulong seed, float side)
    {
        BuddyLab? lab = await M4ObjectScenarioSupport.LoadLab(tree, seed);
        if (lab is null)
            return (false, "corner lab failed to load");

        Rect2 room = lab.Boundaries.InnerBounds;
        float radius = lab.SafeObjectProfile.Radius;
        // Hard into the corner: the ball's own collision is what stops it, not a margin the
        // test picked. This is the position the owner reported as unreachable.
        float spawnX = side < 0.0f ? room.Position.X + radius : room.End.X - radius;
        float floorY = room.End.Y - radius - 1.0f;
        LooseObjectBody? ball = lab.SpawnLooseObject(
            lab.SafeObjectProfile,
            new Vector2(spawnX, floorY));

        bool rested = ball is not null && await M4ObjectScenarioSupport.WaitFor(
            tree,
            () => lab.Objects.TryGetSnapshot(ball!.RuntimeId, out LooseObjectSnapshot s) && s.AtRest,
            600);

        bool held = false;
        float closest = float.MaxValue;
        ObjectPhase deepest = ObjectPhase.Idle;
        for (int tick = 0; tick < 2400 && !held; tick++)
        {
            await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
            held = lab.Buddy.ObjectInteraction.IsHolding && lab.Buddy.ObjectInteraction.IsAttached;
            if (GodotObject.IsInstanceValid(ball))
            {
                closest = Mathf.Min(
                    closest,
                    Mathf.Abs(ball!.GlobalPosition.X - lab.Buddy.Rig.Torso.GlobalPosition.X));
            }
            if (lab.Buddy.ObjectInteraction.Phase > deepest)
                deepest = lab.Buddy.ObjectInteraction.Phase;
        }

        string diagnostics =
            $"torso_x={lab.Buddy.Rig.Torso.GlobalPosition.X:F1} " +
            $"torso_r={lab.Buddy.Rig.Torso.Radius:F1} " +
            $"contact_l={lab.Buddy.AutonomousMotion.ContactLeft} " +
            $"contact_r={lab.Buddy.AutonomousMotion.ContactRight} " +
            $"blocked_l={lab.Buddy.AutonomousMotion.BlockedLeft} " +
            $"blocked_r={lab.Buddy.AutonomousMotion.BlockedRight} " +
            $"clear_l={lab.Buddy.AutonomousMotion.LeftWallClearance:F1} " +
            $"clear_r={lab.Buddy.AutonomousMotion.RightWallClearance:F1} " +
            $"owner={lab.Buddy.Arbiter.Intent.Owner} " +
            $"walk={lab.Buddy.Arbiter.Intent.WalkDirection:F2} " +
            $"approach={lab.Buddy.ObjectInteraction.ApproachDirection:F2} " +
            $"ball_r={lab.SafeObjectProfile.Radius:F1} " +
            $"room=[{room.Position.X:F1},{room.End.X:F1}]";

        string detail = $"rested={rested} held={held} closest_dx={closest:F1} {diagnostics} " +
            $"scoop_gate={lab.Buddy.ObjectInteraction.Profile.ScoopDistance:F0} " +
            $"deepest={deepest} phase={lab.Buddy.ObjectInteraction.Phase} " +
            $"ball_x={(GodotObject.IsInstanceValid(ball) ? ball!.GlobalPosition.X : float.NaN):F1} " +
            $"wall_x={(side < 0.0f ? room.Position.X : room.End.X):F1}";
        await M4ObjectScenarioSupport.Cleanup(tree, lab);
        return (rested && held, detail);
    }
}
