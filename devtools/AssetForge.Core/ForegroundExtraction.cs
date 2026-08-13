namespace DesktopBuddy.AssetForge.Core;

public enum ForegroundExtractionMode
{
    Alpha = 0,
    UniformBackground = 1,
}

public readonly record struct ForegroundDiagnostics(
    ForegroundExtractionMode Mode,
    byte BackgroundR,
    byte BackgroundG,
    byte BackgroundB,
    int ForegroundPixels,
    double ForegroundFraction)
{
    public string Summary => Mode == ForegroundExtractionMode.Alpha
        ? "source alpha"
        : $"auto background rgb({BackgroundR},{BackgroundG},{BackgroundB})";
}

public sealed record ForegroundExtractionResult(
    RgbaImage Image,
    ForegroundDiagnostics Diagnostics);

/// <summary>
/// Converts the author's PNG into the canonical foreground used by the glasses preset.
/// Transparent PNGs use their alpha verbatim. Fully opaque PNGs are also supported when the
/// canvas border is a sufficiently uniform background (for example a white drawing canvas):
/// the border colour is estimated deterministically and removed with a small anti-alias feather.
/// </summary>
public static class ForegroundExtractor
{
    private const int AlphaTransparencyCutoff = 250;
    private const double MinimumTransparentFraction = 0.001;
    private const int BorderSampleStride = 4;
    private const int UniformBorderTolerance = 28;
    private const int BackgroundTolerance = 14;
    private const int BackgroundFeather = 22;

    public static ForegroundExtractionResult Extract(RgbaImage source)
    {
        int pixelCount = checked(source.Width * source.Height);
        int transparent = 0;
        for (int i = 3; i < source.Pixels.Length; i += 4)
            if (source.Pixels[i] < AlphaTransparencyCutoff) transparent++;

        if ((double)transparent / pixelCount >= MinimumTransparentFraction)
        {
            byte[] copy = source.Pixels.ToArray();
            int foreground = 0;
            for (int i = 3; i < copy.Length; i += 4)
                if (copy[i] >= 128) foreground++;
            return new ForegroundExtractionResult(
                new RgbaImage(source.Width, source.Height, copy),
                new ForegroundDiagnostics(
                    ForegroundExtractionMode.Alpha,
                    0,
                    0,
                    0,
                    foreground,
                    (double)foreground / pixelCount));
        }

        (byte r, byte g, byte b, int p90Deviation) = EstimateBorderBackground(source);
        if (p90Deviation > UniformBorderTolerance)
        {
            throw new InvalidOperationException(
                "The PNG is fully opaque and its canvas border is not uniform enough for automatic background removal. " +
                "Use a transparent PNG or keep a single flat background colour around the outer edge of the 1024x1024 canvas.");
        }

        byte[] output = source.Pixels.ToArray();
        int foregroundPixels = 0;
        for (int i = 0; i < output.Length; i += 4)
        {
            int dr = Math.Abs(output[i] - r);
            int dg = Math.Abs(output[i + 1] - g);
            int db = Math.Abs(output[i + 2] - b);
            int distance = Math.Max(dr, Math.Max(dg, db));
            int alpha;
            if (distance <= BackgroundTolerance) alpha = 0;
            else if (distance >= BackgroundTolerance + BackgroundFeather) alpha = 255;
            else alpha = (distance - BackgroundTolerance) * 255 / BackgroundFeather;
            output[i + 3] = (byte)alpha;
            if (alpha >= 128) foregroundPixels++;
        }

        double fraction = (double)foregroundPixels / pixelCount;
        if (foregroundPixels == 0)
            throw new InvalidOperationException("Automatic background removal found no foreground artwork.");
        if (fraction > 0.60)
        {
            throw new InvalidOperationException(
                $"Automatic background removal still classified {fraction:P0} of the canvas as foreground. " +
                "For a glasses source this usually means the background is not clean enough; use transparency or a flatter canvas background.");
        }

        return new ForegroundExtractionResult(
            new RgbaImage(source.Width, source.Height, output),
            new ForegroundDiagnostics(
                ForegroundExtractionMode.UniformBackground,
                r,
                g,
                b,
                foregroundPixels,
                fraction));
    }

    private static (byte R, byte G, byte B, int P90Deviation) EstimateBorderBackground(RgbaImage source)
    {
        var rs = new List<byte>();
        var gs = new List<byte>();
        var bs = new List<byte>();

        void Sample(int x, int y)
        {
            int i = ((y * source.Width) + x) * 4;
            rs.Add(source.Pixels[i]);
            gs.Add(source.Pixels[i + 1]);
            bs.Add(source.Pixels[i + 2]);
        }

        for (int x = 0; x < source.Width; x += BorderSampleStride)
        {
            Sample(x, 0);
            Sample(x, source.Height - 1);
        }
        for (int y = BorderSampleStride; y < source.Height - 1; y += BorderSampleStride)
        {
            Sample(0, y);
            Sample(source.Width - 1, y);
        }

        rs.Sort();
        gs.Sort();
        bs.Sort();
        byte r = rs[rs.Count / 2];
        byte g = gs[gs.Count / 2];
        byte b = bs[bs.Count / 2];

        var deviations = new List<int>(rs.Count);
        // Re-sample in deterministic border order because the channel lists above are sorted independently.
        void Deviation(int x, int y)
        {
            int i = ((y * source.Width) + x) * 4;
            deviations.Add(Math.Max(
                Math.Abs(source.Pixels[i] - r),
                Math.Max(Math.Abs(source.Pixels[i + 1] - g), Math.Abs(source.Pixels[i + 2] - b))));
        }
        for (int x = 0; x < source.Width; x += BorderSampleStride)
        {
            Deviation(x, 0);
            Deviation(x, source.Height - 1);
        }
        for (int y = BorderSampleStride; y < source.Height - 1; y += BorderSampleStride)
        {
            Deviation(0, y);
            Deviation(source.Width - 1, y);
        }
        deviations.Sort();
        int p90 = deviations[(int)Math.Floor((deviations.Count - 1) * 0.90)];
        return (r, g, b, p90);
    }
}
