using System.Numerics;

namespace DesktopBuddy.AssetForge.Core;

/// <summary>
/// Final presentation polish for contour-inflated Buddy part replacements.
///
/// The contour generator deliberately preserves authored XY/UV positions. At very high Paint-Buddy
/// zoom, however, the first few interior rings can still expose the runtime grid as small depth
/// ripples, and max-weighted normals can pick the wrong side at a tight concave contour corner.
/// This pass changes neither silhouette nor UVs: it relaxes only Z near the rim, rebuilds smooth
/// normals, then derives the Z=0 rim normal from the actual contour tangent.
/// </summary>
public static class PartReplacementSurfacePolisher
{
    private const float BoundaryEpsilon = 0.00001f;
    private const int RelaxationRings = 3;

    public static CanonicalMesh Apply(CanonicalMesh mesh)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        if (mesh.TriangleCount == 0)
            return mesh;

        List<int>[] adjacency = BuildAdjacency(mesh);
        bool[] boundary = mesh.Positions
            .Select(static position => MathF.Abs(position.Z) <= BoundaryEpsilon)
            .ToArray();
        int[] ring = ResolveBoundaryRings(adjacency, boundary);

        // One conservative pass is enough to suppress high-frequency helper-grid/raster rows while
        // preserving the broad Poisson inflation profile. Only Z moves; source registration and the
        // authored silhouette remain byte-for-byte at the same XY/UV locations.
        float[] nextZ = mesh.Positions.Select(static position => position.Z).ToArray();
        for (int index = 0; index < mesh.Positions.Count; index++)
        {
            int distance = ring[index];
            if (distance is < 1 or > RelaxationRings)
                continue;

            float z = mesh.Positions[index].Z;
            int sign = z > BoundaryEpsilon ? 1 : z < -BoundaryEpsilon ? -1 : 0;
            if (sign == 0)
                continue;

            float total = 0f;
            int count = 0;
            foreach (int neighbour in adjacency[index])
            {
                float neighbourZ = mesh.Positions[neighbour].Z;
                int neighbourSign = neighbourZ > BoundaryEpsilon ? 1 : neighbourZ < -BoundaryEpsilon ? -1 : 0;
                if (neighbourSign != 0 && neighbourSign != sign)
                    continue;
                total += MathF.Abs(neighbourZ);
                count++;
            }
            if (count == 0)
                continue;

            float blend = distance switch
            {
                1 => 0.26f,
                2 => 0.14f,
                _ => 0.07f,
            };
            float relaxed = Lerp(MathF.Abs(z), total / count, blend);
            nextZ[index] = sign * relaxed;
        }

        for (int index = 0; index < mesh.Positions.Count; index++)
        {
            Vector3 position = mesh.Positions[index];
            if (MathF.Abs(nextZ[index] - position.Z) > 0.000000001f)
                mesh.Positions[index] = new Vector3(position.X, position.Y, nextZ[index]);
        }

        RecalculateMaxWeightedNormals(mesh);
        RepairBoundaryNormals(mesh, adjacency, boundary);
        return mesh;
    }

    private static List<int>[] BuildAdjacency(CanonicalMesh mesh)
    {
        var sets = new HashSet<int>[mesh.Positions.Count];
        for (int index = 0; index < sets.Length; index++)
            sets[index] = [];

        for (int triangle = 0; triangle < mesh.Indices.Count; triangle += 3)
        {
            int a = checked((int)mesh.Indices[triangle]);
            int b = checked((int)mesh.Indices[triangle + 1]);
            int c = checked((int)mesh.Indices[triangle + 2]);
            sets[a].Add(b); sets[a].Add(c);
            sets[b].Add(a); sets[b].Add(c);
            sets[c].Add(a); sets[c].Add(b);
        }
        return sets.Select(static neighbours => neighbours.Order().ToList()).ToArray();
    }

    private static int[] ResolveBoundaryRings(IReadOnlyList<List<int>> adjacency, IReadOnlyList<bool> boundary)
    {
        int[] ring = Enumerable.Repeat(int.MaxValue, adjacency.Count).ToArray();
        var queue = new Queue<int>();
        for (int index = 0; index < boundary.Count; index++)
        {
            if (!boundary[index])
                continue;
            ring[index] = 0;
            queue.Enqueue(index);
        }

        while (queue.Count > 0)
        {
            int current = queue.Dequeue();
            if (ring[current] >= RelaxationRings)
                continue;
            int next = ring[current] + 1;
            foreach (int neighbour in adjacency[current])
            {
                if (ring[neighbour] <= next)
                    continue;
                ring[neighbour] = next;
                queue.Enqueue(neighbour);
            }
        }
        return ring;
    }

    private static void RepairBoundaryNormals(
        CanonicalMesh mesh,
        IReadOnlyList<List<int>> adjacency,
        IReadOnlyList<bool> boundary)
    {
        for (int index = 0; index < mesh.Positions.Count; index++)
        {
            if (!boundary[index])
                continue;

            int[] contourNeighbours = adjacency[index]
                .Where(neighbour => boundary[neighbour])
                .ToArray();
            if (contourNeighbours.Length != 2)
                continue;

            Vector2 before = ToXy(mesh.Positions[contourNeighbours[0]]);
            Vector2 after = ToXy(mesh.Positions[contourNeighbours[1]]);
            Vector2 tangent = after - before;
            if (tangent.LengthSquared() <= 0.000000000001f)
                continue;
            tangent = Vector2.Normalize(tangent);
            Vector2 candidate = new(tangent.Y, -tangent.X);

            Vector2 centre = ToXy(mesh.Positions[index]);
            Vector2 interiorCentre = Vector2.Zero;
            int interiorCount = 0;
            foreach (int neighbour in adjacency[index])
            {
                if (boundary[neighbour])
                    continue;
                interiorCentre += ToXy(mesh.Positions[neighbour]);
                interiorCount++;
            }
            if (interiorCount > 0)
            {
                interiorCentre /= interiorCount;
                if (Vector2.Dot(candidate, centre - interiorCentre) < 0f)
                    candidate = -candidate;
            }
            else
            {
                Vector2 current = new(mesh.Normals[index].X, mesh.Normals[index].Y);
                if (Vector2.Dot(candidate, current) < 0f)
                    candidate = -candidate;
            }

            mesh.Normals[index] = new Vector3(candidate.X, candidate.Y, 0f);
        }
    }

    private static void RecalculateMaxWeightedNormals(CanonicalMesh mesh)
    {
        for (int index = 0; index < mesh.Normals.Count; index++)
            mesh.Normals[index] = Vector3.Zero;

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

        for (int index = 0; index < mesh.Normals.Count; index++)
        {
            float lengthSquared = mesh.Normals[index].LengthSquared();
            mesh.Normals[index] = lengthSquared > 0.000000000001f
                ? Vector3.Normalize(mesh.Normals[index])
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

    private static Vector2 ToXy(Vector3 value) => new(value.X, value.Y);
    private static float Lerp(float a, float b, float t) => a + (b - a) * t;
}
