namespace DesktopBuddy.AssetForge.Core;

/// <summary>
/// Pads visible authored colour a small deterministic distance into transparent texture space.
/// Generated glasses geometry owns the silhouette/lens holes, so this padding cannot create new
/// visible geometry; it only prevents bilinear/box filtering at UV seams from mixing frame colour
/// with transparent canvas pixels.
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
        bool[] filled = new bool[width * height];
        for (int pixel = 0; pixel < filled.Length; pixel++)
        {
            int rgba = pixel * 4;
            if (current[rgba + 3] < seedAlpha) continue;
            filled[pixel] = true;
            // The mesh, not texture alpha, defines the glasses boundary. Normalizing the authored
            // paint seed to opaque avoids retaining antialias alpha inside the 3D tube surface.
            current[rgba + 3] = 255;
        }

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
                    int sourceRgba = neighborPixel * 4;
                    int targetRgba = pixel * 4;
                    next[targetRgba] = current[sourceRgba];
                    next[targetRgba + 1] = current[sourceRgba + 1];
                    next[targetRgba + 2] = current[sourceRgba + 2];
                    next[targetRgba + 3] = 255;
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
}
