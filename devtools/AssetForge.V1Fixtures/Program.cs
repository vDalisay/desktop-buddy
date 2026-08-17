using DesktopBuddy.AssetForge.Core;

namespace DesktopBuddy.AssetForge.V1Fixtures;

internal static class Program
{
    private static int Main(string[] args)
    {
        try
        {
            if (args.Length != 1)
            {
                Console.Error.WriteLine("Usage: DesktopBuddy.AssetForge.V1Fixtures <repository-root>");
                return 2;
            }

            string root = Path.GetFullPath(args[0]);
            Generate(root, Fast(AssetRecipe.TableDefaults(), "decoration.table.ci_simple", "CI Simple Table", 150, 9940), TableSource());
            Generate(root, Fast(AssetRecipe.PlantDefaults(), "decoration.plant.ci_leafy", "CI Leafy Plant", 110, 9950), PlantSource());
            Generate(root, Fast(AssetRecipe.PaintingDefaults(), "decoration.painting.ci_frame", "CI Framed Painting", 90, 9960), PaintingSource());

            AssetForgeRepositoryVerificationResult verified = RepositoryAssetForgeMaintenance.VerifyAll(root);
            if (!verified.Passed)
                throw new InvalidOperationException("v1 Environment fixtures failed Verify All: " +
                    string.Join("; ", verified.Environment.RepositoryDiagnostics.Concat(verified.Environment.Assets.SelectMany(static asset => asset.Diagnostics))));
            Console.WriteLine("Generated and verified Table, Plant and Painting Asset Forge v1 fixtures.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static AssetRecipe Fast(AssetRecipe recipe, string id, string display, int price, int sort) => recipe with
    {
        AssetId = id,
        DisplayName = display,
        PriceCredits = price,
        SortOrder = sort,
        Geometry = recipe.Geometry with
        {
            GeometryResolution = 64,
            RuntimeTextureResolution = 128,
        },
    };

    private static void Generate(string root, AssetRecipe recipe, RgbaImage source)
    {
        byte[] sourcePng = PngCodec.EncodeRgba8(source);
        GeneratedAsset first = AssetForgeCompiler.Generate(sourcePng, recipe);
        GeneratedAsset second = AssetForgeCompiler.Generate(sourcePng, recipe);
        if (!first.GlbBytes.SequenceEqual(second.GlbBytes) || first.CanonicalAssetHash != second.CanonicalAssetHash)
            throw new InvalidOperationException($"{recipe.AssetId} was not deterministic.");
        byte[] thumbnail = EnvironmentThumbnailGenerator.Create(first);
        RepositoryEnvironmentExporter.Export(root, sourcePng, first, thumbnail);
        EnvironmentAssetVerificationResult verification = RepositoryEnvironmentVerifier.Verify(root, recipe.AssetId);
        if (!verification.Passed)
            throw new InvalidOperationException($"{recipe.AssetId} failed verification: {string.Join("; ", verification.Diagnostics)}");
        Console.WriteLine($"Generated {recipe.AssetId}: {first.TriangleCount} triangles, asset {first.CanonicalAssetHash}.");
    }

    private static RgbaImage TableSource()
    {
        byte[] pixels = Canvas();
        Fill(pixels, 270, 480, 754, 555, 170, 106, 65);
        Fill(pixels, 320, 555, 372, EnvironmentTemplateSpace.FloorY, 112, 70, 46);
        Fill(pixels, 652, 555, 704, EnvironmentTemplateSpace.FloorY, 112, 70, 46);
        return new RgbaImage(1024, 1024, pixels);
    }

    private static RgbaImage PlantSource()
    {
        byte[] pixels = Canvas();
        Fill(pixels, 420, 700, 604, EnvironmentTemplateSpace.FloorY, 174, 104, 63);
        Fill(pixels, 482, 410, 542, 720, 57, 122, 72);
        Disc(pixels, 420, 440, 125, 74, 157, 88);
        Disc(pixels, 600, 400, 140, 89, 174, 96);
        Disc(pixels, 510, 280, 150, 78, 165, 91);
        return new RgbaImage(1024, 1024, pixels);
    }

    private static RgbaImage PaintingSource()
    {
        byte[] pixels = Canvas();
        Fill(pixels, 300, 280, 724, 744, 73, 51, 83);
        Fill(pixels, 322, 302, 702, 722, 222, 184, 128);
        Fill(pixels, 350, 500, 675, 690, 106, 153, 176);
        Disc(pixels, 520, 415, 68, 213, 120, 91);
        return new RgbaImage(1024, 1024, pixels);
    }

    private static byte[] Canvas() => new byte[1024 * 1024 * 4];

    private static void Disc(byte[] pixels, int cx, int cy, int radius, byte r, byte g, byte b)
    {
        int rr = radius * radius;
        for (int y = Math.Max(0, cy - radius); y < Math.Min(1024, cy + radius); y++)
        for (int x = Math.Max(0, cx - radius); x < Math.Min(1024, cx + radius); x++)
            if ((x - cx) * (x - cx) + (y - cy) * (y - cy) <= rr) Set(pixels, x, y, r, g, b);
    }

    private static void Fill(byte[] pixels, int x0, int y0, int x1, int y1, byte r, byte g, byte b)
    {
        for (int y = Math.Max(0, y0); y < Math.Min(1024, y1); y++)
        for (int x = Math.Max(0, x0); x < Math.Min(1024, x1); x++) Set(pixels, x, y, r, g, b);
    }

    private static void Set(byte[] pixels, int x, int y, byte r, byte g, byte b)
    {
        int i = (y * 1024 + x) * 4;
        pixels[i] = r;
        pixels[i + 1] = g;
        pixels[i + 2] = b;
        pixels[i + 3] = 255;
    }
}
