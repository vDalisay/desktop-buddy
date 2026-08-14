using DesktopBuddy.AssetForge.Core;
using DesktopBuddy.Buddy.Presentation3D.Shared;
using Godot;

namespace DesktopBuddy.AssetForge;

public partial class AssetForgePreview
{
    private StandardMaterial3D? _lastColorTunedGeneratedMaterial;

    public override void _Process(double delta)
    {
        _ = delta;
        if (!GodotObject.IsInstanceValid(_asset)) return;

        // Runtime generated cosmetics force authored colour textures through sRGB. Do the same in
        // Forge so preview brightness/hue cannot differ from the equipped item. Foot previews share
        // one generated material, so tuning it once updates both members of the pair.
        if (GodotObject.IsInstanceValid(_generatedMaterial) &&
            !ReferenceEquals(_generatedMaterial, _lastColorTunedGeneratedMaterial))
        {
            _generatedMaterial!.AlbedoTextureForceSrgb = true;
            _generatedMaterial.EmissionEnergyMultiplier = BuddySharedMaterialFactory.GeneratedAssetEmissionFloor;
            _lastColorTunedGeneratedMaterial = _generatedMaterial;
        }

        if (_category is not (AssetCategory.TorsoShape or AssetCategory.FootShape)) return;

        // Pixel-derived replacement sidewalls contain many small direction changes. Normal-based
        // StandardMaterial3D.Grow expands those side faces along their local normals; the expanded
        // back-face shell can then intersect the visible surface and appear as dark horizontal
        // stripes. Use the same uniform back-face shell strategy as the shipping Buddy renderer:
        // disable Grow and enlarge the outline node itself by a scale-adjusted world-space amount.
        foreach (Node node in _asset!.FindChildren("Outline", nameof(MeshInstance3D), true, false))
        {
            if (node is not MeshInstance3D outline || outline.GetParent() is not Node3D scaledRoot)
                continue;

            float scale = Mathf.Abs(scaledRoot.Scale.X);
            if (!float.IsFinite(scale) || scale <= 0.0001f) continue;
            if (outline.MaterialOverride is not StandardMaterial3D material ||
                material.ResourceName != "AssetForgeReplacementOutline")
            {
                material = BuddySharedMaterialFactory.CreateOutlineMaterial(_profile.Look);
                material.ResourceName = "AssetForgeReplacementOutline";
                material.Grow = false;
                material.GrowAmount = 0f;
                outline.MaterialOverride = material;
            }
            outline.Scale = Vector3.One * (1f + _profile.Look.OutlineGrowAmount / scale);
        }
    }
}
