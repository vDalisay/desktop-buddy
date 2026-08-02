using DesktopBuddy.CharacterEditor;
using DesktopBuddy.Persistence.Characters;
using Godot;

namespace DesktopBuddy.App;

public partial class Bootstrap
{
    public override void _EnterTree()
    {
        ChildEnteredTree += OnBootstrapChildEntered;
    }

    private void OnBootstrapChildEntered(Node child)
    {
        if (child is not SandboxRoot sandbox)
            return;
        Callable.From(() => ComposeCharacterEditor(sandbox)).CallDeferred();
    }

    private static void ComposeCharacterEditor(SandboxRoot sandbox)
    {
        if (!GodotObject.IsInstanceValid(sandbox) ||
            sandbox.GetNodeOrNull<CharacterEditorHost>(nameof(CharacterEditorHost)) is not null)
        {
            return;
        }

        CharacterSelectionRuntime? selectionRuntime =
            sandbox.GetNodeOrNull<CharacterSelectionRuntime>(nameof(CharacterSelectionRuntime));
        if (selectionRuntime is null)
            return;

        var host = new CharacterEditorHost
        {
            Name = nameof(CharacterEditorHost),
        };
        host.Configure(sandbox, selectionRuntime.Context, selectionRuntime);
        sandbox.AddChild(host);
    }
}
