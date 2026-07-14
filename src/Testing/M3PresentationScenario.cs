using System.Collections.Generic;
using System.Threading.Tasks;
using DesktopBuddy.App;
using DesktopBuddy.Domain.Tools;
using DesktopBuddy.Interaction;
using Godot;

namespace DesktopBuddy.Testing;

/// <summary>Semantic coverage for M3 face, chirp, fear resistance, and money HUD.</summary>
public sealed class M3PresentationScenario : IScenario
{
    public string Id => "m3_presentation";

    public async Task<ScenarioResult> RunAsync(SceneTree tree, ulong seed)
    {
        var checks = new List<StartupCheck>();
        var messages = new List<string> { $"seed={seed}" };
        BuddyLab? lab = await ScenarioSteps.CreateControlledImpactLab(tree, 10.0f);
        if (lab is null)
        {
            checks.Add(new StartupCheck("presentation_scene_loadable", false, "buddy_lab"));
            return new ScenarioResult(false, checks, messages);
        }

        AcceptedImpact? impact = await ScenarioSteps.StrikePart(tree, lab, lab.Buddy.Rig.Head);
        await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
        checks.Add(new StartupCheck("pain_face_has_priority",
            impact is not null && lab.Reactions.CurrentFace == ">_<",
            $"face={lab.Reactions.CurrentFace}"));
        checks.Add(new StartupCheck("pain_chirp_generated",
            lab.ReactionAudio.GetNode<AudioStreamPlayer>("AudioStreamPlayer").Stream is AudioStreamWav,
            "semantic impact produced original PCM chirp"));
        checks.Add(new StartupCheck("ordinary_glove_hit_has_feedback_without_hit_stop",
            lab.ImpactFeedback.FeedbackCount == 1 && lab.ImpactFeedback.HitStopTriggerCount == 0,
            $"feedback={lab.ImpactFeedback.FeedbackCount} hitStops={lab.ImpactFeedback.HitStopTriggerCount}"));
        checks.Add(new StartupCheck("money_hud_uses_whole_credits",
            lab.MoneyHud.BalanceLabel.Text == "$12",
            $"text={lab.MoneyHud.BalanceLabel.Text}"));

        for (int tick = 0; tick < 40 && !lab.MoneyHud.RewardLabel.Visible; tick++)
            await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
        checks.Add(new StartupCheck("reward_feedback_is_coalesced_and_visible",
            lab.MoneyHud.RewardLabel.Visible && lab.MoneyHud.RewardLabel.Text == "+$12.0",
            $"visible={lab.MoneyHud.RewardLabel.Visible} text={lab.MoneyHud.RewardLabel.Text}"));

        var torso = lab.Buddy.Rig.Torso;
        lab.Grab.TryGrab(torso, torso.GlobalPosition);
        lab.Grab.MoveCursor(torso.GlobalPosition + Vector2.Right * 70.0f);
        for (int tick = 0; tick < 3; tick++)
            await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
        checks.Add(new StartupCheck("acute_fear_drives_physical_grab_resistance",
            lab.Reactions.CurrentFear > 0.0f && lab.Buddy.GrabResistance.Intent.Active,
            $"fear={lab.Reactions.CurrentFear:F2} active={lab.Buddy.GrabResistance.Intent.Active}"));
        lab.Grab.Release();

        for (int tick = 0; tick < Engine.PhysicsTicksPerSecond + 5; tick++)
            await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
        lab.Pipeline.SelectTool(ToolId.BoxingGlove);
        lab.Grab.TryGrab(torso, torso.GlobalPosition);
        lab.Grab.MoveCursor(torso.GlobalPosition + Vector2.Right * 70.0f);
        for (int tick = 0; tick < 3; tick++)
            await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
        checks.Add(new StartupCheck("harmful_tool_history_drives_physical_grab_resistance",
            lab.Pipeline.IsToolHarmful((int)ToolId.BoxingGlove) &&
            lab.Reactions.CurrentFear > 0.0f && lab.Buddy.GrabResistance.Intent.Active,
            $"remembered={lab.Pipeline.IsToolHarmful((int)ToolId.BoxingGlove)} " +
            $"fear={lab.Reactions.CurrentFear:F2} active={lab.Buddy.GrabResistance.Intent.Active}"));
        lab.Grab.Release();

        messages.Add($"face={lab.Reactions.CurrentFace} balance={lab.Pipeline.BalanceMilliCredits}");
        lab.QueueFree();
        bool passed = true;
        foreach (StartupCheck check in checks) passed &= check.Passed;
        return new ScenarioResult(passed, checks, messages);
    }
}
