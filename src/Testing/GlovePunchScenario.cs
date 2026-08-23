using System.Collections.Generic;
using System.Threading.Tasks;
using DesktopBuddy.App;
using DesktopBuddy.Domain.Tools;
using DesktopBuddy.Tools;
using Godot;

namespace DesktopBuddy.Testing;

/// <summary>
/// The Boxing Glove's wind-up-and-lash-out chord (owner instruction 2026-08-22): holding
/// secondary drags the glove back behind the cursor like a drawn slingshot, and releasing
/// throws it out past the cursor along the direction it is drawn pointing.
///
/// <para>Measured on the real cursor-tool body through the ordinary tether, because that is
/// the whole design: the punch moves where the tool is <em>told</em> to be and the tether does
/// the rest, so nothing here should need a second way to move a tool.</para>
/// </summary>
public sealed class GlovePunchScenario : IScenario
{
    private const int FacingTicks = 40;
    private const int ChargeTicks = 120;

    public string Id => "glove_punch";

    public async Task<ScenarioResult> RunAsync(SceneTree tree, ulong seed)
    {
        var checks = new List<StartupCheck>();
        var messages = new List<string> { $"seed={seed}" };
        BuddyLab? lab = await M4ObjectScenarioSupport.LoadLab(tree, seed);
        if (lab is null)
        {
            checks.Add(new StartupCheck("glove_punch_lab_loadable", false, "buddy_lab"));
            return new ScenarioResult(false, checks, messages);
        }

        CursorToolController tools = lab.CursorTools;
        lab.Pipeline.SelectTool(ToolId.BoxingGlove);

        // Well clear of the buddy: this leg measures where the glove is held, and a punch that
        // landed on something would be measuring the collision instead.
        Rect2 room = lab.Boundaries.InnerBounds;
        var park = new Vector2(room.Position.X + 90.0f, room.Position.Y + 90.0f);
        tools.MoveCursor(park);
        await Ticks(tree, 30);

        // Facing comes from cursor motion, so the glove has to be pointed before it is wound:
        // walk the cursor steadily to the right and leave it there.
        for (int step = 0; step < FacingTicks; step++)
        {
            tools.MoveCursor(park + new Vector2(step * 6.0f, 0.0f));
            await Ticks(tree, 1);
        }

        Vector2 cursor = park + new Vector2(FacingTicks * 6.0f, 0.0f);
        tools.MoveCursor(cursor);
        await Ticks(tree, 20);
        var facing = Vector2.FromAngle(tools.ToolFacingAngle);
        checks.Add(new StartupCheck(
            "the_glove_faces_the_way_the_cursor_was_moved",
            tools.HasToolFacing && facing.Dot(Vector2.Right) > 0.9f,
            $"has_facing={tools.HasToolFacing} facing={facing} dot={facing.Dot(Vector2.Right):F2}"));

        float restingReach = Reach(tools, cursor, facing);

        tools.SetChargeHeld(true);
        await Ticks(tree, ChargeTicks);
        float chargedReach = Reach(tools, cursor, facing);
        float charge = tools.PunchCharge;
        bool charging = tools.IsPunchCharging;

        checks.Add(new StartupCheck(
            "holding_secondary_winds_the_glove_back_behind_the_cursor",
            charging &&
            charge > 0.99f &&
            chargedReach < restingReach - 8.0f,
            $"charge={charge:F2} charging={charging} resting_reach={restingReach:F1}px " +
            $"charged_reach={chargedReach:F1}px"));

        int punchesBefore = tools.PunchCount;
        tools.SetChargeHeld(false);
        // Peak reach rather than one reading: the lunge is out and back inside its own window.
        float peakReach = chargedReach;
        for (int tick = 0; tick < 24; tick++)
        {
            await Ticks(tree, 1);
            peakReach = Mathf.Max(peakReach, Reach(tools, cursor, facing));
        }

        checks.Add(new StartupCheck(
            "releasing_throws_the_glove_out_past_the_cursor",
            tools.PunchCount == punchesBefore + 1 &&
            peakReach > restingReach + 8.0f,
            $"punches={tools.PunchCount - punchesBefore} peak_reach={peakReach:F1}px " +
            $"resting_reach={restingReach:F1}px"));

        await Ticks(tree, 60);
        checks.Add(new StartupCheck(
            "the_glove_settles_back_onto_the_cursor",
            !tools.IsPunchLunging && !tools.IsPunchCharging &&
            Mathf.Abs(Reach(tools, cursor, facing) - restingReach) < 12.0f,
            $"lunging={tools.IsPunchLunging} charging={tools.IsPunchCharging} " +
            $"reach={Reach(tools, cursor, facing):F1}px resting={restingReach:F1}px"));

        lab.QueueFree();
        bool passed = true;
        foreach (StartupCheck check in checks) passed &= check.Passed;
        return new ScenarioResult(passed, checks, messages);
    }

    /// <summary>How far along its own facing the glove is sitting, relative to the cursor.</summary>
    private static float Reach(CursorToolController tools, Vector2 cursor, Vector2 facing) =>
        tools.Body is null ? 0.0f : (tools.Body.GlobalPosition - cursor).Dot(facing);

    private static async Task Ticks(SceneTree tree, int ticks)
    {
        for (int tick = 0; tick < ticks; tick++)
            await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
    }
}
