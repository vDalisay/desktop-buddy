using System;
using DesktopBuddy.App;
using DesktopBuddy.Diagnostics;
using DesktopBuddy.Domain.Environment;
using DesktopBuddy.Persistence;
using DesktopBuddy.Persistence.Characters;
using DesktopBuddy.UI.Win98;
using Godot;

namespace DesktopBuddy.Environment;

/// <summary>
/// Reserved composition root for the environment-customization branch. It owns Paint Background
/// and Environment Decorator composition without widening the shared command-bar bootstrap.
/// </summary>
public partial class EnvironmentCustomizationBootstrap : Node
{
    private const string LogCategory = "EnvironmentCustomization";
    private IDisposable? _registration;
    private IDisposable? _decoratorRegistration;
    private SandboxRoot? _sandbox;
    private EnvironmentBackgroundEditor? _backgroundEditor;
    private EnvironmentBackgroundPresenter? _backgroundPresenter;
    private EnvironmentPaintStore? _paintStore;
    private EnvironmentPaintToolIconBootstrap? _paintIconBootstrap;
    private EnvironmentDecorationLayer? _decorationLayer;
    private EnvironmentDecorator? _decorator;
    private IEnvironmentProgressPersistence? _environmentPersistence;
    private readonly EnvironmentPresentationVisibility _presentationVisibility = new();
    private bool _workCompanionSubscribed;
    internal EnvironmentPaintStore? PaintStore => _paintStore;

    /// <summary>
    /// Supplies the normal-run composition root directly. Autoload startup can precede the
    /// sandbox entering the scene tree, so this seam avoids using the scene tree as a service
    /// locator during normal boot. Isolated scenarios may still use the discovery fallback.
    /// </summary>
    public void Configure(SandboxRoot sandbox)
    {
        ArgumentNullException.ThrowIfNull(sandbox);
        if (GodotObject.IsInstanceValid(_sandbox) && !ReferenceEquals(_sandbox, sandbox))
            throw new InvalidOperationException("Environment customization is already bound to another sandbox.");
        _sandbox = sandbox;
    }

    /// <summary>Reset Progress wipes the painted room along with the rest of the save.</summary>
    public void ClearPaintedBackground()
    {
        _paintStore?.Delete();
        if (!GodotObject.IsInstanceValid(_backgroundPresenter)) return;
        _backgroundPresenter!.Canvas.Reset();
        _backgroundPresenter.Canvas.MarkSaved();
    }
    internal bool HasPaintBackgroundRegistration => _registration is not null;
    internal bool HasDecorateRoomRegistration => _decoratorRegistration is not null;

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        if (!DemoScope.IncludesPaintRoom)
        {
            // The itch.io build has no environment workspace at all. The normal Demo already
            // hides Room Decorator, so stopping this bootstrap also avoids constructing the
            // Paint Background canvas/editor and their supporting runtime nodes.
            SetProcess(false);
            Log.Info(LogCategory, "Paint Room omitted by the active distribution scope.");
            return;
        }

        SetProcess(true);
    }

    public override void _Process(double delta)
    {
        if (DisplayServer.GetName() == "headless") return;

        // Startup-test scenes do not pass through Bootstrap, so retain discovery as a narrow
        // compatibility fallback. Normal boot injects this reference before the sandbox enters.
        if (!GodotObject.IsInstanceValid(_sandbox))
            _sandbox = FindFirst<SandboxRoot>(GetTree().Root);

        if (_registration is not null)
        {
            SubscribeWorkCompanionState();
            SetProcess(false);
            return;
        }

        Win98CommandBarBootstrap? commandBar = GetNodeOrNull<Win98CommandBarBootstrap>(
            "/root/Win98CommandBarBootstrap");
        if (!GodotObject.IsInstanceValid(commandBar))
            commandBar = FindFirst<Win98CommandBarBootstrap>(GetTree().Root);
        if (!GodotObject.IsInstanceValid(_sandbox) || !GodotObject.IsInstanceValid(commandBar)) return;

        EnvironmentProgressState state;
        IEnvironmentProgressPersistence persistence;
        if (_sandbox!.SceneProgress is { } sceneProgress)
        {
            EnvironmentProgressSnapshot snapshot = sceneProgress.ActiveEnvironmentProgress;
            state = new EnvironmentProgressState(snapshot.Layout, snapshot.Revision, snapshot.OwnedUnplaced);
            persistence = new SceneEnvironmentProgressPersistence(
                sceneProgress,
                sceneProgress.ActiveSceneId,
                state);
        }
        else
        {
            EnvironmentProgressState? legacyState = _sandbox.Saves.EnvironmentProgress;
            if (legacyState is null) return;
            state = legacyState;
            persistence = new LegacyEnvironmentProgressPersistence(_sandbox.Progress, legacyState, _sandbox.Saves);
        }

        Compose(state, persistence, commandBar!);
    }

    internal void ComposeForStartupTest(
        EnvironmentProgressState state,
        DesktopBuddy.Persistence.SaveCoordinator saves,
        Win98CommandBarBootstrap commandBar)
    {
        if (!GodotObject.IsInstanceValid(_sandbox))
            throw new InvalidOperationException("Startup-test environment composition requires a configured sandbox.");
        Compose(
            state,
            new LegacyEnvironmentProgressPersistence(_sandbox!.Progress, state, saves),
            commandBar);
    }

    private void Compose(
        EnvironmentProgressState state,
        IEnvironmentProgressPersistence persistence,
        Win98CommandBarBootstrap commandBar)
    {
        if (_registration is not null) return;
        _environmentPersistence = persistence ?? throw new ArgumentNullException(nameof(persistence));
        _backgroundPresenter = new EnvironmentBackgroundPresenter { Name = nameof(EnvironmentBackgroundPresenter) };
        if (!GodotObject.IsInstanceValid(_sandbox))
            _sandbox = FindFirst<SandboxRoot>(GetTree().Root);
        if (GodotObject.IsInstanceValid(_sandbox)) _backgroundPresenter.Configure(_sandbox!.Boundaries);
        GetTree().Root.AddChild(_backgroundPresenter);

        // The stored room painting is a local PNG asset; a missing or unreadable one simply leaves
        // the room blank, so composition never depends on it. Scene-enabled runs derive the path
        // solely from the stable active Scene ID; Initial Demo/fixtures retain the global path.
        var paintFiles = new CharacterFileSystem();
        string saveRoot = ProjectSettings.GlobalizePath("user://");
        _paintStore = _sandbox?.SceneProgress is { } sceneProgress
            ? EnvironmentPaintStore.ForScene(paintFiles, saveRoot, sceneProgress.ActiveSceneId)
            : new EnvironmentPaintStore(paintFiles, saveRoot);
        if (_paintStore.Load() is byte[] painted) _backgroundPresenter.Canvas.Replace(painted);
        _backgroundEditor = new EnvironmentBackgroundEditor { Name = nameof(EnvironmentBackgroundEditor) };
        _backgroundEditor.Configure(
            _backgroundPresenter,
            _paintStore,
            GodotObject.IsInstanceValid(_sandbox) ? _sandbox!.Economy : null);
        GetTree().Root.AddChild(_backgroundEditor);
        _paintIconBootstrap = new EnvironmentPaintToolIconBootstrap { Name = nameof(EnvironmentPaintToolIconBootstrap) };
        GetTree().Root.AddChild(_paintIconBootstrap);

        if (GodotObject.IsInstanceValid(_sandbox))
        {
            _decorationLayer = new EnvironmentDecorationLayer { Name = nameof(EnvironmentDecorationLayer) };
            _decorationLayer.Configure(state, _sandbox!.Boundaries);
            GetTree().Root.AddChild(_decorationLayer);
            _presentationVisibility.Configure(_backgroundPresenter, _decorationLayer);
            _decorator = new EnvironmentDecorator { Name = nameof(EnvironmentDecorator) };
            _decorator.Configure(_sandbox.Economy, _sandbox.Pointer, _sandbox.Buddy,
                _sandbox.VisualPresenter, persistence, _decorationLayer);
            _decorator.ConfigurePreferences(_sandbox.Shell);
            GetTree().Root.AddChild(_decorator);
            RegisterDecorator(commandBar, _decorator);
            SubscribeWorkCompanionState();
        }
        _registration = commandBar.RegisterCustomizeCommand(
            new CustomizeCommandDefinition(
                CustomizeCommandIds.PaintBackground,
                "Background",
                "Paint the room background.",
                CustomizeCommandIds.PaintBackgroundOrder),
            _backgroundEditor.Open);
        SetProcess(false);
        Log.Info(LogCategory, "Paint Background registered in the Paint menu.");
    }

    private void SubscribeWorkCompanionState()
    {
        if (_workCompanionSubscribed || !GodotObject.IsInstanceValid(_sandbox) ||
            !GodotObject.IsInstanceValid(_sandbox!.Window))
            return;

        _sandbox.Window.WorkCompanionActiveChanged += OnWorkCompanionActiveChanged;
        _workCompanionSubscribed = true;
        _presentationVisibility.SetWorkCompanionActive(_sandbox.Window.WorkCompanionActive);
    }

    private void OnWorkCompanionActiveChanged(bool active) =>
        _presentationVisibility.SetWorkCompanionActive(active);

    /// <summary>
    /// Registers the command whatever this build's scope is. The scenario that proves the
    /// decorator's own wiring must keep running in a Demo-scoped build, or hiding the feature
    /// would quietly stop testing it.
    /// </summary>
    internal void RegisterDecoratorForStartupTest(Win98CommandBarBootstrap commandBar, EnvironmentDecorator decorator) =>
        RegisterDecoratorCommand(commandBar, decorator);

    private void RegisterDecorator(Win98CommandBarBootstrap commandBar, EnvironmentDecorator decorator)
    {
        // The Initial Demo ships without the Room Decorator. Scene-enabled Next Fest can only be
        // enabled after this composition path and its build-scope regression gates are green.
        if (!DemoScope.IncludesRoomDecorator)
            return;

        RegisterDecoratorCommand(commandBar, decorator);
    }

    private void RegisterDecoratorCommand(Win98CommandBarBootstrap commandBar, EnvironmentDecorator decorator)
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
        if (_workCompanionSubscribed && GodotObject.IsInstanceValid(_sandbox) &&
            GodotObject.IsInstanceValid(_sandbox!.Window))
        {
            _sandbox.Window.WorkCompanionActiveChanged -= OnWorkCompanionActiveChanged;
        }
        _workCompanionSubscribed = false;
        _registration?.Dispose();
        _registration = null;
        _decoratorRegistration?.Dispose();
        _decoratorRegistration = null;
        if (GodotObject.IsInstanceValid(_paintIconBootstrap)) _paintIconBootstrap!.QueueFree();
        if (GodotObject.IsInstanceValid(_backgroundEditor)) _backgroundEditor!.QueueFree();
        if (GodotObject.IsInstanceValid(_backgroundPresenter)) _backgroundPresenter!.QueueFree();
        if (GodotObject.IsInstanceValid(_decorationLayer)) _decorationLayer!.QueueFree();
        if (GodotObject.IsInstanceValid(_decorator)) _decorator!.QueueFree();
        _presentationVisibility.SetWorkCompanionActive(false);
        _sandbox = null;
        _paintIconBootstrap = null;
        _backgroundEditor = null;
        _backgroundPresenter = null;
        _paintStore = null;
        _decorationLayer = null;
        _decorator = null;
        _environmentPersistence = null;
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
