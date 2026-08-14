using DesktopBuddy.AssetForge.Core;
using Godot;

namespace DesktopBuddy.AssetForge;

public partial class AssetForgeMain
{
    private const int RecommendedReplacementRuntimeTriangleBudget = 20_000;
    private Label _bridgeThicknessLabel = null!;
    private SpinBox _bridgeThickness = null!;
    private bool _bridgeThicknessUiInstalled;

    public override void _Process(double delta)
    {
        if (!_bridgeThicknessUiInstalled && GodotObject.IsInstanceValid(_frameThickness))
            InstallBridgeThicknessUi();

        EnsureModernWorkspaceUi();
        EnsureCategoryWorkflowUi();
        EnsureCategorySourceHandler();
    }

    private void InstallBridgeThicknessUi()
    {
        if (_frameThickness.GetParent() is not Container controls) return;

        _bridgeThicknessLabel = new Label { Text = "Bridge thickness adjustment (px)" };
        _bridgeThickness = new SpinBox
        {
            MinValue = -24,
            MaxValue = 24,
            Step = 1,
            AllowGreater = false,
            AllowLesser = false,
            TooltipText = "Fine-tunes only the authored glasses nose bridge. 0 preserves the drawing exactly; positive values thicken it and negative values thin it. Lens frames and temples are unchanged.",
        };
        _bridgeThickness.ValueChanged += _ =>
        {
            if (_generated is null) return;
            _export.Disabled = true;
            SetStatus("Bridge thickness changed. Click Generate to commit the new bridge geometry before exporting.");
        };

        int insertionIndex = _frameThickness.GetIndex() + 1;
        controls.AddChild(_bridgeThicknessLabel);
        controls.MoveChild(_bridgeThicknessLabel, insertionIndex);
        controls.AddChild(_bridgeThickness);
        controls.MoveChild(_bridgeThickness, insertionIndex + 1);

        Button? generate = FindButton(this, "Generate");
        if (generate is not null)
        {
            generate.Pressed -= Generate;
            generate.Pressed += GenerateWithBridgeThickness;
        }

        _openRecipeDialog.FileSelected -= OpenRecipe;
        _openRecipeDialog.FileSelected += OpenRecipeWithBridgeThickness;
        _saveRecipeDialog.FileSelected -= SaveRecipe;
        _saveRecipeDialog.FileSelected += SaveRecipeWithBridgeThickness;
        _migratePreset.Pressed += RefreshBridgeThicknessVisibility;

        _bridgeThicknessUiInstalled = true;
        RefreshBridgeThicknessVisibility();
    }

    private void RefreshBridgeThicknessVisibility()
    {
        if (!GodotObject.IsInstanceValid(_bridgeThicknessLabel) || !GodotObject.IsInstanceValid(_bridgeThickness)) return;
        bool visible = _activeCategory == AssetCategory.Glasses && _activePresetVersion >= 2;
        _bridgeThicknessLabel.Visible = visible;
        _bridgeThickness.Visible = visible;
    }

    private AssetRecipe ReadRecipeWithBridgeThickness()
    {
        if (_activeCategory != AssetCategory.Glasses)
            return ReadCategoryRecipeFromUi();

        AssetRecipe recipe = MergeGlassesUiOntoWorkingRecipe(ReadRecipeFromUi());
        return recipe with
        {
            Geometry = recipe.Geometry with
            {
                BridgeThicknessBiasPixels = _activePresetVersion >= 2 ? (int)_bridgeThickness.Value : 0,
            },
        };
    }

    private void GenerateWithBridgeThickness()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(_sourcePath) || !File.Exists(_sourcePath))
                throw new InvalidOperationException("Choose a source PNG first.");

            AssetRecipe recipe = ReadRecipeWithBridgeThickness();
            _generated = AssetForgeCompiler.Generate(File.ReadAllBytes(_sourcePath), recipe);
            _preview.ShowGenerated(_generated, _sourcePath);
            _export.Disabled = false;
            MaskDiagnostics d = _generated.Diagnostics;
            string generation = recipe.Category switch
            {
                AssetCategory.Glasses when _generated.UsedGlassesTemplate => $"glasses@{recipe.PresetVersion} rounded template",
                AssetCategory.Glasses => "silhouette extrusion fallback",
                AssetCategory.TorsoShape or AssetCategory.FootShape => $"{recipe.PresetId}@{recipe.PresetVersion} literal replacement template",
                AssetCategory.Lamp when EnvironmentTemplateMapping.UsesLiteralTemplateSpace(recipe) => $"lamp@{recipe.PresetVersion} literal floor-template placement",
                AssetCategory.Lamp => $"lamp@{recipe.PresetVersion} legacy visible-bounds auto-fit",
                AssetCategory.Sofa => $"sofa@{recipe.PresetVersion} front-derived literal floor-template volume",
                _ => $"{recipe.PresetId}@{recipe.PresetVersion}",
            };
            string bridge = recipe.Category == AssetCategory.Glasses
                ? $" Bridge thickness {recipe.Geometry.BridgeThicknessBiasPixels:+0;-0;0}px."
                : string.Empty;
            bool replacement = recipe.Category is AssetCategory.TorsoShape or AssetCategory.FootShape;
            string performance = replacement && _generated.TriangleCount > RecommendedReplacementRuntimeTriangleBudget
                ? $" ⚠ Runtime mesh is above the recommended {RecommendedReplacementRuntimeTriangleBudget:N0}-triangle Buddy-part budget; lower Runtime mesh resolution in Advanced before export if painting or Studio interaction is slow."
                : replacement
                    ? $" Runtime mesh is within the recommended {RecommendedReplacementRuntimeTriangleBudget:N0}-triangle Buddy-part budget."
                    : string.Empty;
            SetStatus($"Generated with {generation}: {d.Components} foreground component(s), {d.Holes} interior hole(s), {_generated.VertexCount:N0} vertices, {_generated.TriangleCount:N0} triangles.{bridge} Lighting {recipe.LightingLevel:0.00}. Foreground: {_generated.Foreground.Summary}.{performance}");
            _hashes.Text = $"Input {_generated.InputHash[..12]}  Recipe {_generated.RecipeHash[..12]}  Geometry {_generated.GeometryHash[..12]}  Asset {_generated.CanonicalAssetHash[..12]}  ✓ deterministic output";
        }
        catch (Exception exception)
        {
            _generated = null;
            _export.Disabled = true;
            SetStatus("Generate failed: " + exception.Message);
        }
    }

    private void OpenRecipeWithBridgeThickness(string path)
    {
        try
        {
            AssetRecipe recipe = RecipeCodec.Read(File.ReadAllText(path));
            SetActiveCategoryFromRecipe(recipe);
            OpenRecipe(path);
            if (GodotObject.IsInstanceValid(_bridgeThickness))
                _bridgeThickness.Value = recipe.Category == AssetCategory.Glasses
                    ? recipe.Geometry.BridgeThicknessBiasPixels
                    : 0;
            RefreshBridgeThicknessVisibility();
            ConfigureActiveCategoryUi();
            SetStatus($"Opened {recipe.PresetId}@{recipe.PresetVersion} ({recipe.Category}). Its preset version and hidden recipe metadata will be preserved until you explicitly change them.");
        }
        catch (Exception exception)
        {
            SetStatus("Open recipe failed: " + exception.Message);
        }
    }

    private void SaveRecipeWithBridgeThickness(string path)
    {
        try
        {
            if (!path.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) path += ".json";
            AssetRecipe recipe = ReadRecipeWithBridgeThickness();
            File.WriteAllText(path, RecipeCodec.WriteCanonical(recipe));
            SetStatus($"{recipe.PresetId}@{recipe.PresetVersion} recipe saved: {path}");
        }
        catch (Exception exception)
        {
            SetStatus("Save recipe failed: " + exception.Message);
        }
    }

    private static Button? FindButton(Node root, string text)
    {
        foreach (Node child in root.GetChildren())
        {
            if (child is Button button && button.Text == text) return button;
            Button? nested = FindButton(child, text);
            if (nested is not null) return nested;
        }
        return null;
    }
}
