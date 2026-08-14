using DesktopBuddy.AssetForge.Core;
using Xunit;

namespace DesktopBuddy.AssetForge.Core.Tests;

public sealed class EnvironmentLampTests
{
    [Fact]
    public void Lamp_template_is_deterministic_1024_rgba()
    {
        byte[] a = EnvironmentTemplateGenerator.CreateLampPng();
        byte[] b = EnvironmentTemplateGenerator.CreateLampPng();
        Assert.Equal(a, b);
        RgbaImage decoded = PngCodec.DecodeRgba8(a);
        Assert.Equal(1024, decoded.Width);
        Assert.Equal(1024, decoded.Height);
    }

    [Fact]
    public void Lamp_generation_is_floor_pivoted_and_deterministic()
    {
        AssetRecipe recipe = FastLamp();
        byte[] source = LampSource();
        GeneratedAsset a = AssetForgeCompiler.Generate(source, recipe);
        GeneratedAsset b = AssetForgeCompiler.Generate(source, recipe);
        EnvironmentGeneratedBounds bounds = EnvironmentGeneratedBounds.Analyze(a.Mesh);

        Assert.Equal(a.GeometryHash, b.GeometryHash);
        Assert.Equal(a.GlbBytes, b.GlbBytes);
        Assert.InRange(bounds.Height, 149.0f, 151.0f);
        Assert.InRange(a.Mesh.Positions.Max(static p => p.Y), -.001f, .001f);
        Assert.True(a.Mesh.Positions.Min(static p => p.Y) < -149f);
        Assert.True(bounds.Width > 40f);
        Assert.True(bounds.Depth > 1f);
    }

    [Fact]
    public void Lamp_export_round_trips_through_environment_verifier()
    {
        string root = TempRepository();
        try
        {
            AssetRecipe recipe = FastLamp();
            byte[] source = LampSource();
            GeneratedAsset generated = AssetForgeCompiler.Generate(source, recipe);
            byte[] thumbnail = PngCodec.EncodeRgba8(PngCodec.ResizeBox(PngCodec.DecodeRgba8(source), 256));

            ExportResult result = RepositoryEnvironmentExporter.Export(root, source, generated, thumbnail);
            Assert.True(File.Exists(Path.Combine(result.AssetDirectory, "mesh.glb")));
            Assert.True(File.Exists(result.CosmeticDefinitionPath));
            Assert.Contains("authoring", result.AuthoringDirectory, StringComparison.OrdinalIgnoreCase);

            EnvironmentAssetVerificationResult verified = RepositoryEnvironmentVerifier.Verify(root, recipe.AssetId);
            Assert.True(verified.Passed, string.Join(Environment.NewLine, verified.Diagnostics));
            EnvironmentRepositoryVerificationResult all = RepositoryEnvironmentVerifier.VerifyAll(root);
            Assert.True(all.Passed, string.Join(Environment.NewLine, all.RepositoryDiagnostics));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Buddy_verify_all_ignores_environment_recipes()
    {
        string root = TempRepository();
        try
        {
            AssetRecipe recipe = FastLamp();
            byte[] source = LampSource();
            GeneratedAsset generated = AssetForgeCompiler.Generate(source, recipe);
            RepositoryEnvironmentExporter.Export(root, source, generated, source);
            RepositoryVerificationResult buddy = RepositoryAssetVerifier.VerifyAll(root);
            Assert.Empty(buddy.Assets);
            Assert.Empty(buddy.RepositoryDiagnostics);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static AssetRecipe FastLamp() => AssetRecipe.LampDefaults() with
    {
        AssetId = "decoration.lamp.ci_round",
        DisplayName = "CI Round Lamp",
        PriceCredits = 135,
        Geometry = AssetRecipe.LampDefaults().Geometry with
        {
            GeometryResolution = 64,
            RuntimeTextureResolution = 128,
            SurfaceSmoothness = .8,
        },
        Environment = AssetRecipe.LampDefaults().Environment with { LogicalHeight = 150 },
        Light = AssetRecipe.LampDefaults().Light with
        {
            EmissionStrength = 1.4,
            LightEnabled = true,
            Brightness = 1.1,
            Range = 170,
            EmitterX = .5,
            EmitterY = .25,
        },
    };

    private static byte[] LampSource()
    {
        byte[] pixels = new byte[1024 * 1024 * 4];
        Fill(pixels, 472, 320, 552, 835, 65, 72, 82);
        Fill(pixels, 372, 780, 652, 880, 45, 50, 58);
        for (int y = 180; y < 400; y++)
        for (int x = 320; x < 704; x++)
        {
            double nx = (x - 512) / 192.0;
            double ny = (y - 290) / 110.0;
            if (nx * nx + ny * ny <= 1.0) Pixel(pixels, x, y, 245, 184, 72);
        }
        return PngCodec.EncodeRgba8(new RgbaImage(1024, 1024, pixels));
    }

    private static void Fill(byte[] pixels, int x0, int y0, int x1, int y1, byte r, byte g, byte b)
    {
        for (int y = y0; y < y1; y++)
        for (int x = x0; x < x1; x++) Pixel(pixels, x, y, r, g, b);
    }

    private static void Pixel(byte[] pixels, int x, int y, byte r, byte g, byte b)
    {
        int i = (y * 1024 + x) * 4;
        pixels[i] = r; pixels[i + 1] = g; pixels[i + 2] = b; pixels[i + 3] = 255;
    }

    private static string TempRepository()
    {
        string root = Path.Combine(Path.GetTempPath(), "desktop-buddy-af-env-" + Guid.NewGuid().ToString("N"));
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
