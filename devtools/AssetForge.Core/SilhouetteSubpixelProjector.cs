using System.Numerics;

namespace DesktopBuddy.AssetForge.Core;

/// <summary>
/// Shared deterministic rim refinement for silhouette-derived meshes. The coarse occupancy grid
/// remains the topology/runtime-budget authority; only front/back rim vertices are projected onto
/// the original 1024x1024 alpha contour reconstructed with marching squares. This removes the
/// visible grid stair-step without increasing the mesh resolution or filtering the authored art.
/// </summary>
public static class SilhouetteSubpixelProjector
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
        Func<Vector2, Vector2> sourcePixelToWorld)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        ArgumentNullException.ThrowIfNull(foreground);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(sourcePixelToWorld);

        if (mesh.TriangleCount == 0 || settings.SurfaceSmoothness <= 0.000001)
            return mesh;
        if (foreground.Width != AssetForgeGenerator.SourceSize || foreground.Height != AssetForgeGenerator.SourceSize)
            throw new ArgumentException("Subpixel silhouette reconstruction requires the canonical 1024x1024 source.", nameof(foreground));

        // Thickness/symmetry currently operate only on the reduced mask. Projecting their boundary
        // back onto the unmodified source would silently undo the requested operation.
        if (settings.ThicknessBiasPixels != 0 || settings.SymmetryMode != SymmetryMode.Off)
            return mesh;

        float threshold = (float)Math.Clamp(settings.AlphaThreshold * 255.0, 0.0, 255.0);
        List<Segment> contour = BuildMarchingSquaresContour(foreground, threshold);
        if (contour.Count == 0)
            return mesh;

        HashSet<int> rim = FindRimVertices(mesh);
        if (rim.Count == 0)
            return mesh;

        float sourcePixelsPerGridCell = AssetForgeGenerator.SourceSize / (float)settings.GeometryResolution;
        float searchRadius = MathF.Max(MinimumSearchRadiusPixels, sourcePixelsPerGridCell * SearchRadiusInGridCells);
        float searchRadiusSquared = searchRadius * searchRadius;

        foreach (int index in rim)
        {
            Vector2 uv = mesh.Uvs[index];
            Vector2 authoredPixel = new(uv.X * foreground.Width, uv.Y * foreground.Height);
            if (!TryProjectToNearestContour(authoredPixel, contour, searchRadius, searchRadiusSquared, out Vector2 projected))
                continue;

            projected.X = Math.Clamp(projected.X, 0f, foreground.Width);
            projected.Y = Math.Clamp(projected.Y, 0f, foreground.Height);
            Vector2 mapped = sourcePixelToWorld(projected);
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
            if (!IsSideTriangle(mesh, a, b, c)) continue;
            rim.Add(a); rim.Add(b); rim.Add(c);
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
            // The occupancy mask treats every sample below AlphaThreshold as empty. Canonicalize
            // those samples to zero here as well so low-opacity template/reference pixels cannot
            // tug the interpolated full-resolution contour toward themselves when left beneath art.
            float tl = CanonicalContourAlpha(image.Alpha(x, y), threshold);
            float tr = CanonicalContourAlpha(image.Alpha(x + 1, y), threshold);
            float br = CanonicalContourAlpha(image.Alpha(x + 1, y + 1), threshold);
            float bl = CanonicalContourAlpha(image.Alpha(x, y + 1), threshold);
            bool iTl = tl >= threshold;
            bool iTr = tr >= threshold;
            bool iBr = br >= threshold;
            bool iBl = bl >= threshold;
            int mask = (iTl ? 1 : 0) | (iTr ? 2 : 0) | (iBr ? 4 : 0) | (iBl ? 8 : 0);
            if (mask is 0 or 15) continue;

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
            if (count != 4) continue;

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

    private static float CanonicalContourAlpha(float alpha, float threshold) => alpha >= threshold ? alpha : 0f;

    private static Vector2 Interpolate(Vector2 a, Vector2 b, float valueA, float valueB, float threshold)
    {
        float denominator = valueB - valueA;
        if (MathF.Abs(denominator) <= 0.000001f) return (a + b) * 0.5f;
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
            if (distanceSquared >= bestSquared) continue;
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
        if (lengthSquared <= 0.000000000001f) return a;
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
