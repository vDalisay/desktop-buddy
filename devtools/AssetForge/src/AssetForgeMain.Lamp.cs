using DesktopBuddy.AssetForge.Core;
using Godot;

namespace DesktopBuddy.AssetForge;

public partial class AssetForgeMain
{
    private VBoxContainer _lampCard = null!;
    private SpinBox _environmentHeight = null!;
    private SpinBox _lampEmission = null!;
    private CheckButton _lampLocalLight = null!;
    private SpinBox _lampBrightness = null!;
    private SpinBox _lampRange = null!;
    private SpinBox _lampEmitterX = null!;
    private SpinBox _lampEmitterY = null!;

    private void EnsureLampUi()
    {
        if (GodotObject.IsInstanceValid(_lampCard) || !GodotObject.IsInstanceValid(_depth)) return;
        if (_depth.GetParent()?.GetParent()?.GetParent() is not VBoxContainer inspector) return;

        _lampCard = CreateModernCard(inspector, "Lamp & room", "Floor scale and visual-only light metadata. The bottom-centre of the clean drawing is the placement pivot.");
        _lampCard.Visible = false;

        _environmentHeight = LampSpin(32, 600, 1, "Logical room height", "Final in-room visual height. The generated mesh itself is authored at this size; collision/gameplay are unaffected.");
        _lampEmission = LampSpin(0, 8, .05, "Emitter glow", "Brightness of the small visual emitter marker without affecting gameplay.");
        _lampLocalLight = new CheckButton
        {
            Text = "Cast local light",
            TooltipText = "Adds a visual OmniLight3D at the authored emitter point. This remains presentation-only.",
        };
        _lampCard.AddChild(_lampLocalLight);
        _lampBrightness = LampSpin(0, 16, .05, "Local light brightness", "Visual light energy.");
        _lampRange = LampSpin(1, 1024, 1, "Local light range", "Visual light radius in room units.");
        _lampEmitterX = LampSpin(0, 1, .01, "Emitter X", "Normalized horizontal emitter position in the original 1024×1024 template.");
        _lampEmitterY = LampSpin(0, 1, .01, "Emitter Y", "Normalized vertical emitter position in the original 1024×1024 template.");
        _lampLocalLight.Toggled += enabled =>
        {
            _lampBrightness.Disabled = !enabled;
            _lampRange.Disabled = !enabled;
            MarkLampOutputStale();
        };
        foreach (SpinBox field in new[] { _environmentHeight, _lampEmission, _lampBrightness, _lampRange, _lampEmitterX, _lampEmitterY })
            field.ValueChanged += _ => MarkLampOutputStale();
    }

    private SpinBox LampSpin(double min, double max, double step, string label, string tooltip)
    {
        var caption = new Label { Text = label };
        _lampCard.AddChild(caption);
        var field = new SpinBox
        {
            MinValue = min,
            MaxValue = max,
            Step = step,
            AllowGreater = false,
            AllowLesser = false,
            TooltipText = tooltip,
            CustomMinimumSize = new Vector2(0, 34),
        };
        _lampCard.AddChild(field);
        return field;
    }

    private void MarkLampOutputStale()
    {
        if (_generated is null || _activeCategory != AssetCategory.Lamp) return;
        _export.Disabled = true;
        SetStatus("Lamp settings changed. Click Generate before exporting.");
    }

    private void ApplyLampRecipe(AssetRecipe recipe)
    {
        EnsureLampUi();
        if (recipe.Category != AssetCategory.Lamp) return;
        _featureId.Text = recipe.AssetId;
        _contentId.Text = string.Empty;
        _environmentHeight.Value = recipe.Environment.LogicalHeight;
        _lampEmission.Value = recipe.Light.EmissionStrength;
        _lampLocalLight.ButtonPressed = recipe.Light.LightEnabled;
        _lampBrightness.Value = recipe.Light.Brightness;
        _lampRange.Value = recipe.Light.Range;
        _lampEmitterX.Value = recipe.Light.EmitterX;
        _lampEmitterY.Value = recipe.Light.EmitterY;
        _lampBrightness.Disabled = !recipe.Light.LightEnabled;
        _lampRange.Disabled = !recipe.Light.LightEnabled;
        if (GodotObject.IsInstanceValid(_preview))
            _preview.SetLampPreviewSettings(recipe.Environment.LogicalHeight, recipe.Light);
    }

    private AssetRecipe ApplyLampUiToRecipe(AssetRecipe recipe)
    {
        if (_activeCategory != AssetCategory.Lamp) return recipe;
        return recipe with
        {
            AssetId = _featureId.Text.Trim(),
            FeatureId = string.Empty,
            ContentId = string.Empty,
            Environment = recipe.Environment with { LogicalHeight = _environmentHeight.Value },
            Light = recipe.Light with
            {
                Enabled = true,
                EmissionStrength = _lampEmission.Value,
                LightEnabled = _lampLocalLight.ButtonPressed,
                Brightness = _lampBrightness.Value,
                Range = _lampRange.Value,
                EmitterX = _lampEmitterX.Value,
                EmitterY = _lampEmitterY.Value,
            },
        };
    }

    private void ConfigureLampUi(bool lamp)
    {
        EnsureLampUi();
        _lampCard.Visible = lamp;
        if (GodotObject.IsInstanceValid(_contentId)) SetLabeledVisible(_contentId, !lamp);
        if (GodotObject.IsInstanceValid(_featureId) && _featureId.GetIndex() > 0 &&
            _featureId.GetParent().GetChild(_featureId.GetIndex() - 1) is Label featureLabel)
            featureLabel.Text = lamp ? "Decoration ID" : "Feature ID";
        if (GodotObject.IsInstanceValid(_sort)) SetLabeledVisible(_sort, !lamp);
        if (GodotObject.IsInstanceValid(_lightingLevel)) SetLabeledVisible(_lightingLevel, !lamp);

        if (lamp)
        {
            _depth.MaxValue = 1.5;
            _depth.Step = .01;
            _depth.TooltipText = "Lamp front/back visual depth as a fraction of its logical height.";
            if (GodotObject.IsInstanceValid(_surfaceSmoothnessLabel)) _surfaceSmoothnessLabel.Visible = true;
            if (GodotObject.IsInstanceValid(_surfaceSmoothness)) _surfaceSmoothness.Visible = true;
            if (GodotObject.IsInstanceValid(_roundness) && _roundness.GetIndex() > 0 &&
                _roundness.GetParent().GetChild(_roundness.GetIndex() - 1) is Label label)
                label.Text = "Edge roundness";
        }
    }
}
