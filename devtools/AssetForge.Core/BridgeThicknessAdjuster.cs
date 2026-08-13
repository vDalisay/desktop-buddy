namespace DesktopBuddy.AssetForge.Core;

/// <summary>
/// Converts the authored bridge-only thickness adjustment into either source-mask morphology for
/// complex silhouettes or a radius adjustment for thin/open rounded bridge paths. Zero is a strict
/// no-op so existing recipes retain byte-identical geometry.
/// </summary>
internal static class BridgeThicknessAdjuster
{
    private const float MinimumPathRadius = 0.003f;

    public static int BiasCells(MaskGrid grid, GeometrySettings settings)
    {
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentNullException.ThrowIfNull(settings);
        if (settings.BridgeThicknessBiasPixels == 0) return 0;

        double scaled = settings.BridgeThicknessBiasPixels * (double)grid.Width / GlassesTemplateSpace.CanvasSize;
        int cells = (int)Math.Round(scaled, MidpointRounding.AwayFromZero);
        return cells == 0 ? Math.Sign(settings.BridgeThicknessBiasPixels) : cells;
    }

    public static float PathRadius(float baseRadius, GeometrySettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        float authoredAdjustment = settings.BridgeThicknessBiasPixels / GlassesTemplateSpace.HeadRadiusPixels;
        return MathF.Max(MinimumPathRadius, baseRadius + authoredAdjustment);
    }

    public static MaskGrid BuildBiasedRegion(
        MaskGrid source,
        int x0,
        int x1,
        int y0,
        int y1,
        int biasCells)
    {
        ArgumentNullException.ThrowIfNull(source);
        var region = new MaskGrid(source.Width, source.Height);
        for (int y = Math.Max(0, y0); y <= Math.Min(source.Height - 1, y1); y++)
        for (int x = Math.Max(0, x0); x <= Math.Min(source.Width - 1, x1); x++)
            region[x, y] = source[x, y];

        for (int i = 0; i < Math.Abs(biasCells); i++)
            region = biasCells > 0 ? Dilate(region) : Erode(region);
        return region;
    }

    private static MaskGrid Dilate(MaskGrid source)
    {
        var result = source.Clone();
        for (int y = 0; y < source.Height; y++)
        for (int x = 0; x < source.Width; x++)
        {
            if (!source[x, y]) continue;
            result[x - 1, y] = true;
            result[x + 1, y] = true;
            result[x, y - 1] = true;
            result[x, y + 1] = true;
        }
        return result;
    }

    private static MaskGrid Erode(MaskGrid source)
    {
        var result = new MaskGrid(source.Width, source.Height);
        for (int y = 0; y < source.Height; y++)
        for (int x = 0; x < source.Width; x++)
            result[x, y] = source[x, y] && source[x - 1, y] && source[x + 1, y] && source[x, y - 1] && source[x, y + 1];
        return result;
    }
}
