using DesktopBuddy.AssetForge.Core;
using Xunit;

namespace DesktopBuddy.AssetForge.Core.Tests;

public sealed class CombinedRepositoryMaintenanceTests
{
    [Fact]
    public void Combined_verify_all_covers_buddy_and_environment_families_together()
    {
        string root = TempRepository();
        try
        {
            AssetRecipe torsoRecipe = FastTorso();
            byte[] torsoSource = TorsoSource();
            GeneratedAsset torso = AssetForgeCompiler.Generate(torsoSource, torsoRecipe);
            RepositoryBuddyReplacementExporter.Export(
                root,
                torsoSource,
                torso,
                AssetThumbnailCache.GetOrCreate(torso, () => EnvironmentThumbnailGenerator.Create(torso.AlbedoPng)));
            GeneratedCosmeticLightingPersistence.Apply(root, torsoRecipe);

            AssetRecipe sofaRecipe = FastSofa();
            byte[] sofaSource = SofaSource();
            GeneratedAsset sofa = AssetForgeCompiler.Generate(sofaSource, sofaRecipe);
            RepositoryEnvironmentExporter.Export(
                root,
                sofaSource,
                sofa,
                EnvironmentThumbnailGenerator.Create(sofa));

            AssetForgeRepositoryVerificationResult combined = RepositoryAssetForgeMaintenance.VerifyAll(root);

            Assert.True(combined.Passed, Diagnostics(combined));
            Assert.Equal(2, combined.AssetCount);
            Assert.Single(combined.BuddyStudio.Assets);
            Assert.Single(combined.Environment.Assets);
            Assert.Equal(torsoRecipe.FeatureId, combined.BuddyStudio.Assets[0].FeatureId);
            Assert.Equal(sofaRecipe.AssetId, combined.Environment.Assets[0].AssetId);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Combined_regenerate_all_repairs_both_families_in_one_operation()
    {
        string root = TempRepository();
        try
        {
            AssetRecipe torsoRecipe = FastTorso();
            byte[] torsoSource = TorsoSource();
            GeneratedAsset torso = AssetForgeCompiler.Generate(torsoSource, torsoRecipe);
            RepositoryBuddyReplacementExporter.Export(
                root,
                torsoSource,
                torso,
                AssetThumbnailCache.GetOrCreate(torso, () => EnvironmentThumbnailGenerator.Create(torso.AlbedoPng)));
            GeneratedCosmeticLightingPersistence.Apply(root, torsoRecipe);

            AssetRecipe sofaRecipe = FastSofa();
            byte[] sofaSource = SofaSource();
            GeneratedAsset sofa = AssetForgeCompiler.Generate(sofaSource, sofaRecipe);
            RepositoryEnvironmentExporter.Export(
                root,
                sofaSource,
                sofa,
                EnvironmentThumbnailGenerator.Create(sofa));

            string torsoMesh = Path.Combine(root, "assets", "generated", "cosmetics", torsoRecipe.FeatureId, AssetFileNaming.MeshFileName(torsoRecipe));
            string sofaMesh = Path.Combine(root, "assets", "generated", "environment", sofaRecipe.AssetId, AssetFileNaming.MeshFileName(sofaRecipe));
            File.WriteAllBytes(torsoMesh, [1, 2, 3]);
            File.WriteAllBytes(sofaMesh, [4, 5, 6]);
            Assert.False(RepositoryAssetForgeMaintenance.VerifyAll(root).Passed);

            AssetForgeRepositoryRegenerationResult regenerated = RepositoryAssetForgeMaintenance.RegenerateAll(root);

            Assert.True(regenerated.Verification.Passed, Diagnostics(regenerated.Verification));
            Assert.Contains(torsoRecipe.FeatureId, regenerated.RegeneratedIds);
            Assert.Contains(sofaRecipe.AssetId, regenerated.RegeneratedIds);
            Assert.Equal(torso.GlbBytes, File.ReadAllBytes(torsoMesh));
            Assert.Equal(sofa.GlbBytes, File.ReadAllBytes(sofaMesh));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static AssetRecipe FastTorso() => AssetRecipe.TorsoShapeDefaults() with
    {
        FeatureId = "top.combined_maintenance",
        ContentId = "cosmetic.top.combined_maintenance",
        DisplayName = "Combined Maintenance Top",
        PriceCredits = 140,
        SortOrder = 9450,
        Geometry = AssetRecipe.TorsoShapeDefaults().Geometry with
        {
            GeometryResolution = 32,
            RuntimeTextureResolution = 64,
        },
    };

    private static AssetRecipe FastSofa() => AssetRecipe.SofaDefaults() with
    {
        AssetId = "decoration.sofa.combined_maintenance",
        DisplayName = "Combined Maintenance Sofa",
        PriceCredits = 180,
        Geometry = AssetRecipe.SofaDefaults().Geometry with
        {
            GeometryResolution = 32,
            RuntimeTextureResolution = 64,
        },
    };

    private static byte[] TorsoSource()
    {
        byte[] pixels = new byte[1024 * 1024 * 4];
        for (int y = 280; y < 790; y++)
        {
            float t = (y - 280) / 510f;
            int half = (int)MathF.Round(130 + (t * 105));
            Fill(pixels, 512 - half, y, 512 + half + 1, y + 1, 92, 184, 154);
        }
        return PngCodec.EncodeRgba8(new RgbaImage(1024, 1024, pixels));
    }

    private static byte[] SofaSource()
    {
        byte[] pixels = new byte[1024 * 1024 * 4];
        Fill(pixels, 260, 560, 764, 830, 169, 116, 188);
        Fill(pixels, 305, 330, 719, 620, 194, 145, 208);
        Fill(pixels, 225, 510, 325, 825, 124, 83, 144);
        Fill(pixels, 699, 510, 799, 825, 124, 83, 144);
        Fill(pixels, 300, 820, 365, 880, 84, 57, 98);
        Fill(pixels, 659, 820, 724, 880, 84, 57, 98);
        return PngCodec.EncodeRgba8(new RgbaImage(1024, 1024, pixels));
    }

    private static void Fill(byte[] pixels, int x0, int y0, int x1, int y1, byte r, byte g, byte b)
    {
        for (int y = Math.Max(0, y0); y < Math.Min(1024, y1); y++)
        for (int x = Math.Max(0, x0); x < Math.Min(1024, x1); x++)
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
        string root = Path.Combine(Path.GetTempPath(), "desktop-buddy-af-combined-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "DesktopBuddy.csproj"), "<Project />");
        return root;
    }

    private static string Diagnostics(AssetForgeRepositoryVerificationResult result) =>
        string.Join("; ",
            result.BuddyStudio.Assets.SelectMany(static asset => asset.Diagnostics)
                .Concat(result.BuddyStudio.RepositoryDiagnostics)
                .Concat(result.Environment.Assets.SelectMany(static asset => asset.Diagnostics))
                .Concat(result.Environment.RepositoryDiagnostics));
}
