using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace DesktopBuddy.Testing;

/// <summary>
/// Registers the Phase B painting scenarios on the existing stable registry, the same way the
/// Phase A editor tasks extend it.
/// </summary>
internal static class PaintingPhaseBScenarioRegistration
{
    [ModuleInitializer]
    internal static void Register()
    {
        FieldInfo field = typeof(ScenarioCatalog).GetField(
            "Factories",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Scenario registry field was not found.");
        var factories = (Dictionary<string, Func<IScenario>>?)field.GetValue(null)
            ?? throw new InvalidOperationException("Scenario registry was not initialized.");
        factories["paint_frontal_uv_mapping"] = () => new PaintFrontalUvMappingScenario();
        factories["paint_canvas_aspect_mapping"] = () => new PaintCanvasAspectMappingScenario();
        factories["paint_stroke_and_eraser"] = () => new PaintStrokeAndEraserScenario();
        factories["paint_multi_part_stroke_undo"] = () => new PaintMultiPartStrokeUndoScenario();
        factories["paint_erase_all_undo"] = () => new PaintEraseAllUndoScenario();
        factories["paint_memory_budget"] = () => new PaintMemoryBudgetScenario();
        factories["paint_persistence_roundtrip"] = () => new PaintPersistenceRoundtripScenario();
        factories["paint_invalid_png_rejected"] = () => new PaintInvalidPngRejectedScenario();
        factories["paint_preview_has_no_physics"] = () => new PaintPreviewHasNoPhysicsScenario();
        factories["paint_under_expression_layer_order"] = () => new PaintLayerOrderScenario();
        factories["paint_save_failure_preserves_working_copy"] = () => new PaintSaveFailurePreservesWorkingCopyScenario();
        factories["paint_runtime_fidelity"] = () => new PaintRuntimeFidelityScenario();
        factories["paint_upload_coalescing"] = () => new PaintUploadCoalescingScenario();
        factories["paint_localization_fallback"] = () => new PaintLocalizationScenario();
        factories["paint_semantic_layer_filtering"] = () => new PaintSemanticLayerFilteringScenario();
        factories["character_paint_save_use_restart"] = () => new CharacterPaintSaveUseRestartScenario();
    }
}
