using DesktopBuddy.AssetForge.Core;
using Godot;

namespace DesktopBuddy.AssetForge;

public partial class AssetForgeMain
{
    private bool _categoryWorkflowInstalled;
    private AssetCategory _activeCategory = AssetCategory.Glasses;
    private string _activeTemplateId = AuthoringTemplateCatalog.GlassesId;

    private void EnsureCategoryWorkflowUi()
    {
        if (_categoryWorkflowInstalled || !_modernWorkspaceInstalled || !GodotObject.IsInstanceValid(_categorySelector)) return;

        if (_shapeMode.ItemCount < 3) _shapeMode.AddItem("Inflated solid");
        if (_shapeMode.ItemCount < 4) _shapeMode.AddItem("Relief");
        _categorySelector.ItemSelected += OnAuthoringCategorySelected;
        _templateDialog.FileSelected -= SaveTemplate;
        _templateDialog.FileSelected += SaveCategoryTemplate;
        _export.Pressed -= Export;
        _export.Pressed += ExportCategory;
        _categoryWorkflowInstalled = true;
        ConfigureActiveCategoryUi();
    }

    private void OnAuthoringCategorySelected(long rawIndex)
    {
        int index = (int)rawIndex;
        if (index < 0 || index >= AuthoringTemplateCatalog.All.Count) return;
        AuthoringTemplateSpec spec = AuthoringTemplateCatalog.All[index];
        AssetCategory category = spec.Id switch
        {
            AuthoringTemplateCatalog.GlassesId => AssetCategory.Glasses,
            AuthoringTemplateCatalog.TorsoId => AssetCategory.TorsoShape,
            AuthoringTemplateCatalog.FeetId => AssetCategory.FootShape,
            _ => _activeCategory,
        };
        if (!spec.Implemented || category == _activeCategory) return;

        _activeCategory = category;
        _activeTemplateId = spec.Id;
        ApplyRecipe(CategoryDefaults(category));
        _sourcePath = null;
        _source.Text = "Choose a clean 1024×1024 PNG for this category.";
        _generated = null;
        _export.Disabled = true;
        ConfigureActiveCategoryUi();
        SetStatus($"{spec.DisplayName} selected. Save its 1024×1024 guide, draw over it, hide the guide layer, then import the clean art without moving or cropping the canvas.");
    }

    private void SetActiveCategoryFromRecipe(AssetRecipe recipe)
    {
        _activeCategory = recipe.Category;
        _activeTemplateId = recipe.Category switch
        {
            AssetCategory.Glasses => AuthoringTemplateCatalog.GlassesId,
            AssetCategory.TorsoShape => AuthoringTemplateCatalog.TorsoId,
            AssetCategory.FootShape => AuthoringTemplateCatalog.FeetId,
            _ => _activeTemplateId,
        };
        if (GodotObject.IsInstanceValid(_categorySelector))
        {
            for (int i = 0; i < AuthoringTemplateCatalog.All.Count; i++)
                if (AuthoringTemplateCatalog.All[i].Id == _activeTemplateId)
                {
                    _categorySelector.Select(i);
                    break;
                }
        }
        ConfigureActiveCategoryUi();
    }

    private AssetRecipe ReadCategoryRecipeFromUi()
    {
        AssetRecipe defaults = CategoryDefaults(_activeCategory);
        return defaults with
        {
            DisplayName = _displayName.Text.Trim(),
            FeatureId = _featureId.Text.Trim(),
            ContentId = _contentId.Text.Trim(),
            PriceCredits = (int)_price.Value,
            SortOrder = (int)_sort.Value,
            LightingLevel = _lightingLevel.Value,
            Geometry = defaults.Geometry with
            {
                AlphaThreshold = _alpha.Value,
                Depth = _depth.Value,
                Roundness = _roundness.Value,
                ThicknessBiasPixels = (int)_bias.Value,
                GeometryResolution = int.Parse(_geometryResolution.GetItemText(_geometryResolution.Selected)),
                RuntimeTextureResolution = int.Parse(_textureResolution.GetItemText(_textureResolution.Selected)),
                ShapeMode = (ShapeMode)_shapeMode.Selected,
                SymmetryMode = (SymmetryMode)_symmetry.Selected,
            },
        };
    }

    private static AssetRecipe CategoryDefaults(AssetCategory category) => category switch
    {
        AssetCategory.Glasses => AssetRecipe.GlassesDefaults(),
        AssetCategory.TorsoShape => AssetRecipe.TorsoShapeDefaults(),
        AssetCategory.FootShape => AssetRecipe.FootShapeDefaults(),
        _ => throw new NotSupportedException($"Asset Forge category {category} is not enabled yet."),
    };

    private void SaveCategoryTemplate(string path)
    {
        try
        {
            AuthoringTemplateSpec spec = AuthoringTemplateCatalog.Get(_activeTemplateId);
            if (!path.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) path += ".png";
            File.WriteAllBytes(path, AuthoringTemplateCatalog.CreatePng(_activeTemplateId));
            SetStatus($"{spec.DisplayName} coloring guide saved. Keep the 1024×1024 canvas fixed, hide/remove the guide layer before export, then import the clean art.\n{path}");
        }
        catch (Exception exception)
        {
            SetStatus("Save template failed: " + exception.Message);
        }
    }

    private void ExportCategory()
    {
        try
        {
            if (_generated is null || string.IsNullOrWhiteSpace(_sourcePath))
                throw new InvalidOperationException("Generate the asset before export.");
            byte[] thumbnail = _preview.CaptureThumbnailPng();
            if (thumbnail.Length == 0) thumbnail = _generated.AlbedoPng;
            byte[] source = File.ReadAllBytes(_sourcePath);

            ExportResult result = _generated.Recipe.Category == AssetCategory.Glasses
                ? RepositoryExporter.ExportGlasses(_root.Text.Trim(), source, _generated, thumbnail)
                : RepositoryBuddyReplacementExporter.Export(_root.Text.Trim(), source, _generated, thumbnail);
            GeneratedCosmeticLightingPersistence.Apply(_root.Text.Trim(), _generated.Recipe);
            VerificationResult verification = RepositoryAssetVerifier.Verify(_root.Text.Trim(), _generated.Recipe.FeatureId);
            if (!verification.Success)
                throw new InvalidOperationException("Export committed but verification failed: " + FormatVerification(verification));

            string category = _generated.Recipe.Category switch
            {
                AssetCategory.Glasses => "Glasses",
                AssetCategory.TorsoShape => "Tops",
                AssetCategory.FootShape => "Shoes",
                _ => _generated.Recipe.Category.ToString(),
            };
            SetStatus($"Exported {_generated.Recipe.DisplayName} to Buddy Studio > {category} and verified deterministic package.\nAuthoring: {result.AuthoringDirectory}\nGenerated: {result.AssetDirectory}");
        }
        catch (Exception exception)
        {
            SetStatus("Export failed: " + exception.Message);
        }
    }

    private void ConfigureActiveCategoryUi()
    {
        if (!GodotObject.IsInstanceValid(_shapeMode)) return;
        bool glasses = _activeCategory == AssetCategory.Glasses;
        bool replacement = _activeCategory is AssetCategory.TorsoShape or AssetCategory.FootShape;

        SetLabeledVisible(_frameThickness, glasses);
        RefreshBridgeThicknessVisibility();
        if (GodotObject.IsInstanceValid(_templeThickness) && _templeThickness.GetParent()?.GetParent() is Control templeCard)
            templeCard.Visible = glasses;
        if (GodotObject.IsInstanceValid(_migratePreset))
            _migratePreset.Visible = glasses && _activePresetVersion < 2;

        _shapeMode.GetPopup().SetItemDisabled((int)ShapeMode.FlatExtrusion, replacement);
        _shapeMode.GetPopup().SetItemDisabled((int)ShapeMode.RoundedExtrusion, false);
        _shapeMode.GetPopup().SetItemDisabled((int)ShapeMode.InflatedSolid, glasses);
        _shapeMode.GetPopup().SetItemDisabled((int)ShapeMode.Relief, true);

        string display = _activeCategory switch
        {
            AssetCategory.Glasses => "Glasses",
            AssetCategory.TorsoShape => "Top / Torso replacement",
            AssetCategory.FootShape => "Shoes / Foot replacement",
            _ => _activeCategory.ToString(),
        };
        _presetLabel.Text = _activeCategory switch
        {
            AssetCategory.Glasses when _activePresetVersion >= 2 => "Buddy Studio > Glasses / glasses@2 — literal 1024×1024 Buddy-head placement",
            AssetCategory.Glasses => "Buddy Studio > Glasses / glasses@1 — legacy auto-fit placement",
            _ => $"Buddy Studio > {display} / {CategoryDefaults(_activeCategory).PresetId}@1 — literal 1024×1024 replacement placement",
        };
        if (GodotObject.IsInstanceValid(_reference))
            _reference.Text = _activeCategory switch
            {
                AssetCategory.Glasses => "Reference head",
                AssetCategory.TorsoShape => "Reference torso",
                AssetCategory.FootShape => "Reference foot",
                _ => "Reference",
            };
        if (GodotObject.IsInstanceValid(_preview)) _preview.SetCategory(_activeCategory);

        Label? subtitle = FindLabel(this, "Glasses · category settings");
        if (subtitle is not null) subtitle.Text = $"{display} · category settings";
    }

    private static void SetLabeledVisible(Control field, bool visible)
    {
        if (!GodotObject.IsInstanceValid(field)) return;
        if (field.GetIndex() > 0 && field.GetParent().GetChild(field.GetIndex() - 1) is Label label) label.Visible = visible;
        field.Visible = visible;
    }

    private static Label? FindLabel(Node root, string text)
    {
        foreach (Node child in root.GetChildren())
        {
            if (child is Label label && label.Text == text) return label;
            Label? nested = FindLabel(child, text);
            if (nested is not null) return nested;
        }
        return null;
    }
}
