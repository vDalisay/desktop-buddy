using DesktopBuddy.AssetForge.Core;

namespace DesktopBuddy.AssetForge.Cli;

internal static class Program
{
    private static int Main(string[] args)
    {
        try
        {
            if (args.Length != 2 || !string.Equals(args[0], "--ci-fixture", StringComparison.Ordinal))
            {
                Console.Error.WriteLine("Usage: DesktopBuddy.AssetForge.Cli --ci-fixture <repository-root>");
                return 2;
            }

            string repositoryRoot = Path.GetFullPath(args[1]);
            AssetRecipe recipe = AssetRecipe.GlassesDefaults() with
            {
                FeatureId = "glasses.ci_pink_round",
                ContentId = "cosmetic.glasses.ci_pink_round",
                DisplayName = "CI Pink Round",
                PriceCredits = 125,
                SortOrder = 9900,
                Geometry = AssetRecipe.GlassesDefaults().Geometry with
                {
                    GeometryResolution = 128,
                    RuntimeTextureResolution = 256,
                    SymmetryMode = SymmetryMode.Off,
                },
            };

            RgbaImage source = CreatePinkRoundGlasses();
            byte[] sourcePng = PngCodec.EncodeRgba8(source);
            GeneratedAsset first = AssetForgeGenerator.Generate(sourcePng, recipe);
            GeneratedAsset second = AssetForgeGenerator.Generate(sourcePng, recipe);
            if (!first.GlbBytes.SequenceEqual(second.GlbBytes) ||
                !string.Equals(first.CanonicalAssetHash, second.CanonicalAssetHash, StringComparison.Ordinal))
                throw new InvalidOperationException("CI fixture regeneration was not deterministic.");

            RepositoryExporter.ExportGlasses(repositoryRoot, sourcePng, first, first.AlbedoPng);
            string assetRoot = Path.Combine(repositoryRoot, "assets", "generated", "cosmetics", recipe.FeatureId);
            string definition = Path.Combine(repositoryRoot, "data", "cosmetics", "generated", recipe.FeatureId + ".tres");
            string sale = Path.Combine(repositoryRoot, "data", "catalogue", "generated", "cosmetic_glasses_ci_pink_round.tres");
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

            Console.WriteLine($"Generated {recipe.FeatureId}: {first.Diagnostics.Holes} holes, {first.TriangleCount} triangles, asset {first.CanonicalAssetHash}.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static RgbaImage CreatePinkRoundGlasses()
    {
        const int size = 1024;
        byte[] pixels = new byte[size * size * 4];
        DrawFrame(pixels, 215, 390, 475, 620, 38);
        DrawFrame(pixels, 549, 390, 809, 620, 38);
        Fill(pixels, 470, 485, 554, 525);
        return new RgbaImage(size, size, pixels);
    }

    private static void DrawFrame(byte[] pixels, int x0, int y0, int x1, int y1, int thickness)
    {
        Fill(pixels, x0, y0, x1, y0 + thickness);
        Fill(pixels, x0, y1 - thickness, x1, y1);
        Fill(pixels, x0, y0, x0 + thickness, y1);
        Fill(pixels, x1 - thickness, y0, x1, y1);
    }

    private static void Fill(byte[] pixels, int x0, int y0, int x1, int y1)
    {
        const int size = 1024;
        for (int y = y0; y < y1; y++)
        for (int x = x0; x < x1; x++)
        {
            int index = (y * size + x) * 4;
            pixels[index] = 239;
            pixels[index + 1] = 123;
            pixels[index + 2] = 175;
            pixels[index + 3] = 255;
        }
    }
}
