using DesktopBuddy.AssetForge.Core;
using Xunit;

namespace DesktopBuddy.AssetForge.Core.Tests;

public sealed class EnvironmentAdditionalCategoryTests
{
    [Theory]
    [InlineData(AuthoringTemplateCatalog.TableId)]
    [InlineData(AuthoringTemplateCatalog.PlantId)]
    [InlineData(AuthoringTemplateCatalog.PaintingId)]
    public void Remaining_environment_templates_are_deterministic_1024_rgba(string templateId)
    {
        AuthoringTemplateSpec spec = AuthoringTemplateCatalog.Get(templateId);
        Assert.True(spec.Implemented);
        byte[] first = AuthoringTemplateCatalog.CreatePng(templateId);
        byte[] second = AuthoringTemplateCatalog.CreatePng(templateId);
        Assert.Equal(first, second);
        RgbaImage decoded = PngCodec.DecodeRgba8(first);
        Assert.Equal(1024, decoded.Width);
        Assert.Equal(1024, decoded.Height);
    }

    [Fact]
    public void Initial_environment_v1_defaults_validate_and_use_documented_anchors()
    {
        AssetRecipe table = AssetRecipe.TableDefaults();
        AssetRecipe plant = AssetRecipe.PlantDefaults();
        AssetRecipe painting = AssetRecipe.PaintingDefaults();

        Assert.Empty(table.Validate());
        Assert.Empty(plant.Validate());
        Assert.Empty(painting.Validate());
        Assert.Equal(EnvironmentAnchorMode.Floor, table.Environment.Anchor);
        Assert.Equal(EnvironmentAnchorMode.Floor, plant.Environment.Anchor);
        Assert.Equal(EnvironmentAnchorMode.Wall, painting.Environment.Anchor);
        Assert.Equal(EnvironmentRenderMode.WallDecoration, painting.Environment.RenderMode);
        Assert.Equal(ShapeMode.InflatedSolid, plant.Geometry.ShapeMode);
        Assert.Equal(ShapeMode.FlatExtrusion, painting.Geometry.ShapeMode);
    }

    [Theory]
    [MemberData(nameof(CategoryCases))]
    public void Additional_environment_generation_is_deterministic(AssetRecipe recipe, byte[] source)
    {
        GeneratedAsset first = AssetForgeCompiler.Generate(source, recipe);
        GeneratedAsset second = AssetForgeCompiler.Generate(source, recipe);
        Assert.Equal(first.GeometryHash, second.GeometryHash);
        Assert.Equal(first.GlbBytes, second.GlbBytes);
        Assert.Equal(first.CanonicalAssetHash, second.CanonicalAssetHash);
        Assert.True(first.TriangleCount > 0);
    }

    [Fact]
    public void Painting_template_maps_wall_anchor_to_local_origin()
    {
        AssetRecipe painting = AssetRecipe.PaintingDefaults();
        System.Numerics.Vector2 mapped = EnvironmentTemplateMapping.SourcePixelToWorld(
            EnvironmentTemplateSpace.CenterX,
            EnvironmentTemplateSpace.PaintingAnchorY,
            painting);
        Assert.InRange(mapped.X, -.0001f, .0001f);
        Assert.InRange(mapped.Y, -.0001f, .0001f);
    }

    [Fact]
    public void All_initial_environment_categories_export_verify_and_coexist_in_one_catalogue()
    {
        string root = TempRepository();
        try
        {
            var cases = new (AssetRecipe Recipe, byte[] Source)[]
            {
                (Fast(AssetRecipe.TableDefaults(), "decoration.table.ci_table"), TableSource()),
                (Fast(AssetRecipe.PlantDefaults(), "decoration.plant.ci_plant"), PlantSource()),
                (Fast(AssetRecipe.PaintingDefaults(), "decoration.painting.ci_painting"), PaintingSource()),
            };

            foreach ((AssetRecipe recipe, byte[] source) in cases)
            {
                GeneratedAsset generated = AssetForgeCompiler.Generate(source, recipe);
                byte[] thumbnail = EnvironmentThumbnailGenerator.Create(generated.AlbedoPng);
                ExportResult result = RepositoryEnvironmentExporter.Export(root, source, generated, thumbnail);
                Assert.Contains(Path.Combine("authoring", "asset-forge", RepositoryEnvironmentExporter.AuthoringFolder(recipe.Category)), result.AuthoringDirectory, StringComparison.OrdinalIgnoreCase);
                string definition = File.ReadAllText(result.CosmeticDefinitionPath);
                Assert.Contains($"Category = {RepositoryEnvironmentExporter.DecorationCategory(recipe.Category)}", definition, StringComparison.Ordinal);
                Assert.Contains($"AnchorKind = {RepositoryEnvironmentExporter.AnchorKind(recipe.Environment.Anchor)}", definition, StringComparison.Ordinal);
                Assert.DoesNotContain("LightProfile =", definition, StringComparison.Ordinal);
                EnvironmentAssetVerificationResult verification = RepositoryEnvironmentVerifier.Verify(root, recipe.AssetId);
                Assert.True(verification.Passed, string.Join(Environment.NewLine, verification.Diagnostics));
            }

            string aggregate = File.ReadAllText(Path.Combine(root, "data", "environment", "generated_decorations.tres"));
            Assert.Contains("decoration.table.ci_table.tres", aggregate, StringComparison.Ordinal);
            Assert.Contains("decoration.plant.ci_plant.tres", aggregate, StringComparison.Ordinal);
            Assert.Contains("decoration.painting.ci_painting.tres", aggregate, StringComparison.Ordinal);
            EnvironmentRepositoryVerificationResult all = RepositoryEnvironmentVerifier.VerifyAll(root);
            Assert.True(all.Passed, string.Join(Environment.NewLine, all.RepositoryDiagnostics.Concat(all.Assets.SelectMany(static a => a.Diagnostics))));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    public static IEnumerable<object[]> CategoryCases()
    {
        yield return [Fast(AssetRecipe.TableDefaults(), "decoration.table.test"), TableSource()];
        yield return [Fast(AssetRecipe.PlantDefaults(), "decoration.plant.test"), PlantSource()];
        yield return [Fast(AssetRecipe.PaintingDefaults(), "decoration.painting.test"), PaintingSource()];
    }

    private static AssetRecipe Fast(AssetRecipe recipe, string id) => recipe with
    {
        AssetId = id,
        Geometry = recipe.Geometry with
        {
            GeometryResolution = 64,
            RuntimeTextureResolution = 128,
        },
    };

    private static byte[] TableSource()
    {
        byte[] p = Canvas();
        Fill(p, 260, 485, 764, 555, 166, 105, 65);
        Fill(p, 315, 555, 365, EnvironmentTemplateSpace.FloorY, 115, 72, 47);
        Fill(p, 659, 555, 709, EnvironmentTemplateSpace.FloorY, 115, 72, 47);
        return Encode(p);
    }

    private static byte[] PlantSource()
    {
        byte[] p = Canvas();
        Fill(p, 420, 700, 604, EnvironmentTemplateSpace.FloorY, 173, 101, 60);
        Fill(p, 480, 420, 544, 710, 62, 132, 75);
        Disc(p, 430, 440, 130, 72, 158, 88);
        Disc(p, 590, 390, 145, 83, 173, 95);
        Disc(p, 510, 285, 150, 76, 164, 91);
        return Encode(p);
    }

    private static byte[] PaintingSource()
    {
        byte[] p = Canvas();
        Fill(p, 305, 285, 719, 739, 77, 55, 88);
        Fill(p, 325, 305, 699, 719, 218, 181, 125);
        Fill(p, 350, 500, 675, 690, 109, 155, 177);
        return Encode(p);
    }

    private static byte[] Canvas() => new byte[1024 * 1024 * 4];
    private static byte[] Encode(byte[] pixels) => PngCodec.EncodeRgba8(new RgbaImage(1024, 1024, pixels));

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
        pixels[i] = r; pixels[i + 1] = g; pixels[i + 2] = b; pixels[i + 3] = 255;
    }

    private static string TempRepository()
    {
        string root = Path.Combine(Path.GetTempPath(), "desktop-buddy-af-env-v1-" + Guid.NewGuid().ToString("N"));
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
