using DesktopBuddy.AssetForge.Core;
using Xunit;

namespace DesktopBuddy.AssetForge.Core.Tests;

[CollectionDefinition(nameof(AssetForgeDiagnosticsCollection), DisableParallelization = true)]
public sealed class AssetForgeDiagnosticsCollection
{
}

[Collection(nameof(AssetForgeDiagnosticsCollection))]
public sealed class AssetForgeDiagnosticsTests
{
    [Fact]
    public void Compiler_records_generation_cost_and_output_sizes_without_affecting_asset_identity()
    {
        AssetForgeDiagnostics.ResetForTests();
        AssetRecipe recipe = FastSofa("decoration.sofa.metrics");
        GeneratedAsset asset = AssetForgeCompiler.Generate(Source(), recipe);
        AssetForgeGenerationMetrics? metrics = AssetForgeDiagnostics.LastGeneration;

        Assert.NotNull(metrics);
        Assert.Equal(AssetCategory.Sofa, metrics!.Category);
        Assert.Equal(recipe.AssetId, metrics.StableId);
        Assert.True(metrics.ElapsedMilliseconds >= 0);
        Assert.Equal(asset.VertexCount, metrics.VertexCount);
        Assert.Equal(asset.TriangleCount, metrics.TriangleCount);
        Assert.Equal(asset.GlbBytes.Length, metrics.GlbBytes);
        Assert.Equal(asset.AlbedoPng.Length, metrics.AlbedoBytes);
        Assert.True(metrics.RecordedAtUtc <= DateTimeOffset.UtcNow);
    }

    [Fact]
    public void Thumbnail_cache_records_miss_then_hit()
    {
        AssetForgeDiagnostics.ResetForTests();
        AssetThumbnailCache.ClearMemoryCache();
        GeneratedAsset asset = AssetForgeCompiler.Generate(Source(), FastSofa("decoration.sofa.cache_metrics"));

        byte[] first = AssetThumbnailCache.GetOrCreate(
            asset,
            () => EnvironmentThumbnailGenerator.Create(asset.AlbedoPng));
        byte[] second = AssetThumbnailCache.GetOrCreate(
            asset,
            () => throw new InvalidOperationException("Cache producer must not run on hit."));
        AssetForgeThumbnailCacheMetrics metrics = AssetForgeDiagnostics.ThumbnailCache;

        Assert.Equal(first, second);
        Assert.Equal(1, metrics.Misses);
        Assert.Equal(1, metrics.Hits);
        Assert.Equal(2, metrics.Requests);
        Assert.Equal(.5, metrics.HitRate, 8);
    }

    private static AssetRecipe FastSofa(string id) => AssetRecipe.SofaDefaults() with
    {
        AssetId = id,
        Geometry = AssetRecipe.SofaDefaults().Geometry with
        {
            GeometryResolution = 32,
            RuntimeTextureResolution = 64,
        },
    };

    private static byte[] Source()
    {
        byte[] pixels = new byte[1024 * 1024 * 4];
        Fill(pixels, 280, 520, 744, 820, 170, 118, 190);
        Fill(pixels, 330, 350, 694, 600, 202, 150, 214);
        Fill(pixels, 300, 820, 365, 880, 86, 57, 102);
        Fill(pixels, 659, 820, 724, 880, 86, 57, 102);
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
}
