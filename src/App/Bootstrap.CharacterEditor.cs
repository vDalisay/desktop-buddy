using System;
using DesktopBuddy.CharacterEditor;
using DesktopBuddy.Diagnostics;
using DesktopBuddy.Persistence.Characters;
using DesktopBuddy.Work;
using Godot;

namespace DesktopBuddy.App;

public partial class Bootstrap
{
    private const string CharacterEditorStartupCategory = "CharacterEditorStartup";

    public override void _EnterTree()
    {
        ChildEnteredTree += OnBootstrapChildEntered;
    }

    private void OnBootstrapChildEntered(Node child)
    {
        if (child is not SandboxRoot sandbox)
            return;

        Log.Info(CharacterEditorStartupCategory,
            "Sandbox entered the tree; scheduling Character Editor composition.");
        Callable.From(() => ComposeCharacterEditor(sandbox)).CallDeferred();
    }

    private static void ComposeCharacterEditor(SandboxRoot sandbox)
    {
        try
        {
            Log.Info(CharacterEditorStartupCategory,
                $"Composition started: sandboxValid={GodotObject.IsInstanceValid(sandbox)}.");

            if (!GodotObject.IsInstanceValid(sandbox) ||
                sandbox.GetNodeOrNull<CharacterEditorHost>(nameof(CharacterEditorHost)) is not null)
            {
                Log.Info(CharacterEditorStartupCategory,
                    "Composition skipped because the sandbox is invalid or the host already exists.");
                return;
            }

            CharacterSelectionRuntime? selectionRuntime =
                sandbox.GetNodeOrNull<CharacterSelectionRuntime>(nameof(CharacterSelectionRuntime));
            if (selectionRuntime is null)
            {
                Log.Warn(CharacterEditorStartupCategory,
                    "Composition skipped because CharacterSelectionRuntime was not found.");
                return;
            }

            // Work Mode is outside the itch/browser distribution. Do not instantiate its
            // coordinator merely as a side effect of composing the Character Editor: on Web
            // this needlessly constructs a native-desktop subsystem before the inventory and
            // Paint Buddy UI can exist. The normal Windows demo/full build keeps the exact
            // existing coordinator path.
            if (DemoScope.IncludesWorkMode)
            {
                var workCoordinator = new WorkCompanionCoordinator
                {
                    Name = nameof(WorkCompanionCoordinator),
                };
                workCoordinator.Configure(sandbox, selectionRuntime.Context);
                sandbox.AddChild(workCoordinator);
                Log.Info(CharacterEditorStartupCategory,
                    $"WorkCompanionCoordinator added successfully: path={workCoordinator.GetPath()} insideTree={workCoordinator.IsInsideTree()}.");
            }
            else
            {
                Log.Info(CharacterEditorStartupCategory,
                    "WorkCompanionCoordinator omitted by the active distribution scope.");
            }

            var host = new CharacterEditorHost
            {
                Name = nameof(CharacterEditorHost),
            };
            Log.Info(CharacterEditorStartupCategory, "CharacterEditorHost constructed.");

            host.Configure(sandbox, selectionRuntime.Context, selectionRuntime);
            Log.Info(CharacterEditorStartupCategory, "CharacterEditorHost configured; adding to sandbox.");

            sandbox.AddChild(host);
            Log.Info(CharacterEditorStartupCategory,
                $"CharacterEditorHost added successfully: path={host.GetPath()} insideTree={host.IsInsideTree()}.");
        }
        catch (Exception exception)
        {
            Log.Error(CharacterEditorStartupCategory,
                $"Character Editor composition failed: {exception}");
        }
    }
}
