using System;
using System.Collections.Generic;
using DesktopBuddy.Buddy.Physics;
using Godot;

namespace DesktopBuddy.Buddy.Presentation3D;

public partial class BuddyVisualRigView
{
    /// <summary>
    /// Scenario-only deterministic bridge. Editor preview rigs are not guaranteed to receive their
    /// normal rendered _Process callback in headless test composition, so tests explicitly execute
    /// the same production refresh that a live Paint Buddy frame performs.
    /// </summary>
    internal void RefreshGeneratedReplacementVisualsForTest() => RefreshGeneratedReplacementVisuals();

    internal bool GeneratedReplacementPaintUvSeamIsCorrectForTest
    {
        get
        {
            bool any = false;
            bool ok = true;
            Check(_topVisual, BuddyPartId.Torso);
            Check(_shoesVisual, BuddyPartId.LeftFoot);
            Check(_rightShoesVisual, BuddyPartId.RightFoot);
            return any && ok;

            void Check(Node3D? root, BuddyPartId part)
            {
                if (!IsPartVisualReplaced(part) || !GodotObject.IsInstanceValid(root))
                    return;

                MeshInstance3D? surface = ResolveGeneratedReplacementSurface(root!, part);
                MeshInstance3D? paint = surface?.GetNodeOrNull<MeshInstance3D>(GeneratedPaintName);
                if (paint?.Mesh is not ArrayMesh mesh)
                {
                    ok = false;
                    return;
                }

                any = true;
                ok &= PaintMeshHasSplitFrontBackUv(mesh);
            }
        }
    }

    private static bool PaintMeshHasSplitFrontBackUv(ArrayMesh mesh)
    {
        bool hasFront = false;
        bool hasBack = false;
        bool hasSplitCoincidentVertex = false;
        var coincidentUvs = new Dictionary<(int X, int Y, int Z), List<float>>();

        for (int surfaceIndex = 0; surfaceIndex < mesh.GetSurfaceCount(); surfaceIndex++)
        {
            if (mesh.SurfaceGetPrimitiveType(surfaceIndex) != Mesh.PrimitiveType.Triangles)
                return false;

            Godot.Collections.Array arrays = mesh.SurfaceGetArrays(surfaceIndex);
            Vector3[] vertices = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
            Vector2[] uvs = arrays[(int)Mesh.ArrayType.TexUV].AsVector2Array();
            int[] indices = arrays[(int)Mesh.ArrayType.Index].AsInt32Array();
            if (vertices.Length == 0 || uvs.Length != vertices.Length)
                return false;

            for (int i = 0; i < vertices.Length; i++)
            {
                Vector2 uv = uvs[i];
                if (!float.IsFinite(uv.X) || !float.IsFinite(uv.Y) ||
                    uv.X < -0.0001f || uv.X > 1.0001f || uv.Y < -0.0001f || uv.Y > 1.0001f)
                    return false;

                Vector3 vertex = vertices[i];
                var key = (
                    Mathf.RoundToInt(vertex.X * 1_000_000f),
                    Mathf.RoundToInt(vertex.Y * 1_000_000f),
                    Mathf.RoundToInt(vertex.Z * 1_000_000f));
                if (!coincidentUvs.TryGetValue(key, out List<float>? values))
                {
                    values = [];
                    coincidentUvs.Add(key, values);
                }
                foreach (float existing in values)
                    if (Mathf.Abs(existing - uv.X) > 0.49f)
                        hasSplitCoincidentVertex = true;
                values.Add(uv.X);
            }

            if (indices.Length >= 3)
            {
                for (int i = 0; i + 2 < indices.Length; i += 3)
                    if (!CheckTriangle(indices[i], indices[i + 1], indices[i + 2]))
                        return false;
            }
            else
            {
                for (int i = 0; i + 2 < vertices.Length; i += 3)
                    if (!CheckTriangle(i, i + 1, i + 2))
                        return false;
            }

            bool CheckTriangle(int ia, int ib, int ic)
            {
                if ((uint)ia >= (uint)vertices.Length || (uint)ib >= (uint)vertices.Length || (uint)ic >= (uint)vertices.Length)
                    return false;
                bool back = IsBackPaintTriangle(vertices[ia], vertices[ib], vertices[ic]);
                float minU = Mathf.Min(uvs[ia].X, Mathf.Min(uvs[ib].X, uvs[ic].X));
                float maxU = Mathf.Max(uvs[ia].X, Mathf.Max(uvs[ib].X, uvs[ic].X));
                if (back)
                {
                    hasBack = true;
                    return minU >= 0.4999f;
                }

                hasFront = true;
                return maxU <= 0.5001f;
            }
        }

        return hasFront && hasBack && hasSplitCoincidentVertex;
    }
}
