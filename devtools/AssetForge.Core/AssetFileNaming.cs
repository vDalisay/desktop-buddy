using System.Text;

namespace DesktopBuddy.AssetForge.Core;

/// <summary>
/// Human-readable generated-file naming shared by every Asset Forge family. Stable IDs still own
/// directories and persistence; the display name is used only for the developer-facing mesh file.
/// </summary>
public static class AssetFileNaming
{
    public static string MeshFileName(AssetRecipe recipe)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        string stem = PascalFileStem(recipe.DisplayName);
        return stem + "Mesh.glb";
    }

    public static string PascalFileStem(string displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName)) return "Asset";
        string normalized = displayName.Normalize(NormalizationForm.FormKC);
        var result = new StringBuilder(Math.Min(normalized.Length, 72));
        var token = new StringBuilder();

        void FlushToken()
        {
            if (token.Length == 0) return;
            result.Append(char.ToUpperInvariant(token[0]));
            if (token.Length > 1) result.Append(token.ToString(1, token.Length - 1));
            token.Clear();
        }

        foreach (char c in normalized)
        {
            if (char.IsLetterOrDigit(c)) token.Append(c);
            else FlushToken();
            if (result.Length + token.Length >= 72) break;
        }
        FlushToken();
        return result.Length == 0 ? "Asset" : result.ToString();
    }

    /// <summary>
    /// Asset directories are wholly Asset-Forge-owned. After a successful export, remove legacy
    /// mesh.glb or older display-name GLBs so each generated item contains one authoritative mesh.
    /// </summary>
    public static void RemoveStaleMeshes(string assetDirectory, string expectedMeshFileName)
    {
        if (!Directory.Exists(assetDirectory)) return;
        foreach (string path in Directory.GetFiles(assetDirectory, "*.glb", SearchOption.TopDirectoryOnly))
        {
            if (string.Equals(Path.GetFileName(path), expectedMeshFileName, StringComparison.Ordinal)) continue;
            File.Delete(path);
        }
    }
}
