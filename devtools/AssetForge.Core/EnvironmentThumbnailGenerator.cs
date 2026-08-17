using System.Numerics;

namespace DesktopBuddy.AssetForge.Core;

/// <summary>
/// Canonical thumbnail helpers. Environment exports use a deterministic CPU orthographic front
/// render of the final generated mesh so Room Decorator thumbnails resemble the actual model.
/// The raw-albedo overload remains as a maintenance fallback for Buddy content that cannot render
/// its Godot reference composition headlessly.
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
        return AssetThumbnailCache.GetOrCreate(asset, () => CreateFrontView(asset));
    }

    /// <summary>
    /// Software-renders the generated geometry from directly in front of +Z. This deliberately
    /// avoids a GPU dependency while still using final mesh positions, UVs, normals and albedo.
    /// </summary>
    public static byte[] CreateFrontView(GeneratedAsset asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        CanonicalMesh mesh = asset.Mesh;
        if (mesh.Positions.Count == 0 || mesh.TriangleCount == 0)
            throw new InvalidOperationException("Environment thumbnail mesh has no visible geometry.");

        RgbaImage texture = PngCodec.DecodeRgba8(asset.AlbedoPng);
        float minX = float.PositiveInfinity, minY = float.PositiveInfinity;
        float maxX = float.NegativeInfinity, maxY = float.NegativeInfinity;
        foreach (Vector3 p in mesh.Positions)
        {
            minX = MathF.Min(minX, p.X); maxX = MathF.Max(maxX, p.X);
            minY = MathF.Min(minY, p.Y); maxY = MathF.Max(maxY, p.Y);
        }
        float width = MathF.Max(.0001f, maxX - minX);
        float height = MathF.Max(.0001f, maxY - minY);
        double paddingFraction = Math.Clamp(asset.Recipe.Thumbnail.Padding, 0.04, 0.40);
        float paddingPixels = (float)(OutputSize * paddingFraction);
        float available = MathF.Max(8f, OutputSize - (paddingPixels * 2f));
        float scale = available / MathF.Max(width, height);
        float centerX = (minX + maxX) * .5f;
        float centerY = (minY + maxY) * .5f;
        float targetCenter = (OutputSize - 1) * .5f;

        var projected = new Vector3[mesh.Positions.Count];
        for (int i = 0; i < mesh.Positions.Count; i++)
        {
            Vector3 p = mesh.Positions[i];
            projected[i] = new Vector3(
                targetCenter + ((p.X - centerX) * scale),
                targetCenter - ((p.Y - centerY) * scale),
                p.Z);
        }

        byte[] pixels = new byte[OutputSize * OutputSize * 4];
        float[] depth = Enumerable.Repeat(float.NegativeInfinity, OutputSize * OutputSize).ToArray();
        Vector3 light = Vector3.Normalize(new Vector3(-.28f, .35f, 1f));

        for (int triangle = 0; triangle < mesh.Indices.Count; triangle += 3)
        {
            int ia = checked((int)mesh.Indices[triangle]);
            int ib = checked((int)mesh.Indices[triangle + 1]);
            int ic = checked((int)mesh.Indices[triangle + 2]);
            Vector3 a = projected[ia], b = projected[ib], c = projected[ic];
            float area = Edge(a, b, c.X, c.Y);
            if (MathF.Abs(area) <= .00001f) continue;

            int x0 = Math.Clamp((int)MathF.Floor(MathF.Min(a.X, MathF.Min(b.X, c.X))), 0, OutputSize - 1);
            int y0 = Math.Clamp((int)MathF.Floor(MathF.Min(a.Y, MathF.Min(b.Y, c.Y))), 0, OutputSize - 1);
            int x1 = Math.Clamp((int)MathF.Ceiling(MathF.Max(a.X, MathF.Max(b.X, c.X))), 0, OutputSize - 1);
            int y1 = Math.Clamp((int)MathF.Ceiling(MathF.Max(a.Y, MathF.Max(b.Y, c.Y))), 0, OutputSize - 1);

            for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                float px = x + .5f, py = y + .5f;
                float wa = Edge(b, c, px, py) / area;
                float wb = Edge(c, a, px, py) / area;
                float wc = 1f - wa - wb;
                const float epsilon = -.0005f;
                if (wa < epsilon || wb < epsilon || wc < epsilon) continue;

                float z = (a.Z * wa) + (b.Z * wb) + (c.Z * wc);
                int pixelIndex = y * OutputSize + x;
                if (z <= depth[pixelIndex]) continue;

                Vector2 uv = (mesh.Uvs[ia] * wa) + (mesh.Uvs[ib] * wb) + (mesh.Uvs[ic] * wc);
                PixelSample sample = SampleBilinear(texture, uv);
                if (sample.A == 0) continue;

                Vector3 normal = (mesh.Normals[ia] * wa) + (mesh.Normals[ib] * wb) + (mesh.Normals[ic] * wc);
                float diffuse = normal.LengthSquared() <= .000001f
                    ? 1f
                    : MathF.Abs(Vector3.Dot(Vector3.Normalize(normal), light));
                float shade = .76f + (.24f * diffuse);
                int output = pixelIndex * 4;
                pixels[output] = Shade(sample.R, shade);
                pixels[output + 1] = Shade(sample.G, shade);
                pixels[output + 2] = Shade(sample.B, shade);
                pixels[output + 3] = sample.A;
                depth[pixelIndex] = z;
            }
        }

        RgbaImage rendered = new(OutputSize, OutputSize, pixels);
        _ = FindVisibleBounds(rendered);
        return PngCodec.EncodeRgba8(rendered);
    }

    /// <summary>Legacy/item-only alpha crop used only when no generated mesh is available.</summary>
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

        RgbaImage resized = ResizeBilinearSquare(new RgbaImage(cropSide, cropSide, cropPixels), OutputSize);
        return PngCodec.EncodeRgba8(resized);
    }

    private static float Edge(Vector3 a, Vector3 b, float x, float y) =>
        ((x - a.X) * (b.Y - a.Y)) - ((y - a.Y) * (b.X - a.X));

    private static PixelSample SampleBilinear(RgbaImage image, Vector2 uv)
    {
        float sx = Math.Clamp(uv.X, 0f, 1f) * (image.Width - 1);
        float sy = Math.Clamp(uv.Y, 0f, 1f) * (image.Height - 1);
        int x0 = Math.Clamp((int)MathF.Floor(sx), 0, image.Width - 1);
        int y0 = Math.Clamp((int)MathF.Floor(sy), 0, image.Height - 1);
        int x1 = Math.Min(image.Width - 1, x0 + 1);
        int y1 = Math.Min(image.Height - 1, y0 + 1);
        float fx = sx - x0, fy = sy - y0;
        return new PixelSample(
            Bilinear(image, x0, y0, x1, y1, fx, fy, 0),
            Bilinear(image, x0, y0, x1, y1, fx, fy, 1),
            Bilinear(image, x0, y0, x1, y1, fx, fy, 2),
            Bilinear(image, x0, y0, x1, y1, fx, fy, 3));
    }

    private static byte Bilinear(RgbaImage image, int x0, int y0, int x1, int y1, float fx, float fy, int channel)
    {
        int i00 = ((y0 * image.Width) + x0) * 4 + channel;
        int i10 = ((y0 * image.Width) + x1) * 4 + channel;
        int i01 = ((y1 * image.Width) + x0) * 4 + channel;
        int i11 = ((y1 * image.Width) + x1) * 4 + channel;
        float top = image.Pixels[i00] + ((image.Pixels[i10] - image.Pixels[i00]) * fx);
        float bottom = image.Pixels[i01] + ((image.Pixels[i11] - image.Pixels[i01]) * fx);
        return (byte)Math.Clamp((int)MathF.Round(top + ((bottom - top) * fy)), 0, 255);
    }

    private static byte Shade(byte value, float shade) =>
        (byte)Math.Clamp((int)MathF.Round(value * shade), 0, 255);

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

    private readonly record struct PixelSample(byte R, byte G, byte B, byte A);
    private readonly record struct Bounds(int MinX, int MinY, int MaxX, int MaxY);
}
