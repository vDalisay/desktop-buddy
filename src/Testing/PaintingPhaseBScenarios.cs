using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DesktopBuddy.App;
using DesktopBuddy.Buddy.Physics;
using DesktopBuddy.Buddy.Presentation3D;
using DesktopBuddy.Domain.Characters;
using DesktopBuddy.Domain.Painting;
using DesktopBuddy.Persistence.Characters;
using Godot;

namespace DesktopBuddy.Testing;

internal static class PaintingScenarioSupport
{
    public static string Root(string id)
    {
        string root = Path.Combine(Path.GetTempPath(), "desktop-buddy-phase-b", id, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    public static ScenarioResult Result(List<StartupCheck> checks, ulong seed) =>
        new(checks.All(check => check.Passed), checks, [$"seed={seed}"]);

    public static Dictionary<PaintPart, ReadOnlyMemory<byte>> Painted(params PaintPart[] parts)
    {
        var result = new Dictionary<PaintPart, ReadOnlyMemory<byte>>();
        foreach (PaintPart part in parts)
        {
            var surface = new PaintSurface();
            surface.Stamp(new PaintPoint(0.5, 0.5), 32, PaintTool.Brush, new PaintColor(20, 100, 220));
            result.Add(part, surface.ClonePixels());
        }
        return result;
    }

    public static Dictionary<PaintPart, byte[]> PaintedBytes(params PaintPart[] parts)
    {
        var result = new Dictionary<PaintPart, byte[]>();
        foreach ((PaintPart part, ReadOnlyMemory<byte> pixels) in Painted(parts))
            result.Add(part, pixels.ToArray());
        return result;
    }

    public static void Cleanup(string root)
    {
        try { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}

public sealed class PaintFrontalUvMappingScenario : IScenario
{
    public string Id => "paint_frontal_uv_mapping";
    public Task<ScenarioResult> RunAsync(SceneTree tree, ulong seed)
    {
        var checks = new List<StartupCheck>();
        FrontalPaintMapper mapper = FrontalPaintMapper.CreateDefault();
        PaintPoint[] points = [
            new(0, -50), new(0, 0), new(-38, -5), new(38, -5), new(-22, 55), new(22, 55)];
        bool allHit = points.All(point => mapper.TryMap(point, out PaintHit hit) && hit.IsValid);
        checks.Add(new StartupCheck("phase_b_all_parts_have_finite_uv", allHit, $"hits={points.Length}"));
        checks.Add(new StartupCheck("phase_b_empty_canvas_misses", !mapper.TryMap(new PaintPoint(300, 300), out _), "far point"));
        checks.Add(CheckShapesMatchTrustedRig(mapper));
        checks.Add(CheckCanvasFraming());
        return Task.FromResult(PaintingScenarioSupport.Result(checks, seed));
    }

    /// <summary>
    /// Canvas pointer positions must resolve through the same framing the preview camera uses:
    /// 400 world units vertically, widened by the SubViewport aspect.
    /// </summary>
    private static StartupCheck CheckCanvasFraming()
    {
        var canvas = new CharacterEditor.PaintCanvasControl
        {
            Size = new Vector2(420, 360),
            ViewportSize = new Vector2(420, 360),
        };
        try
        {
            // Canvas centre is the torso origin; these are the head and right foot rest centres.
            bool matches =
                canvas.PartAt(new Vector2(210, 180)) == PaintPart.Torso &&
                canvas.PartAt(new Vector2(210, 135)) == PaintPart.Head &&
                canvas.PartAt(new Vector2(230, 230)) == PaintPart.RightFoot &&
                canvas.PartAt(new Vector2(4, 4)) is null;
            return new StartupCheck(
                "phase_b_canvas_pointer_matches_camera_framing",
                matches,
                $"head={canvas.PartAt(new Vector2(210, 135))} foot={canvas.PartAt(new Vector2(230, 230))}");
        }
        finally
        {
            canvas.Free();
        }
    }

    /// <summary>
    /// The engine-free mapper hard-codes the trusted rest anatomy, so this is the one check that
    /// fails if `lab_puppet_rig.tres` or the torso capsule height ever drifts away from it.
    /// </summary>
    private static StartupCheck CheckShapesMatchTrustedRig(FrontalPaintMapper mapper)
    {
        var rig = GD.Load<PuppetRigProfile>("res://data/buddy/lab_puppet_rig.tres");
        var visual = GD.Load<BuddyVisualProfile>("res://data/buddy/lab_buddy_visual.tres");
        if (rig is null || visual is null)
            return new StartupCheck("phase_b_mapper_matches_trusted_rig", false, "trusted resources missing");

        var detail = new List<string>();
        foreach (PaintPartShape shape in mapper.Shapes)
        {
            var partId = (BuddyPartId)(int)shape.Part;
            PuppetPartDefinition? part = rig.FindPart(partId);
            PartVisualDefinition? appearance = visual.FindPart(partId);
            if (part is null || appearance is null)
            {
                detail.Add($"{shape.Part}=missing");
                continue;
            }
            double radius = part.Radius * appearance.MeshRadiusScale;
            bool capsule = shape.Kind == PaintShapeKind.Capsule;
            double expectedHalfHeight = capsule ? radius * visual.CapsuleHeightScale / 2.0 : radius;
            if (capsule != (partId == BuddyPartId.Torso) ||
                !Approximately(shape.Center.X, part.RestPosition.X) ||
                !Approximately(shape.Center.Y, part.RestPosition.Y) ||
                !Approximately(shape.Radius, radius) ||
                !Approximately(shape.HalfHeight, expectedHalfHeight) ||
                !Approximately(shape.Depth, appearance.DepthOffset))
            {
                detail.Add($"{shape.Part}=drifted");
            }
        }

        return new StartupCheck(
            "phase_b_mapper_matches_trusted_rig",
            detail.Count == 0,
            detail.Count == 0 ? "trusted rest anatomy" : string.Join(",", detail));
    }

    private static bool Approximately(double left, double right) => Math.Abs(left - right) < 0.001;
}

public sealed class PaintStrokeAndEraserScenario : IScenario
{
    public string Id => "paint_stroke_and_eraser";
    public Task<ScenarioResult> RunAsync(SceneTree tree, ulong seed)
    {
        var checks = new List<StartupCheck>();
        var workspace = new PaintWorkspace();
        PaintHit a = new(PaintPart.Head, new PaintPoint(0.45, 0.5), 0);
        PaintHit b = new(PaintPart.Head, new PaintPoint(0.55, 0.5), 0);
        string blank = workspace.Surfaces[PaintPart.Head].ComputeHash();
        workspace.BeginGesture(a); workspace.ContinueGesture(b); workspace.EndGesture();
        string painted = workspace.Surfaces[PaintPart.Head].ComputeHash();
        workspace.SelectedTool = PaintTool.Eraser;
        workspace.BeginGesture(new PaintHit(PaintPart.Head, new PaintPoint(0.5, 0.5), 0));
        workspace.EndGesture();
        checks.Add(new StartupCheck("phase_b_stroke_changes_pixels", painted != blank, painted));
        checks.Add(new StartupCheck("phase_b_eraser_changes_alpha", workspace.Surfaces[PaintPart.Head].ComputeHash() != painted, "erased center"));
        return Task.FromResult(PaintingScenarioSupport.Result(checks, seed));
    }
}

public sealed class PaintMultiPartStrokeUndoScenario : IScenario
{
    public string Id => "paint_multi_part_stroke_undo";
    public Task<ScenarioResult> RunAsync(SceneTree tree, ulong seed)
    {
        var checks = new List<StartupCheck>();
        var workspace = new PaintWorkspace();
        string head = workspace.Surfaces[PaintPart.Head].ComputeHash();
        string torso = workspace.Surfaces[PaintPart.Torso].ComputeHash();
        workspace.BeginGesture(new PaintHit(PaintPart.Head, new PaintPoint(0.5, 0.5), 0));
        workspace.ContinueGesture(null);
        workspace.ContinueGesture(new PaintHit(PaintPart.Torso, new PaintPoint(0.5, 0.5), 0));
        workspace.EndGesture();
        bool undone = workspace.Undo();
        checks.Add(new StartupCheck("phase_b_multi_part_is_one_command", undone && !workspace.CanUndo, $"undo={undone}"));
        checks.Add(new StartupCheck("phase_b_multi_part_undo_byte_exact",
            workspace.Surfaces[PaintPart.Head].ComputeHash() == head &&
            workspace.Surfaces[PaintPart.Torso].ComputeHash() == torso, "hashes restored"));
        return Task.FromResult(PaintingScenarioSupport.Result(checks, seed));
    }
}

public sealed class PaintEraseAllUndoScenario : IScenario
{
    public string Id => "paint_erase_all_undo";
    public Task<ScenarioResult> RunAsync(SceneTree tree, ulong seed)
    {
        var checks = new List<StartupCheck>();
        var workspace = new PaintWorkspace();
        workspace.BeginGesture(new PaintHit(PaintPart.LeftHand, new PaintPoint(0.5, 0.5), 0));
        workspace.EndGesture();
        string painted = workspace.Surfaces[PaintPart.LeftHand].ComputeHash();
        workspace.EraseAll();
        bool restored = workspace.Undo() && workspace.Surfaces[PaintPart.LeftHand].ComputeHash() == painted;
        checks.Add(new StartupCheck("phase_b_erase_all_is_undoable", restored, painted));
        return Task.FromResult(PaintingScenarioSupport.Result(checks, seed));
    }
}

public sealed class PaintMemoryBudgetScenario : IScenario
{
    public string Id => "paint_memory_budget";
    public Task<ScenarioResult> RunAsync(SceneTree tree, ulong seed)
    {
        var checks = new List<StartupCheck>();
        var workspace = new PaintWorkspace();
        for (int i = 0; i < 60; i++)
        {
            PaintPart part = (PaintPart)(i % 6);
            workspace.BeginGesture(new PaintHit(part, new PaintPoint((i % 10) / 10.0, 0.5), 0));
            workspace.EndGesture();
        }
        checks.Add(new StartupCheck("phase_b_undo_cap_respected",
            workspace.UndoMemoryBytes <= PaintPolicy.UndoBudgetBytes,
            $"undo={workspace.UndoMemoryBytes}"));
        checks.Add(new StartupCheck("phase_b_cpu_budget_locked",
            PaintPolicy.WorkingSurfaceBudgetBytes + PaintPolicy.UndoBudgetBytes <= PaintPolicy.EditingBudgetBytes,
            $"working={PaintPolicy.WorkingSurfaceBudgetBytes}"));
        return Task.FromResult(PaintingScenarioSupport.Result(checks, seed));
    }
}

public sealed class PaintPersistenceRoundtripScenario : IScenario
{
    public string Id => "paint_persistence_roundtrip";
    public async Task<ScenarioResult> RunAsync(SceneTree tree, ulong seed)
    {
        string root = PaintingScenarioSupport.Root(Id);
        var checks = new List<StartupCheck>();
        try
        {
            Guid id = Guid.Parse("61000000-0000-4000-8000-000000000061");
            var store = new CharacterPaintStore(new CharacterFileSystem(), root);
            Dictionary<PaintPart, ReadOnlyMemory<byte>> source = PaintingScenarioSupport.Painted(PaintPart.Head, PaintPart.RightFoot);
            CharacterPaintSaveResult saved = await store.SaveAsync(CharacterDocument.CreateDefault(id, "Painted"), source);
            CharacterPaintLoadResult loaded = await store.LoadAsync(id);
            bool exact = saved.IsSuccess && loaded.IsSuccess && loaded.Surfaces.Count == 2 &&
                loaded.Surfaces[PaintPart.Head].AsSpan().SequenceEqual(source[PaintPart.Head].Span) &&
                loaded.Surfaces[PaintPart.RightFoot].AsSpan().SequenceEqual(source[PaintPart.RightFoot].Span);
            checks.Add(new StartupCheck("phase_b_png_roundtrip_byte_exact", exact, $"surfaces={loaded.Surfaces.Count}"));
            checks.Add(new StartupCheck("phase_b_manifest_only_declares_nonblank",
                loaded.Character.Document?.Paint.Declared().Count() == 2, "declared paint"));
        }
        finally { PaintingScenarioSupport.Cleanup(root); }
        return PaintingScenarioSupport.Result(checks, seed);
    }
}

public sealed class PaintInvalidPngRejectedScenario : IScenario
{
    public string Id => "paint_invalid_png_rejected";
    public async Task<ScenarioResult> RunAsync(SceneTree tree, ulong seed)
    {
        string root = PaintingScenarioSupport.Root(Id);
        var checks = new List<StartupCheck>();
        try
        {
            Guid id = Guid.Parse("62000000-0000-4000-8000-000000000062");
            var store = new CharacterPaintStore(new CharacterFileSystem(), root);
            await store.SaveAsync(CharacterDocument.CreateDefault(id, "Invalid"), PaintingScenarioSupport.Painted(PaintPart.Head));
            File.WriteAllBytes(Path.Combine(root, id.ToString("N"), "paint", "head.png"), [1, 2, 3, 4]);
            CharacterPaintLoadResult loaded = await store.LoadAsync(id);
            checks.Add(new StartupCheck("phase_b_corrupt_png_rejected", !loaded.IsSuccess, loaded.Detail ?? "rejected"));
        }
        finally { PaintingScenarioSupport.Cleanup(root); }
        return PaintingScenarioSupport.Result(checks, seed);
    }
}

/// <summary>
/// Paint renders on its own shell above the base colour: blank pixels reveal the selected base
/// colour, painted pixels keep the picked colour, decals stay above paint, and paint scorches.
/// </summary>
public sealed class PaintLayerOrderScenario : IScenario
{
    public string Id => "paint_under_expression_layer_order";

    public async Task<ScenarioResult> RunAsync(SceneTree tree, ulong seed)
    {
        var checks = new List<StartupCheck>();
        BuddyLab? lab = await ScenarioSteps.CreateControlledImpactLab(tree, 10.0f, 20.0f);
        if (lab is null)
        {
            checks.Add(new StartupCheck("phase_b_lab_loadable", false, "buddy_lab"));
            return new ScenarioResult(false, checks, [$"seed={seed}"]);
        }

        var preview = new BuddyVisualRigView { Name = "PhaseBPaintLayerPreview" };
        lab.AddChild(preview);
        preview.Initialize(lab.Buddy.VisualProfile, new StaticBuddyVisualTransformSource(
            lab.Buddy.Rig.Profile, Vector2.Zero));
        await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);

        MeshInstance3D head = preview.GetPartMesh(BuddyPartId.Head);
        MeshInstance3D? layer = head.GetParent().GetNodeOrNull<MeshInstance3D>("Paint");
        checks.Add(new StartupCheck(
            "phase_b_unpainted_parts_show_base_colour",
            layer is not null && !layer.Visible &&
            (head.MaterialOverride as StandardMaterial3D)?.AlbedoTexture is null,
            $"layer={layer?.Visible}"));

        Color baseColour = preview.ActiveBasePartAlbedo(BuddyPartId.Head);
        var bridge = new RuntimePaintTextureBridge(preview);
        bridge.Apply(PaintingScenarioSupport.PaintedBytes(PaintPart.Head));
        var paintMaterial = layer?.MaterialOverride as StandardMaterial3D;
        checks.Add(new StartupCheck(
            "phase_b_paint_binds_above_untouched_base",
            layer is { Visible: true } &&
            paintMaterial?.AlbedoTexture is not null &&
            paintMaterial.Transparency == BaseMaterial3D.TransparencyEnum.AlphaScissor &&
            paintMaterial.AlbedoColor == Colors.White &&
            (head.MaterialOverride as StandardMaterial3D)?.AlbedoTexture is null &&
            preview.ActiveBasePartAlbedo(BuddyPartId.Head) == baseColour,
            $"albedo={paintMaterial?.AlbedoColor}"));

        checks.Add(new StartupCheck(
            "phase_b_decals_stay_above_paint",
            preview.FacePlate is null ||
            preview.FacePlate.Position.Z > BuddyLookMaterialLibrary.PaintShellGrowAmount,
            $"plate_z={preview.FacePlate?.Position.Z} grow={BuddyLookMaterialLibrary.PaintShellGrowAmount}"));

        preview.SetPartScorch(BuddyPartId.Head, 1.0f, Colors.Black);
        checks.Add(new StartupCheck(
            "phase_b_paint_scorches_with_the_body",
            paintMaterial?.AlbedoColor == Colors.Black,
            $"scorched={paintMaterial?.AlbedoColor}"));

        bridge.Dispose();
        preview.QueueFree();
        lab.QueueFree();
        await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        return PaintingScenarioSupport.Result(checks, seed);
    }
}

public sealed class PaintPreviewHasNoPhysicsScenario : IScenario
{
    public string Id => "paint_preview_has_no_physics";
    public Task<ScenarioResult> RunAsync(SceneTree tree, ulong seed)
    {
        string assembly = typeof(CharacterEditor.PaintCanvasControl).Assembly.FullName ?? string.Empty;
        var checks = new List<StartupCheck>
        {
            new("phase_b_canvas_is_control", typeof(CharacterEditor.PaintCanvasControl).IsSubclassOf(typeof(Control)), assembly),
            new("phase_b_workspace_is_engine_free", typeof(PaintWorkspace).Assembly != typeof(Control).Assembly, typeof(PaintWorkspace).Assembly.FullName ?? string.Empty),
        };
        return Task.FromResult(PaintingScenarioSupport.Result(checks, seed));
    }
}
