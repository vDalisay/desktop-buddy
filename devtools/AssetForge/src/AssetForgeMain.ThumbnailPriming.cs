using DesktopBuddy.AssetForge.Core;
using Godot;

namespace DesktopBuddy.AssetForge;

public partial class AssetForgeMain
{
    private string _primedThumbnailAssetHash = string.Empty;
    private bool _thumbnailPrimeInFlight;

    public override void _PhysicsProcess(double delta)
    {
        _ = delta;
        EnsureModernWorkspaceUi();
        EnsureCombinedMaintenanceUi();
        EnsureGenerationWarningUi();
        EnsureCategoryPresetMigrationUi();
        RefreshCategoryPresetMigrationUi();
        if (_thumbnailPrimeInFlight ||
            _generated is null ||
            _generated.Recipe.AssetFamily != AssetFamily.BuddyStudio ||
            !GodotObject.IsInstanceValid(_preview) ||
            string.Equals(_primedThumbnailAssetHash, _generated.CanonicalAssetHash, StringComparison.Ordinal))
            return;

        _thumbnailPrimeInFlight = true;
        GeneratedAsset target = _generated;
        _ = PrimeCanonicalBuddyThumbnailAsync(target);
    }

    private async Task PrimeCanonicalBuddyThumbnailAsync(GeneratedAsset target)
    {
        try
        {
            // ShowGenerated() already reset the preview. Reapply the shared recipe pose and then
            // wait one rendered frame so SubViewport.GetTexture() cannot return a stale arbitrary
            // orbit from the previous asset/category.
            _preview.ApplyCanonicalThumbnailPose(target.Recipe.Thumbnail);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (_generated is null ||
                !string.Equals(_generated.CanonicalAssetHash, target.CanonicalAssetHash, StringComparison.Ordinal) ||
                !GodotObject.IsInstanceValid(_preview))
                return;

            byte[] captured = _preview.CaptureThumbnailPng();
            AssetThumbnailCache.GetOrCreate(target, () => captured);
            _primedThumbnailAssetHash = target.CanonicalAssetHash;
        }
        catch (Exception exception)
        {
            // Thumbnail priming is an optimization/consistency step, not generation authority. A
            // later explicit export may still run the normal producer and surface any real error.
            GD.PushWarning("Asset Forge canonical thumbnail priming skipped: " + exception.Message);
        }
        finally
        {
            _thumbnailPrimeInFlight = false;
        }
    }
}
