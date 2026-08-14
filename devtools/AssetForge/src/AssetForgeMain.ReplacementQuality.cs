using DesktopBuddy.AssetForge.Core;
using Godot;

namespace DesktopBuddy.AssetForge;

public partial class AssetForgeMain
{
    private Label _surfaceSmoothnessLabel = null!;
    private SpinBox _surfaceSmoothness = null!;

    private void EnsureReplacementQualityUi()
    {
        if (GodotObject.IsInstanceValid(_surfaceSmoothness) || !GodotObject.IsInstanceValid(_roundness))
            return;
        if (_roundness.GetParent() is not Container shape)
            return;

        _surfaceSmoothnessLabel = new Label { Text = "Surface smoothness" };
        _surfaceSmoothness = new SpinBox
        {
            MinValue = 0.0,
            MaxValue = 1.0,
            Step = 0.01,
            AllowGreater = false,
            AllowLesser = false,
            TooltipText = "Smooths the generated depth field without moving the authored 2D silhouette. Higher values remove grid/ring ridges; 0 preserves legacy replacement geometry exactly.",
        };
        _surfaceSmoothness.ValueChanged += _ =>
        {
            if (_generated is null) return;
            _export.Disabled = true;
            SetStatus("Replacement surface smoothness changed. Click Generate to rebuild deterministic geometry before exporting.");
        };

        int insertionIndex = _roundness.GetIndex() + 1;
        shape.AddChild(_surfaceSmoothnessLabel);
        shape.MoveChild(_surfaceSmoothnessLabel, insertionIndex);
        shape.AddChild(_surfaceSmoothness);
        shape.MoveChild(_surfaceSmoothness, insertionIndex + 1);
    }

    private void ApplyReplacementQualityRecipe(AssetRecipe recipe)
    {
        if (GodotObject.IsInstanceValid(_surfaceSmoothness))
            _surfaceSmoothness.Value = recipe.Geometry.SurfaceSmoothness;
    }

    private double ReadReplacementSmoothness(GeometrySettings defaults) =>
        GodotObject.IsInstanceValid(_surfaceSmoothness)
            ? _surfaceSmoothness.Value
            : defaults.SurfaceSmoothness;

    private void ConfigureReplacementQualityUi(bool replacement)
    {
        EnsureReplacementQualityUi();
        if (GodotObject.IsInstanceValid(_surfaceSmoothnessLabel)) _surfaceSmoothnessLabel.Visible = replacement;
        if (GodotObject.IsInstanceValid(_surfaceSmoothness)) _surfaceSmoothness.Visible = replacement;

        if (GodotObject.IsInstanceValid(_depth))
        {
            _depth.MaxValue = replacement ? 4.0 : 1.0;
            _depth.Step = replacement ? 0.05 : 0.005;
            _depth.TooltipText = replacement
                ? "Visual thickness only. Replacement depth may be authored up to 4× the target-part radius; physics/collision never changes."
                : "Physical frame depth for the generated glasses mesh.";
        }

        if (GodotObject.IsInstanceValid(_roundness))
        {
            _roundness.TooltipText = replacement
                ? "Controls how quickly the surface rounds away from the authored silhouette edge. Combine with Surface smoothness for soft/cartoon forms."
                : "Rounds the front/back edge of the generated glasses frame.";
            if (_roundness.GetIndex() > 0 && _roundness.GetParent().GetChild(_roundness.GetIndex() - 1) is Label label)
                label.Text = replacement ? "Edge roundness" : "Roundness";
        }

        if (!GodotObject.IsInstanceValid(_shapeMode) || _shapeMode.ItemCount < 4) return;
        _shapeMode.SetItemText((int)ShapeMode.FlatExtrusion, "Flat silhouette extrusion");
        _shapeMode.SetItemText((int)ShapeMode.RoundedExtrusion, replacement ? "Rounded extrusion" : "Rounded glasses template");
        _shapeMode.SetItemText((int)ShapeMode.InflatedSolid, "Inflated solid");
        _shapeMode.SetItemText((int)ShapeMode.Relief, "Soft pillow / relief");
    }
}
