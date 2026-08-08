using System;
using DesktopBuddy.App;
using DesktopBuddy.Diagnostics;
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
    private const string LogCategory = "EnvironmentCustomization";
    private IDisposable? _registration;
    private EnvironmentBackgroundEditor? _backgroundEditor;
    private EnvironmentBackgroundPresenter? _backgroundPresenter;
    internal bool HasPaintBackgroundRegistration => _registration is not null;

    public override void _Ready() => ProcessMode = ProcessModeEnum.Always;

    public override void _Process(double delta)
    {
        if (_registration is not null || DisplayServer.GetName() == "headless") return;
        var sandbox = FindFirst<SandboxRoot>(GetTree().Root);
        var commandBar = FindFirst<Win98CommandBarBootstrap>(GetTree().Root);
        EnvironmentProgressState? state = sandbox?.Saves.EnvironmentProgress;
        if (!GodotObject.IsInstanceValid(sandbox) || !GodotObject.IsInstanceValid(commandBar) || state is null) return;

        Compose(state, sandbox!.Saves, commandBar!);
    }

    internal void ComposeForStartupTest(
        EnvironmentProgressState state,
        DesktopBuddy.Persistence.SaveCoordinator saves,
        Win98CommandBarBootstrap commandBar) => Compose(state, saves, commandBar);

    private void Compose(
        EnvironmentProgressState state,
        DesktopBuddy.Persistence.SaveCoordinator saves,
        Win98CommandBarBootstrap commandBar)
    {
        if (_registration is not null) return;
        _backgroundPresenter = new EnvironmentBackgroundPresenter { Name = nameof(EnvironmentBackgroundPresenter) };
        GetTree().Root.AddChild(_backgroundPresenter);
        _backgroundEditor = new EnvironmentBackgroundEditor { Name = nameof(EnvironmentBackgroundEditor) };
        _backgroundEditor.Configure(state, saves, _backgroundPresenter);
        GetTree().Root.AddChild(_backgroundEditor);
        _registration = commandBar.RegisterCustomizeCommand(
            new CustomizeCommandDefinition(
                CustomizeCommandIds.PaintBackground,
                "Paint Background",
                "Change the room wall and floor colors.",
                CustomizeCommandIds.PaintBackgroundOrder),
            _backgroundEditor.Open);
        Log.Info(LogCategory, "Paint Background registered in Customize.");
        SetProcess(false);
    }

    public override void _ExitTree()
    {
        _registration?.Dispose();
        _registration = null;
        if (GodotObject.IsInstanceValid(_backgroundEditor)) _backgroundEditor!.QueueFree();
        if (GodotObject.IsInstanceValid(_backgroundPresenter)) _backgroundPresenter!.QueueFree();
        _backgroundEditor = null;
        _backgroundPresenter = null;
    }

    internal static SandboxRoot? FindSandboxForStartup(Node root) => FindFirst<SandboxRoot>(root);

    private static T? FindFirst<T>(Node root) where T : Node
    {
        if (root is T match) return match;
        foreach (Node child in root.GetChildren())
        {
            T? descendant = FindFirst<T>(child);
            if (descendant is not null) return descendant;
        }
        return null;
    }
}
