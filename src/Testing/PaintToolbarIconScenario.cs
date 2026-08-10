using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DesktopBuddy.UI.Win98;
using Godot;

namespace DesktopBuddy.Testing;

/// <summary>
/// PAINT-R6 headless gate: every shipped compact paint action has a semantic icon, and iconization
/// leaves stable machine-readable identity/focus behavior instead of coupling tests to English text.
/// </summary>
public sealed class PaintToolbarIconScenario : IScenario
{
    public string Id => "paint_toolbar_icons";

    public Task<ScenarioResult> RunAsync(SceneTree tree, ulong seed)
    {
        var checks = new List<StartupCheck>();
        (string Id, string Fallback)[] semantics =
        [
            (PaintToolIconProvider.Brush, "Brush"),
            (PaintToolIconProvider.Pen, "Pen"),
            (PaintToolIconProvider.Eraser, "Eraser"),
            (PaintToolIconProvider.Spray, "Spray"),
            (PaintToolIconProvider.PickColor, "Pick"),
            (PaintToolIconProvider.Curve, "Curve"),
            (PaintToolIconProvider.Pan, "Hand"),
            (PaintToolIconProvider.Fill, "Fill"),
            (PaintToolIconProvider.Shapes, "Shapes"),
            (PaintToolIconProvider.Undo, "Undo"),
            (PaintToolIconProvider.Redo, "Redo"),
            (PaintToolIconProvider.EraseAll, "Erase All"),
            (PaintToolIconProvider.ZoomIn, "Zoom In"),
            (PaintToolIconProvider.ZoomOut, "Zoom Out"),
            (PaintToolIconProvider.ResetView, "Reset View"),
            (PaintToolIconProvider.RotateLeft, "Rotate Left"),
            (PaintToolIconProvider.RotateRight, "Rotate Right"),
        ];

        bool allResolve = semantics.All(item =>
        {
            Texture2D texture = PaintToolIconProvider.Resolve(item.Id);
            return GodotObject.IsInstanceValid(texture) && texture.GetWidth() > 0 && texture.GetHeight() > 0;
        });
        checks.Add(new StartupCheck(
            "paint_toolbar_all_semantic_icons_resolve",
            allResolve,
            $"icons={semantics.Length}"));

        bool identityStable = true;
        foreach ((string semanticId, string fallback) in semantics)
        {
            var button = new Button
            {
                Name = $"IconScenario_{semanticId}",
                FocusMode = Control.FocusModeEnum.All,
            };
            PaintToolIconProvider.Apply(button, semanticId, fallback, $"{fallback} tooltip");
            identityStable &= button.Text.Length == 0 &&
                button.TooltipText.Length > 0 &&
                button.FocusMode == Control.FocusModeEnum.All &&
                button.HasMeta("paint_tool_id") &&
                string.Equals(button.GetMeta("paint_tool_id").AsString(), semanticId, StringComparison.Ordinal) &&
                button.HasMeta("paint_tool_fallback_text") &&
                string.Equals(button.GetMeta("paint_tool_fallback_text").AsString(), fallback, StringComparison.Ordinal);
            button.Free();
        }
        checks.Add(new StartupCheck(
            "paint_toolbar_iconization_preserves_semantic_identity_and_focus",
            identityStable,
            "icon-only text, textual tooltip, stable metadata, focusable"));

        Texture2D first = PaintToolIconProvider.Resolve(PaintToolIconProvider.Brush);
        Texture2D second = PaintToolIconProvider.Resolve(PaintToolIconProvider.Brush);
        checks.Add(new StartupCheck(
            "paint_toolbar_icon_provider_is_stable",
            ReferenceEquals(first, second),
            "semantic icon cache returns the same resource"));

        return Task.FromResult(new ScenarioResult(
            checks.All(static check => check.Passed),
            checks,
            [$"seed={seed}"]));
    }
}
