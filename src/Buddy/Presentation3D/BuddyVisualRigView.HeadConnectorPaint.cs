using DesktopBuddy.Buddy.Physics;
using DesktopBuddy.Domain.Painting;
using Godot;

namespace DesktopBuddy.Buddy.Presentation3D;

/// <summary>
/// Bridges the already-established endpoint/connector paint atlas to the torso-head connector.
/// This is intentionally the same render path used by hands and feet: the head occupies the left
/// half of paint/head.png and the neck samples the right half. No neck-specific save surface exists.
/// </summary>
public partial class BuddyVisualRigView
{
    private int _headConnectorPaintIndex = -1;
    private Texture2D? _headConnectorBoundTexture;

    public override void _Process(double delta)
    {
        if (!IsInitialized)
            return;

        EnsureHeadConnectorPaintLayer();
        SyncHeadConnectorPaintTexture();
    }

    private void EnsureHeadConnectorPaintLayer()
    {
        if (_headConnectorPaintIndex >= 0)
            return;

        for (int index = 0; index < _connectorDefinitions.Length; index++)
        {
            ConnectorVisualDefinition definition = _connectorDefinitions[index];
            bool isNeck =
                (definition.PartA == BuddyPartId.Torso && definition.PartB == BuddyPartId.Head) ||
                (definition.PartB == BuddyPartId.Torso && definition.PartA == BuddyPartId.Head);
            if (!isNeck)
                continue;

            _headConnectorPaintIndex = index;
            if (_connectorPaintLayers[index] is not null)
                return;

            MeshInstance3D connector = _connectorMeshes[index];
            var paintLayer = new MeshInstance3D
            {
                Name = "Paint",
                Mesh = connector.Mesh,
                MaterialOverride = _materials.CreatePaintMaterial(),
                Visible = false,
                PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Inherit,
            };

            // Exact same guarded right-half atlas lane as the other connectors.
            float halfTexel = 0.5f / PaintPolicy.SurfaceSize;
            float guardedLaneWidth = 0.5f - (1.0f / PaintPolicy.SurfaceSize);
            if (paintLayer.MaterialOverride is StandardMaterial3D material)
            {
                material.Uv1Scale = new Vector3(guardedLaneWidth, 1.0f, 1.0f);
                material.Uv1Offset = new Vector3(0.5f + halfTexel, 0.0f, 0.0f);
            }

            connector.AddChild(paintLayer);
            _connectorPaintLayers[index] = paintLayer;
            return;
        }
    }

    private void SyncHeadConnectorPaintTexture()
    {
        if (_headConnectorPaintIndex < 0 ||
            _connectorPaintLayers[_headConnectorPaintIndex] is not MeshInstance3D layer ||
            !GodotObject.IsInstanceValid(layer))
            return;

        Texture2D? texture = _surfaceUnderlays[(int)BuddyPartId.Head];
        if (!ReferenceEquals(texture, _headConnectorBoundTexture))
        {
            _headConnectorBoundTexture = texture;
            if (layer.MaterialOverride is StandardMaterial3D material)
                material.AlbedoTexture = texture;
        }

        layer.Visible = texture is not null && _connectorMeshes[_headConnectorPaintIndex].Visible;
    }
}
