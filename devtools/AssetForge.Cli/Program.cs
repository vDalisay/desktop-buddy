using DesktopBuddy.AssetForge.Core;

namespace DesktopBuddy.AssetForge.Cli;

internal static class Program
{
    private static int Main(string[] args)
    {
        try
        {
            if (args.Length >= 2 && string.Equals(args[0], "--ci-fixture", StringComparison.Ordinal))
                return GenerateCiFixture(Path.GetFullPath(args[1]));
            if (args.Length == 2 && string.Equals(args[0], "--verify-all", StringComparison.Ordinal))
                return PrintVerification(RepositoryAssetVerifier.VerifyAll(Path.GetFullPath(args[1])));
            if (args.Length == 3 && string.Equals(args[0], "--verify", StringComparison.Ordinal))
                return PrintVerification(RepositoryAssetVerifier.Verify(Path.GetFullPath(args[1]), args[2]));
            if (args.Length == 2 && string.Equals(args[0], "--regenerate-all", StringComparison.Ordinal))
            {
                RepositoryRegenerationResult result = RepositoryAssetRegenerator.RegenerateAll(Path.GetFullPath(args[1]));
                Console.WriteLine($"Regenerated {result.RegeneratedFeatureIds.Count} asset(s): {string.Join(", ", result.RegeneratedFeatureIds)}");
                return PrintVerification(result.Verification);
            }
            if (args.Length == 3 && string.Equals(args[0], "--regenerate", StringComparison.Ordinal))
            {
                RepositoryRegenerationResult result = RepositoryAssetRegenerator.Regenerate(Path.GetFullPath(args[1]), args[2]);
                Console.WriteLine($"Regenerated {string.Join(", ", result.RegeneratedFeatureIds)}");
                return PrintVerification(result.Verification);
            }

            PrintUsage();
            return 2;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static int GenerateCiFixture(string repositoryRoot)
    {
        GenerateGlassesFixture(repositoryRoot);
        GenerateReplacementFixture(
            repositoryRoot,
            AssetRecipe.TorsoShapeDefaults() with
            {
                FeatureId = "top.ci_pear_torso",
                ContentId = "cosmetic.top.ci_pear_torso",
                DisplayName = "CI Pear Torso",
                PriceCredits = 175,
                SortOrder = 9910,
                LightingLevel = 0.31,
                Geometry = AssetRecipe.TorsoShapeDefaults().Geometry with
                {
                    GeometryResolution = 64,
                    RuntimeTextureResolution = 128,
                },
            },
            CreateTorsoReplacementFixture());
        GenerateReplacementFixture(
            repositoryRoot,
            AssetRecipe.FootShapeDefaults() with
            {
                FeatureId = "shoes.ci_soft_foot",
                ContentId = "cosmetic.shoes.ci_soft_foot",
                DisplayName = "CI Soft Foot",
                PriceCredits = 160,
                SortOrder = 9920,
                LightingLevel = 0.29,
                Geometry = AssetRecipe.FootShapeDefaults().Geometry with
                {
                    GeometryResolution = 64,
                    RuntimeTextureResolution = 128,
                },
            },
            CreateFootReplacementFixture());

        RepositoryVerificationResult verification = RepositoryAssetVerifier.VerifyAll(repositoryRoot);
        if (!verification.Passed)
            throw new InvalidOperationException("Combined Asset Forge CI fixtures failed verification: " +
                string.Join("; ", verification.Assets.SelectMany(static asset => asset.Diagnostics).Concat(verification.RepositoryDiagnostics)));
        Console.WriteLine($"Generated and verified {verification.Assets.Count} Asset Forge CI fixtures.");
        return 0;
    }

    private static void GenerateGlassesFixture(string repositoryRoot)
    {
        AssetRecipe recipe = AssetRecipe.GlassesDefaults() with
        {
            FeatureId = "glasses.ci_pink_round",
            ContentId = "cosmetic.glasses.ci_pink_round",
            DisplayName = "CI Pink Round",
            PriceCredits = 125,
            SortOrder = 9900,
            LightingLevel = 0.22,
            Geometry = AssetRecipe.GlassesDefaults().Geometry with
            {
                GeometryResolution = 128,
                RuntimeTextureResolution = 256,
                SymmetryMode = SymmetryMode.Off,
                ShapeMode = ShapeMode.RoundedExtrusion,
                Roundness = 0.55,
            },
        };

        RgbaImage source = CreatePinkRoundGlassesOnOpaqueWhiteCanvas();
        byte[] sourcePng = PngCodec.EncodeRgba8(source);
        GeneratedAsset first = AssetForgeGenerator.Generate(sourcePng, recipe);
        GeneratedAsset second = AssetForgeGenerator.Generate(sourcePng, recipe);
        if (!first.GlbBytes.SequenceEqual(second.GlbBytes) ||
            !string.Equals(first.CanonicalAssetHash, second.CanonicalAssetHash, StringComparison.Ordinal))
            throw new InvalidOperationException("CI glasses regeneration was not deterministic.");
        if (first.Foreground.Mode != ForegroundExtractionMode.UniformBackground || first.Diagnostics.Holes != 2)
            throw new InvalidOperationException($"Opaque-canvas glasses were not interpreted as a two-hole frame. mode={first.Foreground.Mode} holes={first.Diagnostics.Holes}.");

        RepositoryExporter.ExportGlasses(repositoryRoot, sourcePng, first, first.AlbedoPng);
        GeneratedCosmeticLightingPersistence.Apply(repositoryRoot, recipe);
        AssertFixtureFiles(repositoryRoot, recipe);
        Console.WriteLine($"Generated {recipe.FeatureId}: {first.TriangleCount} triangles, asset {first.CanonicalAssetHash}.");
    }

    private static void GenerateReplacementFixture(string repositoryRoot, AssetRecipe recipe, RgbaImage source)
    {
        byte[] sourcePng = PngCodec.EncodeRgba8(source);
        GeneratedAsset first = AssetForgeCompiler.Generate(sourcePng, recipe);
        GeneratedAsset second = AssetForgeCompiler.Generate(sourcePng, recipe);
        if (!first.GlbBytes.SequenceEqual(second.GlbBytes) || first.CanonicalAssetHash != second.CanonicalAssetHash)
            throw new InvalidOperationException($"{recipe.FeatureId} regeneration was not deterministic.");
        RepositoryBuddyReplacementExporter.Export(repositoryRoot, sourcePng, first, first.AlbedoPng);
        GeneratedCosmeticLightingPersistence.Apply(repositoryRoot, recipe);
        AssertFixtureFiles(repositoryRoot, recipe);

        string definition = Path.Combine(repositoryRoot, "data", "cosmetics", "generated", recipe.FeatureId + ".tres");
        string text = File.ReadAllText(definition);
        if (!text.Contains(GeneratedCosmeticCategoryPersistence.ExpectedMarker(recipe), StringComparison.Ordinal))
            throw new InvalidOperationException($"{recipe.FeatureId} did not persist its generated slot.");
        Console.WriteLine($"Generated {recipe.FeatureId}: {first.Diagnostics.Holes} holes, {first.TriangleCount} triangles, asset {first.CanonicalAssetHash}.");
    }

    private static void AssertFixtureFiles(string repositoryRoot, AssetRecipe recipe)
    {
        string assetRoot = Path.Combine(repositoryRoot, "assets", "generated", "cosmetics", recipe.FeatureId);
        string definition = Path.Combine(repositoryRoot, "data", "cosmetics", "generated", recipe.FeatureId + ".tres");
        string sale = Path.Combine(repositoryRoot, "data", "catalogue", "generated", recipe.ContentId.Replace('.', '_') + ".tres");
        foreach (string path in new[]
                 {
                     Path.Combine(assetRoot, "mesh.glb"),
                     Path.Combine(assetRoot, "albedo.png"),
                     Path.Combine(assetRoot, "thumbnail.png"),
                     definition,
                     sale,
                     Path.Combine(repositoryRoot, "data", "cosmetics", "generated", "catalogue.tres"),
                     Path.Combine(repositoryRoot, "data", "catalogue", "generated_cosmetics.tres"),
                 })
            if (!File.Exists(path)) throw new FileNotFoundException("Expected Asset Forge export was not written.", path);
    }

    private static int PrintVerification(AssetVerificationResult result)
    {
        Console.WriteLine($"{(result.Passed ? "OK" : "FAIL")} {result.FeatureId}");
        foreach (string diagnostic in result.Diagnostics) Console.WriteLine("  " + diagnostic);
        return result.Passed ? 0 : 1;
    }

    private static int PrintVerification(RepositoryVerificationResult result)
    {
        foreach (AssetVerificationResult asset in result.Assets)
        {
            Console.WriteLine($"{(asset.Passed ? "OK" : "FAIL")} {asset.FeatureId}");
            foreach (string diagnostic in asset.Diagnostics) Console.WriteLine("  " + diagnostic);
        }
        foreach (string diagnostic in result.RepositoryDiagnostics) Console.WriteLine("FAIL repository\n  " + diagnostic);
        if (result.Assets.Count == 0 && result.RepositoryDiagnostics.Count == 0)
            Console.WriteLine("OK repository\n  no authored Asset Forge assets yet");
        return result.Passed ? 0 : 1;
    }

    private static void PrintUsage()
    {
        Console.Error.WriteLine("Desktop Buddy Asset Forge CLI");
        Console.Error.WriteLine("  --verify-all <repository-root>");
        Console.Error.WriteLine("  --verify <repository-root> <feature-id>");
        Console.Error.WriteLine("  --regenerate-all <repository-root>");
        Console.Error.WriteLine("  --regenerate <repository-root> <feature-id>");
        Console.Error.WriteLine("  --ci-fixture <repository-root>");
    }

    private static RgbaImage CreatePinkRoundGlassesOnOpaqueWhiteCanvas()
    {
        const int size = 1024;
        byte[] pixels = new byte[size * size * 4];
        for (int i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = 255;
            pixels[i + 1] = 255;
            pixels[i + 2] = 255;
            pixels[i + 3] = 255;
        }
        DrawFrame(pixels, 215, 390, 475, 620, 38);
        DrawFrame(pixels, 549, 390, 809, 620, 38);
        Fill(pixels, 470, 485, 554, 525, 239, 123, 175);
        return new RgbaImage(size, size, pixels);
    }

    private static RgbaImage CreateTorsoReplacementFixture()
    {
        const int size = 1024;
        byte[] pixels = new byte[size * size * 4];
        for (int y = 260; y <= 770; y++)
        {
            float t = (y - 260) / 510f;
            int halfWidth = (int)MathF.Round(145 + t * 115);
            for (int x = 512 - halfWidth; x <= 512 + halfWidth; x++) Set(pixels, x, y, 97, 190, 158);
        }
        return new RgbaImage(size, size, pixels);
    }

    private static RgbaImage CreateFootReplacementFixture()
    {
        const int size = 1024;
        byte[] pixels = new byte[size * size * 4];
        for (int y = 390; y <= 690; y++)
        for (int x = 325; x <= 755; x++)
        {
            double nx = (x - 520) / 230.0;
            double ny = (y - 545) / 155.0;
            if (nx * nx + ny * ny <= 1.0 && !(x < 430 && y < 460)) Set(pixels, x, y, 238, 173, 96);
        }
        return new RgbaImage(size, size, pixels);
    }

    private static void DrawFrame(byte[] pixels, int x0, int y0, int x1, int y1, int thickness)
    {
        Fill(pixels, x0, y0, x1, y0 + thickness, 239, 123, 175);
        Fill(pixels, x0, y1 - thickness, x1, y1, 239, 123, 175);
        Fill(pixels, x0, y0, x0 + thickness, y1, 239, 123, 175);
        Fill(pixels, x1 - thickness, y0, x1, y1, 239, 123, 175);
    }

    private static void Fill(byte[] pixels, int x0, int y0, int x1, int y1, byte r, byte g, byte b)
    {
        for (int y = y0; y < y1; y++)
        for (int x = x0; x < x1; x++) Set(pixels, x, y, r, g, b);
    }

    private static void Set(byte[] pixels, int x, int y, byte r, byte g, byte b)
    {
        if (x is < 0 or >= 1024 || y is < 0 or >= 1024) return;
        int index = (y * 1024 + x) * 4;
        pixels[index] = r;
        pixels[index + 1] = g;
        pixels[index + 2] = b;
        pixels[index + 3] = 255;
    }
}
