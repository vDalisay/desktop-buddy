using System.Text;

namespace DesktopBuddy.AssetForge.Core;

/// <summary>
/// Transactional repository exporter for generated Buddy part replacements. It writes the same
/// trusted resource/catalogue schema as Glasses, with category-correct authoring roots and slots.
/// </summary>
public static class RepositoryBuddyReplacementExporter
{
    public static ExportResult Export(
        string repositoryRoot,
        ReadOnlySpan<byte> sourcePng,
        GeneratedAsset generated,
        ReadOnlySpan<byte> thumbnailPng)
    {
        AssetRecipe recipe = generated.Recipe;
        if (recipe.Category is not (AssetCategory.TorsoShape or AssetCategory.FootShape))
            throw new ArgumentException("Replacement exporter accepts TorsoShape or FootShape recipes only.", nameof(generated));

        string root = RepositoryAssetVerifier.ValidateRoot(repositoryRoot);
        RgbaImage source = PngCodec.DecodeRgba8(sourcePng);
        if (source.Width != AssetForgeGenerator.SourceSize || source.Height != AssetForgeGenerator.SourceSize)
            throw new FormatException("Source PNG is not the canonical 1024x1024 image.");
        _ = PngCodec.DecodeRgba8(thumbnailPng);
        GlbWriter.ValidateSingleMesh(generated.GlbBytes);

        string slug = recipe.FeatureId[(recipe.FeatureId.IndexOf('.') + 1)..];
        string categoryFolder = recipe.Category == AssetCategory.TorsoShape ? "torso" : "feet";
        string authoringRelative = $"authoring/asset-forge/{categoryFolder}/{slug}";
        string assetRelative = $"assets/generated/cosmetics/{recipe.FeatureId}";
        string meshFileName = AssetFileNaming.MeshFileName(recipe);
        string meshRelative = $"{assetRelative}/{meshFileName}";
        string cosmeticRelative = $"data/cosmetics/generated/{recipe.FeatureId}.tres";
        string saleFile = recipe.ContentId.Replace('.', '_') + ".tres";
        string saleRelative = $"data/catalogue/generated/{saleFile}";
        string cosmeticCatalogueRelative = "data/cosmetics/generated/catalogue.tres";
        string saleCatalogueRelative = "data/catalogue/generated_cosmetics.tres";

        string stageRoot = Path.Combine(root, ".asset-forge-staging", $"{recipe.FeatureId.Replace('.', '_')}_{Environment.ProcessId}");
        if (Directory.Exists(stageRoot)) Directory.Delete(stageRoot, recursive: true);
        Directory.CreateDirectory(stageRoot);
        string backupRoot = Path.Combine(stageRoot, "backup");
        Directory.CreateDirectory(backupRoot);

        var staged = new Dictionary<string, string>(StringComparer.Ordinal);
        void StageBytes(string relative, ReadOnlySpan<byte> bytes)
        {
            EnsureOwned(relative, categoryFolder);
            string stagedPath = Path.Combine(stageRoot, "files", Native(relative));
            Directory.CreateDirectory(Path.GetDirectoryName(stagedPath)!);
            File.WriteAllBytes(stagedPath, bytes.ToArray());
            staged.Add(relative, stagedPath);
        }
        void StageText(string relative, string text) =>
            StageBytes(relative, Encoding.UTF8.GetBytes(text.Replace("\r\n", "\n", StringComparison.Ordinal)));

        try
        {
            StageBytes($"{authoringRelative}/source.png", sourcePng);
            StageText($"{authoringRelative}/recipe.json", RecipeCodec.WriteCanonical(recipe));
            StageBytes(meshRelative, generated.GlbBytes);
            StageBytes($"{assetRelative}/albedo.png", generated.AlbedoPng);
            StageBytes($"{assetRelative}/thumbnail.png", thumbnailPng);
            StageText(cosmeticRelative, CosmeticResource(recipe, generated, meshFileName));
            StageText(saleRelative, SaleResource(recipe, assetRelative));

            string[] cosmetics = ExistingDefinitions(root, "data/cosmetics/generated", "catalogue.tres")
                .Append(cosmeticRelative).Distinct(StringComparer.Ordinal).OrderBy(static p => p, StringComparer.Ordinal).ToArray();
            string[] sales = ExistingDefinitions(root, "data/catalogue/generated", null)
                .Append(saleRelative).Distinct(StringComparer.Ordinal).OrderBy(static p => p, StringComparer.Ordinal).ToArray();
            StageText(cosmeticCatalogueRelative, AggregateCosmetics(cosmetics));
            StageText(saleCatalogueRelative, AggregateSales(sales));

            GlbWriter.ValidateSingleMesh(File.ReadAllBytes(staged[meshRelative]));
            _ = PngCodec.DecodeRgba8(File.ReadAllBytes(staged[$"{assetRelative}/albedo.png"]));
            _ = PngCodec.DecodeRgba8(File.ReadAllBytes(staged[$"{assetRelative}/thumbnail.png"]));

            Commit(root, backupRoot, staged, categoryFolder);
            AssetFileNaming.RemoveStaleMeshes(Path.Combine(root, Native(assetRelative)), meshFileName);
            return new ExportResult(
                recipe.FeatureId,
                Path.Combine(root, Native(assetRelative)),
                Path.Combine(root, Native(cosmeticRelative)),
                Path.Combine(root, Native(saleRelative)),
                Path.Combine(root, Native(authoringRelative)));
        }
        finally
        {
            if (Directory.Exists(stageRoot)) Directory.Delete(stageRoot, recursive: true);
        }
    }

    private static string CosmeticResource(AssetRecipe recipe, GeneratedAsset generated, string meshFileName)
    {
        string asset = $"res://assets/generated/cosmetics/{recipe.FeatureId}";
        var text = new StringBuilder();
        text.AppendLine("[gd_resource type=\"Resource\" script_class=\"GeneratedBuddyCosmeticResource\" load_steps=5 format=3]");
        text.AppendLine();
        text.AppendLine("[ext_resource type=\"Script\" path=\"res://src/CharacterEditor/BuddyStudio/GeneratedBuddyCosmeticResource.cs\" id=\"1\"]");
        text.AppendLine($"[ext_resource type=\"PackedScene\" path=\"{asset}/{meshFileName}\" id=\"2\"]");
        text.AppendLine($"[ext_resource type=\"Texture2D\" path=\"{asset}/albedo.png\" id=\"3\"]");
        text.AppendLine($"[ext_resource type=\"Texture2D\" path=\"{asset}/thumbnail.png\" id=\"4\"]");
        text.AppendLine();
        text.AppendLine("[resource]");
        text.AppendLine("script = ExtResource(\"1\")");
        text.AppendLine($"FeatureId = \"{Escape(recipe.FeatureId)}\"");
        text.AppendLine($"ContentId = \"{Escape(recipe.ContentId)}\"");
        text.AppendLine($"DisplayName = \"{Escape(recipe.DisplayName)}\"");
        text.AppendLine($"Slot = {GeneratedCosmeticCategoryPersistence.SlotNumber(recipe)}");
        text.AppendLine($"SortOrder = {recipe.SortOrder}");
        text.AppendLine("MeshScene = ExtResource(\"2\")");
        text.AppendLine("AlbedoTexture = ExtResource(\"3\")");
        text.AppendLine("Thumbnail = ExtResource(\"4\")");
        text.AppendLine($"LightingLevel = {recipe.LightingLevel.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)}");
        text.AppendLine($"GeneratorVersion = {recipe.GeneratorVersion}");
        text.AppendLine($"CanonicalAssetHash = \"{generated.CanonicalAssetHash}\"");
        return text.ToString();
    }

    private static string SaleResource(AssetRecipe recipe, string assetRelative)
    {
        var text = new StringBuilder();
        text.AppendLine("[gd_resource type=\"Resource\" script_class=\"ToolDefinition\" load_steps=3 format=3]");
        text.AppendLine();
        text.AppendLine("[ext_resource type=\"Script\" path=\"res://src/Content/ToolDefinition.cs\" id=\"1\"]");
        text.AppendLine($"[ext_resource type=\"Texture2D\" path=\"res://{assetRelative}/thumbnail.png\" id=\"2\"]");
        text.AppendLine();
        text.AppendLine("[resource]");
        text.AppendLine("script = ExtResource(\"1\")");
        text.AppendLine($"ContentId = \"{Escape(recipe.ContentId)}\"");
        text.AppendLine("Kind = 4");
        text.AppendLine($"PriceCredits = {recipe.PriceCredits}");
        text.AppendLine($"ProgressionOrder = {10000 + recipe.SortOrder}");
        text.AppendLine("Visible = true");
        text.AppendLine($"NameKey = \"{Escape(recipe.DisplayName)}\"");
        text.AppendLine("DescriptionKey = \"Generated with Desktop Buddy Asset Forge.\"");
        text.AppendLine("Icon = ExtResource(\"2\")");
        text.AppendLine("RequiresLaunchScene = false");
        text.AppendLine("RequiresIcon = true");
        return text.ToString();
    }

    private static string AggregateCosmetics(IReadOnlyList<string> definitions)
    {
        var text = new StringBuilder();
        text.AppendLine($"[gd_resource type=\"Resource\" script_class=\"GeneratedBuddyCosmeticCatalogueResource\" load_steps={definitions.Count + 2} format=3]");
        text.AppendLine();
        text.AppendLine("[ext_resource type=\"Script\" path=\"res://src/CharacterEditor/BuddyStudio/GeneratedBuddyCosmeticCatalogueResource.cs\" id=\"1\"]");
        for (int i = 0; i < definitions.Count; i++) text.AppendLine($"[ext_resource type=\"Resource\" path=\"res://{definitions[i]}\" id=\"{i + 2}\"]");
        text.AppendLine();
        text.AppendLine("[resource]");
        text.AppendLine("script = ExtResource(\"1\")");
        text.Append("Entries = Array[Resource]([");
        for (int i = 0; i < definitions.Count; i++) { if (i > 0) text.Append(", "); text.Append($"ExtResource(\"{i + 2}\")"); }
        text.AppendLine("])");
        return text.ToString();
    }

    private static string AggregateSales(IReadOnlyList<string> definitions)
    {
        var text = new StringBuilder();
        text.AppendLine($"[gd_resource type=\"Resource\" script_class=\"GeneratedCatalogueDefinition\" load_steps={definitions.Count + 2} format=3]");
        text.AppendLine();
        text.AppendLine("[ext_resource type=\"Script\" path=\"res://src/Content/GeneratedCatalogueDefinition.cs\" id=\"1\"]");
        for (int i = 0; i < definitions.Count; i++) text.AppendLine($"[ext_resource type=\"Resource\" path=\"res://{definitions[i]}\" id=\"{i + 2}\"]");
        text.AppendLine();
        text.AppendLine("[resource]");
        text.AppendLine("script = ExtResource(\"1\")");
        text.Append("Entries = Array[Resource]([");
        for (int i = 0; i < definitions.Count; i++) { if (i > 0) text.Append(", "); text.Append($"ExtResource(\"{i + 2}\")"); }
        text.AppendLine("])");
        return text.ToString();
    }

    private static IEnumerable<string> ExistingDefinitions(string root, string relativeDirectory, string? excludeName)
    {
        string directory = Path.Combine(root, Native(relativeDirectory));
        if (!Directory.Exists(directory)) yield break;
        foreach (string path in Directory.GetFiles(directory, "*.tres", SearchOption.TopDirectoryOnly).OrderBy(static path => path, StringComparer.Ordinal))
        {
            if (excludeName is not null && string.Equals(Path.GetFileName(path), excludeName, StringComparison.Ordinal)) continue;
            yield return Path.GetRelativePath(root, path).Replace('\\', '/');
        }
    }

    private static void Commit(string root, string backupRoot, IReadOnlyDictionary<string, string> staged, string categoryFolder)
    {
        var backups = new Dictionary<string, string?>(StringComparer.Ordinal);
        var written = new List<string>();
        try
        {
            foreach ((string relative, string stagedPath) in staged.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
            {
                EnsureOwned(relative, categoryFolder);
                string destination = Path.Combine(root, Native(relative));
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                if (File.Exists(destination))
                {
                    string backup = Path.Combine(backupRoot, Native(relative));
                    Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
                    File.Copy(destination, backup, overwrite: true);
                    backups[relative] = backup;
                }
                else backups[relative] = null;
                File.Copy(stagedPath, destination, overwrite: true);
                written.Add(relative);
            }
        }
        catch
        {
            for (int index = written.Count - 1; index >= 0; index--)
            {
                string relative = written[index];
                string destination = Path.Combine(root, Native(relative));
                if (backups[relative] is string backup) File.Copy(backup, destination, overwrite: true);
                else if (File.Exists(destination)) File.Delete(destination);
            }
            throw;
        }
    }

    private static void EnsureOwned(string relative, string categoryFolder)
    {
        string normalized = relative.Replace('\\', '/').TrimStart('/');
        string[] roots = [$"authoring/asset-forge/{categoryFolder}/", "assets/generated/cosmetics/", "data/cosmetics/generated/", "data/catalogue/generated/"];
        bool owned = roots.Any(prefix => normalized.StartsWith(prefix, StringComparison.Ordinal)) || normalized == "data/catalogue/generated_cosmetics.tres";
        if (!owned) throw new InvalidOperationException($"Asset Forge cannot write outside its trusted replacement roots: {relative}");
        if (normalized.Contains("../", StringComparison.Ordinal) || normalized.Contains("/..", StringComparison.Ordinal)) throw new InvalidOperationException("Path traversal is forbidden.");
    }

    private static string Escape(string value) => value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
    private static string Native(string relative) => relative.Replace('/', Path.DirectorySeparatorChar);
}
