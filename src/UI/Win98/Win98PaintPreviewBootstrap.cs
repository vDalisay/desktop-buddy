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
    private bool? _lastAppliedHiddenState;

    // Opening the editor pauses the tree (GameplayPauseReason.CharacterEditor), so an
    // inherit-mode autoload would stop processing exactly when paint mode opens.
    public override void _Ready() => ProcessMode = ProcessModeEnum.Always;

    public override void _Process(double delta)
    {
        if (!GodotObject.IsInstanceValid(_host))
        {
            _host = GetTree().Root.FindChild(
                nameof(CharacterEditorHost), recursive: true, owned: false) as CharacterEditorHost;
            _lastAppliedHiddenState = null;
        }

        if (!GodotObject.IsInstanceValid(_host) || !_host!.IsInitialized)
            return;

        bool hideConnectors = _host.IsEditorOpen && _host.IsPaintMode;
        if (_lastAppliedHiddenState == hideConnectors)
            return;

        // Do not cache the requested state until the preview rig actually exists and accepts
        // it. The editor composes its preview over deferred frames; caching too early meant the
        // first hide request was lost permanently.
        if (TrySetPreviewConnectorVisibility(!hideConnectors))
            _lastAppliedHiddenState = hideConnectors;
    }

    public override void _ExitTree()
    {
        if (GodotObject.IsInstanceValid(_host) && _host!.IsInitialized)
            TrySetPreviewConnectorVisibility(true);
    }

    private bool TrySetPreviewConnectorVisibility(bool visible)
    {
        if (!GodotObject.IsInstanceValid(_host))
            return false;

        var preview = _host!.PreviewRig;
        if (!GodotObject.IsInstanceValid(preview) || !preview.IsInitialized)
            return false;

        preview.SetConnectorVisualsVisible(visible);
        return true;
    }
}
