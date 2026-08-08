using System;
using DesktopBuddy.Domain.Characters;
using Godot;

namespace DesktopBuddy.Work;

/// <summary>
/// Minimal shared-cosmetic bridge needed by Work Mode before the full Buddy Studio renderer
/// lands. Unknown future glasses IDs are ignored rather than rendered incorrectly.
/// </summary>
public partial class WorkCompanionView
{
    private WorkGlassesOverlay? _workGlassesOverlay;

    public void SetGlassesFeature(string? featureId)
    {
        if (!GodotObject.IsInstanceValid(_root))
            return;

        _workGlassesOverlay ??= CreateWorkGlassesOverlay();
        _workGlassesOverlay.FeatureId = featureId ?? CharacterFeatureIds.GlassesNone;
        _workGlassesOverlay.QueueRedraw();
        _workGlassesOverlay.Visible =
            string.Equals(featureId, CharacterFeatureIds.GlassesWorkClassic, StringComparison.Ordinal);
    }

    private WorkGlassesOverlay CreateWorkGlassesOverlay()
    {
        var overlay = new WorkGlassesOverlay
        {
            Name = "WorkGlassesOverlay",
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        overlay.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _root.AddChild(overlay);
        return overlay;
    }

    private partial class WorkGlassesOverlay : Control
    {
        public string FeatureId { get; set; } = CharacterFeatureIds.GlassesNone;

        public override void _Draw()
        {
            if (!string.Equals(
                    FeatureId,
                    CharacterFeatureIds.GlassesWorkClassic,
                    StringComparison.Ordinal))
            {
                return;
            }

            // Approximate screen-space head anchor for the fixed sideways Work pose. This is
            // presentation-only; Buddy Studio can later replace it with the shared authored
            // cosmetic socket renderer without changing Work ownership/persistence semantics.
            Color frame = new("#2C2018");
            Vector2 left = new(283, 130);
            Vector2 right = new(304, 130);
            const float radius = 10.0f;
            const float width = 3.0f;
            DrawArc(left, radius, 0, Mathf.Tau, 28, frame, width, true);
            DrawArc(right, radius, 0, Mathf.Tau, 28, frame, width, true);
            DrawLine(new Vector2(293, 130), new Vector2(294, 130), frame, width, true);
            DrawLine(new Vector2(273, 128), new Vector2(267, 125), frame, width - 1.0f, true);
            DrawLine(new Vector2(314, 128), new Vector2(320, 125), frame, width - 1.0f, true);
        }
    }
}
