using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DesktopBuddy.App;
using DesktopBuddy.CharacterEditor;
using DesktopBuddy.Domain.Painting;
using Godot;

namespace DesktopBuddy.Testing;

public sealed class PaintUploadCoalescingScenario : IScenario
{
    public string Id => "paint_upload_coalescing";

    public async Task<ScenarioResult> RunAsync(SceneTree tree, ulong seed)
    {
        var checks = new List<StartupCheck>();
        CharacterEditorScenarioSupport.Context context =
            await CharacterEditorScenarioSupport.Create(tree, Id);
        PaintTextureBridge? bridge = null;
        bool originalInputAccumulation = Input.UseAccumulatedInput;
        try
        {
            Input.UseAccumulatedInput = false;
            PaintCanvasControl.EnsurePaintInputAccumulated();
            bool inputAccumulationRestored = Input.UseAccumulatedInput;

            var workspace = new PaintWorkspace();
            bridge = new PaintTextureBridge(context.Preview);

            PaintSurface head = workspace.Surfaces[PaintPart.Head];
            bridge.Queue(PaintPart.Head, head);
            bridge.Queue(PaintPart.Head, head);
            bridge.Queue(PaintPart.Head, head);
            bridge.FlushFrame(workspace.Surfaces);
            int first = bridge.UploadCount;

            bridge.Queue(PaintPart.Head, head);
            bridge.FlushFrame(workspace.Surfaces);
            int equal = bridge.UploadCount;
            bridge.FlushFrame(workspace.Surfaces);
            int idle = bridge.UploadCount;

            workspace.BeginGesture(new PaintHit(PaintPart.Head, new PaintPoint(0.5, 0.5), 0));
            workspace.EndGesture();
            workspace.BeginGesture(new PaintHit(PaintPart.Torso, new PaintPoint(0.5, 0.5), 0));
            workspace.EndGesture();
            foreach ((PaintPart part, PaintSurface surface) in workspace.Surfaces)
                bridge.Queue(part, surface);
            bridge.FlushFrame(workspace.Surfaces);
            int changed = bridge.UploadCount;

            checks.Add(new StartupCheck(
                "phase_b_buddy_paint_keeps_mouse_input_accumulated",
                inputAccumulationRestored,
                $"accumulated={inputAccumulationRestored}"));
            checks.Add(new StartupCheck(
                "phase_b_one_upload_per_queued_part_per_frame",
                first == 1,
                $"first={first}"));
            checks.Add(new StartupCheck(
                "phase_b_equal_revision_and_idle_do_not_upload",
                equal == first && idle == first,
                $"first={first} equal={equal} idle={idle}"));
            checks.Add(new StartupCheck(
                "phase_b_only_dirty_revisions_upload",
                changed == first + 2,
                $"before={first} after={changed}"));
            checks.Add(new StartupCheck(
                "phase_b_gpu_raw_surface_budget_locked",
                PaintPolicy.WorkingSurfaceBudgetBytes == 6L * 1024 * 1024 &&
                PaintPolicy.WorkingSurfaceBudgetBytes <= 8L * 1024 * 1024,
                $"raw={PaintPolicy.WorkingSurfaceBudgetBytes}"));
        }
        finally
        {
            Input.UseAccumulatedInput = originalInputAccumulation;
            bridge?.Dispose();
            await CharacterEditorScenarioSupport.Cleanup(tree, context);
        }

        return new ScenarioResult(
            checks.All(static check => check.Passed),
            checks,
            [$"seed={seed}"]);
    }
}
