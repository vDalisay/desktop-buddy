using DesktopBuddy.AssetForge.Core;
using Godot;

namespace DesktopBuddy.AssetForge;

public partial class AssetForgeMain
{
    private VBoxContainer _sofaCard = null!;
    private SpinBox _sofaEnvironmentHeight = null!;

    private void EnsureSofaUi()
    {
        if (GodotObject.IsInstanceValid(_sofaCard) || !GodotObject.IsInstanceValid(_depth)) return;
        if (_depth.GetParent()?.GetParent()?.GetParent() is not VBoxContainer inspector) return;

        _sofaCard = CreateModernCard(
            inspector,
            "Sofa & room",
            "Front-only stylized 2.5D furniture. The fixed 1024×1024 template controls floor contact, seat/back proportions and room scale.");
        _sofaCard.Visible = false;

        _sofaCard.AddChild(new Label
        {
            Text = "Generation: front-derived stylized volume",
            TooltipText = "Asset Forge creates a deterministic rounded depth from the front drawing. It does not infer a fully authored back/interior furniture model.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        });
        _sofaCard.AddChild(new Label
        {
            Text = "Placement: template bottom-centre = room floor pivot",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        });

        _sofaEnvironmentHeight = new SpinBox
        {
            MinValue = 32,
            MaxValue = 600,
            Step = 1,
            AllowGreater = false,
            AllowLesser = false,
            TooltipText = "World-space height from the template safe-top reference to the floor. Moving/scaling the clean drawing within the fixed template changes its in-room placement literally.",
            CustomMinimumSize = new Vector2(0, 34),
        };
        _sofaCard.AddChild(new Label { Text = "Room scale reference height" });
        _sofaCard.AddChild(_sofaEnvironmentHeight);
        _sofaEnvironmentHeight.ValueChanged += _ => MarkSofaOutputStale();
    }

    private void MarkSofaOutputStale()
    {
        if (_generated is null || _activeCategory != AssetCategory.Sofa) return;
        _export.Disabled = true;
        SetStatus("Sofa settings changed. Click Generate before exporting.");
    }

    private void ApplySofaRecipe(AssetRecipe recipe)
    {
        EnsureSofaUi();
        if (recipe.Category != AssetCategory.Sofa) return;
        _featureId.Text = recipe.AssetId;
        _contentId.Text = string.Empty;
        _sofaEnvironmentHeight.Value = recipe.Environment.LogicalHeight;
    }

    private AssetRecipe ApplySofaUiToRecipe(AssetRecipe recipe)
    {
        if (_activeCategory != AssetCategory.Sofa) return recipe;
        return recipe with
        {
            AssetId = _featureId.Text.Trim(),
            FeatureId = string.Empty,
            ContentId = string.Empty,
            Environment = recipe.Environment with { LogicalHeight = _sofaEnvironmentHeight.Value },
            Light = recipe.Light with
            {
                Enabled = false,
                EmissionStrength = 0,
                LightEnabled = false,
            },
        };
    }

    private void ConfigureSofaUi(bool sofa)
    {
        EnsureSofaUi();
        _sofaCard.Visible = sofa;
        if (!sofa) return;

        if (GodotObject.IsInstanceValid(_contentId)) SetLabeledVisible(_contentId, false);
        if (GodotObject.IsInstanceValid(_sort)) SetLabeledVisible(_sort, false);
        if (GodotObject.IsInstanceValid(_featureId) && _featureId.GetIndex() > 0 &&
            _featureId.GetParent().GetChild(_featureId.GetIndex() - 1) is Label featureLabel)
            featureLabel.Text = "Decoration ID";
        if (GodotObject.IsInstanceValid(_lightingLevel)) SetLabeledVisible(_lightingLevel, true);

        _depth.MaxValue = 1.5;
        _depth.Step = .01;
        _depth.TooltipText = "Front-derived Sofa depth as a fraction of its room-scale reference height.";
        if (GodotObject.IsInstanceValid(_surfaceSmoothnessLabel)) _surfaceSmoothnessLabel.Visible = true;
        if (GodotObject.IsInstanceValid(_surfaceSmoothness)) _surfaceSmoothness.Visible = true;
        if (GodotObject.IsInstanceValid(_roundness) && _roundness.GetIndex() > 0 &&
            _roundness.GetParent().GetChild(_roundness.GetIndex() - 1) is Label label)
            label.Text = "Cushion roundness";
    }
}
