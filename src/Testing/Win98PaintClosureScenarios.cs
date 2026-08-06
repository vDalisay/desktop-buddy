using System.Collections.Generic;
using System.Threading.Tasks;
using DesktopBuddy.Domain.Painting;
using Godot;

namespace DesktopBuddy.Testing;

/// <summary>Regression coverage for the remaining editor-state and eyedropper closure behavior.</summary>
public sealed class PaintEyedropperSamplingScenario : IScenario
{
    public string Id => "paint_eyedropper_sampling";

    public Task<ScenarioResult> RunAsync(SceneTree tree, ulong seed)
    {
        var checks = new List<StartupCheck>();
        var surface = new PaintSurface();
        var wanted = new PaintColor(17, 91, 203);
        var centre = new PaintPoint(0.5, 0.5);

        bool blankRejected = !surface.TrySample(centre, out _);
        surface.Stamp(centre, 24, PaintTool.Brush, wanted);
        bool paintedSampled = surface.TrySample(centre, out PaintColor sampled) && sampled == wanted;
        bool invalidRejected =
            !surface.TrySample(new PaintPoint(-0.01, 0.5), out _) &&
            !surface.TrySample(new PaintPoint(0.5, 1.01), out _);

        surface.Stamp(new PaintPoint(0.0, 0.5), 24, PaintTool.Brush, wanted);
        bool seamSampled =
            surface.TrySample(new PaintPoint(0.0, 0.5), out PaintColor left) && left == wanted;

        checks.Add(new StartupCheck("paint_eyedropper_rejects_transparent_pixels", blankRejected, "blank centre"));
        checks.Add(new StartupCheck("paint_eyedropper_returns_exact_rgb", paintedSampled, $"sample={sampled}"));
        checks.Add(new StartupCheck("paint_eyedropper_rejects_invalid_uv", invalidRejected, "outside [0,1]"));
        checks.Add(new StartupCheck("paint_eyedropper_samples_texture_seam", seamSampled, $"sample={left}"));
        return Task.FromResult(PaintingScenarioSupport.Result(checks, seed));
    }
}

/// <summary>Documents and verifies the editor's dirty/save/undo/redo lifecycle.</summary>
public sealed class PaintDocumentStateScenario : IScenario
{
    public string Id => "paint_document_state";

    public Task<ScenarioResult> RunAsync(SceneTree tree, ulong seed)
    {
        var checks = new List<StartupCheck>();
        var workspace = new PaintWorkspace();
        PaintHit hit = new(PaintPart.Torso, new PaintPoint(0.5, 0.5), 0.0);

        bool startsClean = !workspace.IsDirty && !workspace.CanUndo && !workspace.CanRedo;
        workspace.BeginGesture(hit);
        workspace.EndGesture();
        bool editMarksDirty = workspace.IsDirty && workspace.CanUndo && !workspace.CanRedo;

        workspace.MarkSaved();
        bool saveClearsHistory = !workspace.IsDirty && !workspace.CanUndo && !workspace.CanRedo;

        workspace.BeginGesture(new PaintHit(PaintPart.Head, new PaintPoint(0.5, 0.5), 0.0));
        workspace.EndGesture();
        bool undoWorks = workspace.Undo() && workspace.IsDirty && workspace.CanRedo;
        bool redoWorks = workspace.Redo() && workspace.IsDirty && workspace.CanUndo;

        workspace.MarkSaved();
        workspace.EraseAll();
        bool eraseAllDirty = workspace.IsDirty && workspace.CanUndo;

        checks.Add(new StartupCheck("paint_document_starts_clean", startsClean, "new workspace"));
        checks.Add(new StartupCheck("paint_document_edit_marks_dirty", editMarksDirty, "stroke committed"));
        checks.Add(new StartupCheck("paint_document_save_clears_history", saveClearsHistory, "saved baseline"));
        checks.Add(new StartupCheck("paint_document_undo_enables_redo", undoWorks, "undo"));
        checks.Add(new StartupCheck("paint_document_redo_restores_undo", redoWorks, "redo"));
        checks.Add(new StartupCheck("paint_document_erase_all_marks_dirty", eraseAllDirty, "erase all"));
        return Task.FromResult(PaintingScenarioSupport.Result(checks, seed));
    }
}
