using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DesktopBuddy.App;
using DesktopBuddy.CharacterEditor;
using Godot;

namespace DesktopBuddy.Testing;

public sealed class PaintLocalizationScenario : IScenario
{
    public string Id => "paint_localization_fallback";

    public Task<ScenarioResult> RunAsync(SceneTree tree, ulong seed)
    {
        string[] keys =
        [
            PaintUiText.Open,
            PaintUiText.OpenTooltip,
            PaintUiText.AppearanceControls,
            PaintUiText.Brush,
            PaintUiText.Eraser,
            PaintUiText.ColorTooltip,
            PaintUiText.BrushSize,
            PaintUiText.Undo,
            PaintUiText.EraseAll,
            PaintUiText.ZoomOut,
            PaintUiText.ZoomIn,
            PaintUiText.ResetView,
            PaintUiText.HoverHelp,
            PaintUiText.InputHelp,
            PaintUiText.EraseAllTitle,
            PaintUiText.EraseAllBody,
            PaintUiText.Canvas,
            PaintUiText.Status,
        ];
        bool allFallbacks = keys.All(PaintUiText.HasEnglishFallback);
        bool noRawKeys = keys.All(key => PaintUiText.Get(key) != key);
        string formatted = PaintUiText.Format(PaintUiText.Status, "Brush", "Head", 1.0);
        var checks = new List<StartupCheck>
        {
            new("phase_b_paint_localization_has_complete_english_fallback", allFallbacks,
                $"keys={keys.Length}"),
            new("phase_b_paint_localization_never_shows_raw_key", noRawKeys,
                $"keys={keys.Length}"),
            new("phase_b_paint_localization_formats_status_placeholders",
                formatted.Contains("Brush") && formatted.Contains("Head") && formatted.Contains("1.0"),
                formatted),
        };
        return Task.FromResult(new ScenarioResult(
            checks.All(static check => check.Passed),
            checks,
            [$"seed={seed}"]));
    }
}
