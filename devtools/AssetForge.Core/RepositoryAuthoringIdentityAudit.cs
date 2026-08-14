namespace DesktopBuddy.AssetForge.Core;

/// <summary>
/// AF-15 repository-level authoring identity audit. Stable IDs are the runtime persistence key, so
/// two source recipes may never silently compete for the same generated asset even when they live
/// under different authoring folders. Same-family IDs are checked together; Buddy and Environment
/// namespaces remain intentionally independent.
/// </summary>
public static class RepositoryAuthoringIdentityAudit
{
    public static IReadOnlyList<string> Audit(string repositoryRoot)
    {
        string root = RepositoryAssetVerifier.ValidateRoot(repositoryRoot);
        var diagnostics = new List<string>();
        var authored = new List<(AssetFamily Family, string StableId, string Path)>();

        foreach (string recipePath in RepositoryAssetVerifier.DiscoverAllRecipeFiles(root))
        {
            try
            {
                AssetRecipe recipe = RecipeCodec.Read(File.ReadAllText(recipePath));
                string stableId = recipe.AssetFamily == AssetFamily.Environment
                    ? recipe.AssetId
                    : recipe.FeatureId;
                authored.Add((recipe.AssetFamily, stableId, Relative(root, recipePath)));
            }
            catch (Exception exception)
            {
                diagnostics.Add($"invalid authored recipe {Relative(root, recipePath)}: {exception.Message}");
            }
        }

        foreach (IGrouping<(AssetFamily Family, string StableId), (AssetFamily Family, string StableId, string Path)> group
                 in authored.GroupBy(static item => (item.Family, item.StableId)))
        {
            string[] paths = group.Select(static item => item.Path).OrderBy(static path => path, StringComparer.Ordinal).ToArray();
            if (paths.Length <= 1) continue;
            diagnostics.Add(
                $"duplicate Asset Forge {group.Key.Family} stable ID '{group.Key.StableId}' is authored by: {string.Join(", ", paths)}");
        }

        return diagnostics;
    }

    private static string Relative(string root, string path) =>
        Path.GetRelativePath(root, path).Replace('\\', '/');
}
