using System.Globalization;
using System.Numerics;

namespace DesktopBuddy.AssetForge.Core;

public sealed record EnvironmentAssetVerificationResult(
    string AssetId,
    bool Passed,
    IReadOnlyList<string> Diagnostics);

public sealed record EnvironmentRepositoryVerificationResult(
    IReadOnlyList<EnvironmentAssetVerificationResult> Assets,
    IReadOnlyList<string> RepositoryDiagnostics)
{
    public int PassedCount => Assets.Count(static asset => asset.Passed);
    public bool Passed => Assets.All(static asset => asset.Passed) && RepositoryDiagnostics.Count == 0;
}

public sealed record EnvironmentRepositoryRegenerationResult(
    IReadOnlyList<string> RegeneratedAssetIds,
    EnvironmentRepositoryVerificationResult Verification);

public static class RepositoryEnvironmentVerifier
{
    public static EnvironmentRepositoryVerificationResult VerifyAll(string repositoryRoot)
    {
        string root = RepositoryAssetVerifier.ValidateRoot(repositoryRoot);
        string[] recipes = DiscoverRecipes(root);
        EnvironmentAssetVerificationResult[] assets = recipes.Select(path => VerifyRecipe(root, path)).ToArray();
        var diagnostics = new List<string>();
        VerifyNoOrphans(root, assets, diagnostics);
        return new EnvironmentRepositoryVerificationResult(assets, diagnostics);
    }

    public static EnvironmentAssetVerificationResult Verify(string repositoryRoot, string assetId)
    {
        string root = RepositoryAssetVerifier.ValidateRoot(repositoryRoot);
        return VerifyRecipe(root, FindRecipe(root, assetId));
    }

    internal static string[] DiscoverRecipes(string root) => RepositoryAssetVerifier.DiscoverAllRecipeFiles(root)
        .Where(static path =>
        {
            try { return RecipeCodec.Read(File.ReadAllText(path)).AssetFamily == AssetFamily.Environment; }
            catch { return false; }
        })
        .ToArray();

    internal static string FindRecipe(string root, string assetId)
    {
        if (string.IsNullOrWhiteSpace(assetId)) throw new ArgumentException("Environment AssetId is required.", nameof(assetId));
        foreach (string path in DiscoverRecipes(root))
        {
            AssetRecipe recipe = RecipeCodec.Read(File.ReadAllText(path));
            if (string.Equals(recipe.AssetId, assetId, StringComparison.Ordinal)) return path;
        }
        throw new FileNotFoundException($"No authored Asset Forge Environment recipe exists for {assetId}.");
    }

    private static EnvironmentAssetVerificationResult VerifyRecipe(string root, string recipePath)
    {
        string displayId = Path.GetFileName(Path.GetDirectoryName(recipePath) ?? string.Empty);
        var diagnostics = new List<string>();
        try
        {
            string recipeText = File.ReadAllText(recipePath);
            AssetRecipe recipe = RecipeCodec.Read(recipeText);
            if (recipe.AssetFamily != AssetFamily.Environment)
                throw new InvalidOperationException("Environment verifier received a Buddy recipe.");
            if (!RepositoryEnvironmentExporter.IsSupportedCategory(recipe.Category))
                throw new InvalidOperationException($"Environment verification is not implemented for {recipe.Category}.");
            displayId = recipe.AssetId;
            string canonical = RecipeCodec.WriteCanonical(recipe);
            if (!string.Equals(recipeText.Replace("\r\n", "\n", StringComparison.Ordinal), canonical, StringComparison.Ordinal))
                diagnostics.Add("authoring recipe.json is not canonical; save/regenerate it with Asset Forge");

            string sourcePath = Path.Combine(Path.GetDirectoryName(recipePath)!, recipe.SourceFile);
            if (!File.Exists(sourcePath))
            {
                diagnostics.Add($"missing authoring source: {Relative(root, sourcePath)}");
                return new EnvironmentAssetVerificationResult(displayId, false, diagnostics);
            }

            byte[] source = File.ReadAllBytes(sourcePath);
            GeneratedAsset expected = AssetForgeCompiler.Generate(source, recipe);
            string assetRoot = Path.Combine(root, "assets", "generated", "environment", recipe.AssetId);
            CompareBytes(Path.Combine(assetRoot, "mesh.glb"), expected.GlbBytes, "generated Environment mesh.glb differs from source + recipe", diagnostics);
            CompareBytes(Path.Combine(assetRoot, "albedo.png"), expected.AlbedoPng, "generated Environment albedo.png differs from source + recipe", diagnostics);
            ValidateThumbnail(Path.Combine(assetRoot, "thumbnail.png"), diagnostics);

            EnvironmentGeneratedBounds bounds = EnvironmentGeneratedBounds.Analyze(expected.Mesh);
            string definition = Path.Combine(root, "data", "environment", "generated", recipe.AssetId + ".tres");
            string number(double value) => value.ToString("0.######", CultureInfo.InvariantCulture);
            var required = new List<string>
            {
                $"DefinitionId = \"{Escape(recipe.AssetId)}\"",
                $"DisplayNameKey = \"{Escape(recipe.DisplayName)}\"",
                $"Category = {RepositoryEnvironmentExporter.DecorationCategory(recipe.Category)}",
                $"PriceCredits = {recipe.PriceCredits}",
                $"AnchorKind = {RepositoryEnvironmentExporter.AnchorKind(recipe.Environment.Anchor)}",
                $"AllowsRotation = {(recipe.Environment.AllowsRotation ? "true" : "false")}",
                $"RotationStepDegrees = {recipe.Environment.RotationStepDegrees}",
                "VisualSource = 1",
                $"VisualSize = Vector2({number(bounds.Width)}, {number(bounds.Height)})",
                $"Pivot = Vector2({number(recipe.Environment.PivotX)}, {number(recipe.Environment.PivotY)})",
                $"GeneratorVersion = {recipe.GeneratorVersion}",
                $"CanonicalAssetHash = \"{expected.CanonicalAssetHash}\"",
                $"res://assets/generated/environment/{recipe.AssetId}/mesh.glb",
                $"res://assets/generated/environment/{recipe.AssetId}/albedo.png",
                $"res://assets/generated/environment/{recipe.AssetId}/thumbnail.png",
            };

            bool hasLightProfile = recipe.Category == AssetCategory.Lamp && recipe.Light.Enabled;
            if (hasLightProfile)
            {
                required.Add($"EmissionStrength = {number(recipe.Light.EmissionStrength)}");
                required.Add($"LightEnabled = {(recipe.Light.LightEnabled ? "true" : "false")}");
                required.Add($"Brightness = {number(recipe.Light.Brightness)}");
                required.Add($"Range = {number(recipe.Light.Range)}");
                required.Add($"EmitterPosition = Vector2({number(recipe.Light.EmitterX)}, {number(recipe.Light.EmitterY)})");
                required.Add("LightProfile = SubResource(\"LightProfile\")");
                if (EnvironmentTemplateMapping.UsesLiteralTemplateSpace(recipe))
                {
                    Vector2 local = EnvironmentTemplateMapping.SourcePixelToWorld(
                        recipe.Light.EmitterX * EnvironmentTemplateSpace.CanvasSize,
                        recipe.Light.EmitterY * EnvironmentTemplateSpace.CanvasSize,
                        recipe);
                    required.Add("UsesLocalEmitterPosition = true");
                    required.Add($"LocalEmitterPosition = Vector2({number(local.X)}, {number(local.Y)})");
                }
            }

            VerifyTextFile(definition, required, "generated Environment definition", diagnostics);
            if (!hasLightProfile)
                VerifyTextFileMissing(definition,
                    ["LightProfile =", "DecorationLightProfileResource.cs", "GeneratedLamp"],
                    "non-lighting Environment definition",
                    diagnostics);

            VerifyTextFile(
                Path.Combine(root, "data", "environment", "generated_decorations.tres"),
                [$"res://data/environment/generated/{recipe.AssetId}.tres"],
                "generated Environment catalogue",
                diagnostics);

            if (diagnostics.Count == 0)
            {
                diagnostics.Add($"input {expected.InputHash[..12]} unchanged");
                diagnostics.Add($"recipe {expected.RecipeHash[..12]} unchanged");
                diagnostics.Add($"geometry {expected.GeometryHash[..12]} unchanged");
                diagnostics.Add($"asset {expected.CanonicalAssetHash[..12]} verified");
            }
        }
        catch (Exception exception)
        {
            diagnostics.Add(exception.Message);
        }

        bool passed = diagnostics.Count > 0 && diagnostics.All(static line =>
            line.EndsWith(" unchanged", StringComparison.Ordinal) || line.EndsWith(" verified", StringComparison.Ordinal));
        return new EnvironmentAssetVerificationResult(displayId, passed, diagnostics);
    }

    private static void VerifyNoOrphans(string root, IReadOnlyList<EnvironmentAssetVerificationResult> assets, List<string> diagnostics)
    {
        HashSet<string> ids = assets.Select(static asset => asset.AssetId).ToHashSet(StringComparer.Ordinal);
        string definitions = Path.Combine(root, "data", "environment", "generated");
        if (Directory.Exists(definitions))
            foreach (string file in Directory.GetFiles(definitions, "*.tres", SearchOption.TopDirectoryOnly))
            {
                string id = Path.GetFileNameWithoutExtension(file);
                if (!ids.Contains(id)) diagnostics.Add($"orphan generated Environment definition: {Relative(root, file)}");
            }
        string assetRoot = Path.Combine(root, "assets", "generated", "environment");
        if (Directory.Exists(assetRoot))
            foreach (string directory in Directory.GetDirectories(assetRoot).OrderBy(static path => path, StringComparer.Ordinal))
            {
                string id = Path.GetFileName(directory);
                if (!ids.Contains(id)) diagnostics.Add($"orphan generated Environment asset directory: {Relative(root, directory)}");
            }
    }

    private static void CompareBytes(string path, byte[] expected, string mismatch, List<string> diagnostics)
    {
        if (!File.Exists(path)) { diagnostics.Add($"missing {Relative(Path.GetDirectoryName(path) ?? string.Empty, path)}"); return; }
        if (!File.ReadAllBytes(path).AsSpan().SequenceEqual(expected)) diagnostics.Add(mismatch);
    }

    private static void ValidateThumbnail(string path, List<string> diagnostics)
    {
        if (!File.Exists(path)) { diagnostics.Add("missing generated Environment thumbnail.png"); return; }
        try
        {
            RgbaImage image = PngCodec.DecodeRgba8(File.ReadAllBytes(path));
            if (image.Width != EnvironmentThumbnailGenerator.OutputSize || image.Height != EnvironmentThumbnailGenerator.OutputSize)
                diagnostics.Add($"generated Environment thumbnail.png must be {EnvironmentThumbnailGenerator.OutputSize}x{EnvironmentThumbnailGenerator.OutputSize}; found {image.Width}x{image.Height}");
        }
        catch (Exception exception) { diagnostics.Add("generated Environment thumbnail.png is invalid: " + exception.Message); }
    }

    private static void VerifyTextFile(string path, IReadOnlyList<string> required, string label, List<string> diagnostics)
    {
        if (!File.Exists(path)) { diagnostics.Add($"missing {label}: {path.Replace('\\', '/')}"); return; }
        string text = File.ReadAllText(path);
        foreach (string marker in required)
            if (!text.Contains(marker, StringComparison.Ordinal)) diagnostics.Add($"{label} is stale or missing: {marker}");
    }

    private static void VerifyTextFileMissing(string path, IReadOnlyList<string> forbidden, string label, List<string> diagnostics)
    {
        if (!File.Exists(path)) return;
        string text = File.ReadAllText(path);
        foreach (string marker in forbidden)
            if (text.Contains(marker, StringComparison.Ordinal)) diagnostics.Add($"{label} unexpectedly contains: {marker}");
    }

    private static string Relative(string root, string path) => Path.GetRelativePath(root, path).Replace('\\', '/');
    private static string Escape(string value) => value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
}

public static class RepositoryEnvironmentRegenerator
{
    public static EnvironmentRepositoryRegenerationResult RegenerateAll(string repositoryRoot)
    {
        string root = RepositoryAssetVerifier.ValidateRoot(repositoryRoot);
        var ids = new List<string>();
        foreach (string path in RepositoryEnvironmentVerifier.DiscoverRecipes(root)) ids.Add(RegenerateRecipe(root, path));
        return new EnvironmentRepositoryRegenerationResult(ids, RepositoryEnvironmentVerifier.VerifyAll(root));
    }

    public static EnvironmentRepositoryRegenerationResult Regenerate(string repositoryRoot, string assetId)
    {
        string root = RepositoryAssetVerifier.ValidateRoot(repositoryRoot);
        string id = RegenerateRecipe(root, RepositoryEnvironmentVerifier.FindRecipe(root, assetId));
        return new EnvironmentRepositoryRegenerationResult([id], RepositoryEnvironmentVerifier.VerifyAll(root));
    }

    private static string RegenerateRecipe(string root, string recipePath)
    {
        AssetRecipe recipe = RecipeCodec.Read(File.ReadAllText(recipePath));
        string sourcePath = Path.Combine(Path.GetDirectoryName(recipePath)!, recipe.SourceFile);
        if (!File.Exists(sourcePath)) throw new FileNotFoundException("Authored Environment source image is missing.", sourcePath);
        byte[] source = File.ReadAllBytes(sourcePath);
        GeneratedAsset generated = AssetForgeCompiler.Generate(source, recipe);
        byte[] thumbnail = EnvironmentThumbnailGenerator.Create(generated.AlbedoPng);
        RepositoryEnvironmentExporter.Export(root, source, generated, thumbnail);
        return recipe.AssetId;
    }
}
