using System.Numerics;

namespace DesktopBuddy.AssetForge.Core;

/// <summary>
/// Glasses@2 semantic generator. Source position/scale are interpreted directly through
/// GlassesTemplateSpace and the bridge is traced from authored foreground instead of replaced.
/// </summary>
public static class GlassesTemplateGeneratorV2
{
    private const float SimplificationToleranceCells = 1.25f;
    private const float BridgeSimplificationToleranceCells = 0.85f;
    private const float TempleOutward = 0.18f;
    private const byte OpaqueSampleThreshold = 224;

    private readonly record struct GridPoint(int X, int Y);
    private readonly record struct Edge(GridPoint A, GridPoint B);
    private sealed record HoleContour(IReadOnlyList<Vector2> GridPoints, float Area, Vector2 Centroid);

    public static bool TryGenerate(MaskGrid grid, RgbaImage foreground, GeometrySettings settings, out CanonicalMesh mesh)
    {
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentNullException.ThrowIfNull(foreground);
        ArgumentNullException.ThrowIfNull(settings);

        IReadOnlyList<HoleContour> holes = ExtractHoleContours(grid);
        if (holes.Count < 2) { mesh = null!; return false; }

        HoleContour[] lenses = holes.OrderByDescending(static h => h.Area)
            .ThenBy(static h => h.Centroid.X).Take(2).OrderBy(static h => h.Centroid.X).ToArray();
        HoleContour left = lenses[0];
        HoleContour right = lenses[1];

        List<Vector2> leftWorld = ToHeadLoop(grid, left.GridPoints);
        List<Vector2> rightWorld = ToHeadLoop(grid, right.GridPoints);
        List<Vector2> leftUv = ToPaintUvLoop(grid, foreground, left.GridPoints);
        List<Vector2> rightUv = ToPaintUvLoop(grid, foreground, right.GridPoints);

        mesh = new CanonicalMesh();
        float frameRadius = (float)settings.FrameThickness * 0.5f;
        float depthRadius = (float)settings.Depth * 0.5f;
        int radialSegments = CrossSectionSegments(settings.Roundness);
        AddClosedFrameTube(mesh, leftWorld, leftUv, frameRadius, depthRadius, radialSegments);
        AddClosedFrameTube(mesh, rightWorld, rightUv, frameRadius, depthRadius, radialSegments);

        (int leftInner, int rightInner) = ClosestPair(left.GridPoints, right.GridPoints);
        if (TryTraceAuthoredBridge(grid, foreground, left.GridPoints[leftInner], right.GridPoints[rightInner], out List<Vector2> bridgeGrid, out List<Vector2> bridgeUv))
        {
            AddOpenFrameTube(mesh, ToHeadLoop(grid, bridgeGrid), bridgeUv, frameRadius, depthRadius, radialSegments);
        }

        int leftOuter = IndexOfMinimumX(leftWorld);
        int rightOuter = IndexOfMaximumX(rightWorld);
        AddTemple(mesh, leftWorld[leftOuter], -1f, settings, leftUv[leftOuter], radialSegments);
        AddTemple(mesh, rightWorld[rightOuter], 1f, settings, rightUv[rightOuter], radialSegments);

        mesh.RecalculateNormals();
        return true;
    }

    private static IReadOnlyList<HoleContour> ExtractHoleContours(MaskGrid grid)
    {
        bool[] visited = new bool[grid.Width * grid.Height];
        var contours = new List<HoleContour>();
        var queue = new Queue<GridPoint>();
        for (int y = 0; y < grid.Height; y++)
        for (int x = 0; x < grid.Width; x++)
        {
            int index = y * grid.Width + x;
            if (visited[index] || grid[x, y]) continue;
            visited[index] = true;
            queue.Enqueue(new GridPoint(x, y));
            var cells = new List<GridPoint>();
            bool exterior = false;
            while (queue.Count > 0)
            {
                GridPoint p = queue.Dequeue();
                cells.Add(p);
                if (p.X == 0 || p.Y == 0 || p.X == grid.Width - 1 || p.Y == grid.Height - 1) exterior = true;
                Visit(p.X + 1, p.Y); Visit(p.X - 1, p.Y); Visit(p.X, p.Y + 1); Visit(p.X, p.Y - 1);
            }
            if (exterior || cells.Count < 4) continue;
            IReadOnlyList<Vector2>? loop = BuildBoundaryLoop(grid, cells);
            if (loop is null || loop.Count < 4) continue;
            List<Vector2> simplified = SimplifyClosed(loop, SimplificationToleranceCells);
            if (simplified.Count < 4) simplified = loop.ToList();
            float area = MathF.Abs(SignedArea(simplified));
            Vector2 centroid = simplified.Aggregate(Vector2.Zero, static (sum, p) => sum + p) / simplified.Count;
            contours.Add(new HoleContour(simplified, area, centroid));

            void Visit(int nx, int ny)
            {
                if (nx < 0 || ny < 0 || nx >= grid.Width || ny >= grid.Height) return;
                int ni = ny * grid.Width + nx;
                if (visited[ni] || grid[nx, ny]) return;
                visited[ni] = true;
                queue.Enqueue(new GridPoint(nx, ny));
            }
        }
        return contours;
    }

    private static IReadOnlyList<Vector2>? BuildBoundaryLoop(MaskGrid grid, IReadOnlyList<GridPoint> cells)
    {
        var edges = new List<Edge>();
        foreach (GridPoint c in cells)
        {
            if (grid[c.X - 1, c.Y]) edges.Add(new Edge(new GridPoint(c.X, c.Y), new GridPoint(c.X, c.Y + 1)));
            if (grid[c.X + 1, c.Y]) edges.Add(new Edge(new GridPoint(c.X + 1, c.Y), new GridPoint(c.X + 1, c.Y + 1)));
            if (grid[c.X, c.Y - 1]) edges.Add(new Edge(new GridPoint(c.X, c.Y), new GridPoint(c.X + 1, c.Y)));
            if (grid[c.X, c.Y + 1]) edges.Add(new Edge(new GridPoint(c.X, c.Y + 1), new GridPoint(c.X + 1, c.Y + 1)));
        }
        if (edges.Count == 0) return null;
        var adjacency = new Dictionary<GridPoint, List<GridPoint>>();
        foreach (Edge edge in edges) { Add(edge.A, edge.B); Add(edge.B, edge.A); }
        foreach (List<GridPoint> neighbors in adjacency.Values)
            neighbors.Sort(static (a, b) => a.Y != b.Y ? a.Y.CompareTo(b.Y) : a.X.CompareTo(b.X));

        GridPoint start = adjacency.Keys.OrderBy(static p => p.Y).ThenBy(static p => p.X).First();
        var ordered = new List<Vector2>();
        GridPoint current = start;
        GridPoint? previous = null;
        int guard = edges.Count + 8;
        while (guard-- > 0)
        {
            ordered.Add(new Vector2(current.X, current.Y));
            List<GridPoint> neighbors = adjacency[current];
            GridPoint next;
            if (previous is null) next = neighbors[0];
            else
            {
                GridPoint prev = previous.Value;
                GridPoint[] choices = neighbors.Where(n => !n.Equals(prev)).ToArray();
                if (choices.Length == 0) return null;
                next = choices[0];
            }
            previous = current;
            current = next;
            if (current.Equals(start)) break;
        }
        return current.Equals(start) && ordered.Count >= 4 ? ordered : null;

        void Add(GridPoint from, GridPoint to)
        {
            if (!adjacency.TryGetValue(from, out List<GridPoint>? list)) { list = []; adjacency.Add(from, list); }
            if (!list.Contains(to)) list.Add(to);
        }
    }

    private static bool TryTraceAuthoredBridge(MaskGrid grid, RgbaImage foreground, Vector2 leftInner, Vector2 rightInner, out List<Vector2> bridgeGrid, out List<Vector2> bridgeUv)
    {
        bridgeGrid = [];
        bridgeUv = [];
        if (rightInner.X < leftInner.X) (leftInner, rightInner) = (rightInner, leftInner);
        int x0 = Math.Clamp((int)MathF.Ceiling(leftInner.X), 0, grid.Width - 1);
        int x1 = Math.Clamp((int)MathF.Floor(rightInner.X), 0, grid.Width - 1);
        if (x1 - x0 < 2) return false;

        float previousY = leftInner.Y;
        int band = Math.Max(8, (int)MathF.Round(grid.Height * 0.12f));
        int matchedColumns = 0;
        var raw = new List<Vector2> { leftInner };
        for (int x = x0; x <= x1; x++)
        {
            float t = (float)(x - x0) / Math.Max(1, x1 - x0);
            float expectedY = leftInner.Y + (rightInner.Y - leftInner.Y) * t;
            int minY = Math.Max(0, (int)MathF.Floor(MathF.Min(previousY, expectedY) - band));
            int maxY = Math.Min(grid.Height - 1, (int)MathF.Ceiling(MathF.Max(previousY, expectedY) + band));
            bool found = false;
            float bestCenter = 0;
            float bestScore = float.PositiveInfinity;
            int y = minY;
            while (y <= maxY)
            {
                while (y <= maxY && !grid[x, y]) y++;
                if (y > maxY) break;
                int runStart = y;
                while (y <= maxY && grid[x, y]) y++;
                int runEnd = y - 1;
                float center = (runStart + runEnd + 1) * 0.5f;
                float score = MathF.Abs(center - previousY) * 0.72f + MathF.Abs(center - expectedY) * 0.28f;
                if (!found || score < bestScore - 1e-6f || score == bestScore && center < bestCenter)
                { found = true; bestScore = score; bestCenter = center; }
            }
            if (!found) continue;
            matchedColumns++;
            previousY = bestCenter;
            raw.Add(new Vector2(x + 0.5f, bestCenter));
        }
        raw.Add(rightInner);
        int requiredCoverage = Math.Max(2, (int)MathF.Ceiling((x1 - x0 + 1) * 0.60f));
        if (matchedColumns < requiredCoverage) return false;
        List<Vector2> simplified = SimplifyOpen(raw, BridgeSimplificationToleranceCells);
        if (simplified.Count < 2) return false;
        bridgeGrid = simplified;
        bridgeUv = ToPaintUvLoop(grid, foreground, simplified);
        return true;
    }

    private static List<Vector2> SimplifyClosed(IReadOnlyList<Vector2> source, float tolerance)
    {
        var points = source.ToList();
        for (int pass = 0; pass < 10 && points.Count > 4; pass++)
        {
            bool changed = false;
            var keep = new bool[points.Count]; Array.Fill(keep, true);
            for (int i = 0; i < points.Count; i++)
            {
                Vector2 previous = points[(i - 1 + points.Count) % points.Count];
                Vector2 current = points[i];
                Vector2 next = points[(i + 1) % points.Count];
                if (DistanceToSegment(current, previous, next) <= tolerance) { keep[i] = false; changed = true; i++; }
            }
            if (!changed) break;
            var reduced = new List<Vector2>();
            for (int i = 0; i < points.Count; i++) if (keep[i]) reduced.Add(points[i]);
            if (reduced.Count < 4) break;
            points = reduced;
        }
        return points;
    }

    private static List<Vector2> SimplifyOpen(IReadOnlyList<Vector2> source, float tolerance)
    {
        if (source.Count <= 2) return source.ToList();
        var points = source.ToList();
        for (int pass = 0; pass < 12 && points.Count > 2; pass++)
        {
            bool changed = false;
            var reduced = new List<Vector2> { points[0] };
            for (int i = 1; i < points.Count - 1; i++)
            {
                if (DistanceToSegment(points[i], reduced[^1], points[i + 1]) <= tolerance) { changed = true; continue; }
                reduced.Add(points[i]);
            }
            reduced.Add(points[^1]);
            points = reduced;
            if (!changed) break;
        }
        return points;
    }

    private static float DistanceToSegment(Vector2 point, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float lengthSquared = ab.LengthSquared();
        if (lengthSquared <= 1e-8f) return Vector2.Distance(point, a);
        float t = Math.Clamp(Vector2.Dot(point - a, ab) / lengthSquared, 0f, 1f);
        return Vector2.Distance(point, a + ab * t);
    }

    private static float SignedArea(IReadOnlyList<Vector2> points)
    {
        double area = 0;
        for (int i = 0; i < points.Count; i++)
        {
            Vector2 a = points[i]; Vector2 b = points[(i + 1) % points.Count];
            area += (double)a.X * b.Y - (double)b.X * a.Y;
        }
        return (float)(area * 0.5);
    }

    private static List<Vector2> ToHeadLoop(MaskGrid grid, IReadOnlyList<Vector2> points)
    {
        var result = new List<Vector2>(points.Count);
        foreach (Vector2 p in points) result.Add(GlassesTemplateSpace.GridPointToHead(grid, p));
        return result;
    }

    private static List<Vector2> ToPaintUvLoop(MaskGrid grid, RgbaImage foreground, IReadOnlyList<Vector2> points)
    {
        var result = new List<Vector2>(points.Count);
        int block = Math.Max(1, foreground.Width / grid.Width);
        int maxRadius = Math.Max(6, block * 6);
        foreach (Vector2 point in points)
        {
            Vector2 sourcePoint = GlassesTemplateSpace.GridPointToSourcePixel(grid, point);
            int sourceX = Math.Clamp((int)MathF.Round(sourcePoint.X), 0, foreground.Width - 1);
            int sourceY = Math.Clamp((int)MathF.Round(sourcePoint.Y), 0, foreground.Height - 1);
            (int x, int y) = FindNearestOpaquePixel(foreground, sourceX, sourceY, maxRadius);
            result.Add(new Vector2((x + 0.5f) / foreground.Width, (y + 0.5f) / foreground.Height));
        }
        return result;
    }

    private static (int X, int Y) FindNearestOpaquePixel(RgbaImage image, int originX, int originY, int maxRadius)
    {
        int bestX = originX, bestY = originY;
        byte bestAlpha = image.Alpha(originX, originY);
        if (bestAlpha >= OpaqueSampleThreshold) return (bestX, bestY);
        for (int radius = 1; radius <= maxRadius; radius++)
        {
            bool found = false; byte ringBestAlpha = 0; int ringBestX = 0, ringBestY = 0;
            for (int dy = -radius; dy <= radius; dy++)
            for (int dx = -radius; dx <= radius; dx++)
            {
                if (Math.Abs(dx) != radius && Math.Abs(dy) != radius) continue;
                int x = originX + dx, y = originY + dy;
                if (x < 0 || y < 0 || x >= image.Width || y >= image.Height) continue;
                byte alpha = image.Alpha(x, y);
                if (alpha > bestAlpha) { bestAlpha = alpha; bestX = x; bestY = y; }
                if (alpha < OpaqueSampleThreshold) continue;
                if (!found || alpha > ringBestAlpha || alpha == ringBestAlpha && (y < ringBestY || y == ringBestY && x < ringBestX))
                { found = true; ringBestAlpha = alpha; ringBestX = x; ringBestY = y; }
            }
            if (found) return (ringBestX, ringBestY);
        }
        return (bestX, bestY);
    }

    private static (int Left, int Right) ClosestPair(IReadOnlyList<Vector2> left, IReadOnlyList<Vector2> right)
    {
        float best = float.PositiveInfinity; int bestLeft = 0, bestRight = 0;
        for (int i = 0; i < left.Count; i++)
        for (int j = 0; j < right.Count; j++)
        {
            float distance = Vector2.DistanceSquared(left[i], right[j]);
            if (distance < best - 1e-8f || MathF.Abs(distance - best) <= 1e-8f && (i < bestLeft || i == bestLeft && j < bestRight))
            { best = distance; bestLeft = i; bestRight = j; }
        }
        return (bestLeft, bestRight);
    }

    private static int IndexOfMinimumX(IReadOnlyList<Vector2> points)
    {
        int best = 0;
        for (int i = 1; i < points.Count; i++) if (points[i].X < points[best].X || points[i].X == points[best].X && points[i].Y > points[best].Y) best = i;
        return best;
    }

    private static int IndexOfMaximumX(IReadOnlyList<Vector2> points)
    {
        int best = 0;
        for (int i = 1; i < points.Count; i++) if (points[i].X > points[best].X || points[i].X == points[best].X && points[i].Y > points[best].Y) best = i;
        return best;
    }

    private static int CrossSectionSegments(double roundness)
    {
        int segments = (int)Math.Round(6 + Math.Clamp(roundness, 0, 1) * 10, MidpointRounding.AwayFromZero);
        if ((segments & 1) != 0) segments++;
        return Math.Clamp(segments, 6, 16);
    }

    private static void AddClosedFrameTube(CanonicalMesh mesh, IReadOnlyList<Vector2> centers, IReadOnlyList<Vector2> uvs, float radiusXY, float radiusZ, int radialSegments)
    {
        int count = centers.Count; uint[,] rings = new uint[count, radialSegments];
        for (int i = 0; i < count; i++)
        {
            Vector2 previous = centers[(i - 1 + count) % count], next = centers[(i + 1) % count];
            Vector2 tangent = next - previous;
            if (tangent.LengthSquared() <= 1e-10f) tangent = centers[(i + 1) % count] - centers[i];
            tangent = tangent.LengthSquared() <= 1e-10f ? Vector2.UnitX : Vector2.Normalize(tangent);
            Vector2 normal = new(-tangent.Y, tangent.X);
            for (int r = 0; r < radialSegments; r++)
            {
                float angle = MathF.Tau * r / radialSegments;
                Vector2 xy = centers[i] + normal * (MathF.Cos(angle) * radiusXY);
                float z = MathF.Sin(angle) * radiusZ;
                rings[i, r] = mesh.AddVertex(new Vector3(xy, z), uvs[i]);
            }
        }
        ConnectRings(mesh, rings, count, radialSegments, true);
    }

    private static void AddOpenFrameTube(CanonicalMesh mesh, IReadOnlyList<Vector2> centers, IReadOnlyList<Vector2> uvs, float radiusXY, float radiusZ, int radialSegments)
    {
        if (centers.Count < 2 || uvs.Count != centers.Count) return;
        int count = centers.Count; uint[,] rings = new uint[count, radialSegments];
        for (int i = 0; i < count; i++)
        {
            Vector2 previous = i == 0 ? centers[0] : centers[i - 1];
            Vector2 next = i == count - 1 ? centers[^1] : centers[i + 1];
            Vector2 tangent = next - previous;
            if (tangent.LengthSquared() <= 1e-10f) tangent = i == 0 ? centers[1] - centers[0] : centers[i] - centers[i - 1];
            tangent = tangent.LengthSquared() <= 1e-10f ? Vector2.UnitX : Vector2.Normalize(tangent);
            Vector2 normal = new(-tangent.Y, tangent.X);
            for (int r = 0; r < radialSegments; r++)
            {
                float angle = MathF.Tau * r / radialSegments;
                Vector2 xy = centers[i] + normal * (MathF.Cos(angle) * radiusXY);
                float z = MathF.Sin(angle) * radiusZ;
                rings[i, r] = mesh.AddVertex(new Vector3(xy, z), uvs[i]);
            }
        }
        ConnectRings(mesh, rings, count, radialSegments, false);
    }

    private static void ConnectRings(CanonicalMesh mesh, uint[,] rings, int count, int radialSegments, bool closed)
    {
        int segmentCount = closed ? count : count - 1;
        for (int i = 0; i < segmentCount; i++)
        {
            int j = (i + 1) % count;
            for (int r = 0; r < radialSegments; r++)
            {
                int s = (r + 1) % radialSegments;
                mesh.AddTriangle(rings[i, r], rings[j, s], rings[j, r]);
                mesh.AddTriangle(rings[i, r], rings[i, s], rings[j, s]);
            }
        }
    }

    private static void AddTemple(CanonicalMesh mesh, Vector2 root2, float side, GeometrySettings settings, Vector2 uv, int radialSegments)
    {
        float radius = (float)settings.TempleThickness * 0.5f, length = (float)settings.TempleLength, drop = (float)settings.TempleDrop;
        Vector3 start = new(root2, 0);
        Vector3 hinge = new(root2.X + side * TempleOutward * 0.60f, root2.Y - drop * 0.25f, -MathF.Min(0.10f, length * 0.22f));
        Vector3 end = new(root2.X + side * TempleOutward, root2.Y - drop, -length);
        AddGeneralTubeSegment(mesh, start, hinge, radius, radialSegments, uv);
        AddGeneralTubeSegment(mesh, hinge, end, radius, radialSegments, uv);
    }

    private static void AddGeneralTubeSegment(CanonicalMesh mesh, Vector3 start, Vector3 end, float radius, int radialSegments, Vector2 uv)
    {
        Vector3 axis = end - start;
        if (axis.LengthSquared() <= 1e-12f) return;
        Vector3 forward = Vector3.Normalize(axis);
        Vector3 reference = MathF.Abs(Vector3.Dot(forward, Vector3.UnitY)) < 0.92f ? Vector3.UnitY : Vector3.UnitX;
        Vector3 u = Vector3.Normalize(Vector3.Cross(forward, reference));
        Vector3 v = Vector3.Normalize(Vector3.Cross(forward, u));
        uint[] first = new uint[radialSegments], second = new uint[radialSegments];
        for (int r = 0; r < radialSegments; r++)
        {
            float angle = MathF.Tau * r / radialSegments;
            Vector3 offset = (u * MathF.Cos(angle) + v * MathF.Sin(angle)) * radius;
            first[r] = mesh.AddVertex(start + offset, uv); second[r] = mesh.AddVertex(end + offset, uv);
        }
        for (int r = 0; r < radialSegments; r++)
        {
            int s = (r + 1) % radialSegments;
            mesh.AddTriangle(first[r], second[s], second[r]);
            mesh.AddTriangle(first[r], first[s], second[s]);
        }
    }
}
