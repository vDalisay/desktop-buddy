using System.Globalization;
using System.Numerics;
using System.Text;

namespace DesktopBuddy.AssetForge.Core;

/// <summary>
/// Transactional exporter for trusted generated Environment definitions. It never touches the
/// hand-authored launch catalogue and writes only Asset Forge-owned Environment roots.
/// </summary>
public static class RepositoryEnvironmentExporter
{
    public static ExportResult Export(
        string repositoryRoot,
        ReadOnlySpan<byte> sourcePng,
        GeneratedAsset generated,
        ReadOnlySpan<byte> thumbnailPng)
    {
        AssetRecipe recipe = generated.Recipe;
        if (recipe.AssetFamily != AssetFamily.Environment || !IsSupportedCategory(recipe.Category))
            throw new ArgumentException("Environment exporter currently accepts Lamp and Sofa recipes only.", nameof(generated));

        string root = RepositoryAssetVerifier.ValidateRoot(repositoryRoot);
        RgbaImage source = PngCodec.DecodeRgba8(sourcePng);
        if (source.Width != AssetForgeGenerator.SourceSize || source.Height != AssetForgeGenerator.SourceSize)
            throw new FormatException("Source PNG is not the canonical 1024x1024 image.");
        _ = PngCodec.DecodeRgba8(thumbnailPng);
        GlbWriter.ValidateSingleMesh(generated.GlbBytes);

        string prefix = AssetIdPrefix(recipe.Category);
        if (!recipe.AssetId.StartsWith(prefix, StringComparison.Ordinal))
            throw new InvalidOperationException($"Environment AssetId '{recipe.AssetId}' does not match the {recipe.Category} namespace.");
        string slug = recipe.AssetId[prefix.Length..];
        string authoringRelative = $"authoring/asset-forge/{AuthoringFolder(recipe.Category)}/{slug}";
        string assetRelative = $"assets/generated/environment/{recipe.AssetId}";
        string definitionRelative = $"data/environment/generated/{recipe.AssetId}.tres";
        string aggregateRelative = "data/environment/generated_decorations.tres";

        string stageRoot = Path.Combine(root, ".asset-forge-staging", $"env_{recipe.AssetId.Replace('.', '_')}_{Environment.ProcessId}");
        if (Directory.Exists(stageRoot)) Directory.Delete(stageRoot, recursive: true);
        Directory.CreateDirectory(stageRoot);
        string backupRoot = Path.Combine(stageRoot, "backup");
        Directory.CreateDirectory(backupRoot);
        var staged = new Dictionary<string, string>(StringComparer.Ordinal);

        void StageBytes(string relative, ReadOnlySpan<byte> bytes)
        {
            EnsureOwned(relative);
            string path = Path.Combine(stageRoot, "files", Native(relative));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, bytes.ToArray());
            staged.Add(relative, path);
        }
        void StageText(string relative, string text) =>
            StageBytes(relative, Encoding.UTF8.GetBytes(text.Replace("\r\n", "\n", StringComparison.Ordinal)));

        try
        {
            StageBytes($"{authoringRelative}/source.png", sourcePng);
            StageText($"{authoringRelative}/recipe.json", RecipeCodec.WriteCanonical(recipe));
            StageBytes($"{assetRelative}/mesh.glb", generated.GlbBytes);
            StageBytes($"{assetRelative}/albedo.png", generated.AlbedoPng);
            StageBytes($"{assetRelative}/thumbnail.png", thumbnailPng);
            StageText(definitionRelative, DecorationResource(recipe, generated, assetRelative));

            string[] definitions = ExistingDefinitions(root)
                .Append(definitionRelative)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(static path => path, StringComparer.Ordinal)
                .ToArray();
            StageText(aggregateRelative, Aggregate(definitions));

            GlbWriter.ValidateSingleMesh(File.ReadAllBytes(staged[$"{assetRelative}/mesh.glb"]));
            _ = PngCodec.DecodeRgba8(File.ReadAllBytes(staged[$"{assetRelative}/albedo.png"]));
            _ = PngCodec.DecodeRgba8(File.ReadAllBytes(staged[$"{assetRelative}/thumbnail.png"]));
            Commit(root, backupRoot, staged);

            return new ExportResult(
                recipe.AssetId,
                Path.Combine(root, Native(assetRelative)),
                Path.Combine(root, Native(definitionRelative)),
                Path.Combine(root, Native(aggregateRelative)),
                Path.Combine(root, Native(authoringRelative)));
        }
        finally
        {
            if (Directory.Exists(stageRoot)) Directory.Delete(stageRoot, recursive: true);
        }
    }

    private static string DecorationResource(AssetRecipe recipe, GeneratedAsset generated, string assetRelative)
    {
        EnvironmentGeneratedBounds bounds = EnvironmentGeneratedBounds.Analyze(generated.Mesh);
        bool hasLightProfile = recipe.Category == AssetCategory.Lamp && recipe.Light.Enabled;
        string number(double value) => value.ToString("0.######", CultureInfo.InvariantCulture);
        var text = new StringBuilder();
        text.AppendLine($"[gd_resource type=\"Resource\" script_class=\"EnvironmentDecorationResource\" load_steps={(hasLightProfile ? 7 : 5)} format=3]");
        text.AppendLine();
        text.AppendLine("[ext_resource type=\"Script\" path=\"res://src/Environment/EnvironmentDecorationResource.cs\" id=\"1\"]");
        text.AppendLine($"[ext_resource type=\"PackedScene\" path=\"res://{assetRelative}/mesh.glb\" id=\"2\"]");
        text.AppendLine($"[ext_resource type=\"Texture2D\" path=\"res://{assetRelative}/albedo.png\" id=\"3\"]");
        text.AppendLine($"[ext_resource type=\"Texture2D\" path=\"res://{assetRelative}/thumbnail.png\" id=\"4\"]");
        if (hasLightProfile)
            text.AppendLine("[ext_resource type=\"Script\" path=\"res://src/Environment/DecorationLightProfileResource.cs\" id=\"5\"]");

        if (hasLightProfile)
        {
            text.AppendLine();
            text.AppendLine("[sub_resource type=\"Resource\" id=\"LightProfile\"]");
            text.AppendLine("script = ExtResource(\"5\")");
            text.AppendLine($"Enabled = {Bool(recipe.Light.Enabled)}");
            text.AppendLine($"EmissionStrength = {number(recipe.Light.EmissionStrength)}");
            text.AppendLine($"LightEnabled = {Bool(recipe.Light.LightEnabled)}");
            text.AppendLine($"Brightness = {number(recipe.Light.Brightness)}");
            text.AppendLine($"Range = {number(recipe.Light.Range)}");
            text.AppendLine($"Color = Color({number(recipe.Light.Red / 255.0)}, {number(recipe.Light.Green / 255.0)}, {number(recipe.Light.Blue / 255.0)}, 1)");
            text.AppendLine($"EmitterPosition = Vector2({number(recipe.Light.EmitterX)}, {number(recipe.Light.EmitterY)})");
            if (EnvironmentTemplateMapping.UsesLiteralTemplateSpace(recipe))
            {
                Vector2 local = EnvironmentTemplateMapping.SourcePixelToWorld(
                    recipe.Light.EmitterX * EnvironmentTemplateSpace.CanvasSize,
                    recipe.Light.EmitterY * EnvironmentTemplateSpace.CanvasSize,
                    recipe);
                text.AppendLine("UsesLocalEmitterPosition = true");
                text.AppendLine($"LocalEmitterPosition = Vector2({number(local.X)}, {number(local.Y)})");
            }
        }

        text.AppendLine();
        text.AppendLine("[resource]");
        text.AppendLine("script = ExtResource(\"1\")");
        text.AppendLine($"DefinitionId = \"{Escape(recipe.AssetId)}\"");
        text.AppendLine($"DisplayNameKey = \"{Escape(recipe.DisplayName)}\"");
        text.AppendLine($"Category = {DecorationCategory(recipe.Category)}");
        text.AppendLine($"PriceCredits = {recipe.PriceCredits}");
        text.AppendLine("AnchorKind = 0");
        text.AppendLine($"AllowsRotation = {Bool(recipe.Environment.AllowsRotation)}");
        text.AppendLine($"RotationStepDegrees = {recipe.Environment.RotationStepDegrees}");
        text.AppendLine($"RenderBand = {RenderBand(recipe.Environment.RenderMode)}");
        text.AppendLine("Visible = true");
        text.AppendLine("VisualSource = 1");
        text.AppendLine("VisualKind = 0");
        text.AppendLine($"VisualSize = Vector2({number(bounds.Width)}, {number(bounds.Height)})");
        text.AppendLine("GeneratedMesh = ExtResource(\"2\")");
        text.AppendLine("GeneratedAlbedo = ExtResource(\"3\")");
        text.AppendLine("Thumbnail = ExtResource(\"4\")");
        text.AppendLine("DefaultScale = 1.0");
        text.AppendLine($"Pivot = Vector2({number(recipe.Environment.PivotX)}, {number(recipe.Environment.PivotY)})");
        text.AppendLine($"GeneratorVersion = {recipe.GeneratorVersion}");
        text.AppendLine($"CanonicalAssetHash = \"{generated.CanonicalAssetHash}\"");
        if (hasLightProfile)
            text.AppendLine("LightProfile = SubResource(\"LightProfile\")");
        return text.ToString();
    }

    private static string Aggregate(IReadOnlyList<string> definitions)
    {
        var text = new StringBuilder();
        text.AppendLine($"[gd_resource type=\"Resource\" script_class=\"EnvironmentDecorationCatalogueResource\" load_steps={definitions.Count + 2} format=3]");
        text.AppendLine();
        text.AppendLine("[ext_resource type=\"Script\" path=\"res://src/Environment/EnvironmentDecorationCatalogueResource.cs\" id=\"1\"]");
        for (int i = 0; i < definitions.Count; i++)
            text.AppendLine($"[ext_resource type=\"Resource\" path=\"res://{definitions[i]}\" id=\"{i + 2}\"]");
        text.AppendLine();
        text.AppendLine("[resource]");
        text.AppendLine("script = ExtResource(\"1\")");
        text.Append("Entries = Array[Resource]([");
        for (int i = 0; i < definitions.Count; i++)
        {
            if (i > 0) text.Append(", ");
            text.Append($"ExtResource(\"{i + 2}\")");
        }
        text.AppendLine("])");
        return text.ToString();
    }

    private static IEnumerable<string> ExistingDefinitions(string root)
    {
        string directory = Path.Combine(root, Native("data/environment/generated"));
        if (!Directory.Exists(directory)) yield break;
        foreach (string path in Directory.GetFiles(directory, "*.tres", SearchOption.TopDirectoryOnly)
                     .OrderBy(static path => path, StringComparer.Ordinal))
            yield return Path.GetRelativePath(root, path).Replace('\\', '/');
    }

    private static void Commit(string root, string backupRoot, IReadOnlyDictionary<string, string> staged)
    {
        var backups = new Dictionary<string, string?>(StringComparer.Ordinal);
        var written = new List<string>();
        try
        {
            foreach ((string relative, string stagedPath) in staged.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
            {
                EnsureOwned(relative);
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

    private static void EnsureOwned(string relative)
    {
        string normalized = relative.Replace('\\', '/').TrimStart('/');
        bool owned = normalized.StartsWith("authoring/asset-forge/lamps/", StringComparison.Ordinal) ||
                     normalized.StartsWith("authoring/asset-forge/sofas/", StringComparison.Ordinal) ||
                     normalized.StartsWith("assets/generated/environment/", StringComparison.Ordinal) ||
                     normalized.StartsWith("data/environment/generated/", StringComparison.Ordinal) ||
                     normalized == "data/environment/generated_decorations.tres";
        if (!owned) throw new InvalidOperationException($"Asset Forge cannot write outside its trusted Environment roots: {relative}");
        if (normalized.Contains("../", StringComparison.Ordinal) || normalized.Contains("/..", StringComparison.Ordinal))
            throw new InvalidOperationException("Path traversal is forbidden.");
    }

    private static bool IsSupportedCategory(AssetCategory category) =>
        category is AssetCategory.Lamp or AssetCategory.Sofa;

    private static string AssetIdPrefix(AssetCategory category) => category switch
    {
        AssetCategory.Lamp => "decoration.lamp.",
        AssetCategory.Sofa => "decoration.sofa.",
        _ => throw new ArgumentOutOfRangeException(nameof(category), category, null),
    };

    private static string AuthoringFolder(AssetCategory category) => category switch
    {
        AssetCategory.Lamp => "lamps",
        AssetCategory.Sofa => "sofas",
        _ => throw new ArgumentOutOfRangeException(nameof(category), category, null),
    };

    private static int DecorationCategory(AssetCategory category) => category switch
    {
        AssetCategory.Lamp => 0,
        AssetCategory.Sofa => 1,
        _ => throw new ArgumentOutOfRangeException(nameof(category), category, null),
    };

    private static int RenderBand(EnvironmentRenderMode mode) => mode switch
    {
        EnvironmentRenderMode.BehindBuddyFloor => 3,
        EnvironmentRenderMode.FrontDecoration => 4,
        EnvironmentRenderMode.WallDecoration => 2,
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null),
    };

    private static string Bool(bool value) => value ? "true" : "false";
    private static string Escape(string value) => value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
    private static string Native(string relative) => relative.Replace('/', Path.DirectorySeparatorChar);
}
