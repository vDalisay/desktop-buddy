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
    public void Empty_environment_thumbnail_is_rejected()
    {
        byte[] source = PngCodec.EncodeRgba8(new RgbaImage(64, 64, new byte[64 * 64 * 4]));
        Assert.Throws<InvalidOperationException>(() => EnvironmentThumbnailGenerator.Create(source));
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
