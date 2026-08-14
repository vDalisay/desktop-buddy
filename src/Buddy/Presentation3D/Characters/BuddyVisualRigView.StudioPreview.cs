using System.Linq;
using Godot;

namespace DesktopBuddy.Buddy.Presentation3D;

public partial class BuddyVisualRigView
{
    private bool _studioPreviewMode;

    /// <summary>
    /// Buddy Studio is an appearance editor, not the gameplay rig debugger. Hide limb connector
    /// cylinders while Studio owns the preview so generated torso/feet can be judged without tiny
    /// connector gaps or stubs. This is presentation-only and is restored when Studio detaches.
    /// </summary>
    public void SetStudioPreviewMode(bool enabled)
    {
        if (_studioPreviewMode == enabled) return;
        _studioPreviewMode = enabled;
        if (!IsInitialized) return;
        foreach (MeshInstance3D connector in _connectorMeshes)
            if (GodotObject.IsInstanceValid(connector)) connector.Visible = !enabled;
    }

    internal bool StudioPreviewConnectorsHiddenForTest =>
        _studioPreviewMode && _connectorMeshes.All(static connector => !connector.Visible);
}
