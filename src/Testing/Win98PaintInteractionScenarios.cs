using System.Collections.Generic;
using System.Threading.Tasks;
using DesktopBuddy.CharacterEditor;
using DesktopBuddy.Domain.Painting;
using Godot;

namespace DesktopBuddy.Testing;

/// <summary>
/// Regression coverage for the Win98 editor's semantic layer visibility and target filter.
/// These controls must affect paint and eyedropper hit-testing without mutating paint data.
/// </summary>
public sealed class PaintSemanticLayerFilteringScenario : IScenario
{
    public string Id => "paint_semantic_layer_filtering";

    public Task<ScenarioResult> RunAsync(SceneTree tree, ulong seed)
    {
        var checks = new List<StartupCheck>();
        var canvas = new PaintCanvasControl
        {
            Size = new Vector2(420, 360),
            ViewportSize = new Vector2(420, 360),
        };

        try
        {
            Vector2 headPoint = new(210, 135);
            Vector2 torsoPoint = new(210, 180);

            bool baseline =
                canvas.PartAt(headPoint) == PaintPart.Head &&
                canvas.PartAt(torsoPoint) == PaintPart.Torso;
            checks.Add(new StartupCheck(
                "paint_layers_baseline_hits",
                baseline,
                $"head={canvas.PartAt(headPoint)} torso={canvas.PartAt(torsoPoint)}"));

            canvas.ActivePartFilter = PaintPart.Head;
            bool targetFilter =
                canvas.PartAt(headPoint) == PaintPart.Head &&
                canvas.PartAt(torsoPoint) is null;
            checks.Add(new StartupCheck(
                "paint_layers_target_filter_blocks_other_parts",
                targetFilter,
                $"head={canvas.PartAt(headPoint)} torso={canvas.PartAt(torsoPoint)}"));

            canvas.SetPartVisible(PaintPart.Head, visible: false);
            bool hiddenFiltered =
                !canvas.IsPartVisible(PaintPart.Head) &&
                canvas.PartAt(headPoint) is null;
            checks.Add(new StartupCheck(
                "paint_layers_hidden_part_is_not_paintable",
                hiddenFiltered,
                $"visible={canvas.IsPartVisible(PaintPart.Head)} hit={canvas.PartAt(headPoint)}"));

            canvas.ActivePartFilter = null;
            bool hiddenDoesNotBlockOthers = canvas.PartAt(torsoPoint) == PaintPart.Torso;
            checks.Add(new StartupCheck(
                "paint_layers_hidden_part_does_not_block_visible_parts",
                hiddenDoesNotBlockOthers,
                $"torso={canvas.PartAt(torsoPoint)}"));

            canvas.ShowAllParts();
            bool restored =
                canvas.IsPartVisible(PaintPart.Head) &&
                canvas.PartAt(headPoint) == PaintPart.Head;
            checks.Add(new StartupCheck(
                "paint_layers_show_all_restores_hits",
                restored,
                $"visible={canvas.IsPartVisible(PaintPart.Head)} hit={canvas.PartAt(headPoint)}"));
        }
        finally
        {
            canvas.Free();
        }

        return Task.FromResult(PaintingScenarioSupport.Result(checks, seed));
    }
}
