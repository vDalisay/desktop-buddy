using System.Collections.Generic;
using System.Threading.Tasks;
using DesktopBuddy.App;
using DesktopBuddy.Buddy.Physics;
using DesktopBuddy.Domain.Mood;
using DesktopBuddy.Domain.Tools;
using DesktopBuddy.Tools;
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
        long balanceBeforeCare = lab.Pipeline.BalanceMilliCredits;
        long impactsBeforeCare = lab.Pipeline.ScoredImpactCount;
        long feedbackBeforeCare = lab.Pipeline.FeedbackCount;
        foreach (PuppetPartBody part in lab.Buddy.Rig.Parts)
        {
            part.Freeze = true;
            part.LinearVelocity = Vector2.Zero;
            part.AngularVelocity = 0.0f;
        }

        lab.Pipeline.SelectTool(ToolId.Pet);
        await RubHeadValidTicks(tree, lab, CadenceTicks - 1);
        checks.Add(new StartupCheck("pet_waits_for_three_valid_seconds",
            lab.Pipeline.CareAwardCount == 0,
            $"awards={lab.Pipeline.CareAwardCount} distance={lab.Pipeline.PetDistanceProgress:F3} seconds={lab.Pipeline.PetValidSecondsProgress:F6}"));
        await RubHeadValidTicks(tree, lab, 1);
        checks.Add(new StartupCheck("pet_awards_at_three_valid_seconds",
            lab.Pipeline.CareAwardCount == 1 &&
            lab.Pipeline.PetDistanceProgress < 0.001 &&
            lab.Pipeline.PetValidSecondsProgress < 1.0 / 120.0,
            $"awards={lab.Pipeline.CareAwardCount} mood={lab.Pipeline.Mood:F4} distance={lab.Pipeline.PetDistanceProgress:F3} seconds={lab.Pipeline.PetValidSecondsProgress:F6}"));

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
        await HoldHeadValidTicks(tree, lab, CadenceTicks);
        checks.Add(new StartupCheck("tickle_has_independent_cadence",
            lab.Pipeline.CareAwardCount == 2 &&
            lab.Pipeline.PetValidSecondsProgress < 1.0 / 120.0 &&
            lab.Pipeline.TickleContactSeconds >= 3.0 - 1e-6,
            $"awards={lab.Pipeline.CareAwardCount} pet={lab.Pipeline.PetValidSecondsProgress:F6} tickle={lab.Pipeline.TickleContactSeconds:F6}"));
        checks.Add(new StartupCheck("care_never_pays_money",
            lab.Pipeline.ScoredImpactCount == impactsBeforeCare &&
            lab.Pipeline.FeedbackCount == feedbackBeforeCare,
            $"balance={balanceBeforeCare}->{lab.Pipeline.BalanceMilliCredits} " +
            $"impacts={impactsBeforeCare}->{lab.Pipeline.ScoredImpactCount} " +
            $"feedback={feedbackBeforeCare}->{lab.Pipeline.FeedbackCount}"));

        lab.CareStroke.SetStroke(false, Vector2.Zero);
        messages.Add($"mood={lab.Pipeline.Mood:F4} awards={lab.Pipeline.CareAwardCount} validTicks={lab.CareStroke.ValidContactTicks}");
        lab.QueueFree();
        await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        return new ScenarioResult(AllPassed(checks), checks, messages);
    }

    private static async Task RubHeadValidTicks(SceneTree tree, BuddyLab lab, int ticks)
    {
        long target = lab.CareStroke.ValidContactTicks + ticks;
        int timeout = ticks + 8;
        for (int iteration = 0;
             iteration < timeout && lab.CareStroke.ValidContactTicks < target;
             iteration++)
        {
            float offset = iteration % 2 == 0 ? -8.0f : 8.0f;
            lab.CareStroke.SetStroke(true, lab.Buddy.Rig.Head.GlobalPosition + Vector2.Right * offset);
            await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
        }

        // physics_frame resumes before node _PhysicsProcess callbacks. Clearing
        // here prevents the signal frame used to observe the target count from
        // adding an unintended extra valid-contact tick.
        lab.CareStroke.SetStroke(false, Vector2.Zero);
    }

    private static async Task HoldHeadValidTicks(SceneTree tree, BuddyLab lab, int ticks)
    {
        long target = lab.CareStroke.ValidContactTicks + ticks;
        int timeout = ticks + 8;
        for (int iteration = 0;
             iteration < timeout && lab.CareStroke.ValidContactTicks < target;
             iteration++)
        {
            lab.CareStroke.SetStroke(
                true, lab.CareStroke.PointerForContactAt(lab.Buddy.Rig.Head.GlobalPosition));
            await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
        }

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
