using System.Numerics;

namespace DesktopBuddy.AssetForge.Core;

/// <summary>
/// Deterministic micro-weld for the contour-conforming replacement path.
///
/// The same runtime-grid edge is clipped independently by its two incident triangles. Reversing the
/// endpoint order can produce sub-micropixel floating-point differences after projection onto the
/// full-resolution marching-squares contour. Those points are geometrically identical, but keeping
/// separate indices creates a topological crack. Welding in source-UV space at 0.01 source pixel is
/// far below visible/source resolution while making shared cut edges canonical.
/// </summary>
public static class PartReplacementTopologyWeld
{
    private const float SourcePixelsPerUv = PartReplacementTemplateSpace.CanvasSize;
    private const float WeldsPerSourcePixel = 100f; // 0.01 source pixel.
    private const float SideEpsilon = 0.00001f;

    private readonly record struct Key(int U, int V, int Surface);

    public static CanonicalMesh Apply(CanonicalMesh source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.TriangleCount == 0)
            return source;

        var result = new CanonicalMesh();
        var canonical = new Dictionary<Key, uint>();
        uint[] remap = new uint[source.Positions.Count];

        for (int i = 0; i < source.Positions.Count; i++)
        {
            Vector3 position = source.Positions[i];
            Vector2 uv = source.Uvs[i];
            int surface = position.Z > SideEpsilon ? 1 : position.Z < -SideEpsilon ? -1 : 0;
            var key = new Key(
                checked((int)MathF.Round(uv.X * SourcePixelsPerUv * WeldsPerSourcePixel)),
                checked((int)MathF.Round(uv.Y * SourcePixelsPerUv * WeldsPerSourcePixel)),
                surface);
            if (!canonical.TryGetValue(key, out uint index))
            {
                index = result.AddVertex(position, uv);
                canonical.Add(key, index);
            }
            remap[i] = index;
        }

        var triangles = new HashSet<(uint A, uint B, uint C)>();
        for (int i = 0; i < source.Indices.Count; i += 3)
        {
            uint a = remap[checked((int)source.Indices[i])];
            uint b = remap[checked((int)source.Indices[i + 1])];
            uint c = remap[checked((int)source.Indices[i + 2])];
            if (a == b || b == c || c == a)
                continue;

            // Do not keep duplicate coincident faces that can appear when an almost-zero cut polygon
            // collapses during the micro-weld. Preserve winding in the emitted triangle itself.
            (uint A, uint B, uint C) ordered = SortTriangle(a, b, c);
            if (!triangles.Add(ordered))
                continue;
            result.AddTriangle(a, b, c);
        }

        RecalculateMaxWeightedNormals(result);
        return result;
    }

    private static (uint A, uint B, uint C) SortTriangle(uint a, uint b, uint c)
    {
        if (a > b) (a, b) = (b, a);
        if (b > c) (b, c) = (c, b);
        if (a > b) (a, b) = (b, a);
        return (a, b, c);
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
}
