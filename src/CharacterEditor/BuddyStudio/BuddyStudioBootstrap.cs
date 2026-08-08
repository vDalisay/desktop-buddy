using System;
using DesktopBuddy.UI.Win98;
using Godot;

namespace DesktopBuddy.CharacterEditor.BuddyStudio;

/// <summary>
/// Reserved composition root for the Buddy Studio branch. It is intentionally inert on the
/// shared baseline: the branch may compose/register Buddy Studio here without touching
/// project.godot or the shared command-bar bootstrap.
/// </summary>
public partial class BuddyStudioBootstrap : Node
{
    private IDisposable? _registration;

    public override void _Ready() => ProcessMode = ProcessModeEnum.Always;

    public override void _Process(double delta)
    {
        if (_registration is not null)
            return;
        var host = GetTree().Root.FindChild(nameof(CharacterEditorHost), true, false) as CharacterEditorHost;
        var commandBar = GetTree().Root.FindChild(nameof(Win98CommandBarBootstrap), true, false) as Win98CommandBarBootstrap;
        if (!GodotObject.IsInstanceValid(host) || !GodotObject.IsInstanceValid(commandBar) ||
            !host!.EnsureBuddyStudioReady())
            return;

        _registration = commandBar!.RegisterCustomizeCommand(
            new CustomizeCommandDefinition(
                CustomizeCommandIds.BuddyStudio,
                "Buddy Studio",
                "Customize your buddy's appearance.",
                CustomizeCommandIds.BuddyStudioOrder),
            () => _ = host.OpenBuddyStudioAsync(),
            isVisible: () => host.IsBuddyStudioReady,
            isEnabled: () => host.IsBuddyStudioReady && !host.IsEditorOpen);
    }

    public override void _ExitTree()
    {
        _registration?.Dispose();
        _registration = null;
    }
}
