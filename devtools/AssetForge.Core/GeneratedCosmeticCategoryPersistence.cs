namespace DesktopBuddy.AssetForge.Core;

/// <summary>
/// Narrow post-export adapter while the original transactional exporter remains backwards-compatible
/// with its Glasses vertical slice. It can only rewrite the generated resource for the exact recipe
/// feature ID and only the typed Slot marker.
/// </summary>
public static class GeneratedCosmeticCategoryPersistence
{
    public static int SlotNumber(AssetRecipe recipe) => recipe.Category switch
    {
        AssetCategory.Glasses => 8,
        AssetCategory.TorsoShape => 10,
        AssetCategory.FootShape => 11,
        _ => throw new NotSupportedException($"No generated Buddy Studio slot exists for {recipe.Category}."),
    };

    public static string ExpectedMarker(AssetRecipe recipe) => $"Slot = {SlotNumber(recipe)}";

    public static void Apply(string repositoryRoot, AssetRecipe recipe)
    {
        string root = RepositoryAssetVerifier.ValidateRoot(repositoryRoot);
        string path = Path.Combine(root, "data", "cosmetics", "generated", recipe.FeatureId + ".tres");
        if (!File.Exists(path))
            throw new FileNotFoundException("Generated cosmetic definition is missing.", path);

        string text = File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal);
        string[] lines = text.Split('\n');
        bool replaced = false;
        string wanted = ExpectedMarker(recipe);
        for (int i = 0; i < lines.Length; i++)
        {
            if (!lines[i].StartsWith("Slot = ", StringComparison.Ordinal)) continue;
            lines[i] = wanted;
            replaced = true;
            break;
        }
        if (!replaced) throw new FormatException("Generated cosmetic definition has no Slot marker.");
        File.WriteAllText(path, string.Join("\n", lines));
    }
}
