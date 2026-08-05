using Godot;

namespace DesktopBuddy.Buddy.Presentation3D;

public partial class BuddyVisualRigView
{
    /// <summary>
    /// Explicit presentation-only control used by the character paint preview. This changes
    /// only the connector mesh visibility; trusted part meshes, paint shells and geometry stay
    /// intact. Gameplay rigs remain unchanged unless their caller explicitly invokes it.
    /// </summary>
    public void SetConnectorVisualsVisible(bool visible)
    {
        if (!IsInitialized)
            return;

        for (int index = 0; index < _connectorMeshes.Length; index++)
        {
            MeshInstance3D connector = _connectorMeshes[index];
            if (GodotObject.IsInstanceValid(connector))
                connector.Visible = visible;
        }
    }
}
