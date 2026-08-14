using System.Numerics;

namespace DesktopBuddy.AssetForge.Core;

/// <summary>
/// Refines the coarse occupancy-grid rim against the original 1024x1024 foreground alpha before
/// the normal replacement smoothing/bevel pass runs.
///
/// The runtime cap deliberately remains a 64/128 grid for predictable cost. Only vertices that
/// belong to the front/back rim are moved. A marching-squares contour is reconstructed from the
/// full-resolution alpha field with linear interpolation, then every coarse rim vertex is projected
/// onto the nearest local contour segment. UVs move with the rim so painting/texture lookup remains
/// registered to the authored source.
///
/// This is intentionally geometry-only: the source PNG is never filtered or rewritten. The later
/// 3D fairing pass is still responsible for removing deliberate/high-frequency authored wobble.
/// Zero SurfaceSmoothness remains the strict v1 compatibility path.
///
/// Thickness-bias and symmetry alter the coarse mask independently from the source alpha. Until a
/// full-resolution equivalent of those operations exists, those uncommon modes conservatively keep
/// the established grid contour rather than silently undoing the requested authoring operation.
/// </summary>
public static class PartReplacementSubpixelContour
{
    private const float SideSignEpsilon = 0.000001f;
    private const float MinimumSearchRadiusPixels = 2f;
    private const float SearchRadiusInGridCells = 1.75f;

    private readonly record struct Segment(Vector2 A, Vector2 B)
    {
        public float MinX => MathF.Min(A.X, B.X);
        public float MaxX => MathF.Max(A.X, B.X);
        public float MinY => MathF.Min(A.Y, B.Y);
        public float MaxY => MathF.Max(A.Y, B.Y);
    }

    public static CanonicalMesh Apply(
        CanonicalMesh mesh,
        RgbaImage foreground,
        GeometrySettings settings,
        AssetCategory category)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        ArgumentNullException.ThrowIfNull(foreground);
        ArgumentNullException.ThrowIfNull(settings);

        if (category is not (AssetCategory.TorsoShape or AssetCategory.FootShape))
            throw new ArgumentOutOfRangeException(nameof(category), category, "Subpixel contour reconstruction is Buddy-part replacement-only.");
        if (mesh.TriangleCount == 0 || settings.SurfaceSmoothness <= 0.000001)
            return mesh;
        if (foreground.Width != PartReplacementTemplateSpace.CanvasSize ||
            foreground.Height != PartReplacementTemplateSpace.CanvasSize)
            throw new ArgumentException("Replacement subpixel reconstruction requires the canonical 1024x1024 source.", nameof(foreground));

        // These operations currently exist only in MaskGrid space. Falling back is safer than
        // projecting their deliberately modified boundary back onto the unmodified source alpha.
        if (settings.ThicknessBiasPixels != 0 || settings.SymmetryMode != SymmetryMode.Off)
            return mesh;

        float threshold = (float)Math.Clamp(settings.AlphaThreshold * 255.0, 0.0, 255.0);
        List<Segment> contour = BuildMarchingSquaresContour(foreground, threshold);
        if (contour.Count == 0)
            return mesh;

        HashSet<int> rim = FindRimVertices(mesh);
        if (rim.Count == 0)
            return mesh;

        float sourcePixelsPerGridCell = PartReplacementTemplateSpace.CanvasSize / (float)settings.GeometryResolution;
        float searchRadius = MathF.Max(MinimumSearchRadiusPixels, sourcePixelsPerGridCell * SearchRadiusInGridCells);
        float searchRadiusSquared = searchRadius * searchRadius;

        foreach (int index in rim)
        {
            Vector2 uv = mesh.Uvs[index];
            Vector2 authoredPixel = new(
                uv.X * foreground.Width,
                uv.Y * foreground.Height);

            if (!TryProjectToNearestContour(authoredPixel, contour, searchRadius, searchRadiusSquared, out Vector2 projected))
                continue;

            projected.X = Math.Clamp(projected.X, 0f, foreground.Width);
            projected.Y = Math.Clamp(projected.Y, 0f, foreground.Height);
            Vector2 mapped = PartReplacementTemplateSpace.MapPixel(category, projected.X, projected.Y);
            Vector3 position = mesh.Positions[index];
            mesh.Positions[index] = new Vector3(mapped.X, mapped.Y, position.Z);
            mesh.Uvs[index] = new Vector2(projected.X / foreground.Width, projected.Y / foreground.Height);
        }

        mesh.RecalculateNormals();
        return mesh;
    }

    private static HashSet<int> FindRimVertices(CanonicalMesh mesh)
    {
        var rim = new HashSet<int>();
        for (int triangle = 0; triangle < mesh.Indices.Count; triangle += 3)
        {
            int a = checked((int)mesh.Indices[triangle]);
            int b = checked((int)mesh.Indices[triangle + 1]);
            int c = checked((int)mesh.Indices[triangle + 2]);
            if (!IsSideTriangle(mesh, a, b, c))
                continue;
            rim.Add(a);
            rim.Add(b);
            rim.Add(c);
        }
        return rim;
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
            if (iTl != iTr) crossings[count++] = Interpolate(pTl, pTr, tl, tr, threshold); // top
            if (iTr != iBr) crossings[count++] = Interpolate(pTr, pBr, tr, br, threshold); // right
            if (iBl != iBr) crossings[count++] = Interpolate(pBl, pBr, bl, br, threshold); // bottom
            if (iTl != iBl) crossings[count++] = Interpolate(pTl, pBl, tl, bl, threshold); // left

            if (count == 2)
            {
                segments.Add(new Segment(crossings[0], crossings[1]));
                continue;
            }
            if (count != 4)
                continue;

            // Ambiguous diagonal marching-squares cells use the center value as an asymptotic-style
            // deterministic decider. Either choice stays within one source pixel, but this avoids
            // inventing a diagonal bridge when antialiasing tells us which regions are connected.
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

    private static Vector2 Interpolate(Vector2 a, Vector2 b, float valueA, float valueB, float threshold)
    {
        float denominator = valueB - valueA;
        if (MathF.Abs(denominator) <= 0.000001f)
            return (a + b) * 0.5f;
        float t = Math.Clamp((threshold - valueA) / denominator, 0f, 1f);
        return Vector2.Lerp(a, b, t);
    }

    private static bool TryProjectToNearestContour(
        Vector2 point,
        List<Segment> contour,
        float searchRadius,
        float searchRadiusSquared,
        out Vector2 projected)
    {
        projected = default;
        float bestSquared = searchRadiusSquared;
        bool found = false;

        foreach (Segment segment in contour)
        {
            if (point.X < segment.MinX - searchRadius || point.X > segment.MaxX + searchRadius ||
                point.Y < segment.MinY - searchRadius || point.Y > segment.MaxY + searchRadius)
                continue;

            Vector2 candidate = ClosestPoint(point, segment.A, segment.B);
            float distanceSquared = Vector2.DistanceSquared(point, candidate);
            if (distanceSquared >= bestSquared)
                continue;
            bestSquared = distanceSquared;
            projected = candidate;
            found = true;
        }

        return found;
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

    private static bool IsSideTriangle(CanonicalMesh mesh, int a, int b, int c)
    {
        float za = mesh.Positions[a].Z;
        float zb = mesh.Positions[b].Z;
        float zc = mesh.Positions[c].Z;
        bool hasFront = za > SideSignEpsilon || zb > SideSignEpsilon || zc > SideSignEpsilon;
        bool hasBack = za < -SideSignEpsilon || zb < -SideSignEpsilon || zc < -SideSignEpsilon;
        return hasFront && hasBack;
    }
}
