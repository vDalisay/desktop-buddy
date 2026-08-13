namespace DesktopBuddy.AssetForge.Core;

/// <summary>
/// Deterministically extends visible authored colour into texture space that does not itself own
/// geometry. Generated glasses geometry owns the silhouette and the lens holes, so colour padding
/// cannot create visible geometry; it only makes every UV sample on the opaque 3D frame resolve to
/// authored colour instead of transparent-black canvas texels.
/// </summary>
public static class TextureBleed
{
    public static RgbaImage Expand(RgbaImage source, int pixels, byte seedAlpha = 128)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (pixels < 0 || pixels > 64) throw new ArgumentOutOfRangeException(nameof(pixels));
        if (pixels == 0) return new RgbaImage(source.Width, source.Height, source.Pixels.ToArray());

        int width = source.Width;
        int height = source.Height;
        byte[] current = source.Pixels.ToArray();
        bool[] filled = CreateSeedMask(current, seedAlpha);

        // Fixed neighbour priority makes equal-distance colour propagation canonical.
        (int X, int Y)[] neighbors = [(-1, 0), (1, 0), (0, -1), (0, 1)];
        for (int iteration = 0; iteration < pixels; iteration++)
        {
            byte[] next = current.ToArray();
            bool[] nextFilled = filled.ToArray();
            bool changed = false;
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int pixel = y * width + x;
                if (filled[pixel]) continue;
                foreach ((int dx, int dy) in neighbors)
                {
                    int nx = x + dx;
                    int ny = y + dy;
                    if (nx < 0 || ny < 0 || nx >= width || ny >= height) continue;
                    int neighborPixel = ny * width + nx;
                    if (!filled[neighborPixel]) continue;
                    CopyOpaque(current, neighborPixel, next, pixel);
                    nextFilled[pixel] = true;
                    changed = true;
                    break;
                }
            }
            current = next;
            filled = nextFilled;
            if (!changed) break;
        }
        return new RgbaImage(width, height, current);
    }

    /// <summary>
    /// Fills every non-authored texel with the nearest authored colour using a deterministic
    /// multi-source Manhattan-distance flood. This is the correct texture contract for semantic
    /// glasses: the mesh contains the holes, while the material is opaque and therefore must never
    /// sample transparent texels whose RGB would otherwise render black in Godot.
    /// </summary>
    public static RgbaImage FillTransparentWithNearestAuthoredColour(RgbaImage source, byte seedAlpha = 128)
    {
        ArgumentNullException.ThrowIfNull(source);
        int width = source.Width;
        int height = source.Height;
        byte[] output = source.Pixels.ToArray();
        bool[] filled = CreateSeedMask(output, seedAlpha);
        var queue = new Queue<int>(filled.Length);

        // Seeds enter row-major order. Together with fixed neighbour order this gives deterministic
        // tie-breaking when two authored colours are the same distance from a destination texel.
        for (int pixel = 0; pixel < filled.Length; pixel++)
            if (filled[pixel]) queue.Enqueue(pixel);

        if (queue.Count == 0)
            throw new InvalidOperationException("Cannot build semantic albedo because no authored colour pixels remain.");

        (int X, int Y)[] neighbors = [(-1, 0), (1, 0), (0, -1), (0, 1)];
        while (queue.Count > 0)
        {
            int sourcePixel = queue.Dequeue();
            int x = sourcePixel % width;
            int y = sourcePixel / width;
            foreach ((int dx, int dy) in neighbors)
            {
                int nx = x + dx;
                int ny = y + dy;
                if (nx < 0 || ny < 0 || nx >= width || ny >= height) continue;
                int targetPixel = ny * width + nx;
                if (filled[targetPixel]) continue;
                CopyOpaque(output, sourcePixel, output, targetPixel);
                filled[targetPixel] = true;
                queue.Enqueue(targetPixel);
            }
        }

        return new RgbaImage(width, height, output);
    }

    private static bool[] CreateSeedMask(byte[] pixels, byte seedAlpha)
    {
        bool[] filled = new bool[pixels.Length / 4];
        for (int pixel = 0; pixel < filled.Length; pixel++)
        {
            int rgba = pixel * 4;
            if (pixels[rgba + 3] < seedAlpha) continue;
            filled[pixel] = true;
            // Geometry, not texture alpha, defines the generated glasses surface.
            pixels[rgba + 3] = 255;
        }
        return filled;
    }

    private static void CopyOpaque(byte[] source, int sourcePixel, byte[] destination, int destinationPixel)
    {
        int sourceRgba = sourcePixel * 4;
        int targetRgba = destinationPixel * 4;
        destination[targetRgba] = source[sourceRgba];
        destination[targetRgba + 1] = source[sourceRgba + 1];
        destination[targetRgba + 2] = source[sourceRgba + 2];
        destination[targetRgba + 3] = 255;
    }
}
