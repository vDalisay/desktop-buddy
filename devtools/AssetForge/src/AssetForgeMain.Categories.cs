using DesktopBuddy.AssetForge.Core;
using Godot;

namespace DesktopBuddy.AssetForge;

public partial class AssetForgeMain
{
    private bool _categoryWorkflowInstalled;
    private AssetCategory _activeCategory = AssetCategory.Glasses;
    private string _activeTemplateId = AuthoringTemplateCatalog.GlassesId;
    private AssetRecipe _workingRecipeBase = AssetRecipe.GlassesDefaults();

    private void EnsureCategoryWorkflowUi()
    {
        if (_categoryWorkflowInstalled || !_modernWorkspaceInstalled || !GodotObject.IsInstanceValid(_categorySelector)) return;
        if (_shapeMode.ItemCount < 3) _shapeMode.AddItem("Inflated solid");
        if (_shapeMode.ItemCount < 4) _shapeMode.AddItem("Soft pillow / relief");
        EnsureReplacementQualityUi();
        EnsureLampUi();
        EnsureSofaUi();
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
            AuthoringTemplateCatalog.LampId => AssetCategory.Lamp,
            AuthoringTemplateCatalog.SofaId => AssetCategory.Sofa,
            _ => _activeCategory,
        };
        if (!spec.Implemented || category == _activeCategory) return;

        _preview.ClearGenerated();
        _activeCategory = category;
        _activeTemplateId = spec.Id;
        AssetRecipe defaults = CategoryDefaults(category);
        _workingRecipeBase = defaults;
        ApplyRecipe(defaults);
        ApplyReplacementQualityRecipe(defaults);
        ApplyLampRecipe(defaults);
        ApplySofaRecipe(defaults);
        _sourcePath = null;
        _source.Text = "Choose a clean 1024×1024 PNG for this category.";
        _generated = null;
        _export.Disabled = true;
        ConfigureActiveCategoryUi();
        SetStatus($"{spec.DisplayName} selected. Save its 1024×1024 guide, draw over it, hide the guide layer, then import the clean art without moving or cropping the canvas.");
    }

    private void SetActiveCategoryFromRecipe(AssetRecipe recipe)
    {
        _workingRecipeBase = recipe;
        _activeCategory = recipe.Category;
        _activeTemplateId = recipe.Category switch
        {
            AssetCategory.Glasses => AuthoringTemplateCatalog.GlassesId,
            AssetCategory.TorsoShape => AuthoringTemplateCatalog.TorsoId,
            AssetCategory.FootShape => AuthoringTemplateCatalog.FeetId,
            AssetCategory.Lamp => AuthoringTemplateCatalog.LampId,
            AssetCategory.Sofa => AuthoringTemplateCatalog.SofaId,
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
        ApplyReplacementQualityRecipe(recipe);
        ApplyLampRecipe(recipe);
        ApplySofaRecipe(recipe);
        ConfigureActiveCategoryUi();
    }

    private AssetRecipe CurrentCategoryBase()
    {
        AssetRecipe basis = _workingRecipeBase.Category == _activeCategory
            ? _workingRecipeBase
            : CategoryDefaults(_activeCategory);
        return basis with { PresetVersion = _activePresetVersion };
    }

    /// <summary>
    /// The legacy Glasses UI knows all editable Glasses fields but not every recipe field (notably
    /// thumbnail settings). Merge those visible edits onto the opened category recipe so an
    /// open-save cycle never resets hidden metadata. Explicit preset migration still wins through
    /// _activePresetVersion.
    /// </summary>
    private AssetRecipe MergeGlassesUiOntoWorkingRecipe(AssetRecipe edited)
    {
        AssetRecipe basis = CurrentCategoryBase();
        return basis with
        {
            PresetVersion = _activePresetVersion,
            FeatureId = edited.FeatureId,
            ContentId = edited.ContentId,
            DisplayName = edited.DisplayName,
            PriceCredits = edited.PriceCredits,
            SortOrder = edited.SortOrder,
            LightingLevel = edited.LightingLevel,
            Geometry = edited.Geometry,
        };
    }

    private AssetRecipe ReadCategoryRecipeFromUi()
    {
        // Start with the actual opened recipe, not today's defaults. Hidden metadata such as the
        // thumbnail camera and Lamp colour therefore survives open/edit/save; Lamp@1 also remains
        // Lamp@1 until an explicit migration changes its version.
        AssetRecipe defaults = CurrentCategoryBase();
        AssetRecipe recipe = defaults with
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
                SurfaceSmoothness = ReadReplacementSmoothness(defaults.Geometry),
                ThicknessBiasPixels = (int)_bias.Value,
                GeometryResolution = int.Parse(_geometryResolution.GetItemText(_geometryResolution.Selected)),
                RuntimeTextureResolution = int.Parse(_textureResolution.GetItemText(_textureResolution.Selected)),
                ShapeMode = (ShapeMode)_shapeMode.Selected,
                SymmetryMode = (SymmetryMode)_symmetry.Selected,
            },
        };
        recipe = ApplyLampUiToRecipe(recipe);
        return ApplySofaUiToRecipe(recipe);
    }

    private static AssetRecipe CategoryDefaults(AssetCategory category) => category switch
    {
        AssetCategory.Glasses => AssetRecipe.GlassesDefaults(),
        AssetCategory.TorsoShape => AssetRecipe.TorsoShapeDefaults(),
        AssetCategory.FootShape => AssetRecipe.FootShapeDefaults(),
        AssetCategory.Lamp => AssetRecipe.LampDefaults(),
        AssetCategory.Sofa => AssetRecipe.SofaDefaults(),
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
            // AF-14: both families share one canonical cache key. Buddy Studio uses a Godot render
            // producer while Environment uses a pure deterministic crop, but neither category owns
            // per-item thumbnail drawing code or cache identity.
            byte[] thumbnail = AssetThumbnailCache.GetOrCreate(
                _generated,
                () => _generated.Recipe.AssetFamily == AssetFamily.Environment
                    ? EnvironmentThumbnailGenerator.Create(_generated.AlbedoPng)
                    : _preview.CaptureThumbnailPng());
            byte[] source = File.ReadAllBytes(_sourcePath);
            string root = FindRepositoryRoot();

            ExportResult result;
            if (_generated.Recipe.Category == AssetCategory.Glasses)
            {
                result = RepositoryExporter.ExportGlasses(root, source, _generated, thumbnail);
                GeneratedCosmeticLightingPersistence.Apply(root, _generated.Recipe);
            }
            else if (_generated.Recipe.Category is AssetCategory.TorsoShape or AssetCategory.FootShape)
            {
                result = RepositoryBuddyReplacementExporter.Export(root, source, _generated, thumbnail);
                GeneratedCosmeticLightingPersistence.Apply(root, _generated.Recipe);
            }
            else if (_generated.Recipe.Category is AssetCategory.Lamp or AssetCategory.Sofa)
            {
                result = RepositoryEnvironmentExporter.Export(root, source, _generated, thumbnail);
            }
            else throw new NotSupportedException($"Export for {_generated.Recipe.Category} is not implemented.");

            bool verified;
            IReadOnlyList<string> diagnostics;
            if (_generated.Recipe.AssetFamily == AssetFamily.Environment)
            {
                EnvironmentAssetVerificationResult verification = RepositoryEnvironmentVerifier.Verify(root, _generated.Recipe.AssetId);
                verified = verification.Passed;
                diagnostics = verification.Diagnostics;
            }
            else
            {
                AssetVerificationResult verification = RepositoryAssetVerifier.Verify(root, _generated.Recipe.FeatureId);
                verified = verification.Passed;
                diagnostics = verification.Diagnostics;
            }
            if (!verified)
                throw new InvalidOperationException("Export committed but verification failed: " + string.Join("; ", diagnostics));

            string destination = _generated.Recipe.Category switch
            {
                AssetCategory.Glasses => "Buddy Studio > Glasses",
                AssetCategory.TorsoShape => "Buddy Studio > Tops",
                AssetCategory.FootShape => "Buddy Studio > Shoes",
                AssetCategory.Lamp => "Room Decorator > Lamps",
                AssetCategory.Sofa => "Room Decorator > Sofas",
                _ => _generated.Recipe.Category.ToString(),
            };
            SetStatus($"Exported {_generated.Recipe.DisplayName} to {destination} and verified deterministic package.\nAuthoring: {result.AuthoringDirectory}\nGenerated: {result.AssetDirectory}");
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
        bool lamp = _activeCategory == AssetCategory.Lamp;
        bool sofa = _activeCategory == AssetCategory.Sofa;
        bool environmentSilhouette = lamp || sofa;
        bool silhouette = replacement || environmentSilhouette;
        ConfigureReplacementQualityUi(replacement || environmentSilhouette);
        ConfigureLampUi(lamp);
        ConfigureSofaUi(sofa);
        SetLabeledVisible(_frameThickness, glasses);
        RefreshBridgeThicknessVisibility();
        if (GodotObject.IsInstanceValid(_templeThickness) && _templeThickness.GetParent()?.GetParent() is Control templeCard) templeCard.Visible = glasses;
        if (GodotObject.IsInstanceValid(_migratePreset)) _migratePreset.Visible = glasses && _activePresetVersion < 2;

        _shapeMode.GetPopup().SetItemDisabled((int)ShapeMode.FlatExtrusion, silhouette);
        _shapeMode.GetPopup().SetItemDisabled((int)ShapeMode.RoundedExtrusion, false);
        _shapeMode.GetPopup().SetItemDisabled((int)ShapeMode.InflatedSolid, glasses);
        _shapeMode.GetPopup().SetItemDisabled((int)ShapeMode.Relief, glasses);

        string display = _activeCategory switch
        {
            AssetCategory.Glasses => "Glasses",
            AssetCategory.TorsoShape => "Top / Torso replacement",
            AssetCategory.FootShape => "Shoes / Foot replacement",
            AssetCategory.Lamp => "Lamp",
            AssetCategory.Sofa => "Sofa",
            _ => _activeCategory.ToString(),
        };
        _presetLabel.Text = _activeCategory switch
        {
            AssetCategory.Glasses when _activePresetVersion >= 2 => "Buddy Studio > Glasses / glasses@2 — literal 1024×1024 Buddy-head placement",
            AssetCategory.Glasses => "Buddy Studio > Glasses / glasses@1 — legacy auto-fit placement",
            AssetCategory.Lamp when _activePresetVersion >= 2 => "Environment > Lamp / lamp@2 — literal floor-template placement with visual light metadata",
            AssetCategory.Lamp => "Environment > Lamp / lamp@1 — legacy visible-bounds auto-fit placement",
            AssetCategory.Sofa => "Environment > Sofa / sofa@1 — front-only stylized 2.5D, literal floor-template placement",
            _ => $"Buddy Studio > {display} / {CategoryDefaults(_activeCategory).PresetId}@{_activePresetVersion} — literal 1024×1024 replacement placement",
        };
        if (GodotObject.IsInstanceValid(_reference))
            _reference.Text = _activeCategory switch
            {
                AssetCategory.Glasses => "Reference head",
                AssetCategory.TorsoShape => "Reference torso",
                AssetCategory.FootShape => "Reference feet",
                AssetCategory.Lamp or AssetCategory.Sofa => "Room scale reference",
                _ => "Reference",
            };
        if (GodotObject.IsInstanceValid(_preview)) _preview.SetCategory(_activeCategory);

        Label? subtitle = FindLabel(this, "Glasses · category settings") ??
                          FindLabel(this, "Top / Torso replacement · category settings") ??
                          FindLabel(this, "Shoes / Foot replacement · category settings") ??
                          FindLabel(this, "Lamp · category settings") ??
                          FindLabel(this, "Sofa · category settings");
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
