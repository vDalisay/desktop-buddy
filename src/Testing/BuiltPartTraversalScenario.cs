using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using DesktopBuddy.App;
using DesktopBuddy.Buddy.Physics;
using DesktopBuddy.Domain.Autonomy;
using DesktopBuddy.Domain.Environment;
using DesktopBuddy.Domain.Sandbox;
using DesktopBuddy.Sandbox;
using Godot;

namespace DesktopBuddy.Testing;

/// <summary>
/// NF-3T: a Buddy whose hop trait is zero — one of the third who never hop for fun — must treat
/// built Wood Beams as ground rather than a wall (owner report 2026-09-10): it crosses a stack, and
/// it climbs a stepped platform and walks along the top. The parts are real: dynamic bodies on the
/// loose-object layer, settled under gravity. The lab room is narrow and its Buddy stands
/// mid-room, so each build goes on the roomier side and each phase gets a fresh lab.
/// </summary>
public sealed class BuiltPartTraversalScenario : IScenario
{
    private const float BeamWidth = 96.0f;
    private const float BeamHeight = 16.0f;
    private const int WalkTimeoutTicks = 4800;
    // Looser than the room-interest walk's own 28 px arrival, which stops him short of the point.
    private const float ArrivalTolerance = 32.0f;

    public string Id => "built_part_traversal";

    public async Task<ScenarioResult> RunAsync(SceneTree tree, ulong seed)
    {
        var checks = new List<StartupCheck>();
        var messages = new List<string> { $"seed={seed}" };

        // Phase 1: two beams stacked flat, a 32 px step 96 px across, with room beyond it.
        BuddyLab? lab = await M4ObjectScenarioSupport.LoadLab(tree, seed);
        if (lab is null)
        {
            checks.Add(new StartupCheck("built_traversal_lab_loadable", false, "buddy_lab"));
            return new ScenarioResult(false, checks, messages);
        }
        {
            Layout room = Measure(lab);
            float pileCentre = room.TorsoX + room.Direction * (40.0f + BeamWidth * 0.5f);
            SpawnBeam(lab, room.Bounds, new Vector2(pileCentre, room.FloorY - BeamHeight * 0.5f));
            SpawnBeam(lab, room.Bounds, new Vector2(pileCentre, room.FloorY - BeamHeight * 1.5f));
            float farEdge = pileCentre + room.Direction * (BeamWidth * 0.5f + 16.0f);
            float target = room.Clamp(farEdge + room.Direction * 60.0f);
            bool fits = room.Direction * (target - farEdge) > 0.0f;

            Walk walk = await WalkToward(tree, lab, room, target,
                () => room.Direction * (lab.Buddy.Rig.Torso.GlobalPosition.X - farEdge) > 0.0f);
            string detail = $"farEdge={farEdge:0} {walk}";
            checks.Add(new StartupCheck("built_pile_room_fits", fits, detail));
            checks.Add(new StartupCheck("built_part_reads_as_something_to_get_past", walk.SawBlocking, detail));
            checks.Add(new StartupCheck("zero_hop_trait_buddy_crosses_built_pile", walk.Arrived, detail));
            await M4ObjectScenarioSupport.Cleanup(tree, lab);
        }

        // Phase 2: a stepped platform to the wall — two beams side by side, a third on the far one.
        lab = await M4ObjectScenarioSupport.LoadLab(tree, seed);
        if (lab is null)
        {
            checks.Add(new StartupCheck("built_traversal_lab_loadable", false, "buddy_lab second load"));
            return new ScenarioResult(false, checks, messages);
        }
        {
            Layout room = Measure(lab);
            float nearCentre = room.TorsoX + room.Direction * (20.0f + BeamWidth * 0.5f);
            float farCentre = nearCentre + room.Direction * BeamWidth;
            SpawnBeam(lab, room.Bounds, new Vector2(nearCentre, room.FloorY - BeamHeight * 0.5f));
            SpawnBeam(lab, room.Bounds, new Vector2(farCentre, room.FloorY - BeamHeight * 0.5f));
            SpawnBeam(lab, room.Bounds, new Vector2(farCentre, room.FloorY - BeamHeight * 1.5f));
            // The middle of the top beam: any closer to the wall and the wall-comfort margin stops him.
            float target = farCentre;
            float topSurface = room.FloorY - BeamHeight * 2.0f;

            Walk walk = await WalkToward(tree, lab, room, target,
                () => Math.Abs(lab.Buddy.Rig.Torso.GlobalPosition.X - target) <= ArrivalTolerance &&
                      StandsOnPartAbove(lab, topSurface - 4.0f));
            string detail = $"target={target:0} top={topSurface:0} {walk} " +
                $"feetY={lab.Buddy.Rig.LeftFoot.GlobalPosition.Y:0}/{lab.Buddy.Rig.RightFoot.GlobalPosition.Y:0} " +
                $"onPart={StandsOnPartAbove(lab, float.PositiveInfinity)}";
            checks.Add(new StartupCheck("buddy_climbs_stepped_platform_to_top", walk.Arrived, detail));

            // Back along the top and down onto the lower step, without touching the floor.
            float lowerSurface = room.FloorY - BeamHeight;
            Walk back = await WalkToward(tree, lab, room, nearCentre,
                () => Math.Abs(lab.Buddy.Rig.Torso.GlobalPosition.X - nearCentre) <= ArrivalTolerance &&
                      StandsOnPartAbove(lab, lowerSurface - 4.0f));
            detail = $"target={nearCentre:0} lower={lowerSurface:0} {back}";
            checks.Add(new StartupCheck("buddy_walks_along_built_platform",
                walk.Arrived && back.Arrived && back.WalkedOnParts >= 40.0f, detail));
            await M4ObjectScenarioSupport.Cleanup(tree, lab);
        }

        bool passed = true;
        foreach (StartupCheck check in checks) passed &= check.Passed;
        return new ScenarioResult(passed, checks, messages);
    }

    private readonly record struct Layout(Rect2 Bounds, float TorsoX, float Direction, float FloorY)
    {
        public float Clamp(float x) => Mathf.Clamp(x, Bounds.Position.X + 30.0f, Bounds.End.X - 30.0f);
    }

    private readonly record struct Walk(
        bool Arrived, bool SawBlocking, int Hops, float Climbed, float WalkedOnParts, float EndX, int TurnArounds)
    {
        public override string ToString() =>
            $"arrived={Arrived} hops={Hops} climbed={Climbed:0.0} walkedOnParts={WalkedOnParts:0.0} " +
            $"end={EndX:0} turnarounds={TurnArounds}";
    }

    private static Layout Measure(BuddyLab lab)
    {
        Rect2 bounds = lab.Boundaries.InnerBounds;
        float torsoX = lab.Buddy.Rig.Torso.GlobalPosition.X;
        return new Layout(bounds, torsoX, torsoX <= bounds.GetCenter().X ? 1.0f : -1.0f, bounds.End.Y);
    }

    private static async Task<Walk> WalkToward(
        SceneTree tree, BuddyLab lab, Layout room, float target, Func<bool> arrived)
    {
        for (int tick = 0; tick < 60; tick++)
            await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);

        lab.Progress.SeedTraits(new BuddyTraits(0));
        float direction = Math.Sign(target - lab.Buddy.Rig.Torso.GlobalPosition.X);
        bool sawBlocking = false;
        bool done = false;
        int hops = 0;
        bool wasJumping = false;
        float highestFeet = room.FloorY;
        float walkedOnParts = 0.0f;
        float lastX = lab.Buddy.Rig.Torso.GlobalPosition.X;
        for (int tick = 0; tick < WalkTimeoutTicks && !done; tick++)
        {
            if (!lab.Buddy.AutonomousMotion.HasRoomInterest)
                lab.Buddy.AutonomousMotion.SuggestRoomInterest(new Vector2(target, room.FloorY - 80.0f), 600);
            await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);

            sawBlocking |= lab.Buddy.AutonomousMotion.BlockingObstacleInCommittedPath(direction);
            bool jumping = lab.Buddy.Arbiter.Intent.JumpRequested;
            if (jumping && !wasJumping)
                hops++;
            wasJumping = jumping;
            highestFeet = Math.Min(highestFeet, Math.Min(
                lab.Buddy.Rig.LeftFoot.GlobalPosition.Y, lab.Buddy.Rig.RightFoot.GlobalPosition.Y));
            // Walking on parts: a foot stands on a built part while the Buddy walks and moves on.
            float x = lab.Buddy.Rig.Torso.GlobalPosition.X;
            if (StandsOnPartAbove(lab, float.PositiveInfinity) &&
                lab.Buddy.AutonomousMotion.Intent.WalkDirection != 0.0f)
            {
                walkedOnParts += Math.Max(0.0f, direction * (x - lastX));
            }
            lastX = x;
            done = arrived();
        }

        return new Walk(done, sawBlocking, hops, room.FloorY - highestFeet, walkedOnParts,
            lab.Buddy.Rig.Torso.GlobalPosition.X, lab.Buddy.AutonomousMotion.ObstacleTurnAroundCount);
    }

    /// <summary>A foot has support from a built part and stands higher than <paramref name="maxY"/>.</summary>
    private static bool StandsOnPartAbove(BuddyLab lab, float maxY) =>
        StandsOnPart(lab.Buddy.Rig.LeftFoot, maxY) || StandsOnPart(lab.Buddy.Rig.RightFoot, maxY);

    private static bool StandsOnPart(PuppetPartBody foot, float maxY)
    {
        if (!foot.HasSupportContact || foot.GlobalPosition.Y > maxY)
            return false;
        foreach (Node2D body in foot.GetCollidingBodies())
        {
            if (body is SandboxPartBody)
                return true;
        }
        return false;
    }

    private static void SpawnBeam(BuddyLab lab, Rect2 bounds, Vector2 world)
    {
        SandboxPartCatalogue.TryGet(SandboxPartCatalogue.WoodBeam, out SandboxPartDefinition definition);
        var part = new PlacedSandboxPart(
            SandboxPartId.New(),
            SandboxPartCatalogue.WoodBeam,
            new CanonicalRoomPosition(
                Mathf.Clamp((world.X - bounds.Position.X) / bounds.Size.X, 0.0f, 1.0f),
                Mathf.Clamp((world.Y - bounds.Position.Y) / bounds.Size.Y, 0.0f, 1.0f)),
            0.0f,
            SandboxPartOverrides.None);
        var body = new SandboxPartBody { GlobalPosition = world };
        body.Configure(part, definition);
        lab.AddChild(body);
    }
}
