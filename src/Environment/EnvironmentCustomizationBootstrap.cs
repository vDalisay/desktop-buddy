using System;
using DesktopBuddy.App;
using DesktopBuddy.Domain.Environment;
using DesktopBuddy.UI.Win98;
using Godot;

namespace DesktopBuddy.Environment;

/// <summary>
/// Reserved composition root for the environment-customization branch. It is intentionally
/// inert on the shared baseline: the branch may add Paint Background and Environment Decorator
/// composition here without touching project.godot or the shared command-bar bootstrap.
/// </summary>
public partial class EnvironmentCustomizationBootstrap : Node
{
    private IDisposable? _registration;
    private EnvironmentBackgroundEditor? _backgroundEditor;

    public override void _Ready() => ProcessMode = ProcessModeEnum.Always;

    public override void _Process(double delta)
    {
        if (_registration is not null || DisplayServer.GetName() == "headless") return;
        var sandbox = GetTree().Root.FindChild(nameof(SandboxRoot), true, false) as SandboxRoot;
        var commandBar = GetTree().Root.FindChild(nameof(Win98CommandBarBootstrap), true, false) as Win98CommandBarBootstrap;
        EnvironmentProgressState? state = sandbox?.Saves.EnvironmentProgress;
        if (!GodotObject.IsInstanceValid(sandbox) || !GodotObject.IsInstanceValid(commandBar) || state is null) return;

        var presenter = new EnvironmentBackgroundPresenter { Name = nameof(EnvironmentBackgroundPresenter) };
        GetTree().Root.AddChild(presenter);
        _backgroundEditor = new EnvironmentBackgroundEditor { Name = nameof(EnvironmentBackgroundEditor) };
        _backgroundEditor.Configure(state, sandbox!.Saves, presenter);
        GetTree().Root.AddChild(_backgroundEditor);
        _registration = commandBar!.RegisterCustomizeCommand(
            new CustomizeCommandDefinition(
                CustomizeCommandIds.PaintBackground,
                "Paint Background",
                "Change the room wall and floor colors.",
                CustomizeCommandIds.PaintBackgroundOrder),
            _backgroundEditor.Open);
        SetProcess(false);
    }

    public override void _ExitTree()
    {
        _registration?.Dispose();
        _registration = null;
    }
}
