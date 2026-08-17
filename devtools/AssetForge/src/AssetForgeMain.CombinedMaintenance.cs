using DesktopBuddy.AssetForge.Core;
using Godot;

namespace DesktopBuddy.AssetForge;

public partial class AssetForgeMain
{
    private bool _combinedMaintenanceInstalled;
    private IReadOnlyList<ExportedAssetForgeAsset> _combinedDeleteAssets = [];

    private void EnsureCombinedMaintenanceUi()
    {
        if (_combinedMaintenanceInstalled || !_modernWorkspaceInstalled) return;
        Button? regenerate = RepositoryToolButton("Regenerate");
        Button? regenerateAll = RepositoryToolButton("Regenerate All");
        Button? verify = RepositoryToolButton("Verify");
        Button? verifyAll = RepositoryToolButton("Verify All");
        Button? delete = RepositoryToolButton("Delete Asset…");
        if (!GodotObject.IsInstanceValid(regenerate) || !GodotObject.IsInstanceValid(regenerateAll) ||
            !GodotObject.IsInstanceValid(verify) || !GodotObject.IsInstanceValid(verifyAll) ||
            !GodotObject.IsInstanceValid(delete)) return;

        regenerate!.Pressed -= RegenerateCurrent;
        regenerateAll!.Pressed -= RegenerateAll;
        verify!.Pressed -= VerifyCurrent;
        verifyAll!.Pressed -= VerifyAll;
        delete!.Pressed -= ShowDeleteDialog;
        if (GodotObject.IsInstanceValid(_deleteDialog)) _deleteDialog.Confirmed -= DeleteSelected;

        regenerate.Pressed += RegenerateActiveAsset;
        regenerateAll.Pressed += RegenerateAllAssets;
        verify.Pressed += VerifyActiveAsset;
        verifyAll.Pressed += VerifyAllAssets;
        delete.Pressed += ShowCombinedDeleteDialog;
        if (GodotObject.IsInstanceValid(_deleteDialog)) _deleteDialog.Confirmed += DeleteCombinedSelected;
        _combinedMaintenanceInstalled = true;
    }

    private Button? RepositoryToolButton(string text)
    {
        Node? tools = FindChild("RepositoryTools", recursive: true, owned: false);
        if (!GodotObject.IsInstanceValid(tools)) return null;
        foreach (Node child in tools!.GetChildren())
            if (child is Button button && string.Equals(button.Text, text, StringComparison.Ordinal))
                return button;
        return null;
    }

    private void ShowCombinedDeleteDialog()
    {
        try
        {
            _deleteList.Clear();
            _combinedDeleteAssets = RepositoryAssetForgeDeletion.ListExported(FindRepositoryRoot());
            foreach (ExportedAssetForgeAsset asset in _combinedDeleteAssets)
            {
                string family = asset.Family == AssetFamily.Environment ? "Environment" : "Buddy Studio";
                _deleteList.AddItem($"{family} / {asset.Category} · {asset.DisplayName}  ({asset.StableId})");
            }
            _deleteDialog.GetOkButton().Disabled = _combinedDeleteAssets.Count == 0;
            if (_combinedDeleteAssets.Count == 0) _deleteList.AddItem("No exported Asset Forge assets.", selectable: false);
            var size = new Vector2I(DeleteDialogSize.X, Math.Min(DeleteDialogSize.Y, (int)GetViewportRect().Size.Y - 120));
            _deleteDialog.PopupCentered(size);
            _deleteDialog.Size = size;
        }
        catch (Exception exception)
        {
            SetStatus("Could not list exported assets: " + exception.Message);
        }
    }

    private void DeleteCombinedSelected()
    {
        try
        {
            int[] selected = _deleteList.GetSelectedItems();
            if (selected.Length == 0) throw new InvalidOperationException("Select an asset to delete.");
            if (selected[0] < 0 || selected[0] >= _combinedDeleteAssets.Count)
                throw new InvalidOperationException("The selected Asset Forge entry is no longer available.");
            ExportedAssetForgeAsset asset = _combinedDeleteAssets[selected[0]];
            AssetForgeRepositoryVerificationResult verification = RepositoryAssetForgeDeletion.Delete(FindRepositoryRoot(), asset.StableId);
            _hashes.Text = Format(verification);
            SetStatus(verification.Passed
                ? $"Deleted {asset.DisplayName} ({asset.StableId}); Verify All passed. It no longer ships with the game."
                : $"Deleted {asset.DisplayName} ({asset.StableId}), but Verify All reports remaining repository drift.");
            _combinedDeleteAssets = [];
        }
        catch (Exception exception)
        {
            SetStatus("Delete failed: " + exception.Message);
        }
    }

    private void VerifyActiveAsset()
    {
        try
        {
            string root = FindRepositoryRoot();
            string id = ActiveMaintenanceId();
            if (RepositoryAssetForgeMaintenance.IsEnvironmentId(id))
            {
                EnvironmentAssetVerificationResult result = RepositoryEnvironmentVerifier.Verify(root, id);
                _hashes.Text = Format(result);
                SetStatus(result.Passed ? $"Verify passed for {result.AssetId}." : $"Verify failed for {result.AssetId}.");
            }
            else
            {
                AssetVerificationResult result = RepositoryAssetVerifier.Verify(root, id);
                _hashes.Text = Format(result);
                SetStatus(result.Passed ? $"Verify passed for {result.FeatureId}." : $"Verify failed for {result.FeatureId}.");
            }
        }
        catch (Exception exception)
        {
            SetStatus("Verify failed: " + exception.Message);
        }
    }

    private void VerifyAllAssets()
    {
        try
        {
            AssetForgeRepositoryVerificationResult result = RepositoryAssetForgeMaintenance.VerifyAll(FindRepositoryRoot());
            _hashes.Text = Format(result);
            SetStatus(result.Passed
                ? $"Verify All passed: {result.PassedCount}/{result.AssetCount} Asset Forge asset(s) match committed generated content."
                : $"Verify All failed: {result.PassedCount}/{result.AssetCount} Asset Forge asset(s) passed. Review diagnostics below.");
        }
        catch (Exception exception)
        {
            SetStatus("Verify All failed: " + exception.Message);
        }
    }

    private void RegenerateActiveAsset()
    {
        try
        {
            string root = FindRepositoryRoot();
            string id = ActiveMaintenanceId();
            if (RepositoryAssetForgeMaintenance.IsEnvironmentId(id))
            {
                EnvironmentRepositoryRegenerationResult result = RepositoryEnvironmentRegenerator.Regenerate(root, id);
                _hashes.Text = Format(result.Verification);
                SetStatus($"Regenerated {string.Join(", ", result.RegeneratedAssetIds)}.\n{Format(result.Verification)}");
            }
            else
            {
                RepositoryRegenerationResult result = RepositoryAssetRegenerator.Regenerate(root, id);
                _hashes.Text = Format(result.Verification);
                SetStatus($"Regenerated {string.Join(", ", result.RegeneratedFeatureIds)}.\n{Format(result.Verification)}");
            }
        }
        catch (Exception exception)
        {
            SetStatus("Regenerate failed: " + exception.Message);
        }
    }

    private void RegenerateAllAssets()
    {
        try
        {
            AssetForgeRepositoryRegenerationResult result = RepositoryAssetForgeMaintenance.RegenerateAll(FindRepositoryRoot());
            _hashes.Text = Format(result.Verification);
            SetStatus($"Regenerated {result.RegeneratedIds.Count} Asset Forge asset(s).\n{Format(result.Verification)}");
        }
        catch (Exception exception)
        {
            SetStatus("Regenerate All failed: " + exception.Message);
        }
    }

    private string ActiveMaintenanceId()
    {
        string id = _featureId.Text.Trim();
        if (string.IsNullOrWhiteSpace(id))
            throw new InvalidOperationException("Enter or open an authored Asset Forge ID first.");
        return id;
    }

    private static string Format(AssetVerificationResult result) =>
        $"{(result.Passed ? "OK" : "FAILED")} {result.FeatureId}\n" + string.Join("\n", result.Diagnostics);

    private static string Format(EnvironmentAssetVerificationResult result) =>
        $"{(result.Passed ? "OK" : "FAILED")} {result.AssetId}\n" + string.Join("\n", result.Diagnostics);

    private static string Format(RepositoryVerificationResult result) =>
        $"{(result.Passed ? "OK" : "FAILED")} Buddy Studio · {result.PassedCount}/{result.Assets.Count} assets\n" +
        string.Join("\n", result.RepositoryDiagnostics);

    private static string Format(EnvironmentRepositoryVerificationResult result) =>
        $"{(result.Passed ? "OK" : "FAILED")} Environment · {result.PassedCount}/{result.Assets.Count} assets\n" +
        string.Join("\n", result.RepositoryDiagnostics);

    private static string Format(AssetForgeRepositoryVerificationResult result)
    {
        var lines = new List<string>
        {
            $"{(result.Passed ? "OK" : "FAILED")} Asset Forge repository · {result.PassedCount}/{result.AssetCount} assets",
            $"Buddy Studio: {result.BuddyStudio.PassedCount}/{result.BuddyStudio.Assets.Count}",
            $"Environment: {result.Environment.PassedCount}/{result.Environment.Assets.Count}",
        };
        lines.AddRange(result.BuddyStudio.RepositoryDiagnostics.Select(static line => "Buddy: " + line));
        lines.AddRange(result.Environment.RepositoryDiagnostics.Select(static line => "Environment: " + line));
        foreach (AssetVerificationResult asset in result.BuddyStudio.Assets.Where(static asset => !asset.Passed))
            lines.AddRange(asset.Diagnostics.Select(line => asset.FeatureId + ": " + line));
        foreach (EnvironmentAssetVerificationResult asset in result.Environment.Assets.Where(static asset => !asset.Passed))
            lines.AddRange(asset.Diagnostics.Select(line => asset.AssetId + ": " + line));
        return string.Join("\n", lines);
    }
}
