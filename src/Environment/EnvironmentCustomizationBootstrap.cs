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
    private IDisposable? _decoratorRegistration;
    private EnvironmentBackgroundEditor? _backgroundEditor;
    private EnvironmentBackgroundPresenter? _backgroundPresenter;
    private EnvironmentDecorationLayer? _decorationLayer;
    private EnvironmentDecorator? _decorator;
    private readonly EnvironmentPresentationVisibility _presentationVisibility = new();
    internal bool HasPaintBackgroundRegistration => _registration is not null;
    internal bool HasDecorateRoomRegistration => _decoratorRegistration is not null;

    public override void _Ready() => ProcessMode = ProcessModeEnum.Always;

    public override void _Process(double delta)
    {
        if (DisplayServer.GetName() == "headless") return;
        if (_registration is not null)
        {
            if (FindFirst<SandboxRoot>(GetTree().Root) is SandboxRoot activeSandbox)
                _presentationVisibility.SetWorkCompanionActive(activeSandbox.Window.WorkCompanionActive);
            return;
        }
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
        SandboxRoot? sandbox = FindFirst<SandboxRoot>(GetTree().Root);
        if (sandbox is not null) _backgroundPresenter.Configure(sandbox.Boundaries);
        GetTree().Root.AddChild(_backgroundPresenter);
        _backgroundEditor = new EnvironmentBackgroundEditor { Name = nameof(EnvironmentBackgroundEditor) };
        _backgroundEditor.Configure(state, saves, _backgroundPresenter);
        GetTree().Root.AddChild(_backgroundEditor);
        if (sandbox is not null)
        {
            _decorationLayer = new EnvironmentDecorationLayer { Name = nameof(EnvironmentDecorationLayer) };
            _decorationLayer.Configure(state, sandbox.Boundaries);
            GetTree().Root.AddChild(_decorationLayer);
            _presentationVisibility.Configure(_backgroundPresenter, _decorationLayer);
            _decorator = new EnvironmentDecorator { Name = nameof(EnvironmentDecorator) };
            _decorator.Configure(sandbox.Progress, sandbox.Economy, sandbox.Pointer, sandbox.Buddy,
                sandbox.VisualPresenter, state, saves, _decorationLayer);
            GetTree().Root.AddChild(_decorator);
            RegisterDecorator(commandBar, _decorator);
        }
        _registration = commandBar.RegisterCustomizeCommand(
            new CustomizeCommandDefinition(
                CustomizeCommandIds.PaintBackground,
                "Paint Background",
                "Change the room wall and floor colors.",
                CustomizeCommandIds.PaintBackgroundOrder),
            _backgroundEditor.Open);
        Log.Info(LogCategory, "Paint Background registered in Customize.");
    }

    internal void RegisterDecoratorForStartupTest(Win98CommandBarBootstrap commandBar, EnvironmentDecorator decorator) =>
        RegisterDecorator(commandBar, decorator);

    private void RegisterDecorator(Win98CommandBarBootstrap commandBar, EnvironmentDecorator decorator)
    {
        _decoratorRegistration = commandBar.RegisterTopLevelCommand(
            new TopLevelCommandDefinition(
                TopLevelCommandIds.DecorateRoom,
                "Decorate Room",
                "Open the room decoration workspace.",
                TopLevelCommandIds.DecorateRoomOrder),
            decorator.Open,
            isEnabled: () => !decorator.IsOpen);
    }

    public override void _ExitTree()
    {
        _registration?.Dispose();
        _registration = null;
        _decoratorRegistration?.Dispose();
        _decoratorRegistration = null;
        if (GodotObject.IsInstanceValid(_backgroundEditor)) _backgroundEditor!.QueueFree();
        if (GodotObject.IsInstanceValid(_backgroundPresenter)) _backgroundPresenter!.QueueFree();
        if (GodotObject.IsInstanceValid(_decorationLayer)) _decorationLayer!.QueueFree();
        if (GodotObject.IsInstanceValid(_decorator)) _decorator!.QueueFree();
        _presentationVisibility.SetWorkCompanionActive(false);
        _backgroundEditor = null;
        _backgroundPresenter = null;
        _decorationLayer = null;
        _decorator = null;
    }

    internal void ApplyWorkCompanionVisibilityForTest(bool active) =>
        _presentationVisibility.SetWorkCompanionActive(active);

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
