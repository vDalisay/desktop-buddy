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
    private static readonly (int X, int Y)[] Neighbors = [(1, 0), (-1, 0), (0, 1), (0, -1)];

    public static CanonicalMesh Generate(MaskGrid grid, GeometrySettings settings, AssetCategory category)
    {
        if (category is not (AssetCategory.TorsoShape or AssetCategory.FootShape))
            throw new ArgumentOutOfRangeException(nameof(category), category, "Only torso and foot replacement categories are supported.");
        if (grid.FilledCount == 0)
            throw new InvalidOperationException("Source contains no visible replacement geometry after thresholding.");

        var mesh = new CanonicalMesh();
        var vertices = new Dictionary<int, uint>();
        int[] inward = BuildInwardDistance(grid);
        int maximumInset = inward.Where(static value => value != int.MaxValue).DefaultIfEmpty(0).Max();
        float sourcePixelsPerCell = PartReplacementTemplateSpace.CanvasSize / (float)grid.Width;
        float halfDepth = (float)settings.Depth * 0.5f;

        uint Vertex(int vx, int vy, bool front)
        {
            int key = (((vy * (grid.Width + 1)) + vx) << 1) | (front ? 1 : 0);
            if (vertices.TryGetValue(key, out uint existing)) return existing;

            float pixelX = vx * sourcePixelsPerCell;
            float pixelY = vy * sourcePixelsPerCell;
            Vector2 mapped = PartReplacementTemplateSpace.MapPixel(category, pixelX, pixelY);
            float surfaceHalf = SurfaceHalfDepth(grid, inward, maximumInset, vx, vy, halfDepth, settings);
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

            uint fTl = Vertex(x, y, true);
            uint fTr = Vertex(x + 1, y, true);
            uint fBl = Vertex(x, y + 1, true);
            uint fBr = Vertex(x + 1, y + 1, true);
            uint bTl = Vertex(x, y, false);
            uint bTr = Vertex(x + 1, y, false);
            uint bBl = Vertex(x, y + 1, false);
            uint bBr = Vertex(x + 1, y + 1, false);

            mesh.AddTriangle(fBl, fBr, fTr);
            mesh.AddTriangle(fBl, fTr, fTl);
            mesh.AddTriangle(bBl, bTr, bBr);
            mesh.AddTriangle(bBl, bTl, bTr);

            if (!grid[x - 1, y])
            {
                mesh.AddTriangle(fTl, bBl, fBl);
                mesh.AddTriangle(fTl, bTl, bBl);
            }
            if (!grid[x + 1, y])
            {
                mesh.AddTriangle(fTr, fBr, bBr);
                mesh.AddTriangle(fTr, bBr, bTr);
            }
            if (!grid[x, y - 1])
            {
                mesh.AddTriangle(fTl, fTr, bTr);
                mesh.AddTriangle(fTl, bTr, bTl);
            }
            if (!grid[x, y + 1])
            {
                mesh.AddTriangle(fBl, bBl, bBr);
                mesh.AddTriangle(fBl, bBr, fBr);
            }
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
            if (!grid[x, y]) continue;
            if (!Neighbors.Any(n => !grid[x + n.X, y + n.Y])) continue;
            distance[y * grid.Width + x] = 0;
            queue.Enqueue((x, y));
        }

        while (queue.Count > 0)
        {
            (int x, int y) = queue.Dequeue();
            int next = distance[y * grid.Width + x] + 1;
            foreach ((int dx, int dy) in Neighbors)
            {
                int nx = x + dx;
                int ny = y + dy;
                if (!grid[nx, ny]) continue;
                int index = ny * grid.Width + nx;
                if (next >= distance[index]) continue;
                distance[index] = next;
                queue.Enqueue((nx, ny));
            }
        }
        return distance;
    }

    private static float SurfaceHalfDepth(
        MaskGrid grid,
        int[] inwardDistance,
        int maximumInset,
        int vx,
        int vy,
        float halfDepth,
        GeometrySettings settings)
    {
        if (settings.ShapeMode == ShapeMode.FlatExtrusion) return halfDepth;

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
            float edgeHalf = halfDepth * MathF.Lerp(0.28f, 0.55f, 1f - roundness);
            return edgeHalf + (halfDepth - edgeHalf) * softened;
        }

        throw new InvalidOperationException($"Part replacement shape mode {settings.ShapeMode} is not supported.");
    }

    private static int VertexInset(MaskGrid grid, int[] inwardDistance, int vx, int vy)
    {
        bool anyFilled = false;
        bool touchesEmpty = false;
        int minimum = int.MaxValue;
        for (int cy = vy - 1; cy <= vy; cy++)
        for (int cx = vx - 1; cx <= vx; cx++)
        {
            if (cx < 0 || cy < 0 || cx >= grid.Width || cy >= grid.Height || !grid[cx, cy])
            {
                touchesEmpty = true;
                continue;
            }
            anyFilled = true;
            minimum = Math.Min(minimum, inwardDistance[cy * grid.Width + cx]);
        }
        if (!anyFilled || touchesEmpty) return 0;
        return minimum == int.MaxValue ? 0 : minimum + 1;
    }
}
