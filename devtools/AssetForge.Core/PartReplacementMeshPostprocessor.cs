using System.Numerics;

namespace DesktopBuddy.AssetForge.Core;

/// <summary>
/// Presentation-quality cleanup for generated torso/foot meshes.
///
/// The source is intentionally sampled onto a deterministic occupancy grid. That gives stable UVs
/// and predictable runtime cost, but it also produces two artifacts which are especially visible on
/// plush/cartoon Buddy parts:
/// - a high-frequency stair-step signal in the XY silhouette; and
/// - a hard front/side/back crease because the canonical generator closes the mesh with one flat
///   side-wall quad per mask edge.
///
/// For non-zero SurfaceSmoothness this postprocessor now treats that border the way a modeller would
/// treat a low-poly bevel: it fairs the authored 3D rim, feathers that displacement into a narrow cap
/// band, and (when Edge roundness is non-zero) replaces the single flat side wall with a small curved
/// bevel shell. The front/back cap vertices and the bevel share vertices, so recalculated normals are
/// continuous across the transition instead of deliberately creating a hard shading seam.
///
/// The source PNG and occupancy mask are never modified. The outermost point of the rounded shell is
/// still the faired authored contour; the front/back cap rim moves inward by less than roughly one
/// local grid segment to make room for the bevel. Zero SurfaceSmoothness remains the strict legacy
/// compatibility path. The bevel adds only O(perimeter) geometry and is capped at four side segments.
/// </summary>
public static class PartReplacementMeshPostprocessor
{
    private const float SideSignEpsilon = 0.000001f;
    private const float TaubinLambda = 0.50f;
    private const float TaubinMu = -0.53f;
    private const int MaximumBevelSegments = 4;
    private const int CapTransitionRings = 3;

    private readonly record struct UvKey(int U, int V);

    private sealed record RoundedLoop(
        int[] Front,
        int[] Back,
        Vector2[] Outer,
        Vector2[] Inner,
        Vector2[] Outward,
        float Radius,
        bool CounterClockwise);

    public static CanonicalMesh Apply(CanonicalMesh mesh, GeometrySettings settings)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        if (settings.SurfaceSmoothness <= 0.000001 || mesh.TriangleCount == 0)
            return mesh;

        SmoothRim(mesh, settings.SurfaceSmoothness);

        float roundness = (float)Math.Clamp(settings.Roundness, 0.0, 1.0);
        if (roundness > 0.0001f)
            return BuildRoundedSideShell(mesh, settings.SurfaceSmoothness, roundness);

        SmoothCapTransition(mesh, BuildRimAdjacency(mesh).Keys, settings.SurfaceSmoothness, 0f);
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
        int passes = Math.Clamp((int)Math.Ceiling(clampedSmoothness * 10.0), 1, 30);

        // The previous <=0.75-segment clamp was safe but visually too subtle on a 64/128 grid.
        // A wider, still-local clamp lets the low-pass remove alternating pixel-grid turns while
        // keeping every vertex tied to its authored neighbourhood. Higher smoothness is now visibly
        // stronger rather than merely doing more iterations against the same tiny displacement cap.
        float displacementFraction = 1.15f + (float)clampedSmoothness * 0.28f;
        displacementFraction = Math.Clamp(displacementFraction, 1.15f, 2.0f);

        foreach (int[] loop in loops)
            FairClosedLoop(mesh, loop, passes, displacementFraction);

        mesh.RecalculateNormals();
    }

    private static CanonicalMesh BuildRoundedSideShell(
        CanonicalMesh source,
        double smoothness,
        float roundness)
    {
        Dictionary<int, HashSet<int>> rimAdjacency = BuildRimAdjacency(source);
        IReadOnlyList<int[]> allLoops = ExtractClosedLoops(rimAdjacency);
        int[][] frontLoops = allLoops
            .Where(loop => loop.Length >= 4 && loop.Average(index => source.Positions[index].Z) > SideSignEpsilon)
            .ToArray();
        if (frontLoops.Length == 0)
        {
            SmoothCapTransition(source, rimAdjacency.Keys, smoothness, roundness);
            return SplitSideWallNormals(source);
        }

        var backByUv = new Dictionary<UvKey, int>();
        foreach (int index in rimAdjacency.Keys)
        {
            if (source.Positions[index].Z < -SideSignEpsilon)
                backByUv[Key(source.Uvs[index])] = index;
        }

        var roundedLoops = new List<RoundedLoop>(frontLoops.Length);
        var roundedRimVertices = new HashSet<int>();
        foreach (int[] front in frontLoops)
        {
            int[] back = new int[front.Length];
            bool complete = true;
            for (int i = 0; i < front.Length; i++)
            {
                if (!backByUv.TryGetValue(Key(source.Uvs[front[i]]), out back[i]))
                {
                    complete = false;
                    break;
                }
            }
            if (!complete)
                continue;

            Vector2[] outer = front.Select(index => Xy(source.Positions[index])).ToArray();
            float[] segments = new float[outer.Length];
            for (int i = 0; i < outer.Length; i++)
                segments[i] = Vector2.Distance(outer[i], outer[(i + 1) % outer.Length]);
            Array.Sort(segments);
            float medianSegment = segments[segments.Length / 2];
            if (medianSegment <= 0.000001f)
                continue;

            bool counterClockwise = SignedArea(outer) > 0f;
            Vector2[] outward = BuildOutwardNormals(outer, counterClockwise);

            // Preserve the faired contour as the outer silhouette and inset only the cap junction.
            // At the default 0.9 roundness this is ~0.88 of one local rim segment: large enough to
            // produce a visible curved shoulder without eating meaningful authored silhouette data.
            float radius = medianSegment * (0.30f + roundness * 0.65f);
            radius = Math.Clamp(radius, medianSegment * 0.30f, medianSegment * 0.95f);
            Vector2[] inner = new Vector2[outer.Length];
            for (int i = 0; i < outer.Length; i++)
            {
                inner[i] = outer[i] - outward[i] * radius;
                MoveXy(source, front[i], inner[i]);
                MoveXy(source, back[i], inner[i]);
                roundedRimVertices.Add(front[i]);
                roundedRimVertices.Add(back[i]);
            }

            roundedLoops.Add(new RoundedLoop(front, back, outer, inner, outward, radius, counterClockwise));
        }

        if (roundedLoops.Count == 0)
        {
            SmoothCapTransition(source, rimAdjacency.Keys, smoothness, roundness);
            return SplitSideWallNormals(source);
        }

        // The rim has just moved inward to create room for the bevel. Relax a narrow three-ring cap
        // band in full XYZ so the cap does not form long/folded triangles against that moved border.
        // This also softens the last discrete depth steps before the rounded side shell.
        SmoothCapTransition(source, roundedRimVertices, smoothness, roundness);

        var result = new CanonicalMesh();
        var sourceMap = new Dictionary<uint, uint>();

        // Keep the generated front/back surfaces, but discard the original one-quad-thick side wall.
        for (int triangle = 0; triangle < source.Indices.Count; triangle += 3)
        {
            uint a = source.Indices[triangle];
            uint b = source.Indices[triangle + 1];
            uint c = source.Indices[triangle + 2];
            if (IsSideTriangle(source, checked((int)a), checked((int)b), checked((int)c)))
                continue;
            result.AddTriangle(Copy(a), Copy(b), Copy(c));
        }

        int bevelSegments = Math.Clamp(2 + (int)MathF.Round(roundness * 2f), 2, MaximumBevelSegments);
        foreach (RoundedLoop loop in roundedLoops)
            AddRoundedLoop(loop, bevelSegments);

        // Unlike the previous side-wall split, the cap and bevel deliberately share endpoint
        // vertices. Recalculation therefore averages normals across the front/bevel/back shoulder and
        // removes the harsh dark ring that was visible even with a perfectly clean circular source.
        result.RecalculateNormals();
        return result;

        uint Copy(uint sourceIndex)
        {
            if (sourceMap.TryGetValue(sourceIndex, out uint existing))
                return existing;
            int index = checked((int)sourceIndex);
            uint created = result.AddVertex(source.Positions[index], source.Uvs[index]);
            sourceMap.Add(sourceIndex, created);
            return created;
        }

        void AddRoundedLoop(RoundedLoop loop, int segments)
        {
            var rings = new uint[segments + 1][];
            for (int segment = 0; segment <= segments; segment++)
            {
                rings[segment] = new uint[loop.Front.Length];
                if (segment == 0)
                {
                    for (int i = 0; i < loop.Front.Length; i++)
                        rings[segment][i] = Copy(checked((uint)loop.Front[i]));
                    continue;
                }
                if (segment == segments)
                {
                    for (int i = 0; i < loop.Back.Length; i++)
                        rings[segment][i] = Copy(checked((uint)loop.Back[i]));
                    continue;
                }

                float t = segment / (float)segments;
                float theta = MathF.PI * 0.5f - MathF.PI * t;
                float bulge = MathF.Cos(theta);
                float sine = MathF.Sin(theta);
                for (int i = 0; i < loop.Front.Length; i++)
                {
                    Vector3 front = source.Positions[loop.Front[i]];
                    Vector3 back = source.Positions[loop.Back[i]];
                    Vector2 xy = loop.Inner[i] + loop.Outward[i] * (loop.Radius * bulge);
                    float z = sine >= 0f
                        ? MathF.Abs(front.Z) * sine
                        : MathF.Abs(back.Z) * sine;
                    rings[segment][i] = result.AddVertex(
                        new Vector3(xy.X, xy.Y, z),
                        source.Uvs[loop.Front[i]]);
                }
            }

            for (int segment = 0; segment < segments; segment++)
            for (int i = 0; i < loop.Front.Length; i++)
            {
                int next = (i + 1) % loop.Front.Length;
                uint a = rings[segment][i];
                uint b = rings[segment][next];
                uint c = rings[segment + 1][i];
                uint d = rings[segment + 1][next];

                // A CCW contour viewed from +Z needs the strip winding reversed so its normals point
                // away from the solid. CW contours use the opposite winding.
                if (loop.CounterClockwise)
                {
                    result.AddTriangle(a, d, b);
                    result.AddTriangle(a, c, d);
                }
                else
                {
                    result.AddTriangle(a, b, d);
                    result.AddTriangle(a, d, c);
                }
            }
        }
    }

    private static void SmoothCapTransition(
        CanonicalMesh mesh,
        IEnumerable<int> rimVertices,
        double smoothness,
        float roundness)
    {
        var rim = rimVertices.ToHashSet();
        if (rim.Count == 0)
            return;

        var adjacency = new Dictionary<int, HashSet<int>>();
        for (int triangle = 0; triangle < mesh.Indices.Count; triangle += 3)
        {
            int a = checked((int)mesh.Indices[triangle]);
            int b = checked((int)mesh.Indices[triangle + 1]);
            int c = checked((int)mesh.Indices[triangle + 2]);
            if (IsSideTriangle(mesh, a, b, c))
                continue;
            Add(a, b); Add(b, c); Add(c, a);
        }

        var distance = new Dictionary<int, int>();
        var queue = new Queue<int>();
        foreach (int seed in rim)
        {
            distance[seed] = 0;
            queue.Enqueue(seed);
        }
        while (queue.Count > 0)
        {
            int current = queue.Dequeue();
            int nextDistance = distance[current] + 1;
            if (nextDistance > CapTransitionRings || !adjacency.TryGetValue(current, out HashSet<int>? neighbors))
                continue;
            foreach (int neighbor in neighbors)
            {
                if (distance.ContainsKey(neighbor))
                    continue;
                distance[neighbor] = nextDistance;
                queue.Enqueue(neighbor);
            }
        }

        Vector3[] currentPositions = mesh.Positions.ToArray();
        Vector3[] nextPositions = mesh.Positions.ToArray();
        float baseStrength = 0.10f + (float)Math.Clamp(smoothness, 0.0, 3.0) * 0.035f + roundness * 0.08f;
        baseStrength = Math.Clamp(baseStrength, 0.10f, 0.28f);

        for (int pass = 0; pass < 2; pass++)
        {
            Array.Copy(currentPositions, nextPositions, currentPositions.Length);
            foreach ((int index, int ring) in distance)
            {
                if (ring <= 0 || ring > CapTransitionRings || !adjacency.TryGetValue(index, out HashSet<int>? neighbors) || neighbors.Count == 0)
                    continue;

                Vector3 average = Vector3.Zero;
                foreach (int neighbor in neighbors)
                    average += currentPositions[neighbor];
                average /= neighbors.Count;
                float falloff = (CapTransitionRings + 1 - ring) / (float)CapTransitionRings;
                float amount = baseStrength * falloff;
                nextPositions[index] = Vector3.Lerp(currentPositions[index], average, amount);
            }
            (currentPositions, nextPositions) = (nextPositions, currentPositions);
        }

        foreach ((int index, int ring) in distance)
        {
            if (ring > 0 && ring <= CapTransitionRings)
                mesh.Positions[index] = currentPositions[index];
        }
        mesh.RecalculateNormals();
        return;

        void Add(int left, int right)
        {
            AddOne(left, right);
            AddOne(right, left);
        }

        void AddOne(int index, int neighbor)
        {
            if (!adjacency.TryGetValue(index, out HashSet<int>? set))
            {
                set = [];
                adjacency[index] = set;
            }
            set.Add(neighbor);
        }
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

    private static Vector2[] BuildOutwardNormals(Vector2[] loop, bool counterClockwise)
    {
        var normals = new Vector2[loop.Length];
        for (int i = 0; i < loop.Length; i++)
        {
            Vector2 tangent = loop[(i + 1) % loop.Length] - loop[(i - 1 + loop.Length) % loop.Length];
            if (tangent.LengthSquared() <= 0.000000000001f)
            {
                normals[i] = Vector2.Zero;
                continue;
            }
            tangent = Vector2.Normalize(tangent);
            normals[i] = counterClockwise
                ? new Vector2(tangent.Y, -tangent.X)
                : new Vector2(-tangent.Y, tangent.X);
        }
        return normals;
    }

    private static float SignedArea(Vector2[] points)
    {
        double area = 0.0;
        for (int i = 0; i < points.Length; i++)
        {
            Vector2 a = points[i];
            Vector2 b = points[(i + 1) % points.Length];
            area += (double)a.X * b.Y - (double)b.X * a.Y;
        }
        return (float)(area * 0.5);
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

    private static UvKey Key(Vector2 uv) => new(
        BitConverter.SingleToInt32Bits(uv.X),
        BitConverter.SingleToInt32Bits(uv.Y));

    private static Vector2 Xy(Vector3 value) => new(value.X, value.Y);

    private static void MoveXy(CanonicalMesh mesh, int index, Vector2 xy)
    {
        Vector3 value = mesh.Positions[index];
        mesh.Positions[index] = new Vector3(xy.X, xy.Y, value.Z);
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
