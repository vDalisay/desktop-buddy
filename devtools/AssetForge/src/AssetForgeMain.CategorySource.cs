using DesktopBuddy.AssetForge.Core;

namespace DesktopBuddy.AssetForge;

public partial class AssetForgeMain
{
    private bool _categorySourceHandlerInstalled;

    private void EnsureCategorySourceHandler()
    {
        if (_categorySourceHandlerInstalled || _sourceDialog is null) return;
        _sourceDialog.FileSelected -= SetSource;
        _sourceDialog.FileSelected += SetCategorySource;
        _categorySourceHandlerInstalled = true;
    }

    private void SetCategorySource(string path)
    {
        _sourcePath = path;
        _source.Text = path;
        _generated = null;
        _export.Disabled = true;

        string message = _activeCategory switch
        {
            AssetCategory.Glasses when _activePresetVersion >= 2 =>
                "Source selected. glasses@2 keeps this exact 1024×1024 placement relative to the Buddy-head guide.",
            AssetCategory.Glasses =>
                "Source selected. Legacy glasses@1 auto-fits the detected frame as before.",
            AssetCategory.TorsoShape =>
                "Source selected. torso_shape@1 keeps the fixed torso-template placement and derives only the visible replacement volume.",
            AssetCategory.FootShape =>
                "Source selected. foot_shape@1 keeps the fixed single-foot template placement; the paired counterpart is generated deterministically.",
            AssetCategory.Lamp when _activePresetVersion >= 3 =>
                "Source selected. lamp@3 preserves fixed floor-template coordinates and smooths the generated rim against the full-resolution source alpha.",
            AssetCategory.Lamp when _activePresetVersion == 2 =>
                "Source selected. lamp@2 preserves fixed floor-template coordinates with the accepted v0.1 mesh-edge behavior for reproducible existing recipes.",
            AssetCategory.Lamp =>
                "Source selected. Legacy lamp@1 auto-fits the visible lamp bounds while retaining the authored light metadata.",
            AssetCategory.Sofa when _activePresetVersion >= 2 =>
                "Source selected. sofa@2 preserves fixed floor-template coordinates and generates a smoothed front-derived stylized volume.",
            AssetCategory.Sofa =>
                "Source selected. sofa@1 preserves fixed floor-template coordinates with the accepted v0.1 front-derived mesh behavior.",
            AssetCategory.Table =>
                "Source selected. table@1 preserves the fixed floor/template coordinates; tabletop and supports remain where they were authored.",
            AssetCategory.Plant =>
                "Source selected. plant@1 preserves the fixed floor/template coordinates and generates an inflated smoothed volume from the clean silhouette.",
            AssetCategory.Painting =>
                "Source selected. painting@1 preserves fixed wall-template coordinates; the template centre becomes the wall anchor and the artwork is not auto-fitted.",
            _ => $"Source selected for {_activeCategory}.",
        };
        SetStatus(message);
    }
}
