using DesktopBuddy.App;
using DesktopBuddy.Domain.Content;
using DesktopBuddy.Ui;
using Godot;

namespace DesktopBuddy.UI.Win98;

public partial class Win98BuddyShellController
{
    private SandboxRoot? _toolStatusSandbox;
    private string _lastToolStatus = string.Empty;

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

        string name = ContentDisplayName.For(ContentIds.ForTool(_toolStatusSandbox.Pipeline.SelectedTool));
        string status = $"Tool: {name}";
        if (status == _lastToolStatus)
            return;

        Frame.ToolStatusText = status;
        _lastToolStatus = status;
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
