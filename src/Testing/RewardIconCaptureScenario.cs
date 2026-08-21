using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using DesktopBuddy.Objects;
using DesktopBuddy.Presentation3D;
using DesktopBuddy.Tools;
using Godot;

namespace DesktopBuddy.Testing;

/// <summary>
/// Authoring tool, not a gate: renders every tool's own shipped mesh into the reward-icon
/// folder that <see cref="DesktopBuddy.UI.RewardIconProvider"/> already prefers over its drawn
/// fallback. Hand-drawn pixel icons were guesses at what each tool looks like; the game
/// already knows, so the icon is a render of the real thing (owner instruction 2026-08-20).
///
/// <para>Run it when a tool's mesh or authored colours change:
/// <c>--scenario=reward_icon_capture</c>, windowed, then commit the PNGs it writes.</para>
/// </summary>
public sealed class RewardIconCaptureScenario : IScenario
{
    private const int IconSize = 128;

    /// <summary>Rendered wide, then cropped to what landed; see the framing note below.</summary>
    private const int RenderSize = 512;

    public string Id => "reward_icon_capture";

    /// <summary>Yaw/pitch that shows each silhouette off, and how much of the frame it fills.</summary>
    private readonly record struct Shot(string Slug, Vector3 Euler, float Margin = 1.18f);

    public async Task<ScenarioResult> RunAsync(SceneTree tree, ulong seed)
    {
        var checks = new List<StartupCheck>();
        var messages = new List<string>();
        string directory = ProjectSettings.GlobalizePath("res://assets/ui/reward_icons");
        DirAccess.MakeDirRecursiveAbsolute(directory);

        var viewport = new SubViewport
        {
            Size = new Vector2I(RenderSize, RenderSize),
            OwnWorld3D = true,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
            TransparentBg = true,
        };
        tree.Root.AddChild(viewport);
        var camera = new Camera3D
        {
            Position = new Vector3(0.0f, 0.0f, 400.0f),
            Projection = Camera3D.ProjectionType.Orthogonal,
            Size = 260.0f,
            Current = true,
        };
        viewport.AddChild(camera);
        viewport.AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-32.0f, -34.0f, 0.0f) });
        viewport.AddChild(new DirectionalLight3D
        {
            RotationDegrees = new Vector3(24.0f, 140.0f, 0.0f),
            LightEnergy = 0.45f,
        });
        var instance = new MeshInstance3D
        {
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = Colors.White,
                VertexColorUseAsAlbedo = true,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.PerPixel,
                Roughness = 0.7f,
                Metallic = 0.0f,
            },
        };
        viewport.AddChild(instance);

        var written = new List<string>();
        try
        {
            foreach (Shot shot in Shots())
            {
                ArrayMesh? mesh = MeshFor(shot.Slug);
                if (mesh is null)
                {
                    messages.Add($"skipped={shot.Slug} reason=no_mesh");
                    continue;
                }

                instance.Mesh = mesh;
                instance.RotationDegrees = shot.Euler;

                // Framing is done on the rendered pixels, not on the mesh AABB: the tools are
                // authored in wildly different units (a bat in world pixels, a ball in collider
                // radii), so fitting the camera to bounds put half the set at a tenth of its
                // tile. Render wide, then crop what actually landed and scale it up.
                await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
                await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
                string path = Path.Combine(directory, $"{shot.Slug}.png");
                Image image = viewport.GetTexture().GetImage();
                Rect2I used = image.GetUsedRect();
                if (used.Size == Vector2I.Zero)
                {
                    messages.Add($"skipped={shot.Slug} reason=empty_render");
                    continue;
                }

                image = Square(image, used, shot.Margin);
                image.SavePng(path);
                written.Add(shot.Slug);
                messages.Add($"wrote={path}");
            }
        }
        finally
        {
            viewport.QueueFree();
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        }

        bool headless = DisplayServer.GetName() == "headless";
        checks.Add(new StartupCheck(
            "reward_icons_rendered_from_shipped_meshes",
            headless || written.Count == 16,
            $"headless={headless} written={written.Count}/16 [{string.Join(',', written)}]"));
        bool passed = true;
        foreach (StartupCheck check in checks) passed &= check.Passed;
        return new ScenarioResult(passed, checks, messages);
    }

    /// <summary>Crops to the rendered subject, pads it square with a margin, and resizes.</summary>
    private static Image Square(Image image, Rect2I used, float margin)
    {
        Image cropped = image.GetRegion(used);
        int side = Mathf.RoundToInt(Mathf.Max(used.Size.X, used.Size.Y) * margin);
        Image square = Image.CreateEmpty(side, side, false, image.GetFormat());
        square.Fill(new Color(0.0f, 0.0f, 0.0f, 0.0f));
        square.BlitRect(
            cropped,
            new Rect2I(Vector2I.Zero, used.Size),
            new Vector2I((side - used.Size.X) / 2, (side - used.Size.Y) / 2));
        square.Resize(IconSize, IconSize, Image.Interpolation.Lanczos);
        return square;
    }

    private static IEnumerable<Shot> Shots() =>
    [
        new Shot("grab", new Vector3(0.0f, 0.0f, 0.0f)),
        new Shot("power_grab", new Vector3(0.0f, 0.0f, 0.0f)),
        new Shot("pet", new Vector3(0.0f, -28.0f, 0.0f)),
        new Shot("tickle", new Vector3(0.0f, -28.0f, 0.0f)),
        new Shot("baseball_bat", new Vector3(0.0f, -8.0f, 38.0f)),
        new Shot("boxing_glove", new Vector3(-10.0f, 44.0f, 0.0f)),
        new Shot("baseball", new Vector3(-14.0f, 26.0f, 0.0f)),
        new Shot("soccer_ball", new Vector3(-14.0f, 26.0f, 0.0f)),
        new Shot("meal", new Vector3(-34.0f, 22.0f, 0.0f)),
        new Shot("drink", new Vector3(-12.0f, 24.0f, 0.0f)),
        new Shot("repair_kit", new Vector3(-16.0f, 28.0f, 0.0f)),
        new Shot("grenade", new Vector3(-10.0f, 26.0f, 0.0f)),
        new Shot("nerf_blaster", new Vector3(-8.0f, -22.0f, 8.0f)),
        new Shot("pistol", new Vector3(-8.0f, -22.0f, 0.0f)),
        new Shot("shotgun", new Vector3(-8.0f, -22.0f, 0.0f)),
        new Shot("fire_sprayer", new Vector3(-8.0f, -22.0f, 0.0f)),
    ];

    private static ArrayMesh? MeshFor(string slug) => slug switch
    {
        // Grab has no model of its own — it is a cursor and a tether. The tether is what the
        // player actually sees, so both grab tools are drawn as the rope, the paid one thicker
        // and red (owner instruction 2026-08-20).
        "grab" => RopeMesh(radius: 3.2f, new Color("8a8f98")),
        "power_grab" => RopeMesh(radius: 5.6f, new Color("c0392b")),
        "pet" => CareToolMeshBuilder.BuildBrush(),
        "tickle" => CareToolMeshBuilder.BuildFeatherDuster(worldForm: true),
        "baseball_bat" => BatMeshBuilder.Build(
            GD.Load<CursorToolProfile>("res://data/buddy/lab_cursor_tool_baseball_bat.tres")),
        "boxing_glove" => BoxingGloveMeshBuilder.Build(
            GD.Load<CursorToolProfile>("res://data/buddy/lab_cursor_tool_boxing_glove.tres")),
        "baseball" => LooseMesh("res://data/objects/baseball.tres"),
        "soccer_ball" => LooseMesh("res://data/objects/soccer_ball.tres"),
        "meal" => LooseMesh("res://data/objects/meal.tres"),
        "drink" => LooseMesh("res://data/objects/drink.tres"),
        "repair_kit" => LooseMesh("res://data/objects/repair_kit.tres"),
        "grenade" => GrenadeMeshBuilder.Build(
            GD.Load<GrenadeProfile>("res://data/tools/grenade.tres"),
            GD.Load<LooseObjectProfile>("res://data/objects/grenade.tres").Radius,
            pinIn: true),
        "nerf_blaster" => GunMeshBuilder.Build(
            GD.Load<GunProfile>("res://data/tools/gun_nerf_blaster.tres")),
        "pistol" => GunMeshBuilder.Build(GD.Load<GunProfile>("res://data/tools/gun_pistol.tres")),
        "shotgun" => GunMeshBuilder.Build(GD.Load<GunProfile>("res://data/tools/gun_shotgun.tres")),
        "fire_sprayer" => SprayerMeshBuilder.Build(
            GD.Load<FireSprayerProfile>("res://data/tools/fire_sprayer.tres")),
        _ => null,
    };

    private static ArrayMesh? LooseMesh(string path)
    {
        var profile = GD.Load<LooseObjectProfile>(path);
        return profile.Visual3D switch
        {
            LooseObjectVisualKind.SoccerBall => LooseObjectMeshBuilder.SoccerBall(
                profile.Radius, profile.FillColor, profile.OutlineColor),
            LooseObjectVisualKind.Can => LooseObjectMeshBuilder.Can(
                profile.Radius, profile.FillColor, profile.OutlineColor),
            LooseObjectVisualKind.RepairKit => LooseObjectMeshBuilder.RepairKit(
                profile.Radius, profile.FillColor, profile.OutlineColor),
            LooseObjectVisualKind.Baseball => LooseObjectMeshBuilder.Baseball(
                profile.Radius, profile.FillColor, profile.OutlineColor),
            LooseObjectVisualKind.Meal => MealMeshBuilder.PlatedSandwich(
                profile.Radius, profile.FillColor, profile.OutlineColor),
            _ => null,
        };
    }

    /// <summary>A slack length of rope: one swept tube along a shallow catenary.</summary>
    private static ArrayMesh RopeMesh(float radius, Color color)
    {
        const int Segments = 22;
        const int Sides = 10;
        const float Length = 46.0f;
        const float Sag = 13.0f;
        var surface = new SurfaceTool();
        surface.Begin(Mesh.PrimitiveType.Triangles);

        Vector3 Point(int step, int side, float twist)
        {
            float t = (float)step / Segments;
            float x = Mathf.Lerp(-Length, Length, t);
            float y = (Sag * 4.0f * ((t * t) - t)) + Sag;
            float theta = (Mathf.Tau * side / Sides) + twist;
            return new Vector3(x, y + (radius * Mathf.Sin(theta)), radius * Mathf.Cos(theta));
        }

        for (int step = 0; step < Segments; step++)
        {
            // The twist is what makes it read as rope rather than as a hose: the ridges run
            // diagonally across the tube.
            float twist0 = step * 0.55f;
            float twist1 = (step + 1) * 0.55f;
            for (int side = 0; side < Sides; side++)
            {
                Color shade = color.Lerp(Colors.Black, side % 2 == 0 ? 0.0f : 0.18f);
                Vector3 a = Point(step, side, twist0);
                Vector3 b = Point(step + 1, side, twist1);
                Vector3 c = Point(step + 1, side + 1, twist1);
                Vector3 d = Point(step, side + 1, twist0);
                foreach (Vector3 vertex in new[] { a, b, c, a, c, d })
                {
                    surface.SetColor(shade);
                    surface.AddVertex(vertex);
                }
            }
        }

        surface.GenerateNormals();
        return surface.Commit() ?? throw new InvalidOperationException("Rope mesh failed to build.");
    }
}
