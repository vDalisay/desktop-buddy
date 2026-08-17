using System.Text;

namespace DesktopBuddy.AssetForge.Core;

/// <summary>
/// Human-readable filenames for exported generated geometry. The stable asset/feature ID remains
/// the package identity; this only makes the GLB itself useful when copied out for inspection.
/// </summary>
public static class AssetExportFileNames
{
    public static string MeshFileName(AssetRecipe recipe)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        string stem = PascalFileStem(recipe.DisplayName);
        if (stem.Length == 0)
        {
            string fallback = recipe.AssetFamily == AssetFamily.Environment ? recipe.AssetId : recipe.FeatureId;
            stem = PascalFileStem(fallback);
        }
        if (stem.Length == 0) stem = "GeneratedAsset";
        return stem + "Mesh.glb";
    }

    public static string PascalFileStem(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var result = new StringBuilder(value.Length);
        bool startWord = true;
        foreach (char c in value)
        {
            if (!char.IsLetterOrDigit(c))
            {
                startWord = true;
                continue;
            }
            result.Append(startWord ? char.ToUpperInvariant(c) : c);
            startWord = false;
        }
        return result.ToString();
    }
}
