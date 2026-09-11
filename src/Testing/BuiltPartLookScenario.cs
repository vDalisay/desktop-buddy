using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DesktopBuddy.App;
using DesktopBuddy.Domain.Content;
using DesktopBuddy.Domain.Environment;
using DesktopBuddy.Domain.Sandbox;
using DesktopBuddy.Presentation3D;
using DesktopBuddy.Sandbox;
using Godot;

namespace DesktopBuddy.Testing;

/// <summary>
/// NF-3: built parts draw as 3D shapes in the frontal presentation and flat in the legacy one —
/// one silhouette per mode, as every other body in the room. Run without <c>--headless</c> to
/// also save a screenshot of all four parts for the owner.
/// </summary>
public sealed class BuiltPartLookScenario : IScenario
{
    public string Id => "built_part_look";

    public async Task<ScenarioResult> RunAsync(SceneTree tree, ulong seed)
    {
        var checks = new List<StartupCheck>();
        var messages = new List<string> { $"seed={seed}" };
        var loaded = await M4LifecycleScenarioSupport.Load(tree, new ManualMonotonicTimeSource());
        if (loaded is not { } room)
        {
            checks.Add(new StartupCheck("built_look_sandbox_loadable", false, "sandbox"));
            return new ScenarioResult(false, checks, messages);
        }
        SandboxRoot sandbox = room.Sandbox;
        sandbox.SetPresentationMode(PresentationMode.Mii3D);

        // Frozen, so the picture is of the shapes rather than of where they fell.
        Place(sandbox, SandboxPartCatalogue.WoodBeam, 0.25f, 0.55f, 0.0f);
        Place(sandbox, SandboxPartCatalogue.MetalBlock, 0.45f, 0.55f, 20.0f);
        Place(sandbox, SandboxPartCatalogue.MetalPlate, 0.65f, 0.55f, 0.0f);
        Place(sandbox, SandboxPartCatalogue.Wheel, 0.82f, 0.55f, 30.0f);
        for (int frame = 0; frame < 10; frame++)
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);

        SandboxPartBody[] bodies = sandbox.BuiltParts.Values.ToArray();
        int drawn = sandbox.PartVisual?.DrawnCount ?? 0;
        bool shapesIn3D = drawn == 4 && bodies.All(body => !body.DrawsShape) &&
            sandbox.PartVisual!.Visible;
        // Each mesh sits where its body is.
        bool aligned = sandbox.PartVisual is { } visual && bodies.All(body =>
            visual.GetChildren().OfType<Node3D>().Any(node =>
                WorldPlaneMapping.To2D(node.GlobalPosition).DistanceTo(body.GlobalPosition) < 1.0f &&
                Mathf.IsEqualApprox(node.GlobalRotation.Z, WorldPlaneMapping.To3DRotationZ(body.GlobalRotation), 0.01f)));

        string? picture = null;
        if (DisplayServer.GetName() != "headless")
        {
            await tree.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            string directory = Path.GetFullPath(ScenarioArtifacts.Directory ?? ".artifacts/built_part_look");
            Directory.CreateDirectory(directory);
            picture = Path.Combine(directory, "built_parts_3d.png");
            if (tree.Root.GetTexture().GetImage().SavePng(picture) != Error.Ok)
                picture = null;
            messages.Add($"picture={picture}");
        }

        sandbox.SetPresentationMode(PresentationMode.LegacyCircles);
        await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        bool flatInLegacy = bodies.All(body => body.DrawsShape) && !sandbox.PartVisual!.Visible;

        string detail = $"drawn={drawn} parts={bodies.Length} picture={picture ?? "none"}";
        checks.Add(new StartupCheck("built_parts_draw_as_3d_shapes", shapesIn3D, detail));
        checks.Add(new StartupCheck("built_part_shapes_follow_their_bodies", aligned, detail));
        checks.Add(new StartupCheck("built_parts_draw_flat_in_legacy", flatInLegacy, detail));

        await M4LifecycleScenarioSupport.Cleanup(tree, sandbox);
        bool passed = checks.All(check => check.Passed);
        return new ScenarioResult(passed, checks, messages);
    }

    private static void Place(SandboxRoot sandbox, SemanticDefinitionId definition, float x, float y, float rotation) =>
        sandbox.PlaceBuiltPart(new PlacedSandboxPart(
            SandboxPartId.New(), definition, new CanonicalRoomPosition(x, y), rotation,
            new SandboxPartOverrides(Frozen: true)));
}
