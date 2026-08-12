using DesktopBuddy.AssetForge.Core;
using Xunit;

namespace DesktopBuddy.AssetForge.Core.Tests;

public sealed class AssetForgeCoreTests
{
    [Fact]
    public void Canonical_recipe_round_trips_and_hashes_identically()
    {
        AssetRecipe source = AssetRecipe.GlassesDefaults() with
        {
            FeatureId = "glasses.pink_round",
            ContentId = "cosmetic.glasses.pink_round",
            DisplayName = "Pink Round",
            PriceCredits = 125,
        };
        string json = RecipeCodec.WriteCanonical(source);
        AssetRecipe loaded = RecipeCodec.Read(json);
        Assert.Equal(json, RecipeCodec.WriteCanonical(loaded));
        Assert.Equal(RecipeCodec.Hash(source), RecipeCodec.Hash(loaded));
    }

    [Fact]
    public void Png_round_trip_is_lossless()
    {
        RgbaImage image = TestGlassesImage();
        byte[] png = PngCodec.EncodeRgba8(image);
        RgbaImage decoded = PngCodec.DecodeRgba8(png);
        Assert.Equal(1024, decoded.Width);
        Assert.Equal(1024, decoded.Height);
        Assert.Equal(image.Pixels, decoded.Pixels);
    }

    [Fact]
    public void Glasses_mask_retains_two_lens_holes()
    {
        AssetRecipe recipe = Recipe();
        MaskGrid mask = MaskGrid.FromImage(TestGlassesImage(), recipe.Geometry);
        MaskDiagnostics diagnostics = MaskAnalyzer.Analyze(mask);
        Assert.Equal(1, diagnostics.Components);
        Assert.Equal(2, diagnostics.Holes);
        Assert.True(diagnostics.FilledCells > 0);
        Assert.True(diagnostics.BoundaryEdges > 0);
    }

    [Fact]
    public void Same_input_and_recipe_generate_byte_identical_glb()
    {
        byte[] png = PngCodec.EncodeRgba8(TestGlassesImage());
        AssetRecipe recipe = Recipe();
        GeneratedAsset first = AssetForgeGenerator.Generate(png, recipe);
        GeneratedAsset second = AssetForgeGenerator.Generate(png, recipe);
        Assert.Equal(first.GeometryHash, second.GeometryHash);
        Assert.Equal(first.GlbHash, second.GlbHash);
        Assert.Equal(first.CanonicalAssetHash, second.CanonicalAssetHash);
        Assert.Equal(first.GlbBytes, second.GlbBytes);
        Assert.True(first.TriangleCount > 0);
        GlbWriter.ValidateSingleMesh(first.GlbBytes);
    }

    [Fact]
    public void Geometry_setting_changes_canonical_hash()
    {
        byte[] png = PngCodec.EncodeRgba8(TestGlassesImage());
        AssetRecipe firstRecipe = Recipe();
        AssetRecipe secondRecipe = firstRecipe with
        {
            Geometry = firstRecipe.Geometry with { Depth = firstRecipe.Geometry.Depth + 0.05 },
        };
        Assert.NotEqual(
            AssetForgeGenerator.Generate(png, firstRecipe).CanonicalAssetHash,
            AssetForgeGenerator.Generate(png, secondRecipe).CanonicalAssetHash);
    }

    private static AssetRecipe Recipe() => AssetRecipe.GlassesDefaults() with
    {
        FeatureId = "glasses.test_round",
        ContentId = "cosmetic.glasses.test_round",
        DisplayName = "Test Round",
        Geometry = AssetRecipe.GlassesDefaults().Geometry with
        {
            GeometryResolution = 128,
            SymmetryMode = SymmetryMode.Off,
        },
    };

    private static RgbaImage TestGlassesImage()
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
            int i = (y * size + x) * 4;
            pixels[i] = 239; pixels[i + 1] = 123; pixels[i + 2] = 175; pixels[i + 3] = 255;
        }
    }
}
