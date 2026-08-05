using DesktopBuddy.CharacterEditor;
using Godot;

namespace DesktopBuddy.UI.Win98;

/// <summary>
/// Keeps the paint-editor preview clean by hiding only the rig's stretchy connector meshes.
/// The six trusted circular/capsule body-part meshes remain visible and paintable. Connector
/// visibility is restored immediately when paint mode closes, so gameplay rendering is unchanged.
/// </summary>
public partial class Win98PaintPreviewBootstrap : Node
{
    private CharacterEditorHost? _host;
    private bool? _lastHiddenState;

    public override void _Process(double delta)
    {
        if (!GodotObject.IsInstanceValid(_host))
        {
            _host = GetTree().Root.FindChild(
                nameof(CharacterEditorHost), recursive: true, owned: false) as CharacterEditorHost;
            _lastHiddenState = null;
        }

        if (!GodotObject.IsInstanceValid(_host) || !_host!.IsInitialized)
            return;

        bool hideConnectors = _host.IsEditorOpen && _host.IsPaintMode;
        if (_lastHiddenState == hideConnectors)
            return;

        SetPreviewConnectorVisibility(!hideConnectors);
        _lastHiddenState = hideConnectors;
    }

    public override void _ExitTree()
    {
        if (GodotObject.IsInstanceValid(_host) && _host!.IsInitialized)
            SetPreviewConnectorVisibility(true);
    }

    private void SetPreviewConnectorVisibility(bool visible)
    {
        if (!GodotObject.IsInstanceValid(_host))
            return;

        var preview = _host!.PreviewRig;
        if (!GodotObject.IsInstanceValid(preview) || !preview.IsInitialized)
            return;

        for (int index = 0; index < preview.ConnectorVisualCount; index++)
        {
            Node3D connector = preview.GetConnectorVisual(index);
            if (GodotObject.IsInstanceValid(connector))
                connector.Visible = visible;
        }
    }
}
