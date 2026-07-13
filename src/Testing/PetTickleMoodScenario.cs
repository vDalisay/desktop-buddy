using System.Collections.Generic;
using System.Threading.Tasks;
using DesktopBuddy.App;
using DesktopBuddy.Domain.Mood;
using DesktopBuddy.Domain.Tools;
using Godot;

namespace DesktopBuddy.Testing;

/// <summary>
/// Exercises Pet/Tickle valid-contact cadence through the production stroke
/// detector and asserts mood/currency semantic state rather than pixels.
/// </summary>
public sealed class PetTickleMoodScenario : IScenario
{
    private const int CadenceTicks = 360;

    public string Id => "pet_tickle_mood";

    public async Task<ScenarioResult> RunAsync(SceneTree tree, ulong seed)
    {
        var checks = new List<StartupCheck>();
        var messages = new List<string> { $"seed={seed}" };
        PackedScene? packed = GD.Load<PackedScene>("res://scenes/buddy_lab.tscn");
        if (packed is null)
        {
            checks.Add(new StartupCheck("care_scene_loadable", false, "res://scenes/buddy_lab.tscn"));
            return new ScenarioResult(false, checks, messages);
        }

        BuddyLab lab = packed.Instantiate<BuddyLab>();
        tree.Root.AddChild(lab);
        await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);

        lab.Pipeline.SelectTool(ToolId.Pet);
        await StrokeHeadValidTicks(tree, lab, CadenceTicks - 1);
        checks.Add(new StartupCheck("pet_waits_for_three_valid_seconds",
            lab.Pipeline.CareAwardCount == 0,
            $"awards={lab.Pipeline.CareAwardCount} progress={lab.Pipeline.CareProgressSeconds(CareKind.Pet):F6}"));
        await StrokeHeadValidTicks(tree, lab, 1);
        checks.Add(new StartupCheck("pet_awards_at_three_valid_seconds",
            lab.Pipeline.CareAwardCount == 1,
            $"awards={lab.Pipeline.CareAwardCount} mood={lab.Pipeline.Mood:F4}"));

        lab.CareStroke.SetStroke(true, new Vector2(-1000.0f, -1000.0f));
        long validBeforeEmpty = lab.CareStroke.ValidContactTicks;
        for (int tick = 0; tick < CadenceTicks; tick++)
        {
            await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
        }
        checks.Add(new StartupCheck("empty_space_hold_awards_nothing",
            lab.Pipeline.CareAwardCount == 1 && lab.CareStroke.ValidContactTicks == validBeforeEmpty,
            $"awards={lab.Pipeline.CareAwardCount} validTicks={lab.CareStroke.ValidContactTicks - validBeforeEmpty}"));

        lab.Pipeline.SelectTool(ToolId.Tickle);
        await StrokeHeadValidTicks(tree, lab, CadenceTicks);
        checks.Add(new StartupCheck("tickle_has_independent_cadence",
            lab.Pipeline.CareAwardCount == 2 &&
            lab.Pipeline.CareProgressSeconds(CareKind.Pet) < 1.0 / 120.0 &&
            lab.Pipeline.CareProgressSeconds(CareKind.Tickle) < 1.0 / 120.0,
            $"awards={lab.Pipeline.CareAwardCount} pet={lab.Pipeline.CareProgressSeconds(CareKind.Pet):F6} tickle={lab.Pipeline.CareProgressSeconds(CareKind.Tickle):F6}"));
        checks.Add(new StartupCheck("care_never_pays_money",
            lab.Pipeline.BalanceMilliCredits == 0,
            $"balance={lab.Pipeline.BalanceMilliCredits}"));

        lab.CareStroke.SetStroke(false, Vector2.Zero);
        messages.Add($"mood={lab.Pipeline.Mood:F4} awards={lab.Pipeline.CareAwardCount} validTicks={lab.CareStroke.ValidContactTicks}");
        lab.QueueFree();
        return new ScenarioResult(AllPassed(checks), checks, messages);
    }

    private static async Task StrokeHeadValidTicks(SceneTree tree, BuddyLab lab, int ticks)
    {
        long target = lab.CareStroke.ValidContactTicks + ticks;
        int timeout = ticks + 8;
        for (int iteration = 0;
             iteration < timeout && lab.CareStroke.ValidContactTicks < target;
             iteration++)
        {
            lab.CareStroke.SetStroke(true, lab.Buddy.Rig.Head.GlobalPosition);
            await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
        }

        // physics_frame resumes before node _PhysicsProcess callbacks. Clearing
        // here prevents the signal frame used to observe the target count from
        // adding an unintended extra valid-contact tick.
        lab.CareStroke.SetStroke(false, Vector2.Zero);
    }

    private static bool AllPassed(IReadOnlyList<StartupCheck> checks)
    {
        foreach (StartupCheck check in checks)
        {
            if (!check.Passed) return false;
        }

        return true;
    }
}
