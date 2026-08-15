using DesktopBuddy.AssetForge.Core;
using Xunit;

namespace DesktopBuddy.AssetForge.Core.Tests;

public sealed class EnvironmentThumbnailTests
{
    [Fact]
    public void Environment_thumbnail_is_deterministic_square_256_rgba()
    {
        byte[] pixels = new byte[512 * 512 * 4];
        Fill(pixels, 512, 180, 90, 330, 440, 80, 170, 210);
        byte[] source = PngCodec.EncodeRgba8(new RgbaImage(512, 512, pixels));

        byte[] first = EnvironmentThumbnailGenerator.Create(source);
        byte[] second = EnvironmentThumbnailGenerator.Create(source);
        Assert.Equal(first, second);
        RgbaImage decoded = PngCodec.DecodeRgba8(first);
        Assert.Equal(256, decoded.Width);
        Assert.Equal(256, decoded.Height);
        Assert.True(VisibleBounds(decoded).Width < 256);
        Assert.True(VisibleBounds(decoded).Height < 256);
    }

    [Fact]
    public void Environment_thumbnail_discards_large_transparent_template_margins()
    {
        byte[] pixels = new byte[512 * 512 * 4];
        Fill(pixels, 512, 420, 400, 490, 500, 220, 130, 70);
        byte[] source = PngCodec.EncodeRgba8(new RgbaImage(512, 512, pixels));
        RgbaImage thumbnail = PngCodec.DecodeRgba8(EnvironmentThumbnailGenerator.Create(source));
        Bounds visible = VisibleBounds(thumbnail);

        Assert.InRange((visible.MinX + visible.MaxX) * .5, 124, 132);
        Assert.InRange((visible.MinY + visible.MaxY) * .5, 124, 132);
    }

    [Fact]
    public void Shared_thumbnail_cache_reuses_canonical_asset_and_returns_defensive_copies()
    {
        AssetThumbnailCache.ClearMemoryCache();
        GeneratedAsset asset = SofaAsset();
        int calls = 0;
        byte[] first = AssetThumbnailCache.GetOrCreate(asset, () =>
        {
            calls++;
            return EnvironmentThumbnailGenerator.Create(asset.AlbedoPng);
        });
        byte originalFirstByte = first[0];
        first[0] ^= 0xff;

        byte[] second = AssetThumbnailCache.GetOrCreate(asset, () =>
            throw new InvalidOperationException("Canonical thumbnail producer should not run twice."));

        Assert.Equal(1, calls);
        Assert.Equal(originalFirstByte, second[0]);
        Assert.NotSame(first, second);
    }

    [Fact]
    public void Thumbnail_recipe_changes_cache_key_without_changing_geometry_or_texture_identity()
    {
        GeneratedAsset asset = SofaAsset();
        GeneratedAsset changed = asset with
        {
            Recipe = asset.Recipe with
            {
                Thumbnail = asset.Recipe.Thumbnail with { YawDegrees = asset.Recipe.Thumbnail.YawDegrees + 5 },
            },
        };

        Assert.Equal(asset.GeometryHash, changed.GeometryHash);
        Assert.Equal(asset.AlbedoHash, changed.AlbedoHash);
        Assert.NotEqual(AssetThumbnailCache.KeyFor(asset), AssetThumbnailCache.KeyFor(changed));
    }

    [Fact]
    public void Cached_thumbnail_producer_must_return_canonical_size()
    {
        AssetThumbnailCache.ClearMemoryCache();
        GeneratedAsset asset = SofaAsset();
        byte[] wrong = PngCodec.EncodeRgba8(new RgbaImage(64, 64, new byte[64 * 64 * 4]));
        Assert.Throws<InvalidOperationException>(() => AssetThumbnailCache.GetOrCreate(asset, () => wrong));
    }

    [Fact]
    public void Empty_environment_thumbnail_is_rejected()
    {
        byte[] source = PngCodec.EncodeRgba8(new RgbaImage(64, 64, new byte[64 * 64 * 4]));
        Assert.Throws<InvalidOperationException>(() => EnvironmentThumbnailGenerator.Create(source));
    }

    private static GeneratedAsset SofaAsset()
    {
        AssetRecipe recipe = AssetRecipe.SofaDefaults() with
        {
            AssetId = "decoration.sofa.thumbnail_test",
            Geometry = AssetRecipe.SofaDefaults().Geometry with
            {
                GeometryResolution = 32,
                RuntimeTextureResolution = 64,
            },
        };
        byte[] pixels = new byte[1024 * 1024 * 4];
        Fill(pixels, 1024, 300, 390, 724, 820, 178, 125, 195);
        Fill(pixels, 1024, 340, 300, 684, 600, 204, 151, 214);
        return AssetForgeCompiler.Generate(
            PngCodec.EncodeRgba8(new RgbaImage(1024, 1024, pixels)),
            recipe);
    }

    private static Bounds VisibleBounds(RgbaImage image)
    {
        int minX = image.Width, minY = image.Height, maxX = -1, maxY = -1;
        for (int y = 0; y < image.Height; y++)
        for (int x = 0; x < image.Width; x++)
        {
            if (image.Pixels[((y * image.Width + x) * 4) + 3] == 0) continue;
            minX = Math.Min(minX, x); minY = Math.Min(minY, y);
            maxX = Math.Max(maxX, x); maxY = Math.Max(maxY, y);
        }
        return new Bounds(minX, minY, maxX, maxY);
    }

    private static void Fill(byte[] pixels, int width, int x0, int y0, int x1, int y1, byte r, byte g, byte b)
    {
        for (int y = y0; y < y1; y++)
        for (int x = x0; x < x1; x++)
        {
            int i = (y * width + x) * 4;
            pixels[i] = r; pixels[i + 1] = g; pixels[i + 2] = b; pixels[i + 3] = 255;
        }
    }

    private readonly record struct Bounds(int MinX, int MinY, int MaxX, int MaxY)
    {
        public int Width => MaxX - MinX + 1;
        public int Height => MaxY - MinY + 1;
    }
}
