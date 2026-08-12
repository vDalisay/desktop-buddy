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

    [Fact]
    public void Rounded_extrusion_produces_a_bevel_profile_and_rounded_temples()
    {
        byte[] png = PngCodec.EncodeRgba8(TestGlassesImage());
        AssetRecipe roundedRecipe = Recipe() with
        {
            Geometry = Recipe().Geometry with
            {
                ShapeMode = ShapeMode.RoundedExtrusion,
                Roundness = 0.65,
            },
        };
        AssetRecipe flatRecipe = roundedRecipe with
        {
            Geometry = roundedRecipe.Geometry with
            {
                ShapeMode = ShapeMode.FlatExtrusion,
                Roundness = 0.0,
            },
        };

        GeneratedAsset rounded = AssetForgeGenerator.Generate(png, roundedRecipe);
        GeneratedAsset flat = AssetForgeGenerator.Generate(png, flatRecipe);
        int roundedPositiveDepths = rounded.Mesh.Positions
            .Where(static p => p.Z > 0)
            .Select(static p => MathF.Round(p.Z, 5))
            .Distinct()
            .Count();

        Assert.True(roundedPositiveDepths >= 3, $"Expected a bevel profile, got {roundedPositiveDepths} positive Z levels.");
        Assert.NotEqual(flat.GeometryHash, rounded.GeometryHash);
        Assert.True(rounded.Mesh.Normals.All(static n => float.IsFinite(n.X) && float.IsFinite(n.Y) && float.IsFinite(n.Z)));
    }

    [Fact]
    public void Glasses_authoring_template_is_deterministic_1024_rgba()
    {
        byte[] first = AuthoringTemplateGenerator.CreateGlassesTemplatePng();
        byte[] second = AuthoringTemplateGenerator.CreateGlassesTemplatePng();
        Assert.Equal(first, second);
        RgbaImage image = PngCodec.DecodeRgba8(first);
        Assert.Equal(1024, image.Width);
        Assert.Equal(1024, image.Height);
        Assert.Contains(image.Pixels.Where((_, index) => index % 4 == 3), alpha => alpha > 0);
    }

    [Fact]
    public void Verify_all_rederives_committed_asset_and_detects_drift_without_godot()
    {
        string root = Path.Combine(Path.GetTempPath(), "desktop-buddy-asset-forge-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "DesktopBuddy.csproj"), "<Project />\n");
            byte[] png = PngCodec.EncodeRgba8(TestGlassesImage());
            AssetRecipe recipe = Recipe();
            GeneratedAsset generated = AssetForgeGenerator.Generate(png, recipe);
            RepositoryExporter.ExportGlasses(root, png, generated, generated.AlbedoPng);

            RepositoryVerificationResult clean = RepositoryAssetVerifier.VerifyAll(root);
            Assert.True(clean.Passed, string.Join("; ", clean.Assets.SelectMany(static asset => asset.Diagnostics).Concat(clean.RepositoryDiagnostics)));
            Assert.Single(clean.Assets);

            string meshPath = Path.Combine(root, "assets", "generated", "cosmetics", recipe.FeatureId, "mesh.glb");
            byte[] drifted = File.ReadAllBytes(meshPath);
            drifted[^1] ^= 0x01;
            File.WriteAllBytes(meshPath, drifted);

            RepositoryVerificationResult dirty = RepositoryAssetVerifier.VerifyAll(root);
            Assert.False(dirty.Passed);
            Assert.Contains(dirty.Assets.Single().Diagnostics, static diagnostic => diagnostic.Contains("mesh.glb differs", StringComparison.Ordinal));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
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
            pixels[i] = 239;
            pixels[i + 1] = 123;
            pixels[i + 2] = 175;
            pixels[i + 3] = 255;
        }
    }
}
