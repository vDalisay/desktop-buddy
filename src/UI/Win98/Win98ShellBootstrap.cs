using DesktopBuddy.Platform;
using Godot;

namespace DesktopBuddy.UI.Win98;

/// <summary>
/// Incremental W1 composition seam. It attaches the in-scene shell when the normal sandbox
/// appears, without modifying gameplay composition or creating another native OS window.
/// </summary>
public partial class Win98ShellBootstrap : Node
{
    private bool _attached;

    public override void _Process(double delta)
    {
        if (_attached || DisplayServer.GetName() == "headless")
            return;

        var windowController = GetTree().Root.FindChild(
            nameof(DesktopWindowController), recursive: true, owned: false) as DesktopWindowController;
        if (!GodotObject.IsInstanceValid(windowController))
            return;

        Attach(windowController);
    }

    private void Attach(DesktopWindowController windowController)
    {
        var layer = new Win98BuddyShellController
        {
            Name = nameof(Win98BuddyShellController),
            Layer = 100,
            Window = windowController,
        };

        var frame = new Win98WindowFrame
        {
            Name = nameof(Win98WindowFrame),
        };
        frame.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        layer.Frame = frame;

        // Frame enters first so its controls exist before the controller connects signals.
        layer.AddChild(frame);
        GetTree().Root.AddChild(layer);
        _attached = true;
        SetProcess(false);
    }
}
