using System.Numerics;

namespace DesktopBuddy.AssetForge.Core;

/// <summary>
/// Presentation-quality cleanup for generated torso/foot meshes.
///
/// The part generator intentionally starts from a deterministic occupancy grid. That is useful for
/// stable UVs and runtime budgets, but a 128-cell grid also leaves a visible stair-step contour and
/// makes the side wall inherit normals from the front/back surface. At oblique angles those two
/// properties show up as jagged silhouettes and alternating dark/light bands.
///
/// For non-zero SurfaceSmoothness we therefore do two bounded, deterministic cleanup steps:
/// 1. relax only the XY rim vertices along their existing contour (Z/depth and interior vertices are
///    untouched); and
/// 2. split side-wall vertices from front/back vertices before recalculating normals, so side-wall
///    shading is averaged around the contour instead of being contaminated by the face normals.
///
/// Zero smoothness remains a strict compatibility path and returns the legacy mesh unchanged.
/// Triangle count is unchanged; only a small number of rim vertices are duplicated for hard normal
/// separation. This is deliberately cheaper than raising the whole runtime grid back to 256.
/// </summary>
public static class PartReplacementMeshPostprocessor
{
    private const float SideSignEpsilon = 0.000001f;
    private const float Relaxation = 0.42f;
    private const float AnchorWeight = 0.16f;

    public static CanonicalMesh Apply(CanonicalMesh mesh, GeometrySettings settings)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        if (settings.SurfaceSmoothness <= 0.000001 || mesh.TriangleCount == 0)
            return mesh;

        SmoothRim(mesh, settings.SurfaceSmoothness);
        return SplitSideWallNormals(mesh);
    }

    private static void SmoothRim(CanonicalMesh mesh, double smoothness)
    {
        var adjacency = new Dictionary<int, HashSet<int>>();
        for (int triangle = 0; triangle < mesh.Indices.Count; triangle += 3)
        {
            int a = checked((int)mesh.Indices[triangle]);
            int b = checked((int)mesh.Indices[triangle + 1]);
            int c = checked((int)mesh.Indices[triangle + 2]);
            if (!IsSideTriangle(mesh, a, b, c))
                continue;

            AddSameFaceEdge(a, b);
            AddSameFaceEdge(b, c);
            AddSameFaceEdge(c, a);
        }

        if (adjacency.Count == 0)
            return;

        Vector3[] anchors = mesh.Positions.ToArray();
        Vector3[] current = mesh.Positions.ToArray();
        Vector3[] next = mesh.Positions.ToArray();
        int passes = Math.Clamp((int)Math.Ceiling(Math.Clamp(smoothness, 0.0, 3.0) * 2.0), 1, 6);

        for (int pass = 0; pass < passes; pass++)
        {
            Array.Copy(current, next, current.Length);
            foreach ((int index, HashSet<int> neighbors) in adjacency)
            {
                // A regular closed contour has two same-face neighbours. Ambiguous diagonal-touch
                // pixels can produce a branch; keep those anchored rather than risking a foldover.
                if (neighbors.Count != 2)
                    continue;

                using IEnumerator<int> enumerator = neighbors.GetEnumerator();
                enumerator.MoveNext();
                int first = enumerator.Current;
                enumerator.MoveNext();
                int second = enumerator.Current;

                Vector2 currentXy = new(current[index].X, current[index].Y);
                Vector2 average = new(
                    (current[first].X + current[second].X) * 0.5f,
                    (current[first].Y + current[second].Y) * 0.5f);
                Vector2 anchor = new(anchors[index].X, anchors[index].Y);
                Vector2 relaxed = Vector2.Lerp(currentXy, average, Relaxation);
                relaxed = Vector2.Lerp(relaxed, anchor, AnchorWeight);
                next[index] = new Vector3(relaxed.X, relaxed.Y, current[index].Z);
            }
            (current, next) = (next, current);
        }

        for (int index = 0; index < current.Length; index++)
            mesh.Positions[index] = current[index];
        mesh.RecalculateNormals();
        return;

        void AddSameFaceEdge(int left, int right)
        {
            float leftZ = mesh.Positions[left].Z;
            float rightZ = mesh.Positions[right].Z;
            bool bothFront = leftZ > SideSignEpsilon && rightZ > SideSignEpsilon;
            bool bothBack = leftZ < -SideSignEpsilon && rightZ < -SideSignEpsilon;
            if (!bothFront && !bothBack)
                return;
            AddNeighbor(left, right);
            AddNeighbor(right, left);
        }

        void AddNeighbor(int index, int neighbor)
        {
            if (!adjacency.TryGetValue(index, out HashSet<int>? set))
            {
                set = [];
                adjacency[index] = set;
            }
            set.Add(neighbor);
        }
    }

    private static CanonicalMesh SplitSideWallNormals(CanonicalMesh source)
    {
        var result = new CanonicalMesh();
        var vertexMap = new Dictionary<(uint Source, bool Side), uint>();

        for (int triangle = 0; triangle < source.Indices.Count; triangle += 3)
        {
            uint a = source.Indices[triangle];
            uint b = source.Indices[triangle + 1];
            uint c = source.Indices[triangle + 2];
            bool side = IsSideTriangle(source, checked((int)a), checked((int)b), checked((int)c));
            result.AddTriangle(Vertex(a, side), Vertex(b, side), Vertex(c, side));
        }

        result.RecalculateNormals();
        return result;

        uint Vertex(uint sourceIndex, bool side)
        {
            var key = (sourceIndex, side);
            if (vertexMap.TryGetValue(key, out uint existing))
                return existing;
            int index = checked((int)sourceIndex);
            uint created = result.AddVertex(source.Positions[index], source.Uvs[index]);
            vertexMap.Add(key, created);
            return created;
        }
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
