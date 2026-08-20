using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using DesktopBuddy.App;
using DesktopBuddy.Buddy.Physics;
using DesktopBuddy.Domain.Tools;
using DesktopBuddy.Interaction;
using DesktopBuddy.Presentation3D;
using DesktopBuddy.Tools;
using Godot;

namespace DesktopBuddy.Testing;

/// <summary>
/// Windowed evidence capture for the boxing-glove mesh and the cartoon impact frame.
/// Screenshots aid human review; the semantic scenarios remain the pass/fail authority.
/// Two captures: an isolated three-angle turntable of the glove mesh in its own viewport,
/// and a frame sequence through one real cursor-driven punch.
/// </summary>
public sealed class GloveImpactVisualScenario : IScenario
{
    private const int TurntableSize = 512;

    public string Id => "glove_impact_visual";

    public async Task<ScenarioResult> RunAsync(SceneTree tree, ulong seed)
    {
        var checks = new List<StartupCheck>();
        var messages = new List<string> { $"seed={seed}" };
        string directory = Path.GetFullPath(ScenarioArtifacts.Directory ?? ".artifacts/glove_impact_visual");
        Directory.CreateDirectory(directory);

        var saved = new List<string>();
        saved.AddRange(await CaptureTurntable(tree, directory));

        PackedScene? packed = GD.Load<PackedScene>("res://scenes/buddy_lab.tscn");
        if (packed is null)
        {
            checks.Add(new StartupCheck("glove_visual_scene_loadable", false, "buddy_lab"));
            return new ScenarioResult(false, checks, messages);
        }

        BuddyLab lab = packed.Instantiate<BuddyLab>();
        tree.Root.AddChild(lab);
        await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        lab.GetNode<CanvasLayer>("LabUi").Visible = false;
        lab.BoundaryVisualizer.Visible = false;
        await ScenarioSteps.WaitForStanding(tree, lab, 1200);

        lab.Pipeline.SelectTool(ToolId.BoxingGlove);
        PuppetPartBody head = lab.Buddy.Rig.Head;
        Vector2 approach = head.GlobalPosition - new Vector2(180.0f, 0.0f);
        lab.CursorTools.MoveCursor(approach);
        for (int tick = 0; tick < 30; tick++)
            await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);

        bool struck = false;
        void OnAccepted(AcceptedImpact _impact) => struck = true;
        lab.Pipeline.ImpactAccepted += OnAccepted;

        // A fast straight cursor sweep through the head: the same input path a player has,
        // so the captured frames are the shipped effect and not a staged one.
        Vector2 cursor = approach;
        bool approachSaved = false;
        for (int tick = 0; tick < 40 && !struck; tick++)
        {
            cursor += new Vector2(26.0f, 0.0f);
            lab.CursorTools.MoveCursor(cursor);
            await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
            if (!approachSaved && cursor.DistanceTo(head.GlobalPosition) < 70.0f)
            {
                await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
                saved.Add(Save(tree, directory, "impact_1_approach.png", ScreenPoint(lab, head.GlobalPosition)));
                approachSaved = true;
            }
        }

        lab.Pipeline.ImpactAccepted -= OnAccepted;
        string[] sequence = ["impact_2_contact_frame.png", "impact_3_speed_lines.png", "impact_4_shockwave.png", "impact_5_recovery.png"];
        int captured = 0;
        for (int frame = 0; frame < 30 && captured < sequence.Length; frame++)
        {
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            if (frame is 0 or 2 or 6 or 20)
            {
                saved.Add(Save(
                    tree,
                    directory,
                    sequence[captured++],
                    ScreenPoint(lab, lab.ImpactFeedback.LastImpactWorldPoint)));
            }
        }

        checks.Add(new StartupCheck("glove_punch_landed", struck, $"struck={struck}"));
        checks.Add(new StartupCheck(
            "glove_visual_frames_saved",
            saved.TrueForAll(File.Exists) && saved.Count == 3 + 1 + sequence.Length,
            string.Join(' ', saved)));
        messages.AddRange(saved);

        lab.QueueFree();
        bool passed = true;
        foreach (StartupCheck check in checks) passed &= check.Passed;
        return new ScenarioResult(passed, checks, messages);
    }

    /// <summary>
    /// The glove alone, in its own lit viewport, from three yaw angles. The runtime look uses
    /// the same vertex-coloured material the cursor presenter builds.
    /// </summary>
    private static async Task<List<string>> CaptureTurntable(SceneTree tree, string directory)
    {
        var profile = GD.Load<CursorToolProfile>("res://data/buddy/lab_cursor_tool_boxing_glove.tres");
        var viewport = new SubViewport
        {
            Size = new Vector2I(TurntableSize, TurntableSize),
            OwnWorld3D = true,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
            TransparentBg = false,
        };
        tree.Root.AddChild(viewport);
        var glove = new MeshInstance3D
        {
            Mesh = BoxingGloveMeshBuilder.Build(profile),
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = Colors.White,
                VertexColorUseAsAlbedo = true,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.PerPixel,
                Roughness = 0.72f,
                Metallic = 0.0f,
            },
        };
        viewport.AddChild(glove);
        viewport.AddChild(new Camera3D
        {
            Position = new Vector3(0.0f, 0.0f, 300.0f),
            Projection = Camera3D.ProjectionType.Orthogonal,
            Size = 100.0f,
            Current = true,
        });
        viewport.AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-32.0f, -34.0f, 0.0f) });
        viewport.AddChild(new DirectionalLight3D
        {
            RotationDegrees = new Vector3(24.0f, 140.0f, 0.0f),
            LightEnergy = 0.45f,
        });

        var saved = new List<string>();
        (string Name, Vector3 Euler)[] angles =
        [
            ("glove_1_side.png", new Vector3(0.0f, 0.0f, 0.0f)),
            ("glove_2_three_quarter.png", new Vector3(-12.0f, 38.0f, 0.0f)),
            ("glove_3_front.png", new Vector3(-8.0f, 86.0f, 0.0f)),
        ];
        foreach ((string name, Vector3 euler) in angles)
        {
            glove.RotationDegrees = euler;
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            string path = Path.Combine(directory, name);
            viewport.GetTexture().GetImage().SavePng(path);
            saved.Add(path);
        }

        viewport.QueueFree();
        await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        return saved;
    }

    /// <summary>Where a world point currently lands in viewport pixels.</summary>
    private static Vector2 ScreenPoint(BuddyLab lab, Vector2 worldPoint) =>
        lab.ImpactFeedback.GetGlobalTransformWithCanvas() * lab.ImpactFeedback.ToLocal(worldPoint);

    /// <summary>
    /// Saves the frame cropped around the point of interest and tripled with nearest-neighbour
    /// sampling. The game window is far larger than the buddy, and an owner reviewing a hit
    /// should not have to hunt for a 60-pixel effect in a 1280-pixel screenshot.
    /// </summary>
    private static string Save(SceneTree tree, string directory, string name, Vector2? focus = null)
    {
        Image image = tree.Root.GetTexture().GetImage();
        if (focus is Vector2 point)
        {
            const int width = 380;
            const int height = 300;
            var region = new Rect2I(
                Mathf.Clamp((int)point.X - (width / 2), 0, Mathf.Max(0, image.GetWidth() - width)),
                Mathf.Clamp((int)point.Y - (height / 2), 0, Mathf.Max(0, image.GetHeight() - height)),
                Mathf.Min(width, image.GetWidth()),
                Mathf.Min(height, image.GetHeight()));
            image = image.GetRegion(region);
            image.Resize(image.GetWidth() * 3, image.GetHeight() * 3, Image.Interpolation.Nearest);
        }

        string path = Path.Combine(directory, name);
        image.SavePng(path);
        return path;
    }
}
