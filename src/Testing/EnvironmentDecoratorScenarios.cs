using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DesktopBuddy.Domain.Environment;
using DesktopBuddy.Environment;
using Godot;

namespace DesktopBuddy.Testing;

/// <summary>Godot-side launch-content gate for the trusted Environment Decorator catalogue.</summary>
public sealed class EnvironmentDecoratorLaunchCatalogueScenario : IScenario
{
    public string Id => "environment_decorator_launch_catalogue";

    public Task<ScenarioResult> RunAsync(SceneTree tree, ulong seed)
    {
        var checks = new List<StartupCheck>();
        EnvironmentDecorationResource[] entries = EnvironmentDecorationRegistry.Authored.Entries
            .Where(static resource => resource is not null)
            .ToArray();
        DecorationDefinition[] visible = entries
            .Select(static resource => resource.ToDefinition())
            .Where(static definition => definition.Visible)
            .ToArray();

        bool twoPerCategory = Enum.GetValues<DecorationCategory>()
            .All(category => visible.Count(item => item.Category == category) >= 2);
        checks.Add(new StartupCheck(
            "environment_launch_has_two_items_per_category",
            twoPerCategory,
            string.Join(", ", Enum.GetValues<DecorationCategory>()
                .Select(category => $"{category}={visible.Count(item => item.Category == category)}"))));

        bool uniqueIds = visible.Select(item => item.Id.Value).Distinct(StringComparer.Ordinal).Count() == visible.Length;
        checks.Add(new StartupCheck(
            "environment_launch_definition_ids_are_unique",
            uniqueIds,
            $"visible={visible.Length}"));

        bool resourcesValid = entries.All(resource => resource.Validate().Count == 0);
        checks.Add(new StartupCheck(
            "environment_launch_resources_validate",
            resourcesValid,
            $"resources={entries.Length}"));

        bool previewsValid = entries.All(resource =>
        {
            Texture2D preview = EnvironmentDecorationVisualFactory.CreatePreview(resource);
            return preview.GetWidth() == 48 && preview.GetHeight() == 48;
        });
        checks.Add(new StartupCheck(
            "environment_launch_previews_are_renderable",
            previewsValid,
            "48x48 semantic previews"));

        bool visualOnly = true;
        var host = new Node3D { Name = "EnvironmentDecoratorVisualOnlyScenario" };
        tree.Root.AddChild(host);
        try
        {
            int ordinal = 1;
            foreach (EnvironmentDecorationResource resource in entries)
            {
                DecorationDefinition definition = resource.ToDefinition();
                var presenter = new EnvironmentDecorationPresenter();
                host.AddChild(presenter);
                var placed = new PlacedDecoration(
                    new PlacedDecorationId(new Guid(ordinal++, 0, 0, new byte[8])),
                    definition.Id,
                    new CanonicalRoomPosition(.5f, .5f),
                    0,
                    definition.RenderBand,
                    definition.PriceMilliCredits);
                presenter.Configure(placed, resource, new Vector2(480, 360));
                visualOnly &= !ContainsPhysicsNode(presenter);
            }
        }
        finally
        {
            host.Free();
        }
        checks.Add(new StartupCheck(
            "environment_launch_decorations_are_visual_only",
            visualOnly,
            "no collision or physics nodes"));

        return Task.FromResult(new ScenarioResult(
            checks.All(static check => check.Passed),
            checks,
            [$"seed={seed}"]));
    }

    private static bool ContainsPhysicsNode(Node node)
    {
        if (node is CollisionObject3D or CollisionShape3D or CollisionPolygon3D)
            return true;
        foreach (Node child in node.GetChildren())
            if (ContainsPhysicsNode(child)) return true;
        return false;
    }
}
