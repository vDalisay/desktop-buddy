using DesktopBuddy.AssetForge.Core;
using Godot;

namespace DesktopBuddy.AssetForge;

public partial class AssetForgeMain
{
    private bool _categoryPresetMigrationInstalled;

    private void EnsureCategoryPresetMigrationUi()
    {
        if (_categoryPresetMigrationInstalled || !_categoryWorkflowInstalled || !GodotObject.IsInstanceValid(_migratePreset)) return;
        _migratePreset.Pressed -= MigrateToTemplateSpace;
        _migratePreset.Pressed += MigrateActivePreset;
        _categoryPresetMigrationInstalled = true;
        RefreshCategoryPresetMigrationUi();
    }

    private void RefreshCategoryPresetMigrationUi()
    {
        if (!_categoryPresetMigrationInstalled || !GodotObject.IsInstanceValid(_migratePreset)) return;
        AssetRecipe basis = _workingRecipeBase.Category == _activeCategory
            ? _workingRecipeBase with { PresetVersion = _activePresetVersion }
            : CategoryDefaults(_activeCategory);
        AssetRecipeMigrationPlan? plan = AssetRecipeMigration.Plan(basis);
        _migratePreset.Visible = plan is not null;
        if (plan is null) return;
        _migratePreset.Text = plan.ButtonText;
        _migratePreset.TooltipText = plan.Summary + (plan.RequiresSourceRealignment
            ? " The source must be checked/repositioned on the current coloring guide before Generate."
            : " Authored template placement is preserved.");
    }

    private void MigrateActivePreset()
    {
        try
        {
            AssetRecipe current = ReadRecipeWithBridgeThickness();
            AssetRecipeMigrationPlan plan = AssetRecipeMigration.Plan(current)
                ?? throw new InvalidOperationException($"{current.PresetId}@{current.PresetVersion} is already on the latest supported authoring contract.");
            AssetRecipe migrated = AssetRecipeMigration.MigrateToLatest(current);

            _workingRecipeBase = migrated;
            _activePresetVersion = migrated.PresetVersion;
            ApplyRecipe(migrated);
            ApplyReplacementQualityRecipe(migrated);
            ApplyLampRecipe(migrated);
            ApplySofaRecipe(migrated);
            ApplyEnvironmentPropRecipe(migrated);
            _preview.ClearGenerated();
            _generated = null;
            _export.Disabled = true;
            ConfigureActiveCategoryUi();
            RefreshCategoryPresetMigrationUi();

            string next = plan.RequiresSourceRealignment
                ? " Save/open the current category template and reposition or redraw the clean source against it before Generate; the old source placement is not silently reinterpreted."
                : " Existing literal template placement is preserved; click Generate to inspect the new deterministic geometry before export.";
            SetStatus($"Migrated this working recipe to {migrated.PresetId}@{migrated.PresetVersion}. {plan.Summary}{next}");
        }
        catch (Exception exception)
        {
            SetStatus("Preset migration failed: " + exception.Message);
        }
    }
}
