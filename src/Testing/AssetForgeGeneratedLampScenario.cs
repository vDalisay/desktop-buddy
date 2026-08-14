using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DesktopBuddy.Domain.Environment;
using DesktopBuddy.Environment;
using Godot;

namespace DesktopBuddy.Testing;

public sealed class AssetForgeGeneratedLampScenario : IScenario
{
    private static readonly DecorationDefinitionId LampId = new("decoration.lamp.ci_round");
    private const long StartingBalance = 500_000;
    private const long ExpectedPrice = 135_000;

    public string Id => "asset_forge_generated_lamp";

    public async Task<ScenarioResult> RunAsync(SceneTree tree, ulong seed)
    {
        var checks = new List<StartupCheck>();

        EnvironmentDecorationResource? resource = EnvironmentDecorationRegistry.Generated.Find(LampId);
        bool imported = GodotObject.IsInstanceValid(resource) &&
            resource!.VisualSource == EnvironmentDecorationVisualSource.GeneratedMesh &&
            resource.Category == DecorationCategory.Lamp &&
            resource.AnchorKind == DecorationAnchorKind.Floor &&
            resource.PriceCredits == 135 &&
            GodotObject.IsInstanceValid(resource.GeneratedMesh) &&
            GodotObject.IsInstanceValid(resource.GeneratedAlbedo) &&
            GodotObject.IsInstanceValid(resource.Thumbnail) &&
            GodotObject.IsInstanceValid(resource.LightProfile) &&
            resource.LightProfile!.Enabled &&
            resource.LightProfile.LightEnabled &&
            resource.LightProfile.UsesLocalEmitterPosition;
        checks.Add(new StartupCheck(
            "af_generated_lamp_resource_imported",
            imported,
            imported
                ? $"size={resource!.VisualSize} emitter={resource.LightProfile!.LocalEmitterPosition} hash={resource.CanonicalAssetHash[..12]} price={resource.PriceCredits}"
                : "Generated Lamp fixture was not imported through the generated Environment boundary with its Lamp v2 local emitter."));
        if (!imported)
            return Result(checks, seed);

        DecorationCatalogue catalogue = EnvironmentDecorationRegistry.Domain;
        bool composed = catalogue.TryGet(LampId, out DecorationDefinition definition) &&
            definition.Category == DecorationCategory.Lamp &&
            definition.AnchorKind == DecorationAnchorKind.Floor &&
            definition.PriceMilliCredits == ExpectedPrice &&
            definition.Rotation.AllowsRotation && definition.Rotation.StepDegrees == 15;
        checks.Add(new StartupCheck(
            "af_generated_lamp_composed_into_runtime_catalogue",
            composed,
            composed
                ? $"price={definition.PriceMilliCredits} rotation={definition.Rotation.StepDegrees} band={definition.RenderBand}"
                : "Generated Lamp did not compose into the live Room Decorator catalogue."));
        if (!composed)
            return Result(checks, seed);

        int nextId = 1;
        var session = new EnvironmentEditSession(
            new EnvironmentLayout(),
            StartingBalance,
            catalogue,
            () => EnvironmentTrustedDefinitionsClosureScenario.IdFor(nextId++));
        EnvironmentEditResult reserved = session.Reserve(LampId, StartingBalance);
        EnvironmentEditResult placed = session.PlaceReserved(new CanonicalRoomPosition(.62f, .88f));
        EnvironmentEditResult rotated = placed.Succeeded
            ? session.Rotate(placed.InstanceId, +1)
            : new EnvironmentEditResult(EnvironmentEditStatus.UnknownInstance);
        bool projected = session.TryProjectBalance(StartingBalance, out long projectedBalance);
        EnvironmentCommit commit = session.PrepareCommit();
        PlacedDecoration? committedLamp = commit.Layout.Decorations.SingleOrDefault(item => item.DefinitionId == LampId);
        bool transaction = reserved.Succeeded && placed.Succeeded && rotated.Succeeded && projected &&
            projectedBalance == StartingBalance - ExpectedPrice &&
            commit.BalanceMilliCredits == StartingBalance - ExpectedPrice &&
            committedLamp is PlacedDecoration lamp &&
            lamp.PurchasePriceMilliCredits == ExpectedPrice &&
            lamp.RotationDegrees == 15 &&
            lamp.RenderBand == definition.RenderBand;
        checks.Add(new StartupCheck(
            "af_generated_lamp_uses_existing_per_instance_transaction",
            transaction,
            $"reserve={reserved.Status} place={placed.Status} rotate={rotated.Status} projected={projectedBalance} count={commit.Layout.Decorations.Count}"));

        // A fresh editing session from the committed layout proves the save-domain payload needs no
        // generated file paths or mesh state: only the stable decoration ID survives the boundary.
        var reopened = new EnvironmentEditSession(commit.Layout, commit.BalanceMilliCredits, catalogue);
        bool stableRestartPayload = reopened.WorkingLayout.Decorations.Count == 1 &&
            reopened.WorkingLayout.Decorations[0].DefinitionId == LampId &&
            reopened.WorkingLayout.Decorations[0].RotationDegrees == 15;
        checks.Add(new StartupCheck(
            "af_generated_lamp_restart_payload_is_stable_id_only",
            stableRestartPayload,
            stableRestartPayload ? reopened.WorkingLayout.Decorations[0].DefinitionId.Value : "Committed Lamp was not recoverable by stable ID."));

        var host = new Node3D { Name = "AssetForgeGeneratedLampHost" };
        tree.Root.AddChild(host);
        try
        {
            var presenter = new EnvironmentDecorationPresenter { Name = "GeneratedLampPresenter" };
            host.AddChild(presenter);
            presenter.Configure(committedLamp!.Value, resource!);
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);

            Node? generatedRoot = presenter.FindChild("GeneratedDecorationMesh", true, false);
            MeshInstance3D? emitter = presenter.FindChild("GeneratedLampEmitterVisual", true, false) as MeshInstance3D;
            OmniLight3D? localLight = presenter.FindChild("GeneratedLampLocalLight", true, false) as OmniLight3D;
            int authoredMeshes = generatedRoot is null ? 0 : CountMeshes(generatedRoot);
            int physicsNodes = CountPhysics(presenter);
            Vector2 expectedEmitter = resource.LightProfile!.LocalEmitterPosition * resource.DefaultScale;
            bool emitterPlacement = GodotObject.IsInstanceValid(emitter) && GodotObject.IsInstanceValid(localLight) &&
                Mathf.IsEqualApprox(emitter!.Position.X, expectedEmitter.X) &&
                Mathf.IsEqualApprox(emitter.Position.Y, expectedEmitter.Y) &&
                Mathf.IsEqualApprox(localLight!.Position.X, expectedEmitter.X) &&
                Mathf.IsEqualApprox(localLight.Position.Y, expectedEmitter.Y);
            bool visual = GodotObject.IsInstanceValid(generatedRoot) &&
                authoredMeshes == 1 &&
                emitterPlacement &&
                physicsNodes == 0 &&
                Mathf.IsEqualApprox(presenter.Position.Z, EnvironmentDecorationPresenter.ZFor(definition.RenderBand));
            checks.Add(new StartupCheck(
                "af_generated_lamp_renders_visual_only_with_authored_light",
                visual,
                $"generated={GodotObject.IsInstanceValid(generatedRoot)} meshes={authoredMeshes} emitter={emitter?.Position} expected={expectedEmitter} light={localLight?.Position} physics={physicsNodes}"));

            checks.Add(new StartupCheck(
                "af_generated_lamp_v2_uses_baked_local_emitter",
                emitterPlacement,
                $"profile={resource.LightProfile.LocalEmitterPosition} scale={resource.DefaultScale:0.###} emitter={emitter?.Position} light={localLight?.Position}"));

            Texture2D thumbnail = EnvironmentDecorationVisualFactory.CreatePreview(resource!);
            checks.Add(new StartupCheck(
                "af_generated_lamp_thumbnail_available",
                GodotObject.IsInstanceValid(thumbnail) && thumbnail.GetWidth() == 256 && thumbnail.GetHeight() == 256,
                $"thumbnail={thumbnail.GetWidth()}x{thumbnail.GetHeight()}"));
        }
        finally
        {
            host.QueueFree();
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        }

        // Cancelling a brand-new copy remains a no-spend transaction under the generated definition.
        var cancelSession = new EnvironmentEditSession(new EnvironmentLayout(), StartingBalance, catalogue);
        EnvironmentEditResult cancelReserve = cancelSession.Reserve(LampId, StartingBalance);
        EnvironmentEditResult cancel = cancelSession.CancelReservation();
        bool cancelSafe = cancelReserve.Succeeded && cancel.Succeeded &&
            cancelSession.ProjectedBalanceMilliCredits == StartingBalance &&
            cancelSession.WorkingLayout.Decorations.Count == 0 && !cancelSession.IsDirty;
        checks.Add(new StartupCheck(
            "af_generated_lamp_cancel_does_not_spend_or_persist",
            cancelSafe,
            $"reserve={cancelReserve.Status} cancel={cancel.Status} projected={cancelSession.ProjectedBalanceMilliCredits} dirty={cancelSession.IsDirty}"));

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
