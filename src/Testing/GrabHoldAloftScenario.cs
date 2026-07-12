using System.Collections.Generic;
using System.Threading.Tasks;
using DesktopBuddy.App;
using DesktopBuddy.Buddy.Physics;
using Godot;

namespace DesktopBuddy.Testing;

/// <summary>
/// Feel fixture (M1_FEEL_AND_GAIT_PLAN Task 5, target 1): grabbing a part and
/// raising the cursor lifts the whole buddy off the floor and holds it there,
/// and the tether is not force-saturated at steady state. This pins the grab
/// authority the owner's review required ("I should be able to hang him up in the
/// air but it feels too heavy") against regression.
/// </summary>
public sealed class GrabHoldAloftScenario : IScenario
{
    private const int SettleTimeoutTicks = 720;
    private const int HoldTicks = 240;
    private const float LiftClearance = 40.0f;

    public string Id => "grab_hold_aloft";

    public async Task<ScenarioResult> RunAsync(SceneTree tree, ulong seed)
    {
        var checks = new List<StartupCheck>();
        var messages = new List<string> { $"seed={seed}" };
        var packed = GD.Load<PackedScene>("res://scenes/buddy_lab.tscn");
        if (packed is null)
        {
            checks.Add(new StartupCheck("hold_aloft_scene_loadable", false, "res://scenes/buddy_lab.tscn"));
            return new ScenarioResult(false, checks, messages);
        }

        BuddyLab lab = packed.Instantiate<BuddyLab>();
        tree.Root.AddChild(lab);
        await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);

        bool standing = await WaitForStanding(tree, lab, SettleTimeoutTicks);
        checks.Add(new StartupCheck("hold_aloft_starts_from_standing", standing,
            $"stable_ticks={lab.Buddy.Standing.Snapshot.StableTicks}"));

        // Feet-on-floor reference before lifting (largest Y among parts).
        float floorReferenceY = LowestPartY(lab);

        // Grab the head and raise the cursor into the upper third of the room.
        PuppetPartBody head = lab.Buddy.Rig.Head;
        lab.Grab.TryGrab(head, head.GlobalPosition);
        var raised = new Vector2(head.GlobalPosition.X, 90.0f);
        lab.Grab.MoveCursor(raised);

        bool steadyClamped = false;
        for (int tick = 0; tick < HoldTicks; tick++)
        {
            await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
            lab.Grab.MoveCursor(raised);
            if (tick >= HoldTicks - 30)
            {
                steadyClamped |= lab.Grab.Telemetry.ForceClamped;
            }
        }

        float heldLowestY = LowestPartY(lab);
        bool finite = lab.Buddy.Rig.AllBodiesFinite();
        lab.Grab.Release();

        checks.Add(new StartupCheck("hold_aloft_lifts_whole_buddy_off_floor",
            finite && heldLowestY <= floorReferenceY - LiftClearance,
            $"floor_y={floorReferenceY:F1} held_lowest_y={heldLowestY:F1} clearance={floorReferenceY - heldLowestY:F1}"));
        checks.Add(new StartupCheck("hold_aloft_tether_not_saturated", !steadyClamped,
            $"steady_clamped={steadyClamped} force={lab.Grab.Telemetry.Force.Length():F0} max={lab.Grab.Profile.MaximumForce:F0}"));

        lab.QueueFree();
        bool passed = true;
        foreach (StartupCheck check in checks)
        {
            passed &= check.Passed;
        }

        return new ScenarioResult(passed, checks, messages);
    }

    private static float LowestPartY(BuddyLab lab)
    {
        float lowest = float.NegativeInfinity;
        foreach (PuppetPartBody body in lab.Buddy.Rig.Parts)
        {
            lowest = Mathf.Max(lowest, body.GlobalPosition.Y);
        }

        return lowest;
    }

    private static async Task<bool> WaitForStanding(SceneTree tree, BuddyLab lab, int timeoutTicks)
    {
        for (int tick = 0; tick < timeoutTicks; tick++)
        {
            await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
            if (lab.Buddy.Standing.Snapshot.IsStable)
            {
                return true;
            }
        }

        return false;
    }
}
