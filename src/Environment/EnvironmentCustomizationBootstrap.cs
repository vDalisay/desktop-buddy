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
    private EnvironmentDecorationLayer? _decorationLayer;
    private EnvironmentDecorator? _decorator;
    private Button? _decorLauncher;
    private HSeparator? _decorSeparator;
    internal bool HasPaintBackgroundRegistration => _registration is not null;

    public override void _Ready() => ProcessMode = ProcessModeEnum.Always;

    public override void _Process(double delta)
    {
        if (DisplayServer.GetName() == "headless") return;
        if (_registration is not null)
        {
            TryAttachDecorLauncher();
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
            _decorator = new EnvironmentDecorator { Name = nameof(EnvironmentDecorator) };
            _decorator.Configure(sandbox.Progress, sandbox.Economy, sandbox.Pointer, state, saves, _decorationLayer);
            GetTree().Root.AddChild(_decorator);
        }
        _registration = commandBar.RegisterCustomizeCommand(
            new CustomizeCommandDefinition(
                CustomizeCommandIds.PaintBackground,
                "Paint Background",
                "Change the room wall and floor colors.",
                CustomizeCommandIds.PaintBackgroundOrder),
            _backgroundEditor.Open);
        Log.Info(LogCategory, "Paint Background registered in Customize.");
        TryAttachDecorLauncher();
    }

    public override void _ExitTree()
    {
        _registration?.Dispose();
        _registration = null;
        if (GodotObject.IsInstanceValid(_backgroundEditor)) _backgroundEditor!.QueueFree();
        if (GodotObject.IsInstanceValid(_backgroundPresenter)) _backgroundPresenter!.QueueFree();
        if (GodotObject.IsInstanceValid(_decorationLayer)) _decorationLayer!.QueueFree();
        if (GodotObject.IsInstanceValid(_decorator)) _decorator!.QueueFree();
        if (GodotObject.IsInstanceValid(_decorLauncher)) _decorLauncher!.QueueFree();
        if (GodotObject.IsInstanceValid(_decorSeparator)) _decorSeparator!.QueueFree();
        _backgroundEditor = null;
        _backgroundPresenter = null;
        _decorationLayer = null;
        _decorator = null;
        _decorLauncher = null;
        _decorSeparator = null;
    }

    private void TryAttachDecorLauncher()
    {
        if (GodotObject.IsInstanceValid(_decorLauncher) || !GodotObject.IsInstanceValid(_decorator)) return;
        var list = GetTree().Root.FindChild("ShopItemList", true, false) as VBoxContainer;
        if (!GodotObject.IsInstanceValid(list)) return;
        _decorLauncher = new Button
        {
            Name = "EnvironmentDecorateRoomButton",
            Text = "Decorate Room…",
            TooltipText = "Buy and arrange room decorations.",
            CustomMinimumSize = new Vector2(0, 34),
        };
        _decorLauncher.Pressed += _decorator!.Open;
        _decorSeparator = new HSeparator { Name = "EnvironmentDecorSeparator" };
        list!.AddChild(_decorSeparator);
        list.AddChild(_decorLauncher);
        Log.Info(LogCategory, "Environment Decorator registered in Shop.");
        SetProcess(false);
    }

    internal bool AttachDecorLauncherForStartupTest(EnvironmentDecorator decorator)
    {
        _decorator = decorator ?? throw new ArgumentNullException(nameof(decorator));
        TryAttachDecorLauncher();
        return GodotObject.IsInstanceValid(_decorLauncher);
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
