using DesktopBuddy.AssetForge.Core;
using Xunit;

namespace DesktopBuddy.AssetForge.Core.Tests;

public sealed class AssetFileNamingTests
{
    [Theory]
    [InlineData("Round Lamp", "RoundLampMesh.glb")]
    [InlineData("Oak dining-table", "OakDiningTableMesh.glb")]
    [InlineData("  soft_pink sofa  ", "SoftPinkSofaMesh.glb")]
    [InlineData("Mona Lisa #2", "MonaLisa2Mesh.glb")]
    public void Mesh_file_name_is_readable_pascal_display_name_plus_mesh(string displayName, string expected)
    {
        AssetRecipe recipe = AssetRecipe.LampDefaults() with { DisplayName = displayName };
        Assert.Equal(expected, AssetFileNaming.MeshFileName(recipe));
    }

    [Fact]
    public void Exported_environment_uses_named_mesh_and_removes_legacy_mesh_file()
    {
        string root = Path.Combine(Path.GetTempPath(), "desktop-buddy-af-naming-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "DesktopBuddy.csproj"), "<Project />");
            AssetRecipe recipe = AssetRecipe.TableDefaults() with
            {
                AssetId = "decoration.table.round_table",
                DisplayName = "Round Table",
                Geometry = AssetRecipe.TableDefaults().Geometry with
                {
                    GeometryResolution = 32,
                    RuntimeTextureResolution = 64,
                },
            };
            byte[] pixels = new byte[1024 * 1024 * 4];
            Fill(pixels, 260, 520, 764, 650, 152, 91, 58);
            Fill(pixels, 300, 650, 360, 880, 118, 70, 44);
            Fill(pixels, 664, 650, 724, 880, 118, 70, 44);
            byte[] source = PngCodec.EncodeRgba8(new RgbaImage(1024, 1024, pixels));
            GeneratedAsset generated = AssetForgeCompiler.Generate(source, recipe);

            string assetDirectory = Path.Combine(root, "assets", "generated", "environment", recipe.AssetId);
            Directory.CreateDirectory(assetDirectory);
            File.WriteAllBytes(Path.Combine(assetDirectory, "mesh.glb"), [1, 2, 3]);

            ExportResult result = RepositoryEnvironmentExporter.Export(
                root,
                source,
                generated,
                EnvironmentThumbnailGenerator.Create(generated));

            Assert.True(File.Exists(Path.Combine(result.AssetDirectory, "RoundTableMesh.glb")));
            Assert.False(File.Exists(Path.Combine(result.AssetDirectory, "mesh.glb")));
            Assert.Contains("RoundTableMesh.glb", File.ReadAllText(result.CosmeticDefinitionPath), StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
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
}
