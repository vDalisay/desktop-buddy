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
        Assert.Contains("\"frameThickness\"", json, StringComparison.Ordinal);
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
    public void Opaque_white_canvas_is_auto_removed_instead_of_becoming_a_slab()
    {
        AssetRecipe recipe = Recipe();
        byte[] transparentPng = PngCodec.EncodeRgba8(TestGlassesImage());
        byte[] opaquePng = PngCodec.EncodeRgba8(TestGlassesImage(opaqueWhiteBackground: true));

        GeneratedAsset transparent = AssetForgeGenerator.Generate(transparentPng, recipe);
        GeneratedAsset opaque = AssetForgeGenerator.Generate(opaquePng, recipe);

        Assert.Equal(ForegroundExtractionMode.UniformBackground, opaque.Foreground.Mode);
        Assert.Equal((byte)255, opaque.Foreground.BackgroundR);
        Assert.Equal((byte)255, opaque.Foreground.BackgroundG);
        Assert.Equal((byte)255, opaque.Foreground.BackgroundB);
        Assert.Equal(1, opaque.Diagnostics.Components);
        Assert.Equal(2, opaque.Diagnostics.Holes);
        Assert.True(opaque.UsedGlassesTemplate);
        Assert.Equal(transparent.GeometryHash, opaque.GeometryHash);

        // Semantic glasses use geometry for their lens holes. Their final opaque material texture
        // therefore deliberately fills non-authored canvas texels from the nearest authored colour
        // instead of retaining transparent-black texels that Godot would render as black.
        RgbaImage albedo = PngCodec.DecodeRgba8(opaque.AlbedoPng);
        Assert.Equal((byte)255, albedo.Alpha(0, 0));
        Assert.Equal((byte)239, albedo.Pixels[0]);
        Assert.Equal((byte)123, albedo.Pixels[1]);
        Assert.Equal((byte)175, albedo.Pixels[2]);
    }

    [Fact]
    public void Rounded_glasses_use_semantic_template_fit_from_two_lens_openings()
    {
        GeneratedAsset generated = AssetForgeGenerator.Generate(
            PngCodec.EncodeRgba8(TestGlassesImage(opaqueWhiteBackground: true)),
            Recipe());

        Assert.True(generated.UsedGlassesTemplate);
        Assert.Equal(2, generated.Diagnostics.Holes);
        float minX = generated.Mesh.Positions.Min(static p => p.X);
        float maxX = generated.Mesh.Positions.Max(static p => p.X);
        float minY = generated.Mesh.Positions.Min(static p => p.Y);
        float maxY = generated.Mesh.Positions.Max(static p => p.Y);
        float width = maxX - minX;
        float height = maxY - minY;
        Assert.InRange(width, 1.25f, 2.10f);
        Assert.InRange(height, 0.40f, 1.15f);
        Assert.True(width > height, $"Expected glasses proportions, got {width:0.###} x {height:0.###}.");
    }

    [Fact]
    public void Frame_thickness_is_a_real_template_parameter_not_source_stroke_width()
    {
        byte[] png = PngCodec.EncodeRgba8(TestGlassesImage(opaqueWhiteBackground: true));
        AssetRecipe thinRecipe = Recipe() with
        {
            Geometry = Recipe().Geometry with { FrameThickness = 0.035 },
        };
        AssetRecipe thickRecipe = Recipe() with
        {
            Geometry = Recipe().Geometry with { FrameThickness = 0.11 },
        };

        GeneratedAsset thin = AssetForgeGenerator.Generate(png, thinRecipe);
        GeneratedAsset thick = AssetForgeGenerator.Generate(png, thickRecipe);
        Assert.True(thin.UsedGlassesTemplate && thick.UsedGlassesTemplate);
        Assert.NotEqual(thin.GeometryHash, thick.GeometryHash);

        // Temples intentionally extend farther sideways than either lens, so total X width is not
        // a useful frame-thickness measurement. The lens/frame vertical envelope is.
        float thinHeight = thin.Mesh.Positions.Max(static p => p.Y) - thin.Mesh.Positions.Min(static p => p.Y);
        float thickHeight = thick.Mesh.Positions.Max(static p => p.Y) - thick.Mesh.Positions.Min(static p => p.Y);
        Assert.True(thickHeight > thinHeight + 0.03f,
            $"Expected thicker template to expand the frame envelope: thin={thinHeight}, thick={thickHeight}");
    }

    [Fact]
    public void Diamond_lens_drawing_keeps_two_distinct_lens_shapes_and_uses_template()
    {
        byte[] png = PngCodec.EncodeRgba8(TestDiamondGlassesImage());
        GeneratedAsset generated = AssetForgeGenerator.Generate(png, Recipe());
        Assert.True(generated.UsedGlassesTemplate);
        Assert.Equal(2, generated.Diagnostics.Holes);
        Assert.True(generated.VertexCount > 100);
        Assert.True(generated.TriangleCount > 100);
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
    public void Rounded_template_produces_round_cross_section_and_3d_temples()
    {
        byte[] png = PngCodec.EncodeRgba8(TestGlassesImage());
        AssetRecipe roundedRecipe = Recipe() with
        {
            Geometry = Recipe().Geometry with
            {
                ShapeMode = ShapeMode.RoundedExtrusion,
                Roundness = 0.85,
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
        int roundedDepths = rounded.Mesh.Positions
            .Select(static p => MathF.Round(p.Z, 5))
            .Distinct()
            .Count();

        Assert.True(rounded.UsedGlassesTemplate);
        Assert.False(flat.UsedGlassesTemplate);
        Assert.True(roundedDepths >= 6, $"Expected a rounded tube cross-section, got {roundedDepths} Z levels.");
        Assert.NotEqual(flat.GeometryHash, rounded.GeometryHash);
        Assert.True(rounded.Mesh.Positions.Min(static p => p.Z) < -0.20f, "Template should include temples extending behind the frame plane.");
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

    [Fact]
    public void Delete_removes_every_exported_file_and_empties_the_catalogues()
    {
        string root = Path.Combine(Path.GetTempPath(), "desktop-buddy-asset-forge-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "DesktopBuddy.csproj"), "<Project />\n");
            byte[] png = PngCodec.EncodeRgba8(TestGlassesImage());
            AssetRecipe recipe = Recipe();
            RepositoryExporter.ExportGlasses(root, png, AssetForgeGenerator.Generate(png, recipe), png);
            Assert.Single(RepositoryExporter.ListExported(root));

            RepositoryExporter.Delete(root, recipe.FeatureId);

            Assert.Empty(RepositoryExporter.ListExported(root));
            Assert.False(Directory.Exists(Path.Combine(root, "assets", "generated", "cosmetics", recipe.FeatureId)));
            Assert.False(File.Exists(Path.Combine(root, "data", "cosmetics", "generated", recipe.FeatureId + ".tres")));
            Assert.False(File.Exists(Path.Combine(root, "data", "catalogue", "generated", recipe.ContentId.Replace('.', '_') + ".tres")));
            string cosmeticCatalogue = File.ReadAllText(Path.Combine(root, "data", "cosmetics", "generated", "catalogue.tres"));
            string saleCatalogue = File.ReadAllText(Path.Combine(root, "data", "catalogue", "generated_cosmetics.tres"));
            Assert.Contains("Entries = Array[Resource]([])", cosmeticCatalogue, StringComparison.Ordinal);
            Assert.Contains("Entries = Array[Resource]([])", saleCatalogue, StringComparison.Ordinal);
            Assert.DoesNotContain("\r\n", cosmeticCatalogue, StringComparison.Ordinal);
            Assert.DoesNotContain("\r\n", saleCatalogue, StringComparison.Ordinal);
            Assert.True(RepositoryAssetVerifier.VerifyAll(root).Passed);
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
            GeometryResolution = 256,
            RuntimeTextureResolution = 256,
            SymmetryMode = SymmetryMode.Off,
        },
    };

    private static RgbaImage TestGlassesImage(bool opaqueWhiteBackground = false)
    {
        const int size = 1024;
        byte[] pixels = NewCanvas(opaqueWhiteBackground);
        DrawFrame(pixels, 215, 390, 475, 620, 38);
        DrawFrame(pixels, 549, 390, 809, 620, 38);
        FillPink(pixels, 470, 485, 554, 525);
        return new RgbaImage(size, size, pixels);
    }

    private static RgbaImage TestDiamondGlassesImage()
    {
        const int size = 1024;
        byte[] pixels = NewCanvas(opaqueWhite: true);
        const int brush = 18;
        DrawThickLine(pixels, 300, 395, 425, 265, brush);
        DrawThickLine(pixels, 425, 265, 490, 395, brush);
        DrawThickLine(pixels, 490, 395, 375, 535, brush);
        DrawThickLine(pixels, 375, 535, 300, 395, brush);
        DrawThickLine(pixels, 640, 400, 735, 275, brush);
        DrawThickLine(pixels, 735, 275, 805, 415, brush);
        DrawThickLine(pixels, 805, 415, 710, 545, brush);
        DrawThickLine(pixels, 710, 545, 640, 400, brush);
        DrawThickLine(pixels, 485, 395, 645, 400, brush);
        return new RgbaImage(size, size, pixels);
    }

    private static byte[] NewCanvas(bool opaqueWhite)
    {
        const int size = 1024;
        byte[] pixels = new byte[size * size * 4];
        if (!opaqueWhite) return pixels;
        for (int i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = 255;
            pixels[i + 1] = 255;
            pixels[i + 2] = 255;
            pixels[i + 3] = 255;
        }
        return pixels;
    }

    private static void DrawFrame(byte[] pixels, int x0, int y0, int x1, int y1, int thickness)
    {
        FillPink(pixels, x0, y0, x1, y0 + thickness);
        FillPink(pixels, x0, y1 - thickness, x1, y1);
        FillPink(pixels, x0, y0, x0 + thickness, y1);
        FillPink(pixels, x1 - thickness, y0, x1, y1);
    }

    private static void DrawThickLine(byte[] pixels, int x0, int y0, int x1, int y1, int radius)
    {
        int dx = Math.Abs(x1 - x0);
        int dy = Math.Abs(y1 - y0);
        int steps = Math.Max(dx, dy);
        for (int step = 0; step <= steps; step++)
        {
            double t = steps == 0 ? 0 : (double)step / steps;
            int cx = (int)Math.Round(x0 + (x1 - x0) * t);
            int cy = (int)Math.Round(y0 + (y1 - y0) * t);
            FillPink(pixels, cx - radius, cy - radius, cx + radius + 1, cy + radius + 1);
        }
    }

    private static void FillPink(byte[] pixels, int x0, int y0, int x1, int y1)
    {
        const int size = 1024;
        x0 = Math.Clamp(x0, 0, size);
        x1 = Math.Clamp(x1, 0, size);
        y0 = Math.Clamp(y0, 0, size);
        y1 = Math.Clamp(y1, 0, size);
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
