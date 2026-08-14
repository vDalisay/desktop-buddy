using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DesktopBuddy.Domain.Environment;
using DesktopBuddy.Environment;
using Godot;

namespace DesktopBuddy.Testing;

/// <summary>
/// AF-13 end-to-end gate: generated Sofa content composes into the existing Environment catalogue,
/// purchases per placed instance, persists by stable definition ID, renders its one trusted visual
/// mesh and never introduces a Lamp light, collision or gameplay node.
/// </summary>
public sealed class AssetForgeGeneratedSofaScenario : IScenario
{
    private static readonly DecorationDefinitionId SofaId = new("decoration.sofa.ci_soft");
    private const long StartingBalance = 600_000;
    private const long ExpectedPrice = 185_000;

    public string Id => "asset_forge_generated_sofa";

    public async Task<ScenarioResult> RunAsync(SceneTree tree, ulong seed)
    {
        var checks = new List<StartupCheck>();
        EnvironmentDecorationResource? resource = EnvironmentDecorationRegistry.Generated.Find(SofaId);
        bool imported = GodotObject.IsInstanceValid(resource) &&
            resource!.VisualSource == EnvironmentDecorationVisualSource.GeneratedMesh &&
            resource.Category == DecorationCategory.Sofa &&
            resource.AnchorKind == DecorationAnchorKind.Floor &&
            resource.PriceCredits == 185 &&
            GodotObject.IsInstanceValid(resource.GeneratedMesh) &&
            GodotObject.IsInstanceValid(resource.GeneratedAlbedo) &&
            GodotObject.IsInstanceValid(resource.Thumbnail) &&
            !GodotObject.IsInstanceValid(resource.LightProfile);
        checks.Add(new StartupCheck(
            "af_generated_sofa_resource_imported",
            imported,
            imported
                ? $"size={resource!.VisualSize} hash={resource.CanonicalAssetHash[..12]} price={resource.PriceCredits}"
                : "Generated Sofa fixture was not imported as trusted non-lighting Environment content."));
        if (!imported) return Result(checks, seed);

        DecorationCatalogue catalogue = EnvironmentDecorationRegistry.Domain;
        bool composed = catalogue.TryGet(SofaId, out DecorationDefinition definition) &&
            definition.Category == DecorationCategory.Sofa &&
            definition.AnchorKind == DecorationAnchorKind.Floor &&
            definition.PriceMilliCredits == ExpectedPrice &&
            definition.Rotation.AllowsRotation && definition.Rotation.StepDegrees == 15;
        checks.Add(new StartupCheck(
            "af_generated_sofa_composed_into_runtime_catalogue",
            composed,
            composed
                ? $"price={definition.PriceMilliCredits} rotation={definition.Rotation.StepDegrees} band={definition.RenderBand}"
                : "Generated Sofa did not compose into the live Room Decorator catalogue."));
        if (!composed) return Result(checks, seed);

        int nextId = 1;
        var session = new EnvironmentEditSession(
            new EnvironmentLayout(),
            StartingBalance,
            catalogue,
            () => EnvironmentTrustedDefinitionsClosureScenario.IdFor(nextId++));
        EnvironmentEditResult firstReserve = session.Reserve(SofaId, StartingBalance);
        EnvironmentEditResult firstPlace = session.PlaceReserved(new CanonicalRoomPosition(.42f, .88f));
        EnvironmentEditResult secondReserve = session.Reserve(SofaId, StartingBalance);
        EnvironmentEditResult secondPlace = session.PlaceReserved(new CanonicalRoomPosition(.68f, .88f));
        bool projected = session.TryProjectBalance(StartingBalance, out long projectedBalance);
        EnvironmentCommit commit = session.PrepareCommit();
        bool transaction = firstReserve.Succeeded && firstPlace.Succeeded && secondReserve.Succeeded && secondPlace.Succeeded &&
            projected && projectedBalance == StartingBalance - (ExpectedPrice * 2) &&
            commit.BalanceMilliCredits == StartingBalance - (ExpectedPrice * 2) &&
            commit.Layout.Decorations.Count(item => item.DefinitionId == SofaId) == 2 &&
            commit.Layout.Decorations.Where(item => item.DefinitionId == SofaId)
                .All(item => item.PurchasePriceMilliCredits == ExpectedPrice);
        checks.Add(new StartupCheck(
            "af_generated_sofa_uses_per_instance_purchase_model",
            transaction,
            $"first={firstPlace.Status} second={secondPlace.Status} projected={projectedBalance} count={commit.Layout.Decorations.Count}"));

        var reopened = new EnvironmentEditSession(commit.Layout, commit.BalanceMilliCredits, catalogue);
        bool stableRestartPayload = reopened.WorkingLayout.Decorations.Count(item => item.DefinitionId == SofaId) == 2;
        checks.Add(new StartupCheck(
            "af_generated_sofa_restart_payload_is_stable_id_only",
            stableRestartPayload,
            $"sofas={reopened.WorkingLayout.Decorations.Count(item => item.DefinitionId == SofaId)}"));

        PlacedDecoration sofa = commit.Layout.Decorations.First(item => item.DefinitionId == SofaId);
        var host = new Node3D { Name = "AssetForgeGeneratedSofaHost" };
        tree.Root.AddChild(host);
        try
        {
            var presenter = new EnvironmentDecorationPresenter { Name = "GeneratedSofaPresenter" };
            host.AddChild(presenter);
            presenter.Configure(sofa, resource!);
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);

            Node? generatedRoot = presenter.FindChild("GeneratedDecorationMesh", true, false);
            int authoredMeshes = generatedRoot is null ? 0 : CountMeshes(generatedRoot);
            int physicsNodes = CountPhysics(presenter);
            bool visual = GodotObject.IsInstanceValid(generatedRoot) && authoredMeshes == 1 &&
                presenter.FindChild("GeneratedLampEmitterVisual", true, false) is null &&
                presenter.FindChild("GeneratedLampLocalLight", true, false) is null &&
                physicsNodes == 0 &&
                Mathf.IsEqualApprox(presenter.Position.Z, EnvironmentDecorationPresenter.ZFor(definition.RenderBand));
            checks.Add(new StartupCheck(
                "af_generated_sofa_renders_visual_only_without_lamp_nodes",
                visual,
                $"generated={GodotObject.IsInstanceValid(generatedRoot)} meshes={authoredMeshes} lampEmitter={presenter.FindChild("GeneratedLampEmitterVisual", true, false) is not null} light={presenter.FindChild("GeneratedLampLocalLight", true, false) is not null} physics={physicsNodes}"));

            Texture2D thumbnail = EnvironmentDecorationVisualFactory.CreatePreview(resource!);
            checks.Add(new StartupCheck(
                "af_generated_sofa_thumbnail_is_normalized",
                GodotObject.IsInstanceValid(thumbnail) && thumbnail.GetWidth() == 256 && thumbnail.GetHeight() == 256,
                $"thumbnail={thumbnail.GetWidth()}x{thumbnail.GetHeight()}"));
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
