using System.Collections.Generic;
using System.Threading.Tasks;
using DesktopBuddy.App;
using DesktopBuddy.Buddy.Physics;
using DesktopBuddy.Domain.Buddy;
using Godot;

namespace DesktopBuddy.Testing;

/// <summary>
/// Fear-resistance regression (RAGDOLL_AND_GAMEPLAY_SPEC.md Section 6,
/// Section 12.2): under an identical scripted pull, a fearful conscious buddy
/// produces measurable opposing intent/force and holds the tether farther from
/// the cursor than a calm buddy, while resistance never breaks the tether. An
/// unconscious buddy produces no resistance.
/// </summary>
public sealed class GrabResistanceScenario : IScenario
{
    private const int SettleTimeoutTicks = 720;
    private const int ReSettleTicks = 180;
    private const int HoldTicks = 240;

    public string Id => "grab_resistance";

    public async Task<ScenarioResult> RunAsync(SceneTree tree, ulong seed)
    {
        var checks = new List<StartupCheck>();
        var messages = new List<string> { $"seed={seed}" };
        var packed = GD.Load<PackedScene>("res://scenes/buddy_lab.tscn");
        if (packed is null)
        {
            checks.Add(new StartupCheck("grab_resistance_scene_loadable", false, "res://scenes/buddy_lab.tscn"));
            return new ScenarioResult(false, checks, messages);
        }

        BuddyLab lab = packed.Instantiate<BuddyLab>();
        tree.Root.AddChild(lab);
        await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);

        bool standing = await WaitForStanding(tree, lab, SettleTimeoutTicks);
        checks.Add(new StartupCheck("grab_resistance_starts_from_standing", standing,
            $"stable_ticks={lab.Buddy.Standing.Snapshot.StableTicks}"));

        HoldResult calm = await MeasureHold(tree, lab, fear: 0.0f);
        await ReSettle(tree, lab);
        HoldResult fearful = await MeasureHold(tree, lab, fear: 1.0f);
        await ReSettle(tree, lab);

        lab.Buddy.SetConsciousness(Consciousness.Unconscious);
        HoldResult unconscious = await MeasureHold(tree, lab, fear: 1.0f);
        lab.Buddy.SetConsciousness(Consciousness.Conscious);

        // Cursor is to the right of the torso, so resistance drives left (negative force).
        checks.Add(new StartupCheck("fearful_grab_produces_opposing_force",
            fearful.ResistanceActive && fearful.MinResistForceX < -1000.0f,
            $"active={fearful.ResistanceActive} minForceX={fearful.MinResistForceX:F0}"));
        checks.Add(new StartupCheck("calm_grab_produces_no_resistance",
            !calm.ResistanceActive && Mathf.IsZeroApprox(calm.MinResistForceX),
            $"active={calm.ResistanceActive} minForceX={calm.MinResistForceX:F0}"));
        checks.Add(new StartupCheck("fearful_resists_more_than_calm",
            fearful.FinalExtension > calm.FinalExtension + 5.0f,
            $"calm={calm.FinalExtension:F1} fearful={fearful.FinalExtension:F1}"));
        checks.Add(new StartupCheck("unconscious_grab_produces_no_resistance",
            !unconscious.ResistanceActive && Mathf.IsZeroApprox(unconscious.MinResistForceX),
            $"active={unconscious.ResistanceActive} minForceX={unconscious.MinResistForceX:F0}"));
        checks.Add(new StartupCheck("resistance_never_breaks_tether",
            !calm.Broke && !fearful.Broke && !unconscious.Broke,
            $"calm={calm.Broke} fearful={fearful.Broke} unconscious={unconscious.Broke}"));

        lab.QueueFree();
        bool passed = true;
        foreach (StartupCheck check in checks)
        {
            passed &= check.Passed;
        }

        return new ScenarioResult(passed, checks, messages);
    }

    private static async Task<HoldResult> MeasureHold(SceneTree tree, BuddyLab lab, float fear)
    {
        PuppetPartBody torso = lab.Buddy.Rig.Torso;
        Vector2 cursor = torso.GlobalPosition + new Vector2(70.0f, 0.0f);
        lab.Reactions.FearOverride = fear;
        lab.Grab.TryGrab(torso, torso.GlobalPosition);
        lab.Grab.MoveCursor(cursor);

        bool resistanceActive = false;
        bool broke = false;
        float minResistForceX = 0.0f;

        for (int tick = 0; tick < HoldTicks; tick++)
        {
            await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
            if (lab.Buddy.GrabResistance.Intent.Active)
            {
                resistanceActive = true;
            }

            minResistForceX = Mathf.Min(minResistForceX, lab.Buddy.ActiveDrive.LastResistanceForce.X);
            if (!lab.Grab.IsGrabbing)
            {
                broke = true;
            }
        }

        float finalExtension = lab.Grab.Telemetry.Extension;
        lab.Grab.Release();
        lab.Reactions.FearOverride = null;
        return new HoldResult(finalExtension, minResistForceX, resistanceActive, broke);
    }

    private static async Task ReSettle(SceneTree tree, BuddyLab lab)
    {
        for (int tick = 0; tick < ReSettleTicks; tick++)
        {
            await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
        }
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

    private readonly record struct HoldResult(
        float FinalExtension,
        float MinResistForceX,
        bool ResistanceActive,
        bool Broke);
}
