using System;
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
            float wantedGrow = _trustedProfile.Look.OutlineGrowAmount / Math.Max(0.0001f, targetRadius);
            StandardMaterial3D? existingOutline = outline!.MaterialOverride as StandardMaterial3D;
            if (existingOutline is null || existingOutline.ResourceName != "BuddyLookScaledOutlineMaterial")
            {
                outline.MaterialOverride = _materials.CreateScaledOutlineMaterial(targetRadius);
            }
            else if (!Mathf.IsEqualApprox(existingOutline.GrowAmount, wantedGrow))
            {
                existingOutline.GrowAmount = wantedGrow;
            }
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

    /// <summary>
    /// Paint-editor mapping against the actual visible replacement triangles. This intentionally
    /// avoids the legacy sphere/capsule mapper: arbitrary Asset Forge silhouettes, holes, depth,
    /// body yaw and the mirrored left foot all resolve through their real mesh UVs.
    /// </summary>
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
                ok &= Mathf.Abs(material.GrowAmount * radius - _trustedProfile.Look.OutlineGrowAmount) <= 0.01f;
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

        // Character-editor preview is orthographic and looks down -Z. The paint canvas world point
        // uses the same X/Y plane, with the standard 2D Y-down -> 3D Y-up mapping.
        Vector3 origin = new(paintWorldPoint.X, -paintWorldPoint.Y, 4096f);
        Vector3 direction = new(0f, 0f, -1f);
        bool found = false;
        float bestDistance = float.PositiveInfinity;
        float bestDepth = float.NegativeInfinity;
        Vector2 bestUv = default;

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
                    TestTriangle(indices[i], indices[i + 1], indices[i + 2]);
            }
            else
            {
                for (int i = 0; i + 2 < vertices.Length; i += 3)
                    TestTriangle(i, i + 1, i + 2);
            }

            void TestTriangle(int ia, int ib, int ic)
            {
                if ((uint)ia >= (uint)vertices.Length || (uint)ib >= (uint)vertices.Length || (uint)ic >= (uint)vertices.Length) return;
                Vector3 a = surface.GlobalTransform * vertices[ia];
                Vector3 b = surface.GlobalTransform * vertices[ib];
                Vector3 c = surface.GlobalTransform * vertices[ic];
                if (!RayTriangle(origin, direction, a, b, c, out float distance, out float baryB, out float baryC)) return;
                if (distance >= bestDistance) return;
                float baryA = 1f - baryB - baryC;
                bestUv = (uvs[ia] * baryA) + (uvs[ib] * baryB) + (uvs[ic] * baryC);
                bestDistance = distance;
                bestDepth = origin.Z - distance;
                found = true;
            }
        }

        hitUv = bestUv;
        cameraDistance = bestDistance;
        worldDepth = bestDepth;
        return found;
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
}
