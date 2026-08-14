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

        // Torso/foot meshes are normalized and their preview roots are then scaled by the trusted
        // Buddy part radius. StandardMaterial3D.Grow happens before that node transform. Applying
        // the normal 1.5-unit Buddy outline directly therefore produced the giant navy blobs seen
        // in owner testing. Divide by the preview-root scale so the final world-space outline stays
        // exactly the same thickness as a built-in Buddy part.
        foreach (Node node in _asset!.FindChildren("Outline", nameof(MeshInstance3D), true, false))
        {
            if (node is not MeshInstance3D outline ||
                outline.MaterialOverride is not StandardMaterial3D material ||
                outline.GetParent() is not Node3D scaledRoot)
                continue;

            float scale = Mathf.Abs(scaledRoot.Scale.X);
            if (!float.IsFinite(scale) || scale <= 0.0001f) continue;
            float wanted = _profile.Look.OutlineGrowAmount / scale;
            if (!Mathf.IsEqualApprox(material.GrowAmount, wanted))
                material.GrowAmount = wanted;
        }
    }
}
