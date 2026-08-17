namespace DesktopBuddy.AssetForge.Core;

public sealed record AssetVerificationResult(
    string FeatureId,
    bool Passed,
    IReadOnlyList<string> Diagnostics);

public sealed record RepositoryVerificationResult(
    IReadOnlyList<AssetVerificationResult> Assets,
    IReadOnlyList<string> RepositoryDiagnostics)
{
    public int PassedCount => Assets.Count(static asset => asset.Passed);
    public bool Passed => Assets.All(static asset => asset.Passed) && RepositoryDiagnostics.Count == 0;
}

public sealed record RepositoryRegenerationResult(
    IReadOnlyList<string> RegeneratedFeatureIds,
    RepositoryVerificationResult Verification);

public static class RepositoryAssetVerifier
{
    public static RepositoryVerificationResult VerifyAll(string repositoryRoot)
    {
        repositoryRoot = ValidateRoot(repositoryRoot);
        string[] recipes = DiscoverRecipeFiles(repositoryRoot);
        var assets = recipes.Select(path => VerifyRecipe(repositoryRoot, path)).ToArray();
        var repositoryDiagnostics = new List<string>();
        VerifyNoOrphans(repositoryRoot, assets, repositoryDiagnostics);
        return new RepositoryVerificationResult(assets, repositoryDiagnostics);
    }

    public static AssetVerificationResult Verify(string repositoryRoot, string featureId)
    {
        repositoryRoot = ValidateRoot(repositoryRoot);
        string recipePath = FindRecipe(repositoryRoot, featureId);
        return VerifyRecipe(repositoryRoot, recipePath);
    }

    private static AssetVerificationResult VerifyRecipe(string root, string recipePath)
    {
        string displayId = Path.GetFileName(Path.GetDirectoryName(recipePath) ?? string.Empty);
        var diagnostics = new List<string>();
        try
        {
            string recipeText = File.ReadAllText(recipePath);
            AssetRecipe recipe = RecipeCodec.Read(recipeText);
            if (recipe.AssetFamily != AssetFamily.BuddyStudio)
                throw new InvalidOperationException($"Buddy verifier received non-Buddy recipe {recipe.AssetId}.");
            displayId = recipe.FeatureId;
            string canonicalRecipe = RecipeCodec.WriteCanonical(recipe);
            if (!string.Equals(recipeText.Replace("\r\n", "\n", StringComparison.Ordinal), canonicalRecipe, StringComparison.Ordinal))
                diagnostics.Add("authoring recipe.json is not canonical; save/regenerate it with Asset Forge");

            string sourcePath = Path.Combine(Path.GetDirectoryName(recipePath)!, recipe.SourceFile);
            if (!File.Exists(sourcePath))
            {
                diagnostics.Add($"missing authoring source: {Relative(root, sourcePath)}");
                return new AssetVerificationResult(displayId, false, diagnostics);
            }

            byte[] source = File.ReadAllBytes(sourcePath);
            GeneratedAsset expected = AssetForgeCompiler.Generate(source, recipe);
            string assetRoot = Path.Combine(root, "assets", "generated", "cosmetics", recipe.FeatureId);
            string meshFileName = AssetFileNaming.MeshFileName(recipe);
            CompareBytes(Path.Combine(assetRoot, meshFileName), expected.GlbBytes, $"generated {meshFileName} differs from source + recipe", diagnostics);
            VerifyOnlyExpectedMesh(assetRoot, meshFileName, diagnostics);
            CompareBytes(Path.Combine(assetRoot, "albedo.png"), expected.AlbedoPng, "generated albedo.png differs from source + recipe", diagnostics);
            ValidateThumbnail(Path.Combine(assetRoot, "thumbnail.png"), diagnostics);

            string cosmeticDefinition = Path.Combine(root, "data", "cosmetics", "generated", recipe.FeatureId + ".tres");
            VerifyTextFile(
                cosmeticDefinition,
                [
                    $"FeatureId = \"{Escape(recipe.FeatureId)}\"",
                    $"ContentId = \"{Escape(recipe.ContentId)}\"",
                    $"DisplayName = \"{Escape(recipe.DisplayName)}\"",
                    GeneratedCosmeticCategoryPersistence.ExpectedMarker(recipe),
                    $"SortOrder = {recipe.SortOrder}",
                    GeneratedCosmeticLightingPersistence.ExpectedMarker(recipe),
                    $"GeneratorVersion = {recipe.GeneratorVersion}",
                    $"CanonicalAssetHash = \"{expected.CanonicalAssetHash}\"",
                    $"res://assets/generated/cosmetics/{recipe.FeatureId}/{meshFileName}",
                    $"res://assets/generated/cosmetics/{recipe.FeatureId}/albedo.png",
                    $"res://assets/generated/cosmetics/{recipe.FeatureId}/thumbnail.png",
                ],
                "generated cosmetic definition",
                diagnostics);

            string saleFile = recipe.ContentId.Replace('.', '_') + ".tres";
            string saleDefinition = Path.Combine(root, "data", "catalogue", "generated", saleFile);
            VerifyTextFile(
                saleDefinition,
                [
                    $"ContentId = \"{Escape(recipe.ContentId)}\"",
                    $"PriceCredits = {recipe.PriceCredits}",
                    $"ProgressionOrder = {10000 + recipe.SortOrder}",
                    $"NameKey = \"{Escape(recipe.DisplayName)}\"",
                    $"res://assets/generated/cosmetics/{recipe.FeatureId}/thumbnail.png",
                ],
                "generated sale definition",
                diagnostics);

            VerifyTextFile(
                Path.Combine(root, "data", "cosmetics", "generated", "catalogue.tres"),
                [$"res://data/cosmetics/generated/{recipe.FeatureId}.tres"],
                "generated cosmetic catalogue",
                diagnostics);
            VerifyTextFile(
                Path.Combine(root, "data", "catalogue", "generated_cosmetics.tres"),
                [$"res://data/catalogue/generated/{saleFile}"],
                "generated commerce catalogue",
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
            line.EndsWith(" unchanged", StringComparison.Ordinal) ||
            line.EndsWith(" verified", StringComparison.Ordinal));
        return new AssetVerificationResult(displayId, passed, diagnostics);
    }

    private static void VerifyOnlyExpectedMesh(string assetRoot, string expectedMeshFileName, List<string> diagnostics)
    {
        if (!Directory.Exists(assetRoot)) return;
        foreach (string mesh in Directory.GetFiles(assetRoot, "*.glb", SearchOption.TopDirectoryOnly))
            if (!string.Equals(Path.GetFileName(mesh), expectedMeshFileName, StringComparison.Ordinal))
                diagnostics.Add($"stale generated mesh file should be removed: {Path.GetFileName(mesh)}");
    }

    private static void VerifyNoOrphans(string root, IReadOnlyList<AssetVerificationResult> assets, List<string> diagnostics)
    {
        var featureIds = assets.Select(static asset => asset.FeatureId).ToHashSet(StringComparer.Ordinal);
        string cosmeticDir = Path.Combine(root, "data", "cosmetics", "generated");
        if (Directory.Exists(cosmeticDir))
        {
            foreach (string file in Directory.GetFiles(cosmeticDir, "*.tres", SearchOption.TopDirectoryOnly))
            {
                if (string.Equals(Path.GetFileName(file), "catalogue.tres", StringComparison.Ordinal)) continue;
                string featureId = Path.GetFileNameWithoutExtension(file);
                if (!featureIds.Contains(featureId)) diagnostics.Add($"orphan generated cosmetic definition: {Relative(root, file)}");
            }
        }

        string assetDir = Path.Combine(root, "assets", "generated", "cosmetics");
        if (Directory.Exists(assetDir))
        {
            foreach (string directory in Directory.GetDirectories(assetDir).OrderBy(static path => path, StringComparer.Ordinal))
            {
                string featureId = Path.GetFileName(directory);
                if (!featureIds.Contains(featureId)) diagnostics.Add($"orphan generated cosmetic asset directory: {Relative(root, directory)}");
            }
        }
    }

    private static void CompareBytes(string path, byte[] expected, string mismatch, List<string> diagnostics)
    {
        if (!File.Exists(path)) { diagnostics.Add($"missing {path.Replace('\\', '/')}"); return; }
        byte[] actual = File.ReadAllBytes(path);
        if (!actual.AsSpan().SequenceEqual(expected)) diagnostics.Add(mismatch);
    }

    private static void ValidateThumbnail(string path, List<string> diagnostics)
    {
        if (!File.Exists(path)) { diagnostics.Add("missing generated thumbnail.png"); return; }
        byte[] bytes = File.ReadAllBytes(path);
        if (!AssetThumbnailCache.IsCanonical(bytes))
        {
            try
            {
                RgbaImage image = PngCodec.DecodeRgba8(bytes);
                diagnostics.Add($"generated thumbnail.png must be {EnvironmentThumbnailGenerator.OutputSize}x{EnvironmentThumbnailGenerator.OutputSize}; found {image.Width}x{image.Height}");
            }
            catch (Exception exception)
            {
                diagnostics.Add("generated thumbnail.png is invalid: " + exception.Message);
            }
        }
    }

    private static void VerifyTextFile(string path, IReadOnlyList<string> required, string label, List<string> diagnostics)
    {
        if (!File.Exists(path)) { diagnostics.Add($"missing {label}: {path.Replace('\\', '/')}"); return; }
        string text = File.ReadAllText(path);
        foreach (string marker in required)
            if (!text.Contains(marker, StringComparison.Ordinal)) diagnostics.Add($"{label} is stale or missing: {marker}");
    }

    /// <summary>All authoring recipes, including Environment. Family-specific verifiers filter this list.</summary>
    internal static string[] DiscoverAllRecipeFiles(string root)
    {
        string authoring = Path.Combine(root, "authoring", "asset-forge");
        return Directory.Exists(authoring)
            ? Directory.GetFiles(authoring, "recipe.json", SearchOption.AllDirectories).OrderBy(static path => path, StringComparer.Ordinal).ToArray()
            : [];
    }

    internal static string[] DiscoverRecipeFiles(string root) => DiscoverAllRecipeFiles(root)
        .Where(static path =>
        {
            try { return RecipeCodec.Read(File.ReadAllText(path)).AssetFamily == AssetFamily.BuddyStudio; }
            catch { return true; }
        })
        .ToArray();

    internal static string FindRecipe(string root, string featureId)
    {
        if (string.IsNullOrWhiteSpace(featureId)) throw new ArgumentException("Feature ID is required.", nameof(featureId));
        foreach (string recipePath in DiscoverRecipeFiles(root))
        {
            AssetRecipe recipe = RecipeCodec.Read(File.ReadAllText(recipePath));
            if (string.Equals(recipe.FeatureId, featureId, StringComparison.Ordinal)) return recipePath;
        }
        throw new FileNotFoundException($"No authored Asset Forge recipe exists for {featureId}.");
    }

    internal static string ValidateRoot(string repositoryRoot)
    {
        if (string.IsNullOrWhiteSpace(repositoryRoot)) throw new ArgumentException("Repository root is required.", nameof(repositoryRoot));
        string root = Path.GetFullPath(repositoryRoot);
        if (!File.Exists(Path.Combine(root, "DesktopBuddy.csproj"))) throw new DirectoryNotFoundException("Desktop Buddy repository root was not found.");
        return root;
    }

    private static string Relative(string root, string path) => Path.GetRelativePath(root, path).Replace('\\', '/');
    private static string Escape(string value) => value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
}

public static class RepositoryAssetRegenerator
{
    public static RepositoryRegenerationResult RegenerateAll(string repositoryRoot)
    {
        string root = RepositoryAssetVerifier.ValidateRoot(repositoryRoot);
        var regenerated = new List<string>();
        foreach (string recipePath in RepositoryAssetVerifier.DiscoverRecipeFiles(root))
            regenerated.Add(RegenerateRecipe(root, recipePath));
        RepositoryVerificationResult verification = RepositoryAssetVerifier.VerifyAll(root);
        return new RepositoryRegenerationResult(regenerated, verification);
    }

    public static RepositoryRegenerationResult Regenerate(string repositoryRoot, string featureId)
    {
        string root = RepositoryAssetVerifier.ValidateRoot(repositoryRoot);
        string recipePath = RepositoryAssetVerifier.FindRecipe(root, featureId);
        string regenerated = RegenerateRecipe(root, recipePath);
        RepositoryVerificationResult verification = RepositoryAssetVerifier.VerifyAll(root);
        return new RepositoryRegenerationResult([regenerated], verification);
    }

    private static string RegenerateRecipe(string root, string recipePath)
    {
        AssetRecipe recipe = RecipeCodec.Read(File.ReadAllText(recipePath));
        string sourcePath = Path.Combine(Path.GetDirectoryName(recipePath)!, recipe.SourceFile);
        if (!File.Exists(sourcePath)) throw new FileNotFoundException("Authored source image is missing.", sourcePath);
        byte[] source = File.ReadAllBytes(sourcePath);
        GeneratedAsset generated = AssetForgeCompiler.Generate(source, recipe);
        string thumbnailPath = Path.Combine(root, "assets", "generated", "cosmetics", recipe.FeatureId, "thumbnail.png");
        byte[] thumbnail;
        if (File.Exists(thumbnailPath) && AssetThumbnailCache.IsCanonical(File.ReadAllBytes(thumbnailPath)))
        {
            thumbnail = File.ReadAllBytes(thumbnailPath);
        }
        else
        {
            thumbnail = AssetThumbnailCache.GetOrCreate(
                generated,
                () => EnvironmentThumbnailGenerator.Create(generated.AlbedoPng));
        }
        if (recipe.Category == AssetCategory.Glasses)
            RepositoryExporter.ExportGlasses(root, source, generated, thumbnail);
        else
            RepositoryBuddyReplacementExporter.Export(root, source, generated, thumbnail);
        GeneratedCosmeticLightingPersistence.Apply(root, recipe);
        return recipe.FeatureId;
    }
}
