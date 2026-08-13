using System.Globalization;

namespace DesktopBuddy.AssetForge.Core;

/// <summary>
/// Persists the recipe-owned generated-asset lighting value into Asset Forge's typed cosmetic
/// resource after the transactional exporter has produced the package. This deliberately edits
/// only the exact generated cosmetic definition for the supplied stable feature ID.
/// </summary>
public static class GeneratedCosmeticLightingPersistence
{
    public static string Apply(string repositoryRoot, AssetRecipe recipe)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        IReadOnlyList<string> errors = recipe.Validate();
        if (errors.Count > 0)
            throw new ArgumentException(string.Join("; ", errors), nameof(recipe));

        string root = Path.GetFullPath(repositoryRoot);
        string generatedRoot = Path.GetFullPath(Path.Combine(root, "data", "cosmetics", "generated"));
        string path = Path.GetFullPath(Path.Combine(generatedRoot, recipe.FeatureId + ".tres"));
        string requiredPrefix = generatedRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!path.StartsWith(requiredPrefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Generated lighting metadata path escaped the Asset Forge-owned cosmetic directory.");
        if (!File.Exists(path))
            throw new FileNotFoundException("Generated cosmetic definition was not produced by Asset Forge.", path);

        string marker = "LightingLevel = " + recipe.LightingLevel.ToString("0.###", CultureInfo.InvariantCulture);
        string normalized = File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal);
        string[] lines = normalized.Split('\n');
        bool replaced = false;
        for (int i = 0; i < lines.Length; i++)
        {
            if (!lines[i].StartsWith("LightingLevel = ", StringComparison.Ordinal)) continue;
            lines[i] = marker;
            replaced = true;
            break;
        }

        if (!replaced)
        {
            int insert = Array.FindIndex(lines, static line => line.StartsWith("GeneratorVersion = ", StringComparison.Ordinal));
            if (insert < 0)
                throw new FormatException("Generated cosmetic definition has no GeneratorVersion marker.");
            var updated = new List<string>(lines.Length + 1);
            updated.AddRange(lines[..insert]);
            updated.Add(marker);
            updated.AddRange(lines[insert..]);
            lines = updated.ToArray();
        }

        File.WriteAllText(path, string.Join("\n", lines));
        return path;
    }

    public static string ExpectedMarker(AssetRecipe recipe) =>
        "LightingLevel = " + recipe.LightingLevel.ToString("0.###", CultureInfo.InvariantCulture);
}
