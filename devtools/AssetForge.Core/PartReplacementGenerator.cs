using System.Numerics;

namespace DesktopBuddy.AssetForge.Core;

/// <summary>
/// Literal template-space generator for Buddy part replacements. Unlike glasses@1, this never
/// auto-centres or auto-fits source art: the 1024x1024 guide and these mapping constants are the
/// placement contract. Result coordinates are expressed in target-part-radius units; runtime scales
/// them by the trusted torso/foot radius without mutating any physics geometry.
/// </summary>
public static class PartReplacementGenerator
{
    private const float DiagonalWeight = 1.41421356237f;
    private static readonly (int X, int Y)[] Neighbors = [(1, 0), (-1, 0), (0, 1), (0, -1)];
    private static readonly (int X, int Y, float Weight)[] ChamferNeighbors =
    [
        (-1, 0, 1f), (0, -1, 1f), (-1, -1, DiagonalWeight), (1, -1, DiagonalWeight),
        (1, 0, 1f), (0, 1, 1f), (1, 1, DiagonalWeight), (-1, 1, DiagonalWeight),
    ];

    public static CanonicalMesh Generate(MaskGrid grid, GeometrySettings settings, AssetCategory category)
    {
        if (category is not (AssetCategory.TorsoShape or AssetCategory.FootShape))
            throw new ArgumentOutOfRangeException(nameof(category), category, "Only torso and foot replacement categories are supported.");
        if (grid.FilledCount == 0)
            throw new InvalidOperationException("Source contains no visible replacement geometry after thresholding.");

        var mesh = new CanonicalMesh();
        var vertices = new Dictionary<int, uint>();
        bool legacyDepthField = settings.SurfaceSmoothness <= 0.000001;
        int[] legacyInward = legacyDepthField ? BuildInwardDistance(grid) : Array.Empty<int>();
        int legacyMaximumInset = legacyDepthField
            ? legacyInward.Where(static value => value != int.MaxValue).DefaultIfEmpty(0).Max()
            : 0;
        float[] smoothInward = legacyDepthField ? Array.Empty<float>() : BuildSmoothInwardDistance(grid, settings.SurfaceSmoothness);
        float smoothMaximumInset = legacyDepthField ? 0f : smoothInward.DefaultIfEmpty(0f).Max();
        float sourcePixelsPerCell = PartReplacementTemplateSpace.CanvasSize / (float)grid.Width;
        float halfDepth = (float)settings.Depth * 0.5f;

        uint Vertex(int vx, int vy, bool front)
        {
            int key = (((vy * (grid.Width + 1)) + vx) << 1) | (front ? 1 : 0);
            if (vertices.TryGetValue(key, out uint existing)) return existing;
            float pixelX = vx * sourcePixelsPerCell;
            float pixelY = vy * sourcePixelsPerCell;
            Vector2 mapped = PartReplacementTemplateSpace.MapPixel(category, pixelX, pixelY);
            float surfaceHalf = legacyDepthField
                ? LegacySurfaceHalfDepth(grid, legacyInward, legacyMaximumInset, vx, vy, halfDepth, settings)
                : SmoothSurfaceHalfDepth(grid, smoothInward, smoothMaximumInset, vx, vy, halfDepth, settings);
            uint created = mesh.AddVertex(
                new Vector3(mapped.X, mapped.Y, front ? surfaceHalf : -surfaceHalf),
                new Vector2((float)vx / grid.Width, (float)vy / grid.Height));
            vertices.Add(key, created);
            return created;
        }

        for (int y = 0; y < grid.Height; y++)
        for (int x = 0; x < grid.Width; x++)
        {
            if (!grid[x, y]) continue;
            uint fTl = Vertex(x, y, true); uint fTr = Vertex(x + 1, y, true); uint fBl = Vertex(x, y + 1, true); uint fBr = Vertex(x + 1, y + 1, true);
            uint bTl = Vertex(x, y, false); uint bTr = Vertex(x + 1, y, false); uint bBl = Vertex(x, y + 1, false); uint bBr = Vertex(x + 1, y + 1, false);
            mesh.AddTriangle(fBl, fBr, fTr); mesh.AddTriangle(fBl, fTr, fTl);
            mesh.AddTriangle(bBl, bTr, bBr); mesh.AddTriangle(bBl, bTl, bTr);
            if (!grid[x - 1, y]) { mesh.AddTriangle(fTl, bBl, fBl); mesh.AddTriangle(fTl, bTl, bBl); }
            if (!grid[x + 1, y]) { mesh.AddTriangle(fTr, fBr, bBr); mesh.AddTriangle(fTr, bBr, bTr); }
            if (!grid[x, y - 1]) { mesh.AddTriangle(fTl, fTr, bTr); mesh.AddTriangle(fTl, bTr, bTl); }
            if (!grid[x, y + 1]) { mesh.AddTriangle(fBl, bBl, bBr); mesh.AddTriangle(fBl, bBr, fBr); }
        }
        mesh.RecalculateNormals();
        return mesh;
    }

    private static int[] BuildInwardDistance(MaskGrid grid)
    {
        int[] distance = Enumerable.Repeat(int.MaxValue, grid.Width * grid.Height).ToArray();
        var queue = new Queue<(int X, int Y)>();
        for (int y = 0; y < grid.Height; y++)
        for (int x = 0; x < grid.Width; x++)
        {
            if (!grid[x, y] || !Neighbors.Any(n => !grid[x + n.X, y + n.Y])) continue;
            distance[y * grid.Width + x] = 0;
            queue.Enqueue((x, y));
        }
        while (queue.Count > 0)
        {
            (int x, int y) = queue.Dequeue();
            int next = distance[y * grid.Width + x] + 1;
            foreach ((int dx, int dy) in Neighbors)
            {
                int nx = x + dx, ny = y + dy;
                if (!grid[nx, ny]) continue;
                int index = ny * grid.Width + nx;
                if (next >= distance[index]) continue;
                distance[index] = next;
                queue.Enqueue((nx, ny));
            }
        }
        return distance;
    }

    /// <summary>
    /// Deterministic chamfer-distance + relaxation field. The old generator used four-neighbour
    /// Manhattan rings, which became visible as horizontal/vertical ridges under Buddy lighting.
    /// Diagonal distance plus several bounded relaxation passes produces a continuous-looking
    /// height field while keeping the authored alpha silhouette and holes unchanged.
    /// </summary>
    private static float[] BuildSmoothInwardDistance(MaskGrid grid, double smoothness)
    {
        const float infinity = 1_000_000f;
        float[] distance = new float[grid.Width * grid.Height];
        for (int y = 0; y < grid.Height; y++)
        for (int x = 0; x < grid.Width; x++)
        {
            int index = y * grid.Width + x;
            if (!grid[x, y])
            {
                distance[index] = 0f;
                continue;
            }
            distance[index] = IsBoundary(grid, x, y) ? 0f : infinity;
        }

        // Forward chamfer pass.
        for (int y = 0; y < grid.Height; y++)
        for (int x = 0; x < grid.Width; x++)
        {
            if (!grid[x, y]) continue;
            int index = y * grid.Width + x;
            Relax(grid, distance, x, y, index, -1, 0, 1f);
            Relax(grid, distance, x, y, index, 0, -1, 1f);
            Relax(grid, distance, x, y, index, -1, -1, DiagonalWeight);
            Relax(grid, distance, x, y, index, 1, -1, DiagonalWeight);
        }

        // Backward chamfer pass.
        for (int y = grid.Height - 1; y >= 0; y--)
        for (int x = grid.Width - 1; x >= 0; x--)
        {
            if (!grid[x, y]) continue;
            int index = y * grid.Width + x;
            Relax(grid, distance, x, y, index, 1, 0, 1f);
            Relax(grid, distance, x, y, index, 0, 1, 1f);
            Relax(grid, distance, x, y, index, 1, 1, DiagonalWeight);
            Relax(grid, distance, x, y, index, -1, 1, DiagonalWeight);
        }

        int passes = Math.Clamp((int)Math.Round(Math.Clamp(smoothness, 0.0, 1.0) * 10.0), 1, 10);
        float[] scratch = new float[distance.Length];
        for (int pass = 0; pass < passes; pass++)
        {
            Array.Copy(distance, scratch, distance.Length);
            for (int y = 0; y < grid.Height; y++)
            for (int x = 0; x < grid.Width; x++)
            {
                if (!grid[x, y]) continue;
                int index = y * grid.Width + x;
                if (IsBoundary(grid, x, y))
                {
                    scratch[index] = 0f;
                    continue;
                }

                float total = distance[index] * 4f;
                float weight = 4f;
                foreach ((int dx, int dy, float neighborWeight) in ChamferNeighbors)
                {
                    int nx = x + dx;
                    int ny = y + dy;
                    if (!grid[nx, ny]) continue;
                    float w = neighborWeight > 1f ? 0.7f : 1f;
                    total += distance[ny * grid.Width + nx] * w;
                    weight += w;
                }
                scratch[index] = total / weight;
            }
            (distance, scratch) = (scratch, distance);
        }
        return distance;
    }

    private static void Relax(MaskGrid grid, float[] values, int x, int y, int index, int dx, int dy, float weight)
    {
        int nx = x + dx;
        int ny = y + dy;
        if (!grid[nx, ny]) return;
        float candidate = values[ny * grid.Width + nx] + weight;
        if (candidate < values[index]) values[index] = candidate;
    }

    private static bool IsBoundary(MaskGrid grid, int x, int y) =>
        !grid[x - 1, y] || !grid[x + 1, y] || !grid[x, y - 1] || !grid[x, y + 1];

    private static float LegacySurfaceHalfDepth(MaskGrid grid, int[] inwardDistance, int maximumInset, int vx, int vy, float halfDepth, GeometrySettings settings)
    {
        int inset = VertexInset(grid, inwardDistance, vx, vy);
        float roundness = (float)Math.Clamp(settings.Roundness, 0.0, 1.0);
        if (settings.ShapeMode == ShapeMode.RoundedExtrusion)
        {
            int bevelCells = Math.Max(1, (int)MathF.Round(1f + roundness * 5f));
            float t = Math.Clamp((float)inset / bevelCells, 0f, 1f);
            t = t * t * (3f - 2f * t);
            float sideHalf = halfDepth * (1f - 0.78f * roundness);
            return sideHalf + (halfDepth - sideHalf) * t;
        }
        if (settings.ShapeMode == ShapeMode.InflatedSolid)
        {
            float normalized = maximumInset <= 0 ? 0f : Math.Clamp((float)inset / maximumInset, 0f, 1f);
            float softened = MathF.Sqrt(normalized);
            float edgeFactor = 0.28f + (0.55f - 0.28f) * (1f - roundness);
            float edgeHalf = halfDepth * edgeFactor;
            return edgeHalf + (halfDepth - edgeHalf) * softened;
        }
        if (settings.ShapeMode == ShapeMode.Relief)
        {
            float normalized = maximumInset <= 0 ? 0f : Math.Clamp((float)inset / maximumInset, 0f, 1f);
            float softened = normalized * normalized * (3f - 2f * normalized);
            return halfDepth * (0.55f + 0.45f * softened);
        }
        throw new InvalidOperationException($"Part replacement shape mode {settings.ShapeMode} is not supported.");
    }

    private static float SmoothSurfaceHalfDepth(MaskGrid grid, float[] inwardDistance, float maximumInset, int vx, int vy, float halfDepth, GeometrySettings settings)
    {
        float inset = VertexInset(grid, inwardDistance, vx, vy);
        float roundness = (float)Math.Clamp(settings.Roundness, 0.0, 1.0);
        if (settings.ShapeMode == ShapeMode.RoundedExtrusion)
        {
            float bevelCells = 1.5f + roundness * 10f;
            float t = Math.Clamp(inset / bevelCells, 0f, 1f);
            t = SmoothStep(t);
            float sideHalf = halfDepth * (1f - 0.86f * roundness);
            return sideHalf + (halfDepth - sideHalf) * t;
        }

        float normalized = maximumInset <= 0.0001f ? 0f : Math.Clamp(inset / maximumInset, 0f, 1f);
        if (settings.ShapeMode == ShapeMode.InflatedSolid)
        {
            // A sine dome has a much softer derivative than sqrt(distance) near the medial axis,
            // which removes the hard contour bands visible in the first torso/foot prototype.
            float profile = MathF.Sin(MathF.Pow(normalized, 0.82f + (1f - roundness) * 0.55f) * MathF.PI * 0.5f);
            float edgeFactor = 0.18f + (1f - roundness) * 0.24f;
            return halfDepth * (edgeFactor + (1f - edgeFactor) * profile);
        }
        if (settings.ShapeMode == ShapeMode.Relief)
        {
            // "Soft pillow": a broad, deliberately shallow edge transition for plush/cartoon forms.
            float profile = SmoothStep(MathF.Pow(normalized, 0.72f));
            float edgeFactor = 0.48f - roundness * 0.16f;
            return halfDepth * (edgeFactor + (1f - edgeFactor) * profile);
        }
        throw new InvalidOperationException($"Part replacement shape mode {settings.ShapeMode} is not supported.");
    }

    private static float SmoothStep(float value) => value * value * (3f - 2f * value);

    private static int VertexInset(MaskGrid grid, int[] inwardDistance, int vx, int vy)
    {
        bool anyFilled = false, touchesEmpty = false;
        int minimum = int.MaxValue;
        for (int cy = vy - 1; cy <= vy; cy++)
        for (int cx = vx - 1; cx <= vx; cx++)
        {
            if (cx < 0 || cy < 0 || cx >= grid.Width || cy >= grid.Height || !grid[cx, cy]) { touchesEmpty = true; continue; }
            anyFilled = true;
            minimum = Math.Min(minimum, inwardDistance[cy * grid.Width + cx]);
        }
        if (!anyFilled || touchesEmpty) return 0;
        return minimum == int.MaxValue ? 0 : minimum + 1;
    }

    private static float VertexInset(MaskGrid grid, float[] inwardDistance, int vx, int vy)
    {
        bool anyFilled = false, touchesEmpty = false;
        float total = 0f;
        int count = 0;
        for (int cy = vy - 1; cy <= vy; cy++)
        for (int cx = vx - 1; cx <= vx; cx++)
        {
            if (cx < 0 || cy < 0 || cx >= grid.Width || cy >= grid.Height || !grid[cx, cy])
            {
                touchesEmpty = true;
                continue;
            }
            anyFilled = true;
            total += inwardDistance[cy * grid.Width + cx];
            count++;
        }
        if (!anyFilled || touchesEmpty || count == 0) return 0f;
        return total / count + 1f;
    }
}
