namespace DesktopBuddy.AssetForge.Core;

public sealed record AssetForgeRepositoryVerificationResult(
    RepositoryVerificationResult BuddyStudio,
    EnvironmentRepositoryVerificationResult Environment)
{
    public bool Passed => BuddyStudio.Passed && Environment.Passed;
    public int AssetCount => BuddyStudio.Assets.Count + Environment.Assets.Count;
    public int PassedCount => BuddyStudio.PassedCount + Environment.PassedCount;
}

public sealed record AssetForgeRepositoryRegenerationResult(
    IReadOnlyList<string> RegeneratedIds,
    AssetForgeRepositoryVerificationResult Verification);

public static class RepositoryAssetForgeMaintenance
{
    public static AssetForgeRepositoryVerificationResult VerifyAll(string repositoryRoot) => new(
        RepositoryAssetVerifier.VerifyAll(repositoryRoot),
        RepositoryEnvironmentVerifier.VerifyAll(repositoryRoot));

    public static AssetForgeRepositoryRegenerationResult RegenerateAll(string repositoryRoot)
    {
        RepositoryRegenerationResult buddy = RepositoryAssetRegenerator.RegenerateAll(repositoryRoot);
        EnvironmentRepositoryRegenerationResult environment = RepositoryEnvironmentRegenerator.RegenerateAll(repositoryRoot);
        var ids = new List<string>(buddy.RegeneratedFeatureIds.Count + environment.RegeneratedAssetIds.Count);
        ids.AddRange(buddy.RegeneratedFeatureIds);
        ids.AddRange(environment.RegeneratedAssetIds);
        return new AssetForgeRepositoryRegenerationResult(ids, VerifyAll(repositoryRoot));
    }

    public static bool IsEnvironmentId(string id) =>
        !string.IsNullOrWhiteSpace(id) && id.StartsWith("decoration.", StringComparison.Ordinal);
}
