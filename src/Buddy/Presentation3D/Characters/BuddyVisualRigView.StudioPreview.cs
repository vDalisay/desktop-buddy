using System.Linq;

namespace DesktopBuddy.Buddy.Presentation3D;

public partial class BuddyVisualRigView
{
    private bool _studioPreviewMode;

    /// <summary>
    /// Buddy Studio is an appearance editor, not the gameplay rig debugger. Hide connector meshes
    /// and connector paint shells while Studio owns the preview. The existing connector visibility
    /// seam is reused so normal gameplay/paint preview behavior stays centralized.
    /// </summary>
    public void SetStudioPreviewMode(bool enabled)
    {
        if (_studioPreviewMode == enabled) return;
        _studioPreviewMode = enabled;
        if (!IsInitialized) return;
        SetConnectorVisualsVisible(!enabled);
    }

    internal bool StudioPreviewConnectorsHiddenForTest =>
        _studioPreviewMode && _connectorMeshes.All(static connector => !connector.Visible);
}
