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
            MaxValue = 3.0,
            Step = 0.05,
            AllowGreater = false,
            AllowLesser = false,
            TooltipText = "Relaxes the generated depth field without moving the authored 2D silhouette. 0 preserves legacy geometry; 0-1 is the normal range; values up to 3 add progressively more smoothing passes for very soft/plush forms.",
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

        // Category defaults are applied by a later ItemSelected handler. Prepare the depth range
        // first so replacement defaults above the old 1.0 glasses maximum are never clamped.
        if (GodotObject.IsInstanceValid(_categorySelector))
        {
            _categorySelector.ItemSelected += rawIndex =>
            {
                int index = (int)rawIndex;
                if (index < 0 || index >= AuthoringTemplateCatalog.All.Count) return;
                string id = AuthoringTemplateCatalog.All[index].Id;
                bool replacement = id is AuthoringTemplateCatalog.TorsoId or AuthoringTemplateCatalog.FeetId;
                _depth.MaxValue = replacement ? 4.0 : 1.0;
                _depth.Step = replacement ? 0.05 : 0.005;
            };
        }
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
        if (GodotObject.IsInstanceValid(_surfaceSmoothness))
        {
            _surfaceSmoothness.Visible = replacement;
            _surfaceSmoothness.MaxValue = replacement ? 3.0 : 1.0;
        }

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

        // Geometry resolution is the runtime mesh density for replacement categories. Keep it in
        // Advanced, but label it honestly so authors know lowering it is a performance choice rather
        // than a texture-quality choice. New replacement defaults use 128 instead of the old 256.
        if (GodotObject.IsInstanceValid(_geometryResolution) && _geometryResolution.GetIndex() > 0 &&
            _geometryResolution.GetParent().GetChild(_geometryResolution.GetIndex() - 1) is Label resolutionLabel)
        {
            resolutionLabel.Text = replacement ? "Runtime mesh resolution" : "Geometry resolution";
            _geometryResolution.TooltipText = replacement
                ? "Controls exported mesh density. 128 is the recommended runtime default; 64 is lighter, 256 is high-detail and substantially more expensive to paint/hit-test."
                : "Generator geometry resolution.";
        }

        if (!GodotObject.IsInstanceValid(_shapeMode) || _shapeMode.ItemCount < 4) return;
        _shapeMode.SetItemText((int)ShapeMode.FlatExtrusion, "Flat silhouette extrusion");
        _shapeMode.SetItemText((int)ShapeMode.RoundedExtrusion, replacement ? "Rounded extrusion" : "Rounded glasses template");
        _shapeMode.SetItemText((int)ShapeMode.InflatedSolid, "Inflated solid");
        _shapeMode.SetItemText((int)ShapeMode.Relief, "Soft pillow / relief");
    }
}
