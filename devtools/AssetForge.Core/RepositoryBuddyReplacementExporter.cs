using System.Text;

namespace DesktopBuddy.AssetForge.Core;

/// <summary>
/// Transactional repository exporter for generated Buddy part replacements. The accepted Glasses
/// exporter remains intact; this companion writes the same trusted package shape but uses category-
/// correct authoring roots and slots for torso/foot replacements.
/// </summary>
public static class RepositoryBuddyReplacementExporter
{
    public static ExportResult Export(string repositoryRoot, ReadOnlySpan<byte> sourcePng, GeneratedAsset generated, ReadOnlySpan<byte> thumbnailPng)
    {
        ArgumentNullException.ThrowIfNull(generated);
        AssetRecipe recipe = generated.Recipe;
        if (recipe.Category is not (AssetCategory.TorsoShape or AssetCategory.FootShape))
            throw new ArgumentException("Replacement exporter accepts TorsoShape or FootShape recipes only.", nameof(generated));

        string root = RepositoryAssetVerifier.ValidateRoot(repositoryRoot);
        RgbaImage source = PngCodec.DecodeRgba8(sourcePng);
        if (source.Width != AssetForgeGenerator.SourceSize || source.Height != AssetForgeGenerator.SourceSize)
            throw new FormatException("Source PNG is not the canonical 1024x1024 image.");
        RgbaImage thumbnail = PngCodec.DecodeRgba8(thumbnailPng);
        if (thumbnail.Width <= 0 || thumbnail.Height <= 0)
            throw new FormatException("Thumbnail PNG is empty.");
        GlbWriter.ValidateSingleMesh(generated.GlbBytes);
        EnsureIdentityUnique(root, recipe);

        string slug = recipe.FeatureId[(recipe.FeatureId.IndexOf('.') + 1)..];
        string categoryFolder = recipe.Category == AssetCategory.TorsoShape ? "torso" : "feet";
        string authoring = Path.Combine(root, "authoring", "asset-forge", categoryFolder, slug);
        string asset = Path.Combine(root, "assets", "generated", "cosmetics", recipe.FeatureId);
        string definition = Path.Combine(root, "data", "cosmetics", "generated", recipe.FeatureId + ".tres");
        string sale = Path.Combine(root, "data", "catalogue", "generated", recipe.ContentId + ".tres");
        string featureAggregate = Path.Combine(root, "data", "cosmetics", "generated", "catalogue.tres");
        string saleAggregate = Path.Combine(root, "data", "catalogue", "generated_cosmetics.tres");

        var files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase)
        {
            [Path.Combine(authoring, "source.png")] = sourcePng.ToArray(),
            [Path.Combine(authoring, "recipe.json")] = Encoding.UTF8.GetBytes(RecipeCodec.WriteCanonical(recipe)),
            [Path.Combine(asset, "mesh.glb")] = generated.GlbBytes,
            [Path.Combine(asset, "albedo.png")] = generated.AlbedoPng,
            [Path.Combine(asset, "thumbnail.png")] = thumbnailPng.ToArray(),
            [definition] = Encoding.UTF8.GetBytes(CosmeticResource(recipe, generated)),
            [sale] = Encoding.UTF8.GetBytes(SaleResource(recipe)),
            [featureAggregate] = Encoding.UTF8.GetBytes(FeatureAggregate(root, recipe.FeatureId)),
            [saleAggregate] = Encoding.UTF8.GetBytes(SaleAggregate(root, recipe.ContentId)),
        };

        foreach (string path in files.Keys) ValidateOwnedPath(root, path, recipe.FeatureId, recipe.ContentId, categoryFolder);
        CommitTransaction(root, files);
        return new ExportResult(authoring, asset, definition, sale, featureAggregate, saleAggregate);
    }

    private static void EnsureIdentityUnique(string root, AssetRecipe recipe)
    {
        string authoringRoot = Path.Combine(root, "authoring", "asset-forge");
        if (!Directory.Exists(authoringRoot)) return;
        foreach (string existingRecipePath in Directory.EnumerateFiles(authoringRoot, "recipe.json", SearchOption.AllDirectories))
        {
            AssetRecipe existing;
            try { existing = RecipeCodec.Read(File.ReadAllText(existingRecipePath)); }
            catch { continue; }
            string thisPath = Path.GetFullPath(existingRecipePath);
            if (existing.FeatureId == recipe.FeatureId || existing.ContentId == recipe.ContentId)
            {
                string expectedCategory = recipe.Category == AssetCategory.TorsoShape ? "torso" : "feet";
                string slug = recipe.FeatureId[(recipe.FeatureId.IndexOf('.') + 1)..];
                string expected = Path.GetFullPath(Path.Combine(authoringRoot, expectedCategory, slug, "recipe.json"));
                if (!string.Equals(thisPath, expected, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"Stable ID collision with existing Asset Forge recipe: {existingRecipePath}");
            }
        }
    }

    private static string CosmeticResource(AssetRecipe recipe, GeneratedAsset generated)
    {
        string mesh = $"res://assets/generated/cosmetics/{recipe.FeatureId}/mesh.glb";
        string albedo = $"res://assets/generated/cosmetics/{recipe.FeatureId}/albedo.png";
        string thumbnail = $"res://assets/generated/cosmetics/{recipe.FeatureId}/thumbnail.png";
        return $"""[gd_resource type="Resource" script_class="GeneratedBuddyCosmeticResource" load_steps=5 format=3]

[ext_resource type="Script" path="res://src/CharacterEditor/BuddyStudio/GeneratedBuddyCosmeticResource.cs" id="1_script"]
[ext_resource type="PackedScene" path="{mesh}" id="2_mesh"]
[ext_resource type="Texture2D" path="{albedo}" id="3_albedo"]
[ext_resource type="Texture2D" path="{thumbnail}" id="4_thumb"]

[resource]
script = ExtResource("1_script")
FeatureId = "{recipe.FeatureId}"
ContentId = "{recipe.ContentId}"
DisplayName = "{Escape(recipe.DisplayName)}"
Slot = {GeneratedCosmeticCategoryPersistence.SlotNumber(recipe)}
SortOrder = {recipe.SortOrder}
MeshScene = ExtResource("2_mesh")
AlbedoTexture = ExtResource("3_albedo")
Thumbnail = ExtResource("4_thumb")
LightingLevel = {recipe.LightingLevel.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)}
GeneratorVersion = {recipe.GeneratorVersion}
CanonicalAssetHash = "{generated.CanonicalAssetHash}"
""" + "\n";
    }

    private static string SaleResource(AssetRecipe recipe) => $"""[gd_resource type="Resource" script_class="CatalogueEntryResource" load_steps=2 format=3]

[ext_resource type="Script" path="res://src/Economy/CatalogueEntryResource.cs" id="1_script"]

[resource]
script = ExtResource("1_script")
ContentId = "{recipe.ContentId}"
Kind = 1
PriceMilliCredits = {checked(recipe.PriceCredits * 1000)}
Visible = true
NameKey = "{Escape(recipe.DisplayName)}"
DescriptionKey = "Generated by Desktop Buddy Asset Forge"
""" + "\n";

    private static string FeatureAggregate(string root, string includeFeatureId)
    {
        string dir = Path.Combine(root, "data", "cosmetics", "generated");
        var ids = new HashSet<string>(StringComparer.Ordinal) { includeFeatureId };
        if (Directory.Exists(dir))
            foreach (string file in Directory.EnumerateFiles(dir, "*.tres", SearchOption.TopDirectoryOnly))
            {
                if (Path.GetFileName(file).Equals("catalogue.tres", StringComparison.OrdinalIgnoreCase)) continue;
                string? id = ReadQuotedValue(file, "FeatureId");
                if (!string.IsNullOrWhiteSpace(id)) ids.Add(id);
            }
        string[] ordered = ids.OrderBy(static id => id, StringComparer.Ordinal).ToArray();
        var sb = new StringBuilder();
        sb.AppendLine($"[gd_resource type=\"Resource\" script_class=\"GeneratedBuddyCosmeticCatalogueResource\" load_steps={ordered.Length + 2} format=3]");
        sb.AppendLine();
        sb.AppendLine("[ext_resource type=\"Script\" path=\"res://src/CharacterEditor/BuddyStudio/GeneratedBuddyCosmeticCatalogueResource.cs\" id=\"1_script\"]");
        for (int i = 0; i < ordered.Length; i++)
            sb.AppendLine($"[ext_resource type=\"Resource\" path=\"res://data/cosmetics/generated/{ordered[i]}.tres\" id=\"{i + 2}_item\"]");
        sb.AppendLine();
        sb.AppendLine("[resource]");
        sb.Append("script = ExtResource(\"1_script\")\nEntries = Array[Resource]([");
        sb.Append(string.Join(", ", ordered.Select((_, i) => $"ExtResource(\"{i + 2}_item\")")));
        sb.AppendLine("])");
        return sb.ToString();
    }

    private static string SaleAggregate(string root, string includeContentId)
    {
        string dir = Path.Combine(root, "data", "catalogue", "generated");
        var ids = new HashSet<string>(StringComparer.Ordinal) { includeContentId };
        if (Directory.Exists(dir))
            foreach (string file in Directory.EnumerateFiles(dir, "*.tres", SearchOption.TopDirectoryOnly))
            {
                string? id = ReadQuotedValue(file, "ContentId");
                if (!string.IsNullOrWhiteSpace(id)) ids.Add(id);
            }
        string[] ordered = ids.OrderBy(static id => id, StringComparer.Ordinal).ToArray();
        var sb = new StringBuilder();
        sb.AppendLine($"[gd_resource type=\"Resource\" script_class=\"CatalogueResource\" load_steps={ordered.Length + 2} format=3]");
        sb.AppendLine();
        sb.AppendLine("[ext_resource type=\"Script\" path=\"res://src/Economy/CatalogueResource.cs\" id=\"1_script\"]");
        for (int i = 0; i < ordered.Length; i++)
            sb.AppendLine($"[ext_resource type=\"Resource\" path=\"res://data/catalogue/generated/{ordered[i]}.tres\" id=\"{i + 2}_item\"]");
        sb.AppendLine();
        sb.AppendLine("[resource]");
        sb.Append("script = ExtResource(\"1_script\")\nEntries = Array[Resource]([");
        sb.Append(string.Join(", ", ordered.Select((_, i) => $"ExtResource(\"{i + 2}_item\")")));
        sb.AppendLine("])");
        return sb.ToString();
    }

    private static string? ReadQuotedValue(string path, string key)
    {
        string prefix = key + " = \"";
        foreach (string line in File.ReadLines(path))
            if (line.StartsWith(prefix, StringComparison.Ordinal) && line.EndsWith('"'))
                return line[prefix.Length..^1];
        return null;
    }

    private static void CommitTransaction(string root, IReadOnlyDictionary<string, byte[]> files)
    {
        string staging = Path.Combine(root, ".asset-forge-stage-" + Guid.NewGuid().ToString("N"));
        string backup = Path.Combine(root, ".asset-forge-backup-" + Guid.NewGuid().ToString("N"));
        var written = new List<(string Destination, string? Backup)>();
        try
        {
            Directory.CreateDirectory(staging);
            Directory.CreateDirectory(backup);
            int index = 0;
            foreach ((string destination, byte[] bytes) in files)
            {
                string staged = Path.Combine(staging, index++.ToString("D3"));
                File.WriteAllBytes(staged, bytes);
                if (destination.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) _ = PngCodec.DecodeRgba8(bytes);
                if (destination.EndsWith(".glb", StringComparison.OrdinalIgnoreCase)) GlbWriter.ValidateSingleMesh(bytes);
            }

            index = 0;
            foreach ((string destination, _) in files)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                string? previous = null;
                if (File.Exists(destination))
                {
                    previous = Path.Combine(backup, index.ToString("D3"));
                    File.Copy(destination, previous, overwrite: true);
                }
                string staged = Path.Combine(staging, index++.ToString("D3"));
                File.Move(staged, destination, overwrite: true);
                written.Add((destination, previous));
            }
        }
        catch
        {
            for (int i = written.Count - 1; i >= 0; i--)
            {
                (string destination, string? previous) = written[i];
                if (previous is not null && File.Exists(previous)) File.Copy(previous, destination, overwrite: true);
                else if (File.Exists(destination)) File.Delete(destination);
            }
            throw;
        }
        finally
        {
            if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
            if (Directory.Exists(backup)) Directory.Delete(backup, recursive: true);
        }
    }

    private static void ValidateOwnedPath(string root, string path, string featureId, string contentId, string categoryFolder)
    {
        string full = Path.GetFullPath(path);
        string[] allowed =
        [
            Path.Combine(root, "authoring", "asset-forge", categoryFolder) + Path.DirectorySeparatorChar,
            Path.Combine(root, "assets", "generated", "cosmetics", featureId) + Path.DirectorySeparatorChar,
            Path.Combine(root, "data", "cosmetics", "generated") + Path.DirectorySeparatorChar,
            Path.Combine(root, "data", "catalogue", "generated") + Path.DirectorySeparatorChar,
        ];
        bool generatedSaleAggregate = full.Equals(Path.GetFullPath(Path.Combine(root, "data", "catalogue", "generated_cosmetics.tres")), StringComparison.OrdinalIgnoreCase);
        if (!generatedSaleAggregate && !allowed.Any(prefix => full.StartsWith(Path.GetFullPath(prefix), StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"Asset Forge refused to write outside its owned generated roots: {path}");
        if (full.Contains("..", StringComparison.Ordinal)) throw new InvalidOperationException("Invalid output path.");
    }

    private static string Escape(string value) => value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
}
