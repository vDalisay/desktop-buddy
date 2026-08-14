using DesktopBuddy.Buddy.Physics;
using Godot;

namespace DesktopBuddy.Buddy.Presentation3D;

public partial class BuddyVisualRigView
{
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
