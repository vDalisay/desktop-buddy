using System.Numerics;

namespace DesktopBuddy.AssetForge.Core;

/// <summary>
/// Presentation-quality cleanup for generated torso/foot meshes.
///
/// The part generator intentionally starts from a deterministic occupancy grid. That is useful for
/// stable UVs and runtime budgets, but a 128-cell grid leaves a high-frequency stair-step signal in
/// the XY perimeter. A small local Laplacian pass improves a front-on view, yet the alternating
/// turns remain very visible when the model is viewed obliquely.
///
/// For non-zero SurfaceSmoothness we therefore do two bounded, deterministic cleanup steps:
/// 1. extract each closed front/back rim from the generated side-wall topology and fair that 3D
///    contour with a Taubin-style positive/negative low-pass pair. This attacks the alternating
///    staircase turns much more strongly than the old one-neighbour relaxation while compensating
///    for Laplacian shrinkage. Every point is still displacement-limited to less than one authored
///    rim segment, so small deliberate user features are not free to collapse; and
/// 2. split side-wall vertices from front/back vertices before recalculating normals, so side-wall
///    shading is averaged around the contour instead of being contaminated by face normals.
///
/// The source PNG and occupancy mask are never modified. Zero smoothness remains a strict
/// compatibility path and returns the legacy mesh unchanged. Triangle count is unchanged; only a
/// small number of rim vertices are duplicated for hard normal separation. This is deliberately
/// cheaper than raising the whole runtime grid back to 256.
/// </summary>
public static class PartReplacementMeshPostprocessor
{
    private const float SideSignEpsilon = 0.000001f;
    private const float TaubinLambda = 0.50f;
    private const float TaubinMu = -0.53f;

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
        Dictionary<int, HashSet<int>> adjacency = BuildRimAdjacency(mesh);
        if (adjacency.Count == 0)
            return;

        IReadOnlyList<int[]> loops = ExtractClosedLoops(adjacency);
        if (loops.Count == 0)
            return;

        double clampedSmoothness = Math.Clamp(smoothness, 0.0, 3.0);
        int passes = Math.Clamp((int)Math.Ceiling(clampedSmoothness * 8.0), 1, 24);
        float displacementFraction = 0.60f + (float)clampedSmoothness * 0.05f;
        displacementFraction = Math.Clamp(displacementFraction, 0.60f, 0.75f);

        foreach (int[] loop in loops)
            FairClosedLoop(mesh, loop, passes, displacementFraction);

        mesh.RecalculateNormals();
    }

    private static Dictionary<int, HashSet<int>> BuildRimAdjacency(CanonicalMesh mesh)
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
        return adjacency;

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

    private static IReadOnlyList<int[]> ExtractClosedLoops(Dictionary<int, HashSet<int>> adjacency)
    {
        var loops = new List<int[]>();
        var consumed = new HashSet<int>();

        foreach (int seed in adjacency.Keys.OrderBy(static index => index))
        {
            if (consumed.Contains(seed))
                continue;

            var component = new List<int>();
            var pending = new Stack<int>();
            pending.Push(seed);
            consumed.Add(seed);
            bool regular = true;
            while (pending.Count > 0)
            {
                int current = pending.Pop();
                component.Add(current);
                if (!adjacency.TryGetValue(current, out HashSet<int>? neighbors) || neighbors.Count != 2)
                    regular = false;
                if (neighbors is null)
                    continue;
                foreach (int neighbor in neighbors)
                {
                    if (consumed.Add(neighbor))
                        pending.Push(neighbor);
                }
            }

            // Diagonal-touching mask islands can create branched boundary graphs. Preserving those
            // verbatim is preferable to guessing a path and potentially folding the authored mesh.
            if (!regular || component.Count < 4)
                continue;

            int start = component.Min();
            var loop = new List<int>(component.Count);
            int previous = -1;
            int currentVertex = start;
            for (int guard = 0; guard <= component.Count; guard++)
            {
                loop.Add(currentVertex);
                int[] neighbors = adjacency[currentVertex].OrderBy(static index => index).ToArray();
                int next = neighbors[0] == previous ? neighbors[1] : neighbors[0];
                if (next == start)
                    break;
                previous = currentVertex;
                currentVertex = next;
            }

            if (loop.Count == component.Count && adjacency[loop[^1]].Contains(start))
                loops.Add(loop.ToArray());
        }

        return loops;
    }

    private static void FairClosedLoop(
        CanonicalMesh mesh,
        int[] loop,
        int passes,
        float displacementFraction)
    {
        var anchors = new Vector2[loop.Length];
        var current = new Vector2[loop.Length];
        var scratch = new Vector2[loop.Length];
        float[] segmentLengths = new float[loop.Length];
        for (int i = 0; i < loop.Length; i++)
        {
            Vector3 position = mesh.Positions[loop[i]];
            anchors[i] = current[i] = new Vector2(position.X, position.Y);
        }
        for (int i = 0; i < loop.Length; i++)
            segmentLengths[i] = Vector2.Distance(anchors[i], anchors[(i + 1) % loop.Length]);

        Array.Sort(segmentLengths);
        float medianSegment = segmentLengths[segmentLengths.Length / 2];
        if (medianSegment <= 0.000001f)
            return;
        float maximumDisplacement = medianSegment * displacementFraction;

        for (int pass = 0; pass < passes; pass++)
        {
            FairStep(current, scratch, TaubinLambda);
            FairStep(scratch, current, TaubinMu);
            ClampToAuthoredNeighborhood(current, anchors, maximumDisplacement);
        }

        for (int i = 0; i < loop.Length; i++)
        {
            int index = loop[i];
            Vector3 original = mesh.Positions[index];
            mesh.Positions[index] = new Vector3(current[i].X, current[i].Y, original.Z);
        }
    }

    private static void FairStep(Vector2[] source, Vector2[] destination, float amount)
    {
        for (int i = 0; i < source.Length; i++)
        {
            Vector2 previous = source[(i - 1 + source.Length) % source.Length];
            Vector2 next = source[(i + 1) % source.Length];
            Vector2 average = (previous + next) * 0.5f;
            destination[i] = source[i] + (average - source[i]) * amount;
        }
    }

    private static void ClampToAuthoredNeighborhood(Vector2[] points, Vector2[] anchors, float maximumDisplacement)
    {
        float maximumSquared = maximumDisplacement * maximumDisplacement;
        for (int i = 0; i < points.Length; i++)
        {
            Vector2 delta = points[i] - anchors[i];
            float lengthSquared = delta.LengthSquared();
            if (lengthSquared <= maximumSquared || lengthSquared <= 0.000000000001f)
                continue;
            points[i] = anchors[i] + delta * (maximumDisplacement / MathF.Sqrt(lengthSquared));
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
