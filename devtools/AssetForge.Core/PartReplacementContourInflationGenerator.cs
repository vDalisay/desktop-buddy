using System.Numerics;

namespace DesktopBuddy.AssetForge.Core;

/// <summary>
/// Boundary-conforming generator for non-legacy inflated Torso/Foot replacements.
///
/// The original replacement generator deliberately uses a regular occupancy grid. That is cheap and
/// deterministic, but moving the already-triangulated rim onto the 1024px source contour can create
/// extremely thin cut triangles. Those slivers are especially harmful for smooth shading: the mesh
/// can have an accurate XY silhouette and still render with a serrated/dark rim.
///
/// This generator changes the order of operations for the normal InflatedSolid authoring path:
/// 1. reconstruct a continuous full-resolution alpha contour;
/// 2. sample a signed distance to that contour at the runtime grid vertices;
/// 3. clip the regular grid triangles against that continuous boundary, so topology conforms to the
///    contour instead of moving a finished grid after triangulation;
/// 4. add a small amount of O(perimeter) contour refinement while keeping the interior at the chosen
///    runtime density;
/// 5. solve a smooth Poisson-like inflation field on a 2x helper grid and sample it for Z; and
/// 6. make front/back meet at Z=0 on the authored contour, producing a genuinely rounded closed shell
///    instead of a front cap plus an explicit side wall.
///
/// The source image is never rewritten. UVs are derived from the same sub-pixel positions as the
/// geometry, so generated-mesh painting remains registered to the source. The strict legacy path,
/// transformed masks (symmetry/thickness bias), and non-inflated shape modes stay on the established
/// generator until they receive equivalent contour-space implementations.
/// </summary>
public static class PartReplacementContourInflationGenerator
{
    private const float SideEpsilon = 0.00001f;
    private const float QuantizationScale = 10000f;
    private const float BoundarySnapFraction = 0.20f;
    private const float BoundaryRefinementFraction = 0.50f;
    private const float MinimumBoundarySegmentPixels = 2f;

    private readonly record struct Segment(Vector2 A, Vector2 B)
    {
        public float MinX => MathF.Min(A.X, B.X);
        public float MaxX => MathF.Max(A.X, B.X);
        public float MinY => MathF.Min(A.Y, B.Y);
        public float MaxY => MathF.Max(A.Y, B.Y);
    }

    private readonly record struct GridNode(Vector2 Pixel, float SignedDistance, bool Boundary);
    private readonly record struct ClipVertex(Vector2 Pixel, float SignedDistance, bool Boundary);
    private readonly record struct VertexKey(int X, int Y, int Surface);

    private sealed class ContourField
    {
        private readonly List<Segment> _segments;

        public ContourField(List<Segment> segments)
        {
            _segments = segments;
        }

        public int SegmentCount => _segments.Count;

        public float Distance(Vector2 point)
        {
            float bestSquared = float.PositiveInfinity;
            foreach (Segment segment in _segments)
            {
                float boxSquared = DistanceToBoundsSquared(point, segment);
                if (boxSquared >= bestSquared)
                    continue;
                Vector2 nearest = ClosestPoint(point, segment.A, segment.B);
                float distanceSquared = Vector2.DistanceSquared(point, nearest);
                if (distanceSquared < bestSquared)
                    bestSquared = distanceSquared;
            }
            return float.IsPositiveInfinity(bestSquared) ? 0f : MathF.Sqrt(bestSquared);
        }

        public Vector2 Project(Vector2 point, float preferredRadius)
        {
            float preferredSquared = preferredRadius * preferredRadius;
            float bestSquared = float.PositiveInfinity;
            Vector2 best = point;

            // The preferred radius keeps a boundary refinement point on its local contour around
            // narrow necks. If no local segment is found, fall back to the global nearest segment.
            bool foundPreferred = false;
            foreach (Segment segment in _segments)
            {
                float boxSquared = DistanceToBoundsSquared(point, segment);
                if (boxSquared > preferredSquared || boxSquared >= bestSquared)
                    continue;
                Vector2 nearest = ClosestPoint(point, segment.A, segment.B);
                float distanceSquared = Vector2.DistanceSquared(point, nearest);
                if (distanceSquared < bestSquared)
                {
                    bestSquared = distanceSquared;
                    best = nearest;
                    foundPreferred = true;
                }
            }
            if (foundPreferred)
                return best;

            bestSquared = float.PositiveInfinity;
            foreach (Segment segment in _segments)
            {
                float boxSquared = DistanceToBoundsSquared(point, segment);
                if (boxSquared >= bestSquared)
                    continue;
                Vector2 nearest = ClosestPoint(point, segment.A, segment.B);
                float distanceSquared = Vector2.DistanceSquared(point, nearest);
                if (distanceSquared < bestSquared)
                {
                    bestSquared = distanceSquared;
                    best = nearest;
                }
            }
            return best;
        }

        private static float DistanceToBoundsSquared(Vector2 point, Segment segment)
        {
            float dx = point.X < segment.MinX
                ? segment.MinX - point.X
                : point.X > segment.MaxX ? point.X - segment.MaxX : 0f;
            float dy = point.Y < segment.MinY
                ? segment.MinY - point.Y
                : point.Y > segment.MaxY ? point.Y - segment.MaxY : 0f;
            return dx * dx + dy * dy;
        }
    }

    private sealed class InflationField
    {
        private readonly float[] _values;
        private readonly int _size;
        private readonly float _cellPixels;

        public InflationField(float[] values, int size)
        {
            _values = values;
            _size = size;
            _cellPixels = PartReplacementTemplateSpace.CanvasSize / (float)size;
        }

        public float Sample(Vector2 pixel)
        {
            // Values live at helper-cell centres. Convert source pixels to that lattice and sample
            // bilinearly so the runtime mesh never inherits the helper grid's block boundaries.
            float fx = pixel.X / _cellPixels - 0.5f;
            float fy = pixel.Y / _cellPixels - 0.5f;
            fx = Math.Clamp(fx, 0f, _size - 1f);
            fy = Math.Clamp(fy, 0f, _size - 1f);
            int x0 = Math.Clamp((int)MathF.Floor(fx), 0, _size - 1);
            int y0 = Math.Clamp((int)MathF.Floor(fy), 0, _size - 1);
            int x1 = Math.Min(x0 + 1, _size - 1);
            int y1 = Math.Min(y0 + 1, _size - 1);
            float tx = fx - x0;
            float ty = fy - y0;
            float top = Lerp(_values[y0 * _size + x0], _values[y0 * _size + x1], tx);
            float bottom = Lerp(_values[y1 * _size + x0], _values[y1 * _size + x1], tx);
            return Math.Clamp(Lerp(top, bottom, ty), 0f, 1f);
        }
    }

    public static bool CanGenerate(GeometrySettings settings) =>
        settings.SurfaceSmoothness > 0.000001 &&
        settings.ShapeMode == ShapeMode.InflatedSolid &&
        settings.ThicknessBiasPixels == 0 &&
        settings.SymmetryMode == SymmetryMode.Off;

    public static CanonicalMesh Generate(
        RgbaImage foreground,
        GeometrySettings settings,
        AssetCategory category)
    {
        ArgumentNullException.ThrowIfNull(foreground);
        ArgumentNullException.ThrowIfNull(settings);
        if (category is not (AssetCategory.TorsoShape or AssetCategory.FootShape))
            throw new ArgumentOutOfRangeException(nameof(category), category, "Contour inflation is Buddy-part replacement-only.");
        if (!CanGenerate(settings))
            throw new InvalidOperationException("Contour inflation requires non-zero smoothing, InflatedSolid, no thickness bias, and symmetry Off.");
        if (foreground.Width != PartReplacementTemplateSpace.CanvasSize ||
            foreground.Height != PartReplacementTemplateSpace.CanvasSize)
            throw new ArgumentException("Contour inflation requires the canonical 1024x1024 replacement source.", nameof(foreground));

        float threshold = (float)Math.Clamp(settings.AlphaThreshold * 255.0, 0.0, 255.0);
        var contour = new ContourField(BuildMarchingSquaresContour(foreground, threshold));
        if (contour.SegmentCount == 0)
            throw new InvalidOperationException("No full-resolution replacement contour could be reconstructed from the source alpha.");

        int resolution = settings.GeometryResolution;
        float cellPixels = PartReplacementTemplateSpace.CanvasSize / (float)resolution;
        float snapDistance = cellPixels * BoundarySnapFraction;
        float projectionRadius = cellPixels * 1.75f;
        float refinementTarget = MathF.Max(MinimumBoundarySegmentPixels, cellPixels * BoundaryRefinementFraction);
        GridNode[,] nodes = BuildGridNodes(foreground, threshold, contour, resolution, cellPixels, snapDistance, projectionRadius);
        InflationField inflation = BuildInflationField(foreground, threshold, settings);

        var mesh = new CanonicalMesh();
        var vertexMap = new Dictionary<VertexKey, uint>();
        float halfDepth = (float)settings.Depth * 0.5f;
        float roundness = (float)Math.Clamp(settings.Roundness, 0.0, 1.0);
        float profileExponent = 0.50f + (1f - roundness) * 0.45f;

        for (int y = 0; y < resolution; y++)
        for (int x = 0; x < resolution; x++)
        {
            GridNode tl = nodes[x, y];
            GridNode tr = nodes[x + 1, y];
            GridNode bl = nodes[x, y + 1];
            GridNode br = nodes[x + 1, y + 1];

            // Alternating the internal cell diagonal avoids baking one global diagonal direction into
            // the smooth shading while preserving a regular, cache-friendly interior triangulation.
            if (((x + y) & 1) == 0)
            {
                AddClippedTriangle(bl, br, tr);
                AddClippedTriangle(bl, tr, tl);
            }
            else
            {
                AddClippedTriangle(tl, bl, br);
                AddClippedTriangle(tl, br, tr);
            }
        }

        if (mesh.TriangleCount == 0)
            throw new InvalidOperationException("Contour-conforming replacement generation produced no triangles.");

        RecalculateMaxWeightedNormals(mesh);
        return mesh;

        void AddClippedTriangle(GridNode a, GridNode b, GridNode c)
        {
            List<ClipVertex> polygon = ClipTriangle(a, b, c, contour, projectionRadius);
            if (polygon.Count < 3)
                return;

            polygon = RefineBoundaryEdges(polygon, contour, refinementTarget, projectionRadius);
            IReadOnlyList<(int A, int B, int C)> triangles = TriangulateClippedPolygon(polygon);
            foreach ((int ia, int ib, int ic) in triangles)
            {
                ClipVertex va = polygon[ia];
                ClipVertex vb = polygon[ib];
                ClipVertex vc = polygon[ic];
                if (PixelTriangleAreaSquared(va.Pixel, vb.Pixel, vc.Pixel) <= 0.000001f)
                    continue;

                uint fa = Vertex(va, true);
                uint fb = Vertex(vb, true);
                uint fc = Vertex(vc, true);
                EnsureFrontWinding(ref fa, ref fb, ref fc);
                mesh.AddTriangle(fa, fb, fc);

                uint ba = Vertex(va, false);
                uint bb = Vertex(vb, false);
                uint bc = Vertex(vc, false);
                EnsureBackWinding(ref ba, ref bb, ref bc);
                mesh.AddTriangle(ba, bb, bc);
            }
        }

        uint Vertex(ClipVertex vertex, bool front)
        {
            float inflationValue = vertex.Boundary ? 0f : inflation.Sample(vertex.Pixel);
            float z = vertex.Boundary
                ? 0f
                : halfDepth * MathF.Pow(Math.Clamp(inflationValue, 0f, 1f), profileExponent);

            // Front/back deliberately share the exact contour vertex. Their opposing axial normal
            // components then cancel during smooth-normal reconstruction, leaving the expected
            // outward/horizontal silhouette normal of a rounded closed surface.
            int surface = z <= SideEpsilon ? 0 : front ? 1 : -1;
            var key = new VertexKey(
                checked((int)MathF.Round(vertex.Pixel.X * QuantizationScale)),
                checked((int)MathF.Round(vertex.Pixel.Y * QuantizationScale)),
                surface);
            if (vertexMap.TryGetValue(key, out uint existing))
                return existing;

            Vector2 mapped = PartReplacementTemplateSpace.MapPixel(category, vertex.Pixel.X, vertex.Pixel.Y);
            Vector2 uv = new(
                Math.Clamp(vertex.Pixel.X / foreground.Width, 0f, 1f),
                Math.Clamp(vertex.Pixel.Y / foreground.Height, 0f, 1f));
            uint created = mesh.AddVertex(
                new Vector3(mapped.X, mapped.Y, surface == 0 ? 0f : front ? z : -z),
                uv);
            vertexMap.Add(key, created);
            return created;
        }

        void EnsureFrontWinding(ref uint a, ref uint b, ref uint c)
        {
            Vector3 pa = mesh.Positions[checked((int)a)];
            Vector3 pb = mesh.Positions[checked((int)b)];
            Vector3 pc = mesh.Positions[checked((int)c)];
            if (Vector3.Cross(pb - pa, pc - pa).Z >= 0f)
                return;
            (b, c) = (c, b);
        }

        void EnsureBackWinding(ref uint a, ref uint b, ref uint c)
        {
            Vector3 pa = mesh.Positions[checked((int)a)];
            Vector3 pb = mesh.Positions[checked((int)b)];
            Vector3 pc = mesh.Positions[checked((int)c)];
            if (Vector3.Cross(pb - pa, pc - pa).Z <= 0f)
                return;
            (b, c) = (c, b);
        }
    }

    private static GridNode[,] BuildGridNodes(
        RgbaImage foreground,
        float threshold,
        ContourField contour,
        int resolution,
        float cellPixels,
        float snapDistance,
        float projectionRadius)
    {
        var nodes = new GridNode[resolution + 1, resolution + 1];
        for (int y = 0; y <= resolution; y++)
        for (int x = 0; x <= resolution; x++)
        {
            Vector2 pixel = new(x * cellPixels, y * cellPixels);
            bool inside = SampleAlpha(foreground, pixel) >= threshold;
            float distance = contour.Distance(pixel);
            float signed = inside ? distance : -distance;
            bool boundary = MathF.Abs(signed) <= snapDistance;
            if (boundary)
            {
                pixel = contour.Project(pixel, projectionRadius);
                signed = 0f;
            }
            nodes[x, y] = new GridNode(pixel, signed, boundary);
        }
        return nodes;
    }

    private static InflationField BuildInflationField(
        RgbaImage foreground,
        float threshold,
        GeometrySettings settings)
    {
        int size = Math.Clamp(settings.GeometryResolution * 2, 128, 384);
        float cellPixels = PartReplacementTemplateSpace.CanvasSize / (float)size;
        bool[] inside = new bool[size * size];
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            Vector2 centre = new((x + 0.5f) * cellPixels, (y + 0.5f) * cellPixels);
            inside[y * size + x] = SampleAlpha(foreground, centre) >= threshold;
        }

        float[] current = new float[size * size];
        float[] scratch = new float[current.Length];
        int iterations = Math.Clamp(
            220 + (int)Math.Round(Math.Clamp(settings.SurfaceSmoothness, 0.0, 3.0) * 40.0),
            220,
            340);

        // Repeated Jacobi relaxation of Δu=-1, u=0 outside the silhouette. We only need the
        // normalized field, not the physical scale of u. The result is deliberately smooth across
        // medial-axis locations where a raw Euclidean distance field would form shading ridges.
        for (int pass = 0; pass < iterations; pass++)
        {
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                int index = y * size + x;
                if (!inside[index])
                {
                    scratch[index] = 0f;
                    continue;
                }

                float left = x > 0 ? current[index - 1] : 0f;
                float right = x + 1 < size ? current[index + 1] : 0f;
                float up = y > 0 ? current[index - size] : 0f;
                float down = y + 1 < size ? current[index + size] : 0f;
                scratch[index] = (left + right + up + down + 1f) * 0.25f;
            }
            (current, scratch) = (scratch, current);
        }

        float maximum = current.DefaultIfEmpty(0f).Max();
        if (maximum <= 0.000001f)
            throw new InvalidOperationException("The replacement inflation field could not be resolved.");
        float inverse = 1f / maximum;
        for (int i = 0; i < current.Length; i++)
            current[i] = Math.Clamp(current[i] * inverse, 0f, 1f);
        return new InflationField(current, size);
    }

    private static List<ClipVertex> ClipTriangle(
        GridNode a,
        GridNode b,
        GridNode c,
        ContourField contour,
        float projectionRadius)
    {
        GridNode[] input = [a, b, c];
        var output = new List<ClipVertex>(5);
        for (int i = 0; i < input.Length; i++)
        {
            GridNode current = input[i];
            GridNode next = input[(i + 1) % input.Length];
            bool currentInside = current.SignedDistance >= 0f;
            bool nextInside = next.SignedDistance >= 0f;

            if (currentInside)
                AddUnique(output, new ClipVertex(current.Pixel, current.SignedDistance, current.Boundary));

            if (currentInside == nextInside)
                continue;

            float denominator = current.SignedDistance - next.SignedDistance;
            float t = MathF.Abs(denominator) <= 0.000001f
                ? 0.5f
                : Math.Clamp(current.SignedDistance / denominator, 0f, 1f);
            Vector2 approximate = Vector2.Lerp(current.Pixel, next.Pixel, t);
            Vector2 projected = contour.Project(approximate, projectionRadius);
            AddUnique(output, new ClipVertex(projected, 0f, true));
        }

        if (output.Count > 1 && Vector2.DistanceSquared(output[0].Pixel, output[^1].Pixel) <= 0.00000001f)
            output.RemoveAt(output.Count - 1);
        return output;
    }

    private static List<ClipVertex> RefineBoundaryEdges(
        List<ClipVertex> polygon,
        ContourField contour,
        float targetLength,
        float projectionRadius)
    {
        if (polygon.Count < 3)
            return polygon;

        var refined = new List<ClipVertex>(polygon.Count + 4);
        for (int i = 0; i < polygon.Count; i++)
        {
            ClipVertex current = polygon[i];
            ClipVertex next = polygon[(i + 1) % polygon.Count];
            AddUnique(refined, current);
            if (!current.Boundary || !next.Boundary)
                continue;

            float length = Vector2.Distance(current.Pixel, next.Pixel);
            int interiorPoints = Math.Max(0, (int)MathF.Ceiling(length / targetLength) - 1);
            for (int point = 1; point <= interiorPoints; point++)
            {
                float t = point / (float)(interiorPoints + 1);
                Vector2 approximate = Vector2.Lerp(current.Pixel, next.Pixel, t);
                Vector2 projected = contour.Project(approximate, projectionRadius);
                AddUnique(refined, new ClipVertex(projected, 0f, true));
            }
        }
        return refined;
    }

    private static IReadOnlyList<(int A, int B, int C)> TriangulateClippedPolygon(List<ClipVertex> polygon)
    {
        if (polygon.Count == 3)
            return [(0, 1, 2)];

        // A clipped source triangle has at least one original inside vertex unless it degenerated to
        // a pure contour sliver. Using that interior vertex as a fan root keeps all added contour
        // refinement O(perimeter) and avoids long diagonals across the cut polygon.
        int root = polygon.FindIndex(static vertex => !vertex.Boundary);
        if (root < 0)
            root = 0;

        var triangles = new List<(int A, int B, int C)>(polygon.Count - 2);
        for (int offset = 1; offset < polygon.Count - 1; offset++)
        {
            int b = (root + offset) % polygon.Count;
            int c = (root + offset + 1) % polygon.Count;
            triangles.Add((root, b, c));
        }
        return triangles;
    }

    private static List<Segment> BuildMarchingSquaresContour(RgbaImage image, float threshold)
    {
        var segments = new List<Segment>();
        Span<Vector2> crossings = stackalloc Vector2[4];

        for (int y = 0; y < image.Height - 1; y++)
        for (int x = 0; x < image.Width - 1; x++)
        {
            float tl = image.Alpha(x, y);
            float tr = image.Alpha(x + 1, y);
            float br = image.Alpha(x + 1, y + 1);
            float bl = image.Alpha(x, y + 1);
            bool iTl = tl >= threshold;
            bool iTr = tr >= threshold;
            bool iBr = br >= threshold;
            bool iBl = bl >= threshold;
            int mask = (iTl ? 1 : 0) | (iTr ? 2 : 0) | (iBr ? 4 : 0) | (iBl ? 8 : 0);
            if (mask is 0 or 15)
                continue;

            Vector2 pTl = new(x + 0.5f, y + 0.5f);
            Vector2 pTr = new(x + 1.5f, y + 0.5f);
            Vector2 pBr = new(x + 1.5f, y + 1.5f);
            Vector2 pBl = new(x + 0.5f, y + 1.5f);
            int count = 0;
            if (iTl != iTr) crossings[count++] = Interpolate(pTl, pTr, tl, tr, threshold);
            if (iTr != iBr) crossings[count++] = Interpolate(pTr, pBr, tr, br, threshold);
            if (iBl != iBr) crossings[count++] = Interpolate(pBl, pBr, bl, br, threshold);
            if (iTl != iBl) crossings[count++] = Interpolate(pTl, pBl, tl, bl, threshold);

            if (count == 2)
            {
                segments.Add(new Segment(crossings[0], crossings[1]));
                continue;
            }
            if (count != 4)
                continue;

            bool centerInside = (tl + tr + br + bl) * 0.25f >= threshold;
            bool tlAndBr = mask == 5;
            bool pairTopRight = tlAndBr ? centerInside : !centerInside;
            if (pairTopRight)
            {
                segments.Add(new Segment(crossings[0], crossings[1]));
                segments.Add(new Segment(crossings[2], crossings[3]));
            }
            else
            {
                segments.Add(new Segment(crossings[0], crossings[3]));
                segments.Add(new Segment(crossings[1], crossings[2]));
            }
        }
        return segments;
    }

    private static void RecalculateMaxWeightedNormals(CanonicalMesh mesh)
    {
        for (int i = 0; i < mesh.Normals.Count; i++)
            mesh.Normals[i] = Vector3.Zero;

        for (int triangle = 0; triangle < mesh.Indices.Count; triangle += 3)
        {
            int a = checked((int)mesh.Indices[triangle]);
            int b = checked((int)mesh.Indices[triangle + 1]);
            int c = checked((int)mesh.Indices[triangle + 2]);
            Vector3 pa = mesh.Positions[a];
            Vector3 pb = mesh.Positions[b];
            Vector3 pc = mesh.Positions[c];
            Vector3 cross = Vector3.Cross(pb - pa, pc - pa);
            float crossLength = cross.Length();
            if (crossLength <= 0.00000001f)
                continue;
            Vector3 faceNormal = cross / crossLength;
            Accumulate(a, pb - pa, pc - pa, faceNormal);
            Accumulate(b, pc - pb, pa - pb, faceNormal);
            Accumulate(c, pa - pc, pb - pc, faceNormal);
        }

        for (int i = 0; i < mesh.Normals.Count; i++)
        {
            float lengthSquared = mesh.Normals[i].LengthSquared();
            mesh.Normals[i] = lengthSquared > 0.000000000001f
                ? Vector3.Normalize(mesh.Normals[i])
                : Vector3.UnitZ;
        }
        return;

        void Accumulate(int index, Vector3 first, Vector3 second, Vector3 faceNormal)
        {
            float firstSquared = first.LengthSquared();
            float secondSquared = second.LengthSquared();
            if (firstSquared <= 0.000000000001f || secondSquared <= 0.000000000001f)
                return;
            float sineNumerator = Vector3.Cross(first, second).Length();
            float weight = sineNumerator / (firstSquared * secondSquared);
            if (float.IsFinite(weight) && weight > 0f)
                mesh.Normals[index] += faceNormal * weight;
        }
    }

    private static float SampleAlpha(RgbaImage image, Vector2 pixel)
    {
        // Image samples are located at pixel centres (x+.5,y+.5), matching marching squares above.
        float fx = Math.Clamp(pixel.X - 0.5f, 0f, image.Width - 1f);
        float fy = Math.Clamp(pixel.Y - 0.5f, 0f, image.Height - 1f);
        int x0 = Math.Clamp((int)MathF.Floor(fx), 0, image.Width - 1);
        int y0 = Math.Clamp((int)MathF.Floor(fy), 0, image.Height - 1);
        int x1 = Math.Min(x0 + 1, image.Width - 1);
        int y1 = Math.Min(y0 + 1, image.Height - 1);
        float tx = fx - x0;
        float ty = fy - y0;
        float top = Lerp(image.Alpha(x0, y0), image.Alpha(x1, y0), tx);
        float bottom = Lerp(image.Alpha(x0, y1), image.Alpha(x1, y1), tx);
        return Lerp(top, bottom, ty);
    }

    private static Vector2 Interpolate(Vector2 a, Vector2 b, float valueA, float valueB, float threshold)
    {
        float denominator = valueB - valueA;
        if (MathF.Abs(denominator) <= 0.000001f)
            return (a + b) * 0.5f;
        float t = Math.Clamp((threshold - valueA) / denominator, 0f, 1f);
        return Vector2.Lerp(a, b, t);
    }

    private static Vector2 ClosestPoint(Vector2 point, Vector2 a, Vector2 b)
    {
        Vector2 edge = b - a;
        float lengthSquared = edge.LengthSquared();
        if (lengthSquared <= 0.000000000001f)
            return a;
        float t = Math.Clamp(Vector2.Dot(point - a, edge) / lengthSquared, 0f, 1f);
        return a + edge * t;
    }

    private static void AddUnique(List<ClipVertex> vertices, ClipVertex vertex)
    {
        if (vertices.Count > 0 && Vector2.DistanceSquared(vertices[^1].Pixel, vertex.Pixel) <= 0.00000001f)
        {
            // Prefer the boundary classification when snapping/projection collapses an intersection
            // onto an existing grid point.
            ClipVertex existing = vertices[^1];
            if (vertex.Boundary && !existing.Boundary)
                vertices[^1] = vertex;
            return;
        }
        vertices.Add(vertex);
    }

    private static float PixelTriangleAreaSquared(Vector2 a, Vector2 b, Vector2 c)
    {
        float twiceArea = MathF.Abs((b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X));
        return twiceArea * twiceArea;
    }

    private static float Lerp(float a, float b, float t) => a + (b - a) * t;
}
