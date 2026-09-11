using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using DesktopBuddy.App;
using DesktopBuddy.Domain.Autonomy;
using DesktopBuddy.Domain.Environment;
using DesktopBuddy.Domain.Sandbox;
using DesktopBuddy.Sandbox;
using Godot;

namespace DesktopBuddy.Testing;

/// <summary>
/// NF-3T: a Buddy whose hop trait is zero — one of the third who never hop for fun — must still
/// cross a pile of built Wood Beams rather than stand against it as a wall (owner report
/// 2026-09-10). The pile is real: dynamic parts on the loose-object layer, settled under gravity./// </summary>
public sealed class BuiltPartTraversalScenario : IScenario
{
    private const float BeamWidth = 96.0f;
    private const float BeamHeight = 16.0f;
    private const float PileNear = 40.0f;
    private const int CrossTimeoutTicks = 4800;

    public string Id => "built_part_traversal";

    public async Task<ScenarioResult> RunAsync(SceneTree tree, ulong seed)
    {
        var checks = new List<StartupCheck>();
        var messages = new List<string> { $"seed={seed}" };
        BuddyLab? lab = await M4ObjectScenarioSupport.LoadLab(tree, seed);
        if (lab is null)
        {
            checks.Add(new StartupCheck("built_traversal_lab_loadable", false, "buddy_lab"));
            return new ScenarioResult(false, checks, messages);
        }

        // The lab room is narrow (the Buddy stands mid-room), so the pile is two beams stacked
        // flat on one side: a 32 px step, as tall as a Buddy's shin, 96 px across.
        Rect2 bounds = lab.Boundaries.InnerBounds;
        float torsoX = lab.Buddy.Rig.Torso.GlobalPosition.X;
        float direction = torsoX <= bounds.GetCenter().X ? 1.0f : -1.0f;
        float floorY = bounds.End.Y;

        float pileCentre = torsoX + direction * (PileNear + BeamWidth * 0.5f);
        SpawnBeam(lab, bounds, new Vector2(pileCentre, floorY - BeamHeight * 0.5f));
        SpawnBeam(lab, bounds, new Vector2(pileCentre, floorY - BeamHeight * 1.5f));
        float farEdge = pileCentre + direction * (BeamWidth * 0.5f + 16.0f);
        float target = Mathf.Clamp(farEdge + direction * 60.0f, bounds.Position.X + 30.0f, bounds.End.X - 30.0f);
        bool fits = direction > 0.0f ? target > farEdge : target < farEdge;

        for (int tick = 0; tick < 60; tick++)
            await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);

        lab.Progress.SeedTraits(new BuddyTraits(0));
        bool sawBlocking = false;
        bool crossed = false;
        int hops = 0;
        bool wasJumping = false;
        float highestFeet = floorY;
        for (int tick = 0; tick < CrossTimeoutTicks && !crossed && fits; tick++)
        {
            if (!lab.Buddy.AutonomousMotion.HasRoomInterest)
                lab.Buddy.AutonomousMotion.SuggestRoomInterest(new Vector2(target, floorY - 80.0f), 600);
            await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);

            sawBlocking |= lab.Buddy.AutonomousMotion.BlockingObstacleInCommittedPath(direction);
            bool jumping = lab.Buddy.Arbiter.Intent.JumpRequested;
            if (jumping && !wasJumping)
                hops++;
            wasJumping = jumping;
            highestFeet = Math.Min(highestFeet, Math.Min(
                lab.Buddy.Rig.LeftFoot.GlobalPosition.Y, lab.Buddy.Rig.RightFoot.GlobalPosition.Y));
            crossed = direction * (lab.Buddy.Rig.Torso.GlobalPosition.X - farEdge) > 0.0f;
        }

        string detail = $"bounds={bounds} dir={direction} start={torsoX:0} farEdge={farEdge:0} target={target:0} " +
            $"end={lab.Buddy.Rig.Torso.GlobalPosition.X:0} hops={hops} " +
            $"climbed={floorY - highestFeet:0.0} turnarounds={lab.Buddy.AutonomousMotion.ObstacleTurnAroundCount}";
        checks.Add(new StartupCheck("built_pile_room_fits", fits, detail));
        checks.Add(new StartupCheck("built_part_reads_as_something_to_get_past", sawBlocking, detail));
        checks.Add(new StartupCheck("zero_hop_trait_buddy_crosses_built_pile", crossed, detail));

        await M4ObjectScenarioSupport.Cleanup(tree, lab);
        bool passed = true;
        foreach (StartupCheck check in checks) passed &= check.Passed;
        return new ScenarioResult(passed, checks, messages);
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
