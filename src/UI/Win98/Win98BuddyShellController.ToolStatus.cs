using DesktopBuddy.App;
using DesktopBuddy.Domain.Content;
using DesktopBuddy.Domain.Tools;
using DesktopBuddy.Ui;
using Godot;

namespace DesktopBuddy.UI.Win98;

public partial class Win98BuddyShellController
{
    private SandboxRoot? _toolStatusSandbox;
    private ToolId? _lastToolStatusTool;

    public override void _PhysicsProcess(double delta)
    {
        if (!GodotObject.IsInstanceValid(Frame))
            return;

        if (!GodotObject.IsInstanceValid(_toolStatusSandbox))
            _toolStatusSandbox = FindFirstOfType<SandboxRoot>(GetTree().Root);
        if (!GodotObject.IsInstanceValid(_toolStatusSandbox) ||
            !GodotObject.IsInstanceValid(_toolStatusSandbox!.Pipeline) ||
            !_toolStatusSandbox.Pipeline.IsInitialized)
        {
            return;
        }

        ToolId selectedTool = _toolStatusSandbox.Pipeline.SelectedTool;
        if (_lastToolStatusTool == selectedTool)
            return;

        string name = ContentDisplayName.For(ContentIds.ForTool(selectedTool));
        Frame.ToolStatusText = $"Tool: {name}";
        _lastToolStatusTool = selectedTool;
    }

    private static T? FindFirstOfType<T>(Node root) where T : Node
    {
        if (root is T match)
            return match;
        foreach (Node child in root.GetChildren())
        {
            T? found = FindFirstOfType<T>(child);
            if (found is not null)
                return found;
        }
        return null;
    }
}