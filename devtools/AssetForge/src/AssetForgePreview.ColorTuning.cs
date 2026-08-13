using Godot;

namespace DesktopBuddy.AssetForge;

public partial class AssetForgePreview
{
    private StandardMaterial3D? _lastColorTunedGeneratedMaterial;

    public override void _Process(double delta)
    {
        _ = delta;
        if (!GodotObject.IsInstanceValid(_asset)) return;
        MeshInstance3D? instance = _asset!.GetNodeOrNull<MeshInstance3D>("Mesh");
        if (instance?.MaterialOverride is not StandardMaterial3D material ||
            ReferenceEquals(material, _lastColorTunedGeneratedMaterial))
        {
            return;
        }

        // Runtime generated cosmetics already force authored color textures through sRGB.
        // Do the same in Forge so preview brightness/hue cannot differ from the equipped item.
        material.AlbedoTextureForceSrgb = true;
        material.EmissionEnergyMultiplier = Buddy.Presentation3D.Shared.BuddySharedMaterialFactory.GeneratedAssetEmissionFloor;
        _lastColorTunedGeneratedMaterial = material;
    }
}
