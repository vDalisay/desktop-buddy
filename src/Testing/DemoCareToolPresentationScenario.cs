using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DesktopBuddy.App;
using DesktopBuddy.Domain.Tools;
using Godot;

namespace DesktopBuddy.Testing;

/// <summary>
/// DEMO-8 presentation gate for the two care tools. Mechanical Pet/Tickle cadence is covered by
/// pet_tickle_mood; this scenario keeps the pointer vocabulary distinct and verifies release cleanup.
/// </summary>
public sealed class DemoCareToolPresentationScenario : IScenario
{
    public string Id => "demo_care_tool_presentation";

    public async Task<ScenarioResult> RunAsync(SceneTree tree, ulong seed)
    {
        var checks = new List<StartupCheck>();
        BuddyLab? lab = await M4ObjectScenarioSupport.LoadLab(tree, seed);
        if (lab is null)
        {
            checks.Add(new StartupCheck("demo_care_cursor_lab_loadable", false, "buddy_lab"));
            return new ScenarioResult(false, checks, [$"seed={seed}"]);
        }

        Vector2 point = lab.Buddy.Rig.Head.GlobalPosition;
        lab.CareCursor.SetPointerState(ToolId.Pet, point, held: true);
        checks.Add(new StartupCheck(
            "demo_pet_keeps_hand_cursor",
            lab.CareCursor.IsHandVisible &&
            lab.CareCursor.Tool == ToolId.Pet &&
            !lab.CareCursor.IsTickleFeatherVisible,
            $"visible={lab.CareCursor.IsHandVisible} tool={lab.CareCursor.Tool} feather={lab.CareCursor.IsTickleFeatherVisible}"));

        lab.CareCursor.SetPointerState(ToolId.Tickle, point, held: true);
        checks.Add(new StartupCheck(
            "demo_tickle_uses_feather_cursor",
            lab.CareCursor.IsHandVisible &&
            lab.CareCursor.Tool == ToolId.Tickle &&
            lab.CareCursor.IsTickleFeatherVisible,
            $"visible={lab.CareCursor.IsHandVisible} tool={lab.CareCursor.Tool} feather={lab.CareCursor.IsTickleFeatherVisible}"));

        lab.CareCursor.SetPointerState(ToolId.Tickle, point, held: false);
        checks.Add(new StartupCheck(
            "demo_care_cursor_hides_on_release",
            !lab.CareCursor.IsHandVisible && !lab.CareCursor.IsTickleFeatherVisible,
            $"visible={lab.CareCursor.IsHandVisible} feather={lab.CareCursor.IsTickleFeatherVisible}"));

        await M4ObjectScenarioSupport.Cleanup(tree, lab);
        return new ScenarioResult(
            checks.All(static check => check.Passed),
            checks,
            [$"seed={seed}"]);
    }
}
