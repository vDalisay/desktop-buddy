using DesktopBuddy.AssetForge.Core;
using DesktopBuddy.Buddy.Presentation3D.Shared;
using Godot;

namespace DesktopBuddy.AssetForge;

public partial class AssetForgePreview
{
    /// <summary>
    /// Replacement silhouettes use a uniformly enlarged back-face shell instead of normal-based
    /// material Grow. Pixel-derived sidewall normals make a grown shell self-intersect and show as
    /// dark horizontal stripes. Keep glasses on their accepted material path.
    /// </summary>
    public override void _Process(double delta)
    {
        _ = delta;
        if (_category is not (AssetCategory.TorsoShape or AssetCategory.FootShape) ||
            !GodotObject.IsInstanceValid(_asset)) return;

        float meshScale = ReferenceRadius();
        float shellScale = 1f + (_profile.Look.OutlineGrowAmount / Mathf.Max(0.0001f, meshScale));
        foreach (Node child in _asset!.FindChildren("Outline", nameof(MeshInstance3D), true, false))
        {
            if (child is not MeshInstance3D outline) continue;
            if (outline.MaterialOverride is not StandardMaterial3D existing || existing.ResourceName != "AssetForgeReplacementOutline")
            {
                StandardMaterial3D material = BuddySharedMaterialFactory.CreateOutlineMaterial(_profile.Look);
                material.ResourceName = "AssetForgeReplacementOutline";
                material.Grow = false;
                material.GrowAmount = 0f;
                outline.MaterialOverride = material;
            }
            outline.Scale = Vector3.One * shellScale;
        }
    }
}
