using System.Text;

namespace DesktopBuddy.AssetForge.Core;

public sealed record ExportedAssetForgeAsset(
    string StableId,
    string DisplayName,
    AssetFamily Family,
    AssetCategory Category,
    AssetRecipe Recipe);

/// <summary>
/// Shared destructive-maintenance boundary. Delete is intentionally explicit developer tooling:
/// source authoring, generated binaries, trusted definition and aggregate catalogue membership are
/// removed together. Git remains the undo/history mechanism, matching the existing Buddy delete.
/// </summary>
public static class RepositoryAssetForgeDeletion
{
    public static IReadOnlyList<ExportedAssetForgeAsset> ListExported(string repositoryRoot)
    {
        string root = RepositoryAssetVerifier.ValidateRoot(repositoryRoot);
        var assets = new List<ExportedAssetForgeAsset>();
        assets.AddRange(RepositoryExporter.ListExported(root).Select(static recipe =>
            new ExportedAssetForgeAsset(recipe.FeatureId, recipe.DisplayName, recipe.AssetFamily, recipe.Category, recipe)));
        assets.AddRange(RepositoryEnvironmentVerifier.DiscoverRecipes(root).Select(static path => RecipeCodec.Read(File.ReadAllText(path))).Select(static recipe =>
            new ExportedAssetForgeAsset(recipe.AssetId, recipe.DisplayName, recipe.AssetFamily, recipe.Category, recipe)));
        return assets.OrderBy(static asset => asset.Family)
            .ThenBy(static asset => asset.Category)
            .ThenBy(static asset => asset.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static asset => asset.StableId, StringComparer.Ordinal)
            .ToArray();
    }

    public static AssetForgeRepositoryVerificationResult Delete(string repositoryRoot, string stableId)
    {
        string root = RepositoryAssetVerifier.ValidateRoot(repositoryRoot);
        if (RepositoryAssetForgeMaintenance.IsEnvironmentId(stableId)) DeleteEnvironment(root, stableId);
        else RepositoryExporter.Delete(root, stableId);
        return RepositoryAssetForgeMaintenance.VerifyAll(root);
    }

    private static void DeleteEnvironment(string root, string assetId)
    {
        string recipePath = RepositoryEnvironmentVerifier.FindRecipe(root, assetId);
        AssetRecipe recipe = RecipeCodec.Read(File.ReadAllText(recipePath));
        if (recipe.AssetFamily != AssetFamily.Environment || !RepositoryEnvironmentExporter.IsSupportedCategory(recipe.Category))
            throw new InvalidOperationException($"{assetId} is not a supported generated Environment asset.");

        string authoringDirectory = Path.GetDirectoryName(recipePath)
            ?? throw new InvalidOperationException("Environment recipe has no authoring directory.");
        EnsureInside(root, authoringDirectory, "authoring/asset-forge/");
        if (Directory.Exists(authoringDirectory)) Directory.Delete(authoringDirectory, recursive: true);

        string generated = Path.Combine(root, Native($"assets/generated/environment/{recipe.AssetId}"));
        EnsureInside(root, generated, "assets/generated/environment/");
        if (Directory.Exists(generated)) Directory.Delete(generated, recursive: true);

        string definition = Path.Combine(root, Native($"data/environment/generated/{recipe.AssetId}.tres"));
        EnsureInside(root, definition, "data/environment/generated/");
        if (File.Exists(definition)) File.Delete(definition);

        RebuildEnvironmentCatalogue(root);
    }

    private static void RebuildEnvironmentCatalogue(string root)
    {
        string directory = Path.Combine(root, Native("data/environment/generated"));
        string[] definitions = Directory.Exists(directory)
            ? Directory.GetFiles(directory, "*.tres", SearchOption.TopDirectoryOnly)
                .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/'))
                .OrderBy(static path => path, StringComparer.Ordinal)
                .ToArray()
            : [];

        var text = new StringBuilder();
        text.AppendLine($"[gd_resource type=\"Resource\" script_class=\"EnvironmentDecorationCatalogueResource\" load_steps={definitions.Length + 2} format=3]");
        text.AppendLine();
        text.AppendLine("[ext_resource type=\"Script\" path=\"res://src/Environment/EnvironmentDecorationCatalogueResource.cs\" id=\"1\"]");
        for (int i = 0; i < definitions.Length; i++)
            text.AppendLine($"[ext_resource type=\"Resource\" path=\"res://{definitions[i]}\" id=\"{i + 2}\"]");
        text.AppendLine();
        text.AppendLine("[resource]");
        text.AppendLine("script = ExtResource(\"1\")");
        text.Append("Entries = Array[Resource]([");
        for (int i = 0; i < definitions.Length; i++)
        {
            if (i > 0) text.Append(", ");
            text.Append($"ExtResource(\"{i + 2}\")");
        }
        text.AppendLine("])");

        string aggregate = Path.Combine(root, Native("data/environment/generated_decorations.tres"));
        Directory.CreateDirectory(Path.GetDirectoryName(aggregate)!);
        File.WriteAllText(aggregate, text.ToString().Replace("\r\n", "\n", StringComparison.Ordinal));
    }

    private static void EnsureInside(string root, string path, string ownedPrefix)
    {
        string relative = Path.GetRelativePath(root, path).Replace('\\', '/');
        if (relative.Contains("../", StringComparison.Ordinal) || relative.StartsWith("..", StringComparison.Ordinal) ||
            !relative.StartsWith(ownedPrefix, StringComparison.Ordinal))
            throw new InvalidOperationException($"Asset Forge cannot delete outside {ownedPrefix}: {relative}");
    }

    private static string Native(string relative) => relative.Replace('/', Path.DirectorySeparatorChar);
}
