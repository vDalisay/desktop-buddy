using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using DesktopBuddy.Content;
using DesktopBuddy.Domain.Content;
using DesktopBuddy.Domain.Tools;
using DesktopBuddy.Grab;
using Godot;

namespace DesktopBuddy.Testing;

/// <summary>
/// The Rope Suspender's own gate: a rope holds what it is tied to at the anchor, a cut only
/// lands when the pointer is actually on the rope, and the tool is a Grab-category tool the
/// launch catalogue carries at its authored price.
/// </summary>
public sealed class RopeSuspenderScenario : IScenario
{
    public string Id => "rope_suspender";

    public async Task<ScenarioResult> RunAsync(SceneTree tree, ulong seed)
    {
        var checks = new List<StartupCheck>();
        var messages = new List<string> { $"seed={seed}" };

        ToolCatalogue catalogue = CatalogueLoader.Catalogue;
        bool selectable = CataloguePolicy.IsSelectable(catalogue, ContentIds.ToolRopeSuspender);
        bool priced = catalogue.TryGet(ContentIds.ToolRopeSuspender, out CatalogueEntry entry) &&
            entry.PriceMilliCredits == 1_500_000;
        bool parsed = ContentIds.TryParseTool(ContentIds.ToolRopeSuspender, out ToolId tool) &&
            tool == ToolId.RopeSuspender;
        bool catalogued = selectable && priced && parsed &&
            ToolCatalog.CategoryOf(ToolId.RopeSuspender) == ToolCategory.Grab;
        checks.Add(new StartupCheck(
            "rope_is_a_grab_tool_in_the_launch_catalogue",
            catalogued,
            $"selectable={selectable} priced={priced} parsed={parsed}"));

        var root = new Node2D { Name = "RopeSuspenderScenarioRoot" };
        tree.Root.AddChild(root);
        var ropes = new RopeSuspensionComponent { Name = "Ropes" };
        root.AddChild(ropes);
        ropes.Initialize();

        var body = new RigidBody2D
        {
            Name = "HungBody",
            Mass = 1.0f,
            GravityScale = 1.0f,
            // Held still by its rope, the body would otherwise fall asleep and never show
            // the drop the cut is supposed to cause.
            CanSleep = false,
            GlobalPosition = new Vector2(200.0f, 200.0f),
        };
        body.AddChild(new CollisionShape2D { Shape = new CircleShape2D { Radius = 8.0f } });
        root.AddChild(body);
        await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);

        var anchor = new Vector2(200.0f, 120.0f);
        bool attached = ropes.Attach(body, body.GlobalPosition, anchor);
        double step = 1.0 / Engine.PhysicsTicksPerSecond;
        for (int tick = 0; tick < 240; tick++)
        {
            ropes.PhysicsTick(step);
            await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
        }

        float held = body.GlobalPosition.DistanceTo(anchor);
        bool suspended = attached && ropes.RopeCount == 1 && held < 24.0f;
        checks.Add(new StartupCheck(
            "rope_holds_its_body_at_the_anchor",
            suspended,
            $"attached={attached} ropes={ropes.RopeCount} distance={held:F1}"));

        // A click nowhere near the rope must not cut it; one on the line must.
        bool missed = !ropes.TryCutAt(anchor + new Vector2(300.0f, 300.0f));
        bool overRope = ropes.IsOverRope(anchor);
        bool cut = ropes.TryCutAt(anchor);
        Vector2 beforeFall = body.GlobalPosition;
        for (int tick = 0; tick < 120; tick++)
        {
            ropes.PhysicsTick(step);
            await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
        }

        bool fell = body.GlobalPosition.Y > beforeFall.Y + 4.0f;
        bool cutting = missed && overRope && cut && ropes.RopeCount == 0 && ropes.CutCount == 1 && fell;
        checks.Add(new StartupCheck(
            "cutting_needs_the_pointer_on_the_rope_and_drops_the_body",
            cutting,
            $"missed={missed} hover={overRope} cut={cut} ropes={ropes.RopeCount} fell={fell}"));

        root.QueueFree();
        return new ScenarioResult(checks.TrueForAll(check => check.Passed), checks, messages);
    }
}

/// <summary>Registers the Rope Suspender gate without expanding the legacy central registry.</summary>
internal static class RopeSuspenderScenarioRegistration
{
    [ModuleInitializer]
    internal static void Register()
    {
        FieldInfo field = typeof(ScenarioCatalog).GetField(
            "Factories",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Scenario registry field was not found.");
        var factories = (Dictionary<string, Func<IScenario>>?)field.GetValue(null)
            ?? throw new InvalidOperationException("Scenario registry was not initialized.");
        factories["rope_suspender"] = () => new RopeSuspenderScenario();
    }
}
