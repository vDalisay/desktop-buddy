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
/// also save a screenshot of the parts and devices (the Lamp lit) for the owner.
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
        Place(sandbox, SandboxPartCatalogue.Button, 0.3f, 0.35f, 0.0f);
        Place(sandbox, SandboxPartCatalogue.Timer, 0.5f, 0.35f, 0.0f);
        Place(sandbox, SandboxPartCatalogue.Lamp, 0.7f, 0.35f, 0.0f);
        Place(sandbox, SandboxPartCatalogue.Piston, 0.85f, 0.35f, 0.0f);
        // Beside the Lamp, to see its light land on something.
        SandboxPartId neighbour = Place(sandbox, SandboxPartCatalogue.MetalBlock, 0.745f, 0.35f, 0.0f);
        for (int frame = 0; frame < 10; frame++)
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);

        SandboxPartBody[] bodies = sandbox.BuiltParts.Values.ToArray();
        foreach (SandboxPartBody body in bodies.Where(body => body.Definition.Device == SandboxDeviceKind.Lamp))
            body.Lit = true;
        int drawn = sandbox.PartVisual?.DrawnCount ?? 0;
        bool shapesIn3D = drawn == bodies.Length && bodies.All(body => !body.DrawsShape) &&
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
            Image litImage = tree.Root.GetTexture().GetImage();
            if (litImage.SavePng(picture) != Error.Ok)
                picture = null;
            messages.Add($"picture={picture}");

            // A lit Lamp lights the block beside it: the block is brighter lit than unlit.
            Vector2 at = sandbox.BuiltParts[neighbour].GetGlobalTransformWithCanvas().Origin;
            float litBrightness = Brightness(litImage, at);
            foreach (SandboxPartBody body in bodies.Where(body => body.Definition.Device == SandboxDeviceKind.Lamp))
                body.Lit = false;
            for (int frame = 0; frame < 3; frame++)
                await tree.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            float darkBrightness = Brightness(tree.Root.GetTexture().GetImage(), at);
            checks.Add(new StartupCheck("lit_lamp_lights_its_neighbour", litBrightness - darkBrightness > 0.04f,
                $"lit={litBrightness:F3} dark={darkBrightness:F3} at={at}"));
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

    private static SandboxPartId Place(SandboxRoot sandbox, SemanticDefinitionId definition, float x, float y, float rotation)
    {
        var part = new PlacedSandboxPart(
            SandboxPartId.New(), definition, new CanonicalRoomPosition(x, y), rotation,
            new SandboxPartOverrides(Frozen: true));
        sandbox.PlaceBuiltPart(part);
        return part.PartId;
    }

    /// <summary>Mean luminance of a small patch, a little off centre to miss the frozen nail.</summary>
    private static float Brightness(Image image, Vector2 centre)
    {
        float sum = 0.0f;
        int count = 0;
        for (int x = -8; x <= -4; x++)
        {
            for (int y = -8; y <= -4; y++)
            {
                int px = Mathf.Clamp((int)centre.X + x, 0, image.GetWidth() - 1);
                int py = Mathf.Clamp((int)centre.Y + y, 0, image.GetHeight() - 1);
                sum += image.GetPixel(px, py).Luminance;
                count++;
            }
        }
        return sum / count;
    }
}
