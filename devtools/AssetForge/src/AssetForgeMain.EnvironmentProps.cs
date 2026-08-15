using DesktopBuddy.AssetForge.Core;
using Godot;

namespace DesktopBuddy.AssetForge;

public partial class AssetForgeMain
{
    private VBoxContainer _environmentPropCard = null!;
    private Label _environmentPropDescription = null!;
    private Label _environmentPropPlacement = null!;
    private Label _environmentPropHeightLabel = null!;
    private SpinBox _environmentPropHeight = null!;

    private void EnsureEnvironmentPropUi()
    {
        if (GodotObject.IsInstanceValid(_environmentPropCard) || !GodotObject.IsInstanceValid(_depth)) return;
        if (_depth.GetParent()?.GetParent()?.GetParent() is not VBoxContainer inspector) return;

        _environmentPropCard = CreateModernCard(
            inspector,
            "Environment placement",
            "Literal template placement for generated room decorations.");
        _environmentPropCard.Visible = false;

        _environmentPropDescription = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _environmentPropCard.AddChild(_environmentPropDescription);
        _environmentPropPlacement = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _environmentPropCard.AddChild(_environmentPropPlacement);
        _environmentPropHeightLabel = new Label { Text = "Room scale reference height" };
        _environmentPropCard.AddChild(_environmentPropHeightLabel);
        _environmentPropHeight = new SpinBox
        {
            MinValue = 32,
            MaxValue = 600,
            Step = 1,
            AllowGreater = false,
            AllowLesser = false,
            TooltipText = "World-space reference height for this fixed 1024×1024 category template.",
            CustomMinimumSize = new Vector2(0, 34),
        };
        _environmentPropCard.AddChild(_environmentPropHeight);
        _environmentPropHeight.ValueChanged += _ => MarkEnvironmentPropOutputStale();
    }

    private static bool IsGenericEnvironmentProp(AssetCategory category) =>
        category is AssetCategory.Table or AssetCategory.Plant or AssetCategory.Painting;

    private void MarkEnvironmentPropOutputStale()
    {
        if (_generated is null || !IsGenericEnvironmentProp(_activeCategory)) return;
        _export.Disabled = true;
        SetStatus($"{_activeCategory} settings changed. Click Generate before exporting.");
    }

    private void ApplyEnvironmentPropRecipe(AssetRecipe recipe)
    {
        EnsureEnvironmentPropUi();
        if (!IsGenericEnvironmentProp(recipe.Category)) return;
        _featureId.Text = recipe.AssetId;
        _contentId.Text = string.Empty;
        _environmentPropHeight.Value = recipe.Environment.LogicalHeight;
    }

    private AssetRecipe ApplyEnvironmentPropUiToRecipe(AssetRecipe recipe)
    {
        if (!IsGenericEnvironmentProp(_activeCategory)) return recipe;
        return recipe with
        {
            AssetId = _featureId.Text.Trim(),
            FeatureId = string.Empty,
            ContentId = string.Empty,
            Environment = recipe.Environment with { LogicalHeight = _environmentPropHeight.Value },
            Light = recipe.Light with
            {
                Enabled = false,
                EmissionStrength = 0,
                LightEnabled = false,
            },
        };
    }

    private void ConfigureEnvironmentPropUi(AssetCategory category)
    {
        EnsureEnvironmentPropUi();
        bool visible = IsGenericEnvironmentProp(category);
        _environmentPropCard.Visible = visible;
        if (!visible) return;

        bool wall = category == AssetCategory.Painting;
        _environmentPropDescription.Text = category switch
        {
            AssetCategory.Table => "Front-derived table volume. The tabletop/support silhouette stays exactly where it is drawn inside the fixed floor template.",
            AssetCategory.Plant => "Inflated front-derived plant volume. Pot contact and foliage scale come directly from the fixed floor template.",
            AssetCategory.Painting => "Thin wall decoration. The template centre is the wall anchor; the artwork keeps its authored position and aspect ratio.",
            _ => string.Empty,
        };
        _environmentPropPlacement.Text = wall
            ? "Placement: template centre = wall anchor. No floor pivot is used."
            : "Placement: template bottom-centre = room floor pivot.";
        _environmentPropHeightLabel.Text = wall ? "Artwork reference height" : "Room scale reference height";

        if (GodotObject.IsInstanceValid(_contentId)) SetLabeledVisible(_contentId, false);
        if (GodotObject.IsInstanceValid(_sort)) SetLabeledVisible(_sort, false);
        if (GodotObject.IsInstanceValid(_featureId) && _featureId.GetIndex() > 0 &&
            _featureId.GetParent().GetChild(_featureId.GetIndex() - 1) is Label featureLabel)
            featureLabel.Text = "Decoration ID";
        if (GodotObject.IsInstanceValid(_lightingLevel)) SetLabeledVisible(_lightingLevel, true);

        _depth.MaxValue = 1.5;
        _depth.Step = .01;
        _depth.TooltipText = wall
            ? "Wall-decoration thickness relative to the authored artwork reference height."
            : "Front/back visual depth relative to the category room-scale reference height.";
        if (GodotObject.IsInstanceValid(_surfaceSmoothnessLabel)) _surfaceSmoothnessLabel.Visible = true;
        if (GodotObject.IsInstanceValid(_surfaceSmoothness)) _surfaceSmoothness.Visible = true;
        if (GodotObject.IsInstanceValid(_roundness) && _roundness.GetIndex() > 0 &&
            _roundness.GetParent().GetChild(_roundness.GetIndex() - 1) is Label label)
            label.Text = category switch
            {
                AssetCategory.Plant => "Volume roundness",
                AssetCategory.Painting => "Edge roundness",
                _ => "Edge roundness",
            };
    }
}
