using DesktopBuddy.AssetForge.Core;
using Xunit;

namespace DesktopBuddy.AssetForge.Core.Tests;

public sealed class EnvironmentSofaTests
{
    [Fact]
    public void Sofa_template_is_deterministic_and_registered()
    {
        AuthoringTemplateSpec spec = AuthoringTemplateCatalog.Get(AuthoringTemplateCatalog.SofaId);
        Assert.True(spec.Implemented);
        Assert.Contains("Seat-height guide", spec.Guides);
        byte[] first = AuthoringTemplateCatalog.CreatePng(spec.Id);
        byte[] second = AuthoringTemplateCatalog.CreatePng(spec.Id);
        Assert.Equal(first, second);
        RgbaImage decoded = PngCodec.DecodeRgba8(first);
        Assert.Equal(1024, decoded.Width);
        Assert.Equal(1024, decoded.Height);
    }

    [Fact]
    public void Sofa_defaults_use_v2_smoothed_literal_floor_content()
    {
        AssetRecipe recipe = AssetRecipe.SofaDefaults();
        Assert.Equal(AssetFamily.Environment, recipe.AssetFamily);
        Assert.Equal(AssetCategory.Sofa, recipe.Category);
        Assert.Equal("sofa", recipe.PresetId);
        Assert.Equal(2, recipe.PresetVersion);
        Assert.Equal(ShapeMode.InflatedSolid, recipe.Geometry.ShapeMode);
        Assert.True(EnvironmentTemplateMapping.UsesLiteralTemplateSpace(recipe));
        Assert.True(EnvironmentSilhouettePolisher.UsesSmoothedLiteralContract(recipe));
        Assert.Equal(.5, recipe.Environment.PivotX, 8);
        Assert.Equal(1, recipe.Environment.PivotY, 8);
        Assert.False(recipe.Light.Enabled);
        Assert.False(recipe.Light.LightEnabled);
        Assert.Empty(recipe.Validate());
    }

    [Fact]
    public void Sofa_v1_keeps_the_accepted_pre_polisher_geometry_path()
    {
        AssetRecipe defaults = FastSofa();
        AssetRecipe recipe = defaults with
        {
            PresetVersion = 1,
            AssetId = "decoration.sofa.compat_v1",
        };
        byte[] source = SofaSource(260, 350);
        RgbaImage foreground = ForegroundExtractor.Extract(PngCodec.DecodeRgba8(source)).Image;
        MaskGrid mask = MaskGrid.FromImage(foreground, recipe.Geometry);
        CanonicalMesh original = EnvironmentSilhouetteGenerator.Generate(mask, recipe);
        GeneratedAsset compiled = AssetForgeCompiler.Generate(source, recipe);

        Assert.False(EnvironmentSilhouettePolisher.UsesSmoothedLiteralContract(recipe));
        Assert.Equal(original.CanonicalHash(), compiled.GeometryHash);
        Assert.Equal(GlbWriter.Write(original), compiled.GlbBytes);
    }

    [Fact]
    public void Sofa_v2_generation_is_deterministic_and_preserves_template_offset()
    {
        AssetRecipe recipe = FastSofa();
        byte[] source = SofaSource(260, 350);
        GeneratedAsset first = AssetForgeCompiler.Generate(source, recipe);
        GeneratedAsset second = AssetForgeCompiler.Generate(source, recipe);
        GeneratedAsset shifted = AssetForgeCompiler.Generate(SofaSource(340, 400), recipe);

        Assert.Equal(first.GlbBytes, second.GlbBytes);
        Assert.Equal(first.CanonicalAssetHash, second.CanonicalAssetHash);
        Assert.NotEqual(first.GeometryHash, shifted.GeometryHash);

        (float X, float Y) a = Center(first.Mesh);
        (float X, float Y) b = Center(shifted.Mesh);
        float units = EnvironmentTemplateMapping.UnitsPerPixel(recipe);
        float oneMaskCellWorld = (EnvironmentTemplateSpace.CanvasSize / (float)recipe.Geometry.GeometryResolution) * units;
        Assert.InRange(b.X - a.X, 80f * units - oneMaskCellWorld, 80f * units + oneMaskCellWorld);
        Assert.InRange(b.Y - a.Y, -50f * units - oneMaskCellWorld, -50f * units + oneMaskCellWorld);
        Assert.True(EnvironmentGeneratedBounds.Analyze(first.Mesh).Depth > 1f);
    }

    [Fact]
    public void Sofa_export_is_trusted_non_lighting_environment_content()
    {
        string root = TempRepository();
        try
        {
            AssetRecipe recipe = FastSofa();
            byte[] source = SofaSource(230, 330);
            GeneratedAsset generated = AssetForgeCompiler.Generate(source, recipe);
            byte[] thumbnail = EnvironmentThumbnailGenerator.Create(generated);

            ExportResult result = RepositoryEnvironmentExporter.Export(root, source, generated, thumbnail);
            Assert.Contains(Path.Combine("authoring", "asset-forge", "sofas"), result.AuthoringDirectory, StringComparison.OrdinalIgnoreCase);
            string definition = File.ReadAllText(result.CosmeticDefinitionPath);
            Assert.Contains("Category = 1", definition, StringComparison.Ordinal);
            Assert.DoesNotContain("LightProfile =", definition, StringComparison.Ordinal);
            Assert.DoesNotContain("DecorationLightProfileResource.cs", definition, StringComparison.Ordinal);

            EnvironmentAssetVerificationResult verified = RepositoryEnvironmentVerifier.Verify(root, recipe.AssetId);
            Assert.True(verified.Passed, string.Join(Environment.NewLine, verified.Diagnostics));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static AssetRecipe FastSofa() => AssetRecipe.SofaDefaults() with
    {
        AssetId = "decoration.sofa.ci_soft",
        DisplayName = "CI Soft Sofa",
        PriceCredits = 185,
        Geometry = AssetRecipe.SofaDefaults().Geometry with
        {
            GeometryResolution = 64,
            RuntimeTextureResolution = 128,
        },
    };

    private static byte[] SofaSource(int x, int y)
    {
        byte[] pixels = new byte[1024 * 1024 * 4];
        Fill(pixels, x, y + 190, x + 500, y + 410, 164, 114, 184);
        Fill(pixels, x + 40, y, x + 460, y + 230, 188, 139, 205);
        Fill(pixels, x - 20, y + 120, x + 70, y + 370, 128, 86, 147);
        Fill(pixels, x + 430, y + 120, x + 520, y + 370, 128, 86, 147);
        return PngCodec.EncodeRgba8(new RgbaImage(1024, 1024, pixels));
    }

    private static (float X, float Y) Center(CanonicalMesh mesh)
    {
        float minX = mesh.Positions.Min(static p => p.X);
        float maxX = mesh.Positions.Max(static p => p.X);
        float minY = mesh.Positions.Min(static p => p.Y);
        float maxY = mesh.Positions.Max(static p => p.Y);
        return ((minX + maxX) * .5f, (minY + maxY) * .5f);
    }

    private static void Fill(byte[] pixels, int x0, int y0, int x1, int y1, byte r, byte g, byte b)
    {
        for (int yy = Math.Max(0, y0); yy < Math.Min(1024, y1); yy++)
        for (int xx = Math.Max(0, x0); xx < Math.Min(1024, x1); xx++)
        {
            int i = (yy * 1024 + xx) * 4;
            pixels[i] = r;
            pixels[i + 1] = g;
            pixels[i + 2] = b;
            pixels[i + 3] = 255;
        }
    }

    private static string TempRepository()
    {
        string root = Path.Combine(Path.GetTempPath(), "desktop-buddy-af-sofa-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "DesktopBuddy.csproj"), "<Project />");
        Directory.CreateDirectory(Path.Combine(root, "data", "environment"));
        File.WriteAllText(Path.Combine(root, "data", "environment", "generated_decorations.tres"),
            "[gd_resource type=\"Resource\" script_class=\"EnvironmentDecorationCatalogueResource\" load_steps=2 format=3]\n\n" +
            "[ext_resource type=\"Script\" path=\"res://src/Environment/EnvironmentDecorationCatalogueResource.cs\" id=\"1\"]\n\n" +
            "[resource]\nscript = ExtResource(\"1\")\nEntries = Array[Resource]([])\n");
        return root;
    }
}
