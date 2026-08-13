using Godot;

namespace DesktopBuddy.AssetForge;

public partial class AssetForgePreview
{
    private const float GeneratedPreviewEmissionFloor = 0.28f;
    private StandardMaterial3D? _lastTunedGeneratedMaterial;

    public override void _Process(double delta)
    {
        _ = delta;
        if (!GodotObject.IsInstanceValid(_asset)) return;
        MeshInstance3D? instance = _asset!.GetNodeOrNull<MeshInstance3D>("Mesh");
        if (instance?.MaterialOverride is not StandardMaterial3D material ||
            ReferenceEquals(material, _lastTunedGeneratedMaterial))
        {
            return;
        }

        material.AlbedoTextureForceSrgb = true;
        material.EmissionEnergyMultiplier = GeneratedPreviewEmissionFloor;
        _lastTunedGeneratedMaterial = material;
    }
}
