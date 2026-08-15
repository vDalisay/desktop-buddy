using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DesktopBuddy.Domain.Environment;
using DesktopBuddy.Environment;
using Godot;

namespace DesktopBuddy.Testing;

/// <summary>
/// Final initial-Environment-preset gate. Table, Plant and Painting must import through the same
/// data-driven generated-mesh path as Lamp/Sofa, participate in the existing per-instance
/// transaction/persistence model and remain presentation-only trusted content.
/// </summary>
public sealed class AssetForgeGeneratedEnvironmentV1Scenario : IScenario
{
    private readonly record struct Case(
        DecorationDefinitionId Id,
        DecorationCategory Category,
        DecorationAnchorKind Anchor,
        int PriceCredits,
        CanonicalRoomPosition Position);

    private static readonly Case[] Cases =
    [
        new(new DecorationDefinitionId("decoration.table.ci_simple"), DecorationCategory.Table, DecorationAnchorKind.Floor, 150, new CanonicalRoomPosition(.35f, .88f)),
        new(new DecorationDefinitionId("decoration.plant.ci_leafy"), DecorationCategory.Plant, DecorationAnchorKind.Floor, 110, new CanonicalRoomPosition(.68f, .88f)),
        new(new DecorationDefinitionId("decoration.painting.ci_frame"), DecorationCategory.Painting, DecorationAnchorKind.Wall, 90, new CanonicalRoomPosition(.52f, .38f)),
    ];

    public string Id => "asset_forge_generated_environment_v1";

    public async Task<ScenarioResult> RunAsync(SceneTree tree, ulong seed)
    {
        var checks = new List<StartupCheck>();
        DecorationCatalogue catalogue = EnvironmentDecorationRegistry.Domain;
        var resources = new List<EnvironmentDecorationResource>();

        foreach (Case item in Cases)
        {
            EnvironmentDecorationResource? resource = EnvironmentDecorationRegistry.Generated.Find(item.Id);
            bool imported = GodotObject.IsInstanceValid(resource) &&
                resource!.VisualSource == EnvironmentDecorationVisualSource.GeneratedMesh &&
                resource.Category == item.Category &&
                resource.AnchorKind == item.Anchor &&
                resource.PriceCredits == item.PriceCredits &&
                GodotObject.IsInstanceValid(resource.GeneratedMesh) &&
                GodotObject.IsInstanceValid(resource.GeneratedAlbedo) &&
                GodotObject.IsInstanceValid(resource.Thumbnail) &&
                !GodotObject.IsInstanceValid(resource.LightProfile) &&
                resource.CanonicalAssetHash.Length == 64;
            checks.Add(new StartupCheck(
                $"af_generated_{item.Category.ToString().ToLowerInvariant()}_resource_imported",
                imported,
                imported
                    ? $"id={item.Id} anchor={resource!.AnchorKind} price={resource.PriceCredits}"
                    : $"Generated {item.Category} fixture did not import as trusted non-lighting Environment content."));
            if (!imported) return Result(checks, seed);
            resources.Add(resource!);

            bool composed = catalogue.TryGet(item.Id, out DecorationDefinition definition) &&
                definition.Category == item.Category &&
                definition.AnchorKind == item.Anchor &&
                definition.PriceMilliCredits == item.PriceCredits * 1000L;
            checks.Add(new StartupCheck(
                $"af_generated_{item.Category.ToString().ToLowerInvariant()}_catalogue_composed",
                composed,
                composed ? $"price={definition.PriceMilliCredits} band={definition.RenderBand}" : "Generated definition missing from live Room Decorator catalogue."));
            if (!composed) return Result(checks, seed);
        }

        const long startingBalance = 1_000_000;
        int nextId = 1;
        var session = new EnvironmentEditSession(
            new EnvironmentLayout(),
            startingBalance,
            catalogue,
            () => EnvironmentTrustedDefinitionsClosureScenario.IdFor(nextId++));
        long expectedSpend = 0;
        foreach (Case item in Cases)
        {
            EnvironmentEditResult reserve = session.Reserve(item.Id, startingBalance);
            EnvironmentEditResult place = session.PlaceReserved(item.Position);
            expectedSpend += item.PriceCredits * 1000L;
            checks.Add(new StartupCheck(
                $"af_generated_{item.Category.ToString().ToLowerInvariant()}_places_through_existing_transaction",
                reserve.Succeeded && place.Succeeded,
                $"reserve={reserve.Status} place={place.Status}"));
        }

        EnvironmentCommit commit = session.PrepareCommit();
        bool transaction = commit.Layout.Decorations.Count == Cases.Length &&
            commit.BalanceMilliCredits == startingBalance - expectedSpend &&
            Cases.All(item => commit.Layout.Decorations.Any(placed =>
                placed.DefinitionId == item.Id && placed.PurchasePriceMilliCredits == item.PriceCredits * 1000L));
        checks.Add(new StartupCheck(
            "af_generated_environment_v1_preserves_per_instance_costs",
            transaction,
            $"placed={commit.Layout.Decorations.Count} balance={commit.BalanceMilliCredits} spend={expectedSpend}"));

        var reopened = new EnvironmentEditSession(commit.Layout, commit.BalanceMilliCredits, catalogue);
        bool stableIds = Cases.All(item => reopened.WorkingLayout.Decorations.Any(placed => placed.DefinitionId == item.Id));
        checks.Add(new StartupCheck(
            "af_generated_environment_v1_restart_payload_uses_stable_definition_ids",
            stableIds,
            $"restored={reopened.WorkingLayout.Decorations.Count}"));

        var host = new Node3D { Name = "AssetForgeGeneratedEnvironmentV1Host" };
        tree.Root.AddChild(host);
        try
        {
            foreach ((Case item, EnvironmentDecorationResource resource) in Cases.Zip(resources))
            {
                PlacedDecoration placed = commit.Layout.Decorations.First(value => value.DefinitionId == item.Id);
                var presenter = new EnvironmentDecorationPresenter { Name = "Generated" + item.Category + "Presenter" };
                host.AddChild(presenter);
                presenter.Configure(placed, resource);
                await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);

                Node? generatedRoot = presenter.FindChild("GeneratedDecorationMesh", true, false);
                int meshCount = generatedRoot is null ? 0 : CountMeshes(generatedRoot);
                int physicsCount = CountPhysics(presenter);
                bool noLampNodes = presenter.FindChild("GeneratedLampEmitterVisual", true, false) is null &&
                                   presenter.FindChild("GeneratedLampLocalLight", true, false) is null;
                Texture2D thumbnail = EnvironmentDecorationVisualFactory.CreatePreview(resource);
                bool visual = GodotObject.IsInstanceValid(generatedRoot) && meshCount == 1 && physicsCount == 0 && noLampNodes &&
                              GodotObject.IsInstanceValid(thumbnail) && thumbnail.GetWidth() == 256 && thumbnail.GetHeight() == 256;
                checks.Add(new StartupCheck(
                    $"af_generated_{item.Category.ToString().ToLowerInvariant()}_renders_visual_only",
                    visual,
                    $"mesh={meshCount} physics={physicsCount} noLamp={noLampNodes} thumbnail={thumbnail.GetWidth()}x{thumbnail.GetHeight()}"));
            }
        }
        finally
        {
            host.QueueFree();
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        }

        return Result(checks, seed);
    }

    private static int CountMeshes(Node root)
    {
        int count = root is MeshInstance3D ? 1 : 0;
        foreach (Node child in root.GetChildren()) count += CountMeshes(child);
        return count;
    }

    private static int CountPhysics(Node node)
    {
        int count = node is CollisionObject2D or CollisionObject3D or CollisionShape2D or CollisionShape3D or
            CollisionPolygon2D or CollisionPolygon3D or Joint2D or Joint3D or PhysicsBody2D or PhysicsBody3D ? 1 : 0;
        foreach (Node child in node.GetChildren()) count += CountPhysics(child);
        return count;
    }

    private static ScenarioResult Result(IReadOnlyList<StartupCheck> checks, ulong seed) =>
        new(checks.All(static check => check.Passed), checks, [$"seed={seed}"]);
}
