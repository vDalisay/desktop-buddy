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
/// mesh and never introduces a Lamp light, collision or gameplay node. Multiple placements must
/// instantiate nodes but continue sharing the imported mesh/texture resources.
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

        PlacedDecoration[] sofas = commit.Layout.Decorations.Where(item => item.DefinitionId == SofaId).ToArray();
        var host = new Node3D { Name = "AssetForgeGeneratedSofaHost" };
        tree.Root.AddChild(host);
        try
        {
            var firstPresenter = new EnvironmentDecorationPresenter { Name = "GeneratedSofaPresenterA" };
            var secondPresenter = new EnvironmentDecorationPresenter { Name = "GeneratedSofaPresenterB" };
            host.AddChild(firstPresenter);
            host.AddChild(secondPresenter);
            firstPresenter.Configure(sofas[0], resource!);
            secondPresenter.Configure(sofas[1], resource!);
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);

            Node? firstRoot = firstPresenter.FindChild("GeneratedDecorationMesh", true, false);
            Node? secondRoot = secondPresenter.FindChild("GeneratedDecorationMesh", true, false);
            MeshInstance3D? firstMesh = FirstMesh(firstRoot);
            MeshInstance3D? secondMesh = FirstMesh(secondRoot);
            int firstAuthoredMeshes = firstRoot is null ? 0 : CountMeshes(firstRoot);
            int secondAuthoredMeshes = secondRoot is null ? 0 : CountMeshes(secondRoot);
            int physicsNodes = CountPhysics(firstPresenter) + CountPhysics(secondPresenter);
            bool noLampNodes =
                firstPresenter.FindChild("GeneratedLampEmitterVisual", true, false) is null &&
                firstPresenter.FindChild("GeneratedLampLocalLight", true, false) is null &&
                secondPresenter.FindChild("GeneratedLampEmitterVisual", true, false) is null &&
                secondPresenter.FindChild("GeneratedLampLocalLight", true, false) is null;
            bool visual = GodotObject.IsInstanceValid(firstRoot) && GodotObject.IsInstanceValid(secondRoot) &&
                firstAuthoredMeshes == 1 && secondAuthoredMeshes == 1 && noLampNodes && physicsNodes == 0 &&
                Mathf.IsEqualApprox(firstPresenter.Position.Z, EnvironmentDecorationPresenter.ZFor(definition.RenderBand)) &&
                Mathf.IsEqualApprox(secondPresenter.Position.Z, EnvironmentDecorationPresenter.ZFor(definition.RenderBand));
            checks.Add(new StartupCheck(
                "af_generated_sofa_renders_visual_only_without_lamp_nodes",
                visual,
                $"roots={GodotObject.IsInstanceValid(firstRoot)}/{GodotObject.IsInstanceValid(secondRoot)} meshes={firstAuthoredMeshes}/{secondAuthoredMeshes} noLamp={noLampNodes} physics={physicsNodes}"));

            bool sharedResources = GodotObject.IsInstanceValid(firstMesh) && GodotObject.IsInstanceValid(secondMesh) &&
                GodotObject.IsInstanceValid(firstMesh!.Mesh) && GodotObject.IsInstanceValid(secondMesh!.Mesh) &&
                firstMesh.Mesh.GetInstanceId() == secondMesh.Mesh.GetInstanceId() &&
                firstMesh.MaterialOverride is StandardMaterial3D firstMaterial &&
                secondMesh.MaterialOverride is StandardMaterial3D secondMaterial &&
                GodotObject.IsInstanceValid(firstMaterial.AlbedoTexture) &&
                GodotObject.IsInstanceValid(secondMaterial.AlbedoTexture) &&
                firstMaterial.AlbedoTexture!.GetInstanceId() == secondMaterial.AlbedoTexture!.GetInstanceId() &&
                firstMaterial.AlbedoTexture.GetInstanceId() == resource!.GeneratedAlbedo!.GetInstanceId();
            checks.Add(new StartupCheck(
                "af_generated_sofa_instances_share_mesh_and_texture_resources",
                sharedResources,
                $"meshIds={firstMesh?.Mesh?.GetInstanceId()}/{secondMesh?.Mesh?.GetInstanceId()} textureIds={(firstMesh?.MaterialOverride as StandardMaterial3D)?.AlbedoTexture?.GetInstanceId()}/{(secondMesh?.MaterialOverride as StandardMaterial3D)?.AlbedoTexture?.GetInstanceId()}"));

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

    private static MeshInstance3D? FirstMesh(Node? root)
    {
        if (!GodotObject.IsInstanceValid(root)) return null;
        if (root is MeshInstance3D own) return own;
        return root!.FindChildren("*", nameof(MeshInstance3D), true, false).OfType<MeshInstance3D>().FirstOrDefault();
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
