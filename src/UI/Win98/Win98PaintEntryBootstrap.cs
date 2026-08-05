using DesktopBuddy.CharacterEditor;
using Godot;

namespace DesktopBuddy.UI.Win98;

/// <summary>
/// Bridges the existing character-editor command to the dedicated paint-first product flow
/// without duplicating the editor's lifecycle or save logic.
/// </summary>
public partial class Win98PaintEntryBootstrap : Node
{
    private bool _connected;

    public override void _Process(double delta)
    {
        if (_connected)
            return;

        var host = GetTree().Root.FindChild(
            nameof(CharacterEditorHost), recursive: true, owned: false) as CharacterEditorHost;
        var legacyButton = GetTree().Root.FindChild(
            "DockCharacterEditorButton", recursive: true, owned: false) as Button;

        if (!GodotObject.IsInstanceValid(host) || !GodotObject.IsInstanceValid(legacyButton))
            return;

        // The original handler still performs the established editor lifecycle transition.
        // This second handler then selects the paint workspace once that transition is ready.
        legacyButton!.Pressed += async () => await host!.OpenWin98PaintEditorAsync();
        _connected = true;
        SetProcess(false);
    }
}
