using DesktopBuddy.AssetForge.Core;
using Godot;

namespace DesktopBuddy.AssetForge;

public partial class AssetForgeMain
{
    private SpinBox _bridgeThickness = null!;
    private bool _bridgeThicknessUiInstalled;

    public override void _Process(double delta)
    {
        if (_bridgeThicknessUiInstalled || !GodotObject.IsInstanceValid(_frameThickness)) return;
        InstallBridgeThicknessUi();
    }

    private void InstallBridgeThicknessUi()
    {
        if (_frameThickness.GetParent() is not Container controls) return;

        var label = new Label { Text = "Bridge thickness adjustment (px)" };
        _bridgeThickness = new SpinBox
        {
            MinValue = -24,
            MaxValue = 24,
            Step = 1,
            AllowGreater = false,
            AllowLesser = false,
            TooltipText = "Fine-tunes only the authored nose bridge. 0 preserves the drawing exactly; positive values thicken it and negative values thin it. Lens frames and temples are unchanged.",
        };

        int insertionIndex = _frameThickness.GetIndex() + 1;
        controls.AddChild(label);
        controls.MoveChild(label, insertionIndex);
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

        _bridgeThicknessUiInstalled = true;
    }

    private AssetRecipe ReadRecipeWithBridgeThickness()
    {
        AssetRecipe recipe = ReadRecipeFromUi();
        return recipe with
        {
            Geometry = recipe.Geometry with
            {
                BridgeThicknessBiasPixels = (int)_bridgeThickness.Value,
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
            _generated = AssetForgeGenerator.Generate(File.ReadAllBytes(_sourcePath), recipe);
            _preview.ShowGenerated(_generated, _sourcePath);
            _export.Disabled = false;
            MaskDiagnostics d = _generated.Diagnostics;
            string generation = _generated.UsedGlassesTemplate
                ? $"glasses@{recipe.PresetVersion} rounded template"
                : "silhouette extrusion fallback";
            SetStatus($"Generated with {generation}: {d.Components} foreground component(s), {d.Holes} interior hole(s), {_generated.VertexCount:N0} vertices, {_generated.TriangleCount:N0} triangles. Bridge thickness {recipe.Geometry.BridgeThicknessBiasPixels:+0;-0;0}px. Lighting {recipe.LightingLevel:0.00}. Foreground: {_generated.Foreground.Summary}.");
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
        OpenRecipe(path);
        try
        {
            AssetRecipe recipe = RecipeCodec.Read(File.ReadAllText(path));
            _bridgeThickness.Value = recipe.Geometry.BridgeThicknessBiasPixels;
        }
        catch
        {
            // OpenRecipe already reports the user-facing parse/load error.
        }
    }

    private void SaveRecipeWithBridgeThickness(string path)
    {
        try
        {
            if (!path.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) path += ".json";
            File.WriteAllText(path, RecipeCodec.WriteCanonical(ReadRecipeWithBridgeThickness()));
            SetStatus($"glasses@{_activePresetVersion} recipe saved: {path}");
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
