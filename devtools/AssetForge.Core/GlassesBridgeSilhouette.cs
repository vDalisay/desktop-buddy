using System.Numerics;

namespace DesktopBuddy.AssetForge.Core;

/// <summary>
/// Preserves complex authored bridge artwork for glasses@2 instead of reducing it to a single
/// center-line. The bridge remains visual-only geometry in the canonical Buddy-head template
/// coordinate system. Empty cells remain empty, so hollow arrows and other interior cut-outs are
/// real mesh holes rather than texture transparency.
/// </summary>
internal static class GlassesBridgeSilhouette
{
    // Bridge art can intentionally extend a little way into the inner lens/frame area (for example
    // arrow stems). Horizontal padding preserves that authored overlap. Vertical placement is not
    // derived from an arbitrary closest lens pair: it is measured from actual foreground in the
    // central lens gap, then padded only slightly.
    private const float RoiPaddingFraction = 0.055f;
    private const float CoreInsetFraction = 0.20f;
    private const float VerticalPaddingFraction = 0.025f;
    private const float RequiredColumnCoverage = 0.55f;
    private const int MinimumComplexRunThickness = 3;

    public static bool TryAdd(
        CanonicalMesh mesh,
        MaskGrid grid,
        RgbaImage foreground,
        Vector2 leftInner,
        Vector2 rightInner,
        GeometrySettings settings)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentNullException.ThrowIfNull(foreground);
        ArgumentNullException.ThrowIfNull(settings);

        if (rightInner.X < leftInner.X)
            (leftInner, rightInner) = (rightInner, leftInner);

        float gapWidth = rightInner.X - leftInner.X;
        if (gapWidth < 2f)
            return false;

        int padding = Math.Max(4, (int)MathF.Round(grid.Width * RoiPaddingFraction));
        int x0 = Math.Clamp((int)MathF.Floor(leftInner.X) - padding, 0, grid.Width - 1);
        int x1 = Math.Clamp((int)MathF.Ceiling(rightInner.X) + padding, 0, grid.Width - 1);
        if (x1 - x0 < 2)
            return false;

        // Measure bridge Y from the middle 60% of the inter-lens gap. That excludes the facing
        // vertical lens bars but still sees the body/arrowheads of an authored bridge. The wider
        // x0..x1 ROI is used only after this vertical bridge corridor is known, so stems that enter
        // the frame area are preserved without copying the whole lens frame.
        int coreX0 = Math.Clamp(
            (int)MathF.Ceiling(leftInner.X + gapWidth * CoreInsetFraction),
            0,
            grid.Width - 1);
        int coreX1 = Math.Clamp(
            (int)MathF.Floor(rightInner.X - gapWidth * CoreInsetFraction),
            0,
            grid.Width - 1);
        if (coreX1 < coreX0)
            return false;

        if (!TryFindAuthoredVerticalSpan(grid, coreX0, coreX1, out int authoredMinY, out int authoredMaxY))
            return false;

        int verticalPadding = Math.Max(6, (int)MathF.Round(grid.Height * VerticalPaddingFraction));
        int y0 = Math.Clamp(authoredMinY - verticalPadding, 0, grid.Height - 1);
        int y1 = Math.Clamp(authoredMaxY + verticalPadding, 0, grid.Height - 1);

        int columnsWithForeground = 0;
        int filledCells = 0;
        int maxVerticalRun = 0;
        for (int x = x0; x <= x1; x++)
        {
            bool columnFilled = false;
            int run = 0;
            for (int y = y0; y <= y1; y++)
            {
                if (!grid[x, y])
                {
                    run = 0;
                    continue;
                }

                columnFilled = true;
                filledCells++;
                run++;
                maxVerticalRun = Math.Max(maxVerticalRun, run);
            }
            if (columnFilled)
                columnsWithForeground++;
        }

        int roiWidth = x1 - x0 + 1;
        int requiredCoverage = Math.Max(2, (int)MathF.Ceiling(roiWidth * RequiredColumnCoverage));
        if (columnsWithForeground < requiredCoverage ||
            filledCells < requiredCoverage * 2 ||
            maxVerticalRun < MinimumComplexRunThickness)
            return false;

        int triangleCountBefore = mesh.TriangleCount;
        float halfDepth = MathF.Max(0.001f, (float)settings.Depth * 0.5f);

        for (int y = y0; y <= y1; y++)
        {
            int x = x0;
            while (x <= x1)
            {
                while (x <= x1 && !grid[x, y]) x++;
                if (x > x1) break;
                int runStart = x;
                while (x <= x1 && grid[x, y]) x++;
                AddFrontBackRun(mesh, grid, foreground, runStart, x, y, halfDepth);
            }
        }

        for (int y = y0; y <= y1; y++)
        for (int x = x0; x <= x1; x++)
        {
            if (!grid[x, y]) continue;
            if (!Filled(grid, x, y - 1)) AddWall(mesh, grid, foreground, x, y, x + 1, y, halfDepth);
            if (!Filled(grid, x + 1, y)) AddWall(mesh, grid, foreground, x + 1, y, x + 1, y + 1, halfDepth);
            if (!Filled(grid, x, y + 1)) AddWall(mesh, grid, foreground, x + 1, y + 1, x, y + 1, halfDepth);
            if (!Filled(grid, x - 1, y)) AddWall(mesh, grid, foreground, x, y + 1, x, y, halfDepth);
        }

        return mesh.TriangleCount > triangleCountBefore;
    }

    private static bool TryFindAuthoredVerticalSpan(
        MaskGrid grid,
        int x0,
        int x1,
        out int minY,
        out int maxY)
    {
        minY = grid.Height;
        maxY = -1;
        for (int x = x0; x <= x1; x++)
        for (int y = 0; y < grid.Height; y++)
        {
            if (!grid[x, y]) continue;
            minY = Math.Min(minY, y);
            maxY = Math.Max(maxY, y);
        }
        return maxY >= minY;
    }

    private static bool Filled(MaskGrid grid, int x, int y) =>
        x >= 0 && y >= 0 && x < grid.Width && y < grid.Height && grid[x, y];

    private static void AddFrontBackRun(
        CanonicalMesh mesh,
        MaskGrid grid,
        RgbaImage foreground,
        int x0,
        int x1Exclusive,
        int y,
        float halfDepth)
    {
        Vector2 g0 = new(x0, y);
        Vector2 g1 = new(x1Exclusive, y);
        Vector2 g2 = new(x1Exclusive, y + 1);
        Vector2 g3 = new(x0, y + 1);
        Vector2 uv0 = Uv(grid, foreground, g0);
        Vector2 uv1 = Uv(grid, foreground, g1);
        Vector2 uv2 = Uv(grid, foreground, g2);
        Vector2 uv3 = Uv(grid, foreground, g3);

        uint f0 = mesh.AddVertex(World(grid, g0, halfDepth), uv0);
        uint f1 = mesh.AddVertex(World(grid, g1, halfDepth), uv1);
        uint f2 = mesh.AddVertex(World(grid, g2, halfDepth), uv2);
        uint f3 = mesh.AddVertex(World(grid, g3, halfDepth), uv3);
        mesh.AddTriangle(f0, f2, f1);
        mesh.AddTriangle(f0, f3, f2);

        uint b0 = mesh.AddVertex(World(grid, g0, -halfDepth), uv0);
        uint b1 = mesh.AddVertex(World(grid, g1, -halfDepth), uv1);
        uint b2 = mesh.AddVertex(World(grid, g2, -halfDepth), uv2);
        uint b3 = mesh.AddVertex(World(grid, g3, -halfDepth), uv3);
        mesh.AddTriangle(b0, b1, b2);
        mesh.AddTriangle(b0, b2, b3);
    }

    private static void AddWall(
        CanonicalMesh mesh,
        MaskGrid grid,
        RgbaImage foreground,
        float ax,
        float ay,
        float bx,
        float by,
        float halfDepth)
    {
        Vector2 a = new(ax, ay);
        Vector2 b = new(bx, by);
        Vector2 uvA = Uv(grid, foreground, a);
        Vector2 uvB = Uv(grid, foreground, b);
        uint frontA = mesh.AddVertex(World(grid, a, halfDepth), uvA);
        uint frontB = mesh.AddVertex(World(grid, b, halfDepth), uvB);
        uint backB = mesh.AddVertex(World(grid, b, -halfDepth), uvB);
        uint backA = mesh.AddVertex(World(grid, a, -halfDepth), uvA);
        mesh.AddTriangle(frontA, frontB, backB);
        mesh.AddTriangle(frontA, backB, backA);
    }

    private static Vector3 World(MaskGrid grid, Vector2 point, float z)
    {
        Vector2 xy = GlassesTemplateSpace.GridPointToHead(grid, point);
        return new Vector3(xy, z);
    }

    private static Vector2 Uv(MaskGrid grid, RgbaImage foreground, Vector2 point)
    {
        Vector2 source = GlassesTemplateSpace.GridPointToSourcePixel(grid, point);
        return new Vector2(
            Math.Clamp(source.X / foreground.Width, 0f, 1f),
            Math.Clamp(source.Y / foreground.Height, 0f, 1f));
    }
}
