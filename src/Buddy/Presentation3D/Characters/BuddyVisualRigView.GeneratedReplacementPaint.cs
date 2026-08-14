using System;
using System.Collections.Generic;
using DesktopBuddy.Buddy.Physics;
using DesktopBuddy.Domain.Painting;
using Godot;

namespace DesktopBuddy.Buddy.Presentation3D;

/// <summary>
/// Presentation-only support for Asset Forge torso/foot replacements. Generated meshes stay visual
/// replacements; the trusted 2D bodies remain authoritative. This seam keeps outline/paint shell
/// thickness in world units, binds the existing character paint surfaces to the replacement mesh,
/// and performs editor-only triangle/UV hit testing against the visible generated geometry.
/// </summary>
public partial class BuddyVisualRigView
{
    private const string GeneratedOutlineName = "GeneratedOutline";
    private const string GeneratedPaintName = "GeneratedPaint";
    private static readonly Dictionary<ulong, GeneratedPaintMeshCache> GeneratedPaintCaches = [];

    private void RefreshGeneratedReplacementVisuals()
    {
        if (!IsInitialized) return;
        RefreshGeneratedReplacement(_topVisual, BuddyPartId.Torso, PaintPart.Torso);
        RefreshGeneratedReplacement(_shoesVisual, BuddyPartId.LeftFoot, PaintPart.LeftFoot);
        RefreshGeneratedReplacement(_rightShoesVisual, BuddyPartId.RightFoot, PaintPart.RightFoot);
    }

    private void RefreshGeneratedReplacement(Node3D? visualRoot, BuddyPartId partId, PaintPart paintPart)
    {
        if (!IsPartVisualReplaced(partId) || !GodotObject.IsInstanceValid(visualRoot)) return;
        MeshInstance3D? surface = FindGeneratedReplacementSurface(visualRoot!);
        if (!GodotObject.IsInstanceValid(surface) || !GodotObject.IsInstanceValid(surface!.Mesh)) return;

        float targetRadius = PartMeshRadius(partId);
        MeshInstance3D? outline = surface.GetNodeOrNull<MeshInstance3D>(GeneratedOutlineName);
        if (GodotObject.IsInstanceValid(outline))
        {
            StandardMaterial3D? existingOutline = outline!.MaterialOverride as StandardMaterial3D;
            if (existingOutline is null || existingOutline.ResourceName != "BuddyLookScaledOutlineMaterial")
                outline.MaterialOverride = _materials.CreateScaledOutlineMaterial(targetRadius);
            outline.Scale = Vector3.One * _materials.ReplacementOutlineScale(targetRadius);
        }

        MeshInstance3D? paint = surface.GetNodeOrNull<MeshInstance3D>(GeneratedPaintName);
        if (!GodotObject.IsInstanceValid(paint))
        {
            StandardMaterial3D paintMaterial = _materials.CreateScaledPaintMaterial(targetRadius);
            if (PaintUvRegion.IsLimb(paintPart))
                paintMaterial.Uv1Scale = new Vector3(0.5f, 1.0f, 1.0f);
            paint = new MeshInstance3D
            {
                Name = GeneratedPaintName,
                Mesh = surface.Mesh,
                MaterialOverride = paintMaterial,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Inherit,
                Visible = false,
            };
            surface.AddChild(paint);
        }

        Texture2D? texture = _surfaceUnderlays[(int)partId];
        if (paint!.MaterialOverride is StandardMaterial3D material &&
            !ReferenceEquals(material.AlbedoTexture, texture))
            material.AlbedoTexture = texture;
        paint.Visible = texture is not null;
    }

    internal bool TryMapGeneratedReplacementPaintHit(Vector2 paintWorldPoint, out PaintHit hit)
    {
        hit = default;
        if (!IsInitialized) return false;

        bool found = false;
        float nearestCameraDistance = float.PositiveInfinity;
        PaintHit bestHit = default;
        TryCandidate(_topVisual, BuddyPartId.Torso, PaintPart.Torso);
        TryCandidate(_shoesVisual, BuddyPartId.LeftFoot, PaintPart.LeftFoot);
        TryCandidate(_rightShoesVisual, BuddyPartId.RightFoot, PaintPart.RightFoot);
        hit = bestHit;
        return found;

        void TryCandidate(Node3D? visualRoot, BuddyPartId partId, PaintPart paintPart)
        {
            if (!IsPartVisualReplaced(partId) || !GodotObject.IsInstanceValid(visualRoot)) return;
            MeshInstance3D? surface = FindGeneratedReplacementSurface(visualRoot!);
            if (!GodotObject.IsInstanceValid(surface) || !GodotObject.IsInstanceValid(surface!.Mesh)) return;
            if (!TryRaycastGeneratedSurface(surface, paintWorldPoint, out Vector2 uv, out float candidateDistance, out float candidateDepth)) return;
            if (found && candidateDistance >= nearestCameraDistance) return;

            PaintPoint mapped = new(uv.X, uv.Y);
            if (PaintUvRegion.IsLimb(paintPart))
                mapped = PaintUvRegion.LimbEnd.MapLocal(mapped);
            bestHit = new PaintHit(paintPart, mapped, candidateDepth);
            nearestCameraDistance = candidateDistance;
            found = true;
        }
    }

    internal bool IsGeneratedReplacementPaintPart(PaintPart part) => part switch
    {
        PaintPart.Torso => _torsoVisualReplaced,
        PaintPart.LeftFoot => _leftFootVisualReplaced,
        PaintPart.RightFoot => _rightFootVisualReplaced,
        _ => false,
    };

    internal int GeneratedReplacementPaintShellCountForTest
    {
        get
        {
            int count = 0;
            Count(_topVisual, BuddyPartId.Torso);
            Count(_shoesVisual, BuddyPartId.LeftFoot);
            Count(_rightShoesVisual, BuddyPartId.RightFoot);
            return count;

            void Count(Node3D? root, BuddyPartId part)
            {
                if (!IsPartVisualReplaced(part) || !GodotObject.IsInstanceValid(root)) return;
                MeshInstance3D? surface = FindGeneratedReplacementSurface(root!);
                if (GodotObject.IsInstanceValid(surface?.GetNodeOrNull<MeshInstance3D>(GeneratedPaintName))) count++;
            }
        }
    }

    internal bool GeneratedReplacementOutlineScaleIsCorrectForTest
    {
        get
        {
            bool ok = true;
            Check(_topVisual, BuddyPartId.Torso);
            Check(_shoesVisual, BuddyPartId.LeftFoot);
            Check(_rightShoesVisual, BuddyPartId.RightFoot);
            return ok;

            void Check(Node3D? root, BuddyPartId part)
            {
                if (!IsPartVisualReplaced(part) || !GodotObject.IsInstanceValid(root)) return;
                MeshInstance3D? surface = FindGeneratedReplacementSurface(root!);
                MeshInstance3D? outline = surface?.GetNodeOrNull<MeshInstance3D>(GeneratedOutlineName);
                if (outline?.MaterialOverride is not StandardMaterial3D material)
                {
                    ok = false;
                    return;
                }
                float radius = PartMeshRadius(part);
                float wantedScale = _materials.ReplacementOutlineScale(radius);
                ok &= !material.Grow && outline.Scale.IsEqualApprox(Vector3.One * wantedScale);
            }
        }
    }

    private static MeshInstance3D? FindGeneratedReplacementSurface(Node3D visualRoot)
    {
        Node? generatedRoot = visualRoot.FindChild("GeneratedMesh", true, false);
        if (!GodotObject.IsInstanceValid(generatedRoot)) return null;
        if (generatedRoot is MeshInstance3D direct) return direct;
        foreach (Node child in generatedRoot!.FindChildren("*", nameof(MeshInstance3D), true, false))
        {
            if (child is MeshInstance3D mesh && mesh.Name != GeneratedOutlineName && mesh.Name != GeneratedPaintName)
                return mesh;
        }
        return null;
    }

    /// <summary>
    /// Fast replacement paint hit-test. Mesh arrays are immutable for an equipped generated asset,
    /// so extraction and BVH construction happen once per imported ArrayMesh. Each pointer sample
    /// transforms one ray into mesh-local space and traverses only intersected BVH nodes instead of
    /// scanning tens of thousands of triangles. This keeps old 256-resolution assets usable while
    /// new replacements export at the lighter 128-resolution default.
    /// </summary>
    private static bool TryRaycastGeneratedSurface(
        MeshInstance3D surface,
        Vector2 paintWorldPoint,
        out Vector2 hitUv,
        out float cameraDistance,
        out float worldDepth)
    {
        hitUv = default;
        cameraDistance = float.PositiveInfinity;
        worldDepth = float.NegativeInfinity;
        if (surface.Mesh is not ArrayMesh mesh) return false;

        ulong cacheKey = mesh.GetInstanceId();
        if (!GeneratedPaintCaches.TryGetValue(cacheKey, out GeneratedPaintMeshCache? cache))
        {
            cache = GeneratedPaintMeshCache.Build(mesh);
            GeneratedPaintCaches[cacheKey] = cache;
        }
        if (cache.Triangles.Length == 0 || cache.Nodes.Length == 0) return false;

        Vector3 worldOrigin = new(paintWorldPoint.X, -paintWorldPoint.Y, 4096f);
        Vector3 worldDirection = new(0f, 0f, -1f);
        Transform3D inverse = surface.GlobalTransform.AffineInverse();
        Vector3 localOrigin = inverse * worldOrigin;
        Vector3 localDirection = (inverse.Basis * worldDirection).Normalized();

        bool found = false;
        float bestWorldDistance = float.PositiveInfinity;
        float bestWorldDepth = float.NegativeInfinity;
        Vector2 bestUv = default;
        int[] stack = new int[Math.Max(64, cache.MaxTreeDepth * 2 + 8)];
        int stackCount = 1;
        stack[0] = 0;

        while (stackCount > 0)
        {
            BvhNode node = cache.Nodes[stack[--stackCount]];
            if (!RayAabb(localOrigin, localDirection, node.Min, node.Max, out _)) continue;

            if (node.IsLeaf)
            {
                int end = node.Start + node.Count;
                for (int i = node.Start; i < end; i++)
                {
                    GeneratedPaintTriangle triangle = cache.Triangles[i];
                    if (!RayTriangle(localOrigin, localDirection, triangle.A, triangle.B, triangle.C,
                            out float localDistance, out float baryB, out float baryC)) continue;
                    Vector3 localHit = localOrigin + localDirection * localDistance;
                    Vector3 worldHit = surface.GlobalTransform * localHit;
                    float candidate = worldOrigin.DistanceTo(worldHit);
                    if (candidate >= bestWorldDistance) continue;
                    float baryA = 1f - baryB - baryC;
                    bestUv = triangle.UvA * baryA + triangle.UvB * baryB + triangle.UvC * baryC;
                    bestWorldDistance = candidate;
                    bestWorldDepth = worldHit.Z;
                    found = true;
                }
                continue;
            }

            if (stackCount + 2 > stack.Length) Array.Resize(ref stack, stack.Length * 2);
            // Traversal order is deterministic. The closest triangle is still chosen by actual
            // world-ray distance, so ordering has no effect on authored paint results.
            stack[stackCount++] = node.Right;
            stack[stackCount++] = node.Left;
        }

        hitUv = bestUv;
        cameraDistance = bestWorldDistance;
        worldDepth = bestWorldDepth;
        return found;
    }

    private static bool RayAabb(Vector3 origin, Vector3 direction, Vector3 min, Vector3 max, out float nearDistance)
    {
        float tMin = 0f;
        float tMax = float.PositiveInfinity;
        bool hit = Axis(origin.X, direction.X, min.X, max.X, ref tMin, ref tMax) &&
                   Axis(origin.Y, direction.Y, min.Y, max.Y, ref tMin, ref tMax) &&
                   Axis(origin.Z, direction.Z, min.Z, max.Z, ref tMin, ref tMax);
        nearDistance = tMin;
        return hit;

        static bool Axis(float originValue, float directionValue, float minValue, float maxValue, ref float near, ref float far)
        {
            const float epsilon = 0.000001f;
            if (Mathf.Abs(directionValue) <= epsilon)
                return originValue >= minValue && originValue <= maxValue;
            float inv = 1f / directionValue;
            float a = (minValue - originValue) * inv;
            float b = (maxValue - originValue) * inv;
            if (a > b) (a, b) = (b, a);
            near = Mathf.Max(near, a);
            far = Mathf.Min(far, b);
            return far >= near;
        }
    }

    private static bool RayTriangle(
        Vector3 origin,
        Vector3 direction,
        Vector3 a,
        Vector3 b,
        Vector3 c,
        out float distance,
        out float baryB,
        out float baryC)
    {
        const float epsilon = 0.000001f;
        distance = baryB = baryC = 0f;
        Vector3 edge1 = b - a;
        Vector3 edge2 = c - a;
        Vector3 p = direction.Cross(edge2);
        float determinant = edge1.Dot(p);
        if (Mathf.Abs(determinant) <= epsilon) return false;
        float inverse = 1f / determinant;
        Vector3 t = origin - a;
        baryB = t.Dot(p) * inverse;
        if (baryB < -epsilon || baryB > 1f + epsilon) return false;
        Vector3 q = t.Cross(edge1);
        baryC = direction.Dot(q) * inverse;
        if (baryC < -epsilon || baryB + baryC > 1f + epsilon) return false;
        distance = edge2.Dot(q) * inverse;
        return distance >= 0f;
    }

    private sealed class GeneratedPaintMeshCache
    {
        private const int LeafSize = 16;
        public GeneratedPaintTriangle[] Triangles { get; }
        public BvhNode[] Nodes { get; }
        public int MaxTreeDepth { get; }

        private GeneratedPaintMeshCache(GeneratedPaintTriangle[] triangles, BvhNode[] nodes, int maxTreeDepth)
        {
            Triangles = triangles;
            Nodes = nodes;
            MaxTreeDepth = maxTreeDepth;
        }

        public static GeneratedPaintMeshCache Build(ArrayMesh mesh)
        {
            var triangleList = new List<GeneratedPaintTriangle>();
            for (int surfaceIndex = 0; surfaceIndex < mesh.GetSurfaceCount(); surfaceIndex++)
            {
                if (mesh.SurfaceGetPrimitiveType(surfaceIndex) != Mesh.PrimitiveType.Triangles) continue;
                Godot.Collections.Array arrays = mesh.SurfaceGetArrays(surfaceIndex);
                Vector3[] vertices = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
                Vector2[] uvs = arrays[(int)Mesh.ArrayType.TexUV].AsVector2Array();
                int[] indices = arrays[(int)Mesh.ArrayType.Index].AsInt32Array();
                if (vertices.Length < 3 || uvs.Length != vertices.Length) continue;

                if (indices.Length >= 3)
                {
                    for (int i = 0; i + 2 < indices.Length; i += 3)
                        Add(indices[i], indices[i + 1], indices[i + 2]);
                }
                else
                {
                    for (int i = 0; i + 2 < vertices.Length; i += 3)
                        Add(i, i + 1, i + 2);
                }

                void Add(int ia, int ib, int ic)
                {
                    if ((uint)ia >= (uint)vertices.Length || (uint)ib >= (uint)vertices.Length || (uint)ic >= (uint)vertices.Length) return;
                    triangleList.Add(GeneratedPaintTriangle.Create(
                        vertices[ia], vertices[ib], vertices[ic],
                        uvs[ia], uvs[ib], uvs[ic]));
                }
            }

            GeneratedPaintTriangle[] triangles = triangleList.ToArray();
            if (triangles.Length == 0) return new GeneratedPaintMeshCache(triangles, [], 0);
            var nodes = new List<BvhNode>(triangles.Length / LeafSize * 2 + 1);
            int maxDepth = 0;
            BuildNode(triangles, nodes, 0, triangles.Length, 0, ref maxDepth);
            return new GeneratedPaintMeshCache(triangles, nodes.ToArray(), maxDepth);
        }

        private static int BuildNode(
            GeneratedPaintTriangle[] triangles,
            List<BvhNode> nodes,
            int start,
            int count,
            int depth,
            ref int maxDepth)
        {
            maxDepth = Math.Max(maxDepth, depth);
            Bounds(triangles, start, count, out Vector3 min, out Vector3 max);
            int nodeIndex = nodes.Count;
            nodes.Add(default);
            if (count <= LeafSize)
            {
                nodes[nodeIndex] = new BvhNode(min, max, start, count, -1, -1);
                return nodeIndex;
            }

            Vector3 extent = max - min;
            int axis = extent.X >= extent.Y && extent.X >= extent.Z ? 0 : extent.Y >= extent.Z ? 1 : 2;
            Array.Sort(triangles, start, count, Comparer<GeneratedPaintTriangle>.Create((a, b) =>
                Axis(a.Center, axis).CompareTo(Axis(b.Center, axis))));
            int leftCount = count / 2;
            int left = BuildNode(triangles, nodes, start, leftCount, depth + 1, ref maxDepth);
            int right = BuildNode(triangles, nodes, start + leftCount, count - leftCount, depth + 1, ref maxDepth);
            nodes[nodeIndex] = new BvhNode(min, max, start, 0, left, right);
            return nodeIndex;
        }

        private static void Bounds(GeneratedPaintTriangle[] triangles, int start, int count, out Vector3 min, out Vector3 max)
        {
            min = triangles[start].Min;
            max = triangles[start].Max;
            int end = start + count;
            for (int i = start + 1; i < end; i++)
            {
                min = new Vector3(
                    Mathf.Min(min.X, triangles[i].Min.X),
                    Mathf.Min(min.Y, triangles[i].Min.Y),
                    Mathf.Min(min.Z, triangles[i].Min.Z));
                max = new Vector3(
                    Mathf.Max(max.X, triangles[i].Max.X),
                    Mathf.Max(max.Y, triangles[i].Max.Y),
                    Mathf.Max(max.Z, triangles[i].Max.Z));
            }
        }

        private static float Axis(Vector3 value, int axis) => axis switch
        {
            0 => value.X,
            1 => value.Y,
            _ => value.Z,
        };
    }

    private readonly record struct BvhNode(Vector3 Min, Vector3 Max, int Start, int Count, int Left, int Right)
    {
        public bool IsLeaf => Count > 0;
    }

    private readonly record struct GeneratedPaintTriangle(
        Vector3 A, Vector3 B, Vector3 C,
        Vector2 UvA, Vector2 UvB, Vector2 UvC,
        Vector3 Min, Vector3 Max, Vector3 Center)
    {
        private const float Padding = 0.0001f;

        public static GeneratedPaintTriangle Create(
            Vector3 a, Vector3 b, Vector3 c,
            Vector2 uvA, Vector2 uvB, Vector2 uvC)
        {
            Vector3 min = new(
                Mathf.Min(a.X, Mathf.Min(b.X, c.X)) - Padding,
                Mathf.Min(a.Y, Mathf.Min(b.Y, c.Y)) - Padding,
                Mathf.Min(a.Z, Mathf.Min(b.Z, c.Z)) - Padding);
            Vector3 max = new(
                Mathf.Max(a.X, Mathf.Max(b.X, c.X)) + Padding,
                Mathf.Max(a.Y, Mathf.Max(b.Y, c.Y)) + Padding,
                Mathf.Max(a.Z, Mathf.Max(b.Z, c.Z)) + Padding);
            return new GeneratedPaintTriangle(a, b, c, uvA, uvB, uvC, min, max, (a + b + c) / 3f);
        }
    }
}
