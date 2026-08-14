namespace DesktopBuddy.AssetForge.Core;

/// <summary>
/// Canonical CPU thumbnail path for generated Environment catalogue items. It crops transparent
/// margins from the generated albedo, adds deterministic breathing room, pads to a square without
/// introducing guide/reference pixels, and outputs exactly 256x256 RGBA PNG.
/// </summary>
public static class EnvironmentThumbnailGenerator
{
    public const int OutputSize = 256;
    private const double PaddingFraction = .10;

    public static byte[] Create(GeneratedAsset asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        if (asset.Recipe.AssetFamily != AssetFamily.Environment)
            throw new ArgumentException("Environment thumbnail generation requires an Environment asset.", nameof(asset));
        return AssetThumbnailCache.GetOrCreate(asset, () => Create(asset.AlbedoPng));
    }

    public static byte[] Create(ReadOnlySpan<byte> albedoPng)
    {
        RgbaImage source = PngCodec.DecodeRgba8(albedoPng);
        Bounds bounds = FindVisibleBounds(source);
        int contentWidth = bounds.MaxX - bounds.MinX + 1;
        int contentHeight = bounds.MaxY - bounds.MinY + 1;
        int contentSide = Math.Max(contentWidth, contentHeight);
        int padding = Math.Max(2, (int)Math.Ceiling(contentSide * PaddingFraction));
        int cropSide = Math.Max(1, contentSide + padding * 2);

        double centerX = (bounds.MinX + bounds.MaxX) * .5;
        double centerY = (bounds.MinY + bounds.MaxY) * .5;
        int cropX = (int)Math.Floor(centerX - (cropSide - 1) * .5);
        int cropY = (int)Math.Floor(centerY - (cropSide - 1) * .5);

        byte[] cropPixels = new byte[cropSide * cropSide * 4];
        for (int y = 0; y < cropSide; y++)
        for (int x = 0; x < cropSide; x++)
        {
            int sx = cropX + x;
            int sy = cropY + y;
            if (sx < 0 || sy < 0 || sx >= source.Width || sy >= source.Height) continue;
            int sourceOffset = (sy * source.Width + sx) * 4;
            int targetOffset = (y * cropSide + x) * 4;
            source.Pixels.AsSpan(sourceOffset, 4).CopyTo(cropPixels.AsSpan(targetOffset, 4));
        }

        // ResizeBox intentionally only supports exact integer-block reductions. Thumbnail crops
        // have arbitrary alpha-derived dimensions, so use a deterministic bilinear sampler rather
        // than altering the crop just to make its side divisible by 256.
        RgbaImage resized = ResizeBilinearSquare(new RgbaImage(cropSide, cropSide, cropPixels), OutputSize);
        return PngCodec.EncodeRgba8(resized);
    }

    private static RgbaImage ResizeBilinearSquare(RgbaImage source, int target)
    {
        if (target <= 0) throw new ArgumentOutOfRangeException(nameof(target));
        if (source.Width <= 0 || source.Height <= 0)
            throw new ArgumentException("Thumbnail source dimensions must be positive.", nameof(source));
        if (source.Width == target && source.Height == target)
            return new RgbaImage(source.Width, source.Height, (byte[])source.Pixels.Clone());

        byte[] pixels = new byte[target * target * 4];
        double scaleX = source.Width / (double)target;
        double scaleY = source.Height / (double)target;
        for (int ty = 0; ty < target; ty++)
        for (int tx = 0; tx < target; tx++)
        {
            double sx = ((tx + .5) * scaleX) - .5;
            double sy = ((ty + .5) * scaleY) - .5;
            double floorX = Math.Floor(sx);
            double floorY = Math.Floor(sy);
            int x0 = Math.Clamp((int)floorX, 0, source.Width - 1);
            int y0 = Math.Clamp((int)floorY, 0, source.Height - 1);
            int x1 = Math.Min(source.Width - 1, x0 + 1);
            int y1 = Math.Min(source.Height - 1, y0 + 1);
            double fx = Math.Clamp(sx - floorX, 0.0, 1.0);
            double fy = Math.Clamp(sy - floorY, 0.0, 1.0);

            int i00 = ((y0 * source.Width) + x0) * 4;
            int i10 = ((y0 * source.Width) + x1) * 4;
            int i01 = ((y1 * source.Width) + x0) * 4;
            int i11 = ((y1 * source.Width) + x1) * 4;
            int output = ((ty * target) + tx) * 4;
            for (int channel = 0; channel < 4; channel++)
            {
                double top = source.Pixels[i00 + channel] +
                    ((source.Pixels[i10 + channel] - source.Pixels[i00 + channel]) * fx);
                double bottom = source.Pixels[i01 + channel] +
                    ((source.Pixels[i11 + channel] - source.Pixels[i01 + channel]) * fx);
                double value = top + ((bottom - top) * fy);
                pixels[output + channel] = (byte)Math.Clamp(
                    (int)Math.Round(value, MidpointRounding.AwayFromZero),
                    0,
                    255);
            }
        }
        return new RgbaImage(target, target, pixels);
    }

    private static Bounds FindVisibleBounds(RgbaImage source)
    {
        int minX = source.Width;
        int minY = source.Height;
        int maxX = -1;
        int maxY = -1;
        for (int y = 0; y < source.Height; y++)
        for (int x = 0; x < source.Width; x++)
        {
            int alpha = source.Pixels[((y * source.Width + x) * 4) + 3];
            if (alpha == 0) continue;
            minX = Math.Min(minX, x);
            minY = Math.Min(minY, y);
            maxX = Math.Max(maxX, x);
            maxY = Math.Max(maxY, y);
        }
        if (maxX < minX || maxY < minY)
            throw new InvalidOperationException("Environment thumbnail source has no visible pixels.");
        return new Bounds(minX, minY, maxX, maxY);
    }

    private readonly record struct Bounds(int MinX, int MinY, int MaxX, int MaxY);
}
