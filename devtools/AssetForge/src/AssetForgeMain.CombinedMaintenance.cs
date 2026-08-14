using DesktopBuddy.AssetForge.Core;

namespace DesktopBuddy.AssetForge;

public partial class AssetForgeMain
{
    private bool _combinedMaintenanceInstalled;

    private void EnsureCombinedMaintenanceUi()
    {
        if (_combinedMaintenanceInstalled || !_modernWorkspaceInstalled ||
            !Godot.GodotObject.IsInstanceValid(_verifyAll)) return;

        _regenerate.Pressed -= Regenerate;
        _regenerateAll.Pressed -= RegenerateAll;
        _verify.Pressed -= Verify;
        _verifyAll.Pressed -= VerifyAll;

        _regenerate.Pressed += RegenerateActiveAsset;
        _regenerateAll.Pressed += RegenerateAllAssets;
        _verify.Pressed += VerifyActiveAsset;
        _verifyAll.Pressed += VerifyAllAssets;
        _combinedMaintenanceInstalled = true;
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
                SetStatus(Format(result));
            }
            else
            {
                AssetVerificationResult result = RepositoryAssetVerifier.Verify(root, id);
                SetStatus(Format(result));
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
            SetStatus(Format(result));
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
                SetStatus($"Regenerated {string.Join(", ", result.RegeneratedAssetIds)}.\n{Format(result.Verification)}");
            }
            else
            {
                RepositoryRegenerationResult result = RepositoryAssetRegenerator.Regenerate(root, id);
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
            SetStatus($"Regenerated {result.RegeneratedIds.Count} Asset Forge asset(s).\n{Format(result.Verification)}");
        }
        catch (Exception exception)
        {
            SetStatus("Regenerate All failed: " + exception.Message);
        }
    }

    private string ActiveMaintenanceId()
    {
        string id = _activeCategory == AssetCategory.Lamp
            ? _featureId.Text.Trim()
            : _featureId.Text.Trim();
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
