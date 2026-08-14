using DesktopBuddy.Buddy.Physics;
using Godot;

namespace DesktopBuddy.Buddy.Presentation3D;

public partial class BuddyVisualRigView
{
    private Vector3[] _lastReplacementConnectorPosition = Array.Empty<Vector3>();
    private Vector3[] _lastReplacementConnectorOffset = Array.Empty<Vector3>();
    private bool[] _replacementConnectorWasCorrected = Array.Empty<bool>();

    /// <summary>
    /// Re-fits only connectors touching an active visual replacement. The authoritative pose and
    /// connector rotation still come from the normal rig update; this presentation pass changes
    /// only the visible connector length/center so it terminates at rendered replacement bounds.
    /// </summary>
    private void RefreshReplacementConnectorAttachment()
    {
        if (!IsInitialized) return;
        EnsureReplacementConnectorTracking();

        if (!_torsoVisualReplaced && !_leftFootVisualReplaced && !_rightFootVisualReplaced)
        {
            Array.Clear(_replacementConnectorWasCorrected);
            return;
        }

        for (int index = 0; index < _connectorDefinitions.Length; index++)
        {
            ConnectorVisualDefinition definition = _connectorDefinitions[index];
            if (!IsPartVisualReplaced(definition.PartA) && !IsPartVisualReplaced(definition.PartB))
            {
                _replacementConnectorWasCorrected[index] = false;
                continue;
            }

            int aIndex = (int)definition.PartA;
            int bIndex = (int)definition.PartB;
            Vector3 a = _sockets[aIndex].GlobalPosition;
            Vector3 b = _sockets[bIndex].GlobalPosition;
            Vector3 delta = b - a;
            float separation = delta.Length();
            if (separation <= Mathf.Epsilon) continue;
            Vector3 direction = delta / separation;

            float trustedA = _meshRadii[aIndex];
            float trustedB = _meshRadii[bIndex];
            float visualA = ConnectorVisualExtent(definition.PartA, direction, trustedA);
            float visualB = ConnectorVisualExtent(definition.PartB, -direction, trustedB);

            float oldGap = separation - trustedA - trustedB;
            float oldLength = Mathf.Max(_trustedProfile.ConnectorMinimumLength, oldGap);
            Vector3 oldCenter = oldGap < _trustedProfile.ConnectorMinimumLength
                ? (a + b) * 0.5f
                : a + direction * (trustedA + oldLength * 0.5f);

            float visualGap = separation - visualA - visualB;
            float visualLength = Mathf.Max(_trustedProfile.ConnectorMinimumLength, visualGap);
            Vector3 visualCenter = visualGap < _trustedProfile.ConnectorMinimumLength
                ? (a + b) * 0.5f
                : a + direction * (visualA + visualLength * 0.5f);

            MeshInstance3D connector = _connectorMeshes[index];
            Vector3 basePosition = connector.GlobalPosition;
            if (_replacementConnectorWasCorrected[index] &&
                basePosition.IsEqualApprox(_lastReplacementConnectorPosition[index]))
            {
                // No authoritative pose update occurred since the previous render frame. Undo our
                // own prior correction first so repeated _Process calls cannot accumulate drift.
                basePosition -= _lastReplacementConnectorOffset[index];
            }

            Vector3 correction = visualCenter - oldCenter;
            connector.GlobalPosition = basePosition + correction;
            Vector3 scale = connector.Scale;
            scale.Y = visualLength / _connectorAuthoringLengths[index];
            connector.Scale = scale;

            _replacementConnectorWasCorrected[index] = true;
            _lastReplacementConnectorOffset[index] = correction;
            _lastReplacementConnectorPosition[index] = connector.GlobalPosition;
        }
    }

    private void EnsureReplacementConnectorTracking()
    {
        if (_lastReplacementConnectorPosition.Length == _connectorDefinitions.Length) return;
        _lastReplacementConnectorPosition = new Vector3[_connectorDefinitions.Length];
        _lastReplacementConnectorOffset = new Vector3[_connectorDefinitions.Length];
        _replacementConnectorWasCorrected = new bool[_connectorDefinitions.Length];
    }

    /// <summary>
    /// Returns the rendered support distance from a part socket toward a connector endpoint.
    /// Normal Buddy parts retain their trusted radius. A visual replacement instead measures its
    /// actual rendered mesh bounds, so connector presentation reaches large/asymmetric authored
    /// shapes without changing any 2D collision, mass, joint or force geometry.
    /// </summary>
    private float ConnectorVisualExtent(BuddyPartId partId, Vector3 worldDirection, float fallbackRadius)
    {
        Node3D? replacement = partId switch
        {
            BuddyPartId.Torso when _torsoVisualReplaced => _topVisual,
            BuddyPartId.LeftFoot when _leftFootVisualReplaced => _shoesVisual,
            BuddyPartId.RightFoot when _rightFootVisualReplaced => _rightShoesVisual,
            _ => null,
        };
        if (!GodotObject.IsInstanceValid(replacement) || worldDirection.LengthSquared() <= Mathf.Epsilon)
            return fallbackRadius;

        Vector3 direction = worldDirection.Normalized();
        Vector3 center = GetPartSocket(partId).GlobalPosition;
        float support = 0f;
        bool found = false;

        if (replacement is MeshInstance3D replacementMesh)
            MeasureMeshSupport(replacementMesh, center, direction, ref support, ref found);
        foreach (Node child in replacement!.FindChildren("*", nameof(MeshInstance3D), true, false))
        {
            if (child is not MeshInstance3D mesh ||
                string.Equals(mesh.Name, "GeneratedOutline", StringComparison.Ordinal))
                continue;
            MeasureMeshSupport(mesh, center, direction, ref support, ref found);
        }

        return found && support > 0.001f ? support : fallbackRadius;
    }

    private static void MeasureMeshSupport(
        MeshInstance3D instance,
        Vector3 center,
        Vector3 direction,
        ref float support,
        ref bool found)
    {
        if (!GodotObject.IsInstanceValid(instance) || !GodotObject.IsInstanceValid(instance.Mesh))
            return;
        Aabb local = instance.GetAabb();
        Vector3 min = local.Position;
        Vector3 max = local.End;
        for (int bits = 0; bits < 8; bits++)
        {
            Vector3 localCorner = new(
                (bits & 1) == 0 ? min.X : max.X,
                (bits & 2) == 0 ? min.Y : max.Y,
                (bits & 4) == 0 ? min.Z : max.Z);
            Vector3 worldCorner = instance.GlobalTransform * localCorner;
            float projected = (worldCorner - center).Dot(direction);
            if (!found || projected > support)
            {
                support = projected;
                found = true;
            }
        }
    }

    internal float ConnectorVisualExtentForTest(BuddyPartId partId, Vector3 direction) =>
        ConnectorVisualExtent(partId, direction, PartMeshRadius(partId));
}
