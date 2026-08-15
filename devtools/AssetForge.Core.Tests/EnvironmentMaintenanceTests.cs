using DesktopBuddy.AssetForge.Core;
using Xunit;

namespace DesktopBuddy.AssetForge.Core.Tests;

public sealed class EnvironmentMaintenanceTests
{
    [Fact]
    public void Regenerate_sofa_repairs_corrupt_mesh_and_thumbnail()
    {
        string root = TempRepository();
        try
        {
            AssetRecipe recipe = FastSofa();
            byte[] source = SofaSource();
            GeneratedAsset generated = AssetForgeCompiler.Generate(source, recipe);
            RepositoryEnvironmentExporter.Export(
                root,
                source,
                generated,
                EnvironmentThumbnailGenerator.Create(generated.AlbedoPng));

            string assetRoot = Path.Combine(root, "assets", "generated", "environment", recipe.AssetId);
            File.WriteAllBytes(Path.Combine(assetRoot, "mesh.glb"), [1, 2, 3, 4]);
            File.WriteAllBytes(Path.Combine(assetRoot, "thumbnail.png"), [5, 6, 7]);
            Assert.False(RepositoryEnvironmentVerifier.Verify(root, recipe.AssetId).Passed);

            EnvironmentRepositoryRegenerationResult repaired = RepositoryEnvironmentRegenerator.Regenerate(root, recipe.AssetId);
            Assert.True(repaired.Verification.Passed, Diagnostics(repaired.Verification));
            Assert.Contains(recipe.AssetId, repaired.RegeneratedAssetIds);
            RgbaImage thumbnail = PngCodec.DecodeRgba8(File.ReadAllBytes(Path.Combine(assetRoot, "thumbnail.png")));
            Assert.Equal(256, thumbnail.Width);
            Assert.Equal(256, thumbnail.Height);
            GlbWriter.ValidateSingleMesh(File.ReadAllBytes(Path.Combine(assetRoot, "mesh.glb")));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Legacy_lamp_v1_remains_regenerable_after_lamp_v2_becomes_default()
    {
        string root = TempRepository();
        try
        {
            AssetRecipe recipe = FastLampV1();
            byte[] source = LampSource();
            GeneratedAsset generated = AssetForgeCompiler.Generate(source, recipe);
            RepositoryEnvironmentExporter.Export(
                root,
                source,
                generated,
                EnvironmentThumbnailGenerator.Create(generated.AlbedoPng));

            EnvironmentRepositoryRegenerationResult result = RepositoryEnvironmentRegenerator.Regenerate(root, recipe.AssetId);
            Assert.True(result.Verification.Passed, Diagnostics(result.Verification));

            string recipePath = Path.Combine(root, "authoring", "asset-forge", "lamps", "legacy_v1", "recipe.json");
            AssetRecipe persisted = RecipeCodec.Read(File.ReadAllText(recipePath));
            Assert.Equal(1, persisted.PresetVersion);
            Assert.False(EnvironmentTemplateMapping.UsesLiteralTemplateSpace(persisted));
            string definition = File.ReadAllText(Path.Combine(root, "data", "environment", "generated", recipe.AssetId + ".tres"));
            Assert.DoesNotContain("UsesLocalEmitterPosition = true", definition, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Verify_all_reports_orphan_generated_environment_content()
    {
        string root = TempRepository();
        try
        {
            string orphanDefinition = Path.Combine(root, "data", "environment", "generated", "decoration.sofa.orphan.tres");
            Directory.CreateDirectory(Path.GetDirectoryName(orphanDefinition)!);
            File.WriteAllText(orphanDefinition, "orphan");
            string orphanAsset = Path.Combine(root, "assets", "generated", "environment", "decoration.sofa.orphan");
            Directory.CreateDirectory(orphanAsset);

            EnvironmentRepositoryVerificationResult result = RepositoryEnvironmentVerifier.VerifyAll(root);
            Assert.False(result.Passed);
            Assert.Contains(result.RepositoryDiagnostics, line => line.Contains("orphan generated Environment definition", StringComparison.Ordinal));
            Assert.Contains(result.RepositoryDiagnostics, line => line.Contains("orphan generated Environment asset directory", StringComparison.Ordinal));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Combined_maintenance_classifies_all_decoration_ids_as_environment()
    {
        Assert.True(RepositoryAssetForgeMaintenance.IsEnvironmentId("decoration.lamp.any"));
        Assert.True(RepositoryAssetForgeMaintenance.IsEnvironmentId("decoration.sofa.any"));
        Assert.False(RepositoryAssetForgeMaintenance.IsEnvironmentId("glasses.any"));
        Assert.False(RepositoryAssetForgeMaintenance.IsEnvironmentId("top.any"));
    }

    private static AssetRecipe FastSofa() => AssetRecipe.SofaDefaults() with
    {
        AssetId = "decoration.sofa.maintenance",
        Geometry = AssetRecipe.SofaDefaults().Geometry with
        {
            GeometryResolution = 64,
            RuntimeTextureResolution = 128,
        },
    };

    private static AssetRecipe FastLampV1() => AssetRecipe.LampDefaults() with
    {
        PresetVersion = 1,
        AssetId = "decoration.lamp.legacy_v1",
        Geometry = AssetRecipe.LampDefaults().Geometry with
        {
            GeometryResolution = 64,
            RuntimeTextureResolution = 128,
        },
    };

    private static byte[] SofaSource()
    {
        byte[] pixels = new byte[1024 * 1024 * 4];
        Fill(pixels, 245, 560, 779, 820, 170, 116, 188);
        Fill(pixels, 285, 330, 739, 610, 194, 144, 207);
        Fill(pixels, 225, 520, 320, 830, 125, 83, 143);
        Fill(pixels, 704, 520, 799, 830, 125, 83, 143);
        Fill(pixels, 300, 820, 365, 880, 78, 54, 93);
        Fill(pixels, 659, 820, 724, 880, 78, 54, 93);
        return PngCodec.EncodeRgba8(new RgbaImage(1024, 1024, pixels));
    }

    private static byte[] LampSource()
    {
        byte[] pixels = new byte[1024 * 1024 * 4];
        Fill(pixels, 475, 320, 549, 835, 70, 75, 84);
        Fill(pixels, 375, 800, 649, 880, 50, 54, 62);
        Fill(pixels, 330, 190, 694, 405, 238, 180, 76);
        return PngCodec.EncodeRgba8(new RgbaImage(1024, 1024, pixels));
    }

    private static void Fill(byte[] pixels, int x0, int y0, int x1, int y1, byte r, byte g, byte b)
    {
        for (int y = y0; y < y1; y++)
        for (int x = x0; x < x1; x++)
        {
            int i = (y * 1024 + x) * 4;
            pixels[i] = r;
            pixels[i + 1] = g;
            pixels[i + 2] = b;
            pixels[i + 3] = 255;
        }
    }

    private static string TempRepository()
    {
        string root = Path.Combine(Path.GetTempPath(), "desktop-buddy-af-env-maintenance-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "DesktopBuddy.csproj"), "<Project />");
        Directory.CreateDirectory(Path.Combine(root, "data", "environment"));
        File.WriteAllText(Path.Combine(root, "data", "environment", "generated_decorations.tres"),
            "[gd_resource type=\"Resource\" script_class=\"EnvironmentDecorationCatalogueResource\" load_steps=2 format=3]\n\n" +
            "[ext_resource type=\"Script\" path=\"res://src/Environment/EnvironmentDecorationCatalogueResource.cs\" id=\"1\"]\n\n" +
            "[resource]\nscript = ExtResource(\"1\")\nEntries = Array[Resource]([])\n");
        return root;
    }

    private static string Diagnostics(EnvironmentRepositoryVerificationResult result) =>
        string.Join("; ", result.Assets.SelectMany(static asset => asset.Diagnostics).Concat(result.RepositoryDiagnostics));
}
