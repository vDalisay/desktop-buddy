using System;
using System.Collections.Generic;
using DesktopBuddy.App;
using DesktopBuddy.Domain.Platform;
using DesktopBuddy.Domain.Scenes;
using DesktopBuddy.Persistence;
using DesktopBuddy.UI.Win98;
using Godot;

namespace DesktopBuddy.Scenes;

/// <summary>Player-facing Scene tabs; Scene mutation and switching stay with their existing owners.</summary>
public partial class SceneStripController : Node
{
    private const int VisibleTabCount = 3;

    private readonly List<SceneId> _knownIds = [];
    private readonly List<string> _knownNames = [];
    private SandboxRoot _sandbox = null!;
    private SceneProgressCoordinator _scenes = null!;
    private Win98CommandBarBootstrap _commandBar = null!;
    private Win98WindowFrame? _frame;
    private HBoxContainer _strip = null!;
    private AcceptDialog _nameDialog = null!;
    private LineEdit _nameInput = null!;
    private SceneId _knownActive;
    private bool _knownBusy;
    private bool _configured;
    private bool _renaming;

    public void Configure(SandboxRoot sandbox, Win98CommandBarBootstrap commandBar)
    {
        if (IsInsideTree())
            throw new InvalidOperationException("Scene strip must be configured before entering the tree.");

        _sandbox = sandbox ?? throw new ArgumentNullException(nameof(sandbox));
        _commandBar = commandBar ?? throw new ArgumentNullException(nameof(commandBar));
        _scenes = sandbox.SceneProgress
            ?? throw new ArgumentException("Scene strip requires Scene-enabled progress.", nameof(sandbox));
        _configured = true;
    }

    public override void _Ready()
    {
        if (!_configured)
            throw new InvalidOperationException("Scene strip was not configured.");

        _strip = new HBoxContainer
        {
            Name = "Win98SceneStrip",
            MouseFilter = Control.MouseFilterEnum.Pass,
        };
        _strip.AddThemeConstantOverride("separation", Win98ThemeFactory.Px(2));
        _commandBar.SetSceneStrip(_strip);
        BuildNameDialog();
        Rebuild();
    }

    public override void _Process(double delta)
    {
        _strip.Visible = _sandbox.Shell.Mode == InputMode.Play;
        _frame ??= GetTree().Root.FindChild(
            nameof(Win98WindowFrame), recursive: true, owned: false) as Win98WindowFrame;

        if (SceneViewChanged())
            Rebuild();
    }

    public override void _ExitTree()
    {
        if (GodotObject.IsInstanceValid(_commandBar))
            _commandBar.SetSceneStrip(null);
        if (GodotObject.IsInstanceValid(_strip))
            _strip.QueueFree();
    }

    private bool SceneViewChanged()
    {
        IReadOnlyList<SceneDocument> scenes = _scenes.Scenes;
        if (_knownActive != _scenes.ActiveSceneId || _knownBusy != _sandbox.IsSceneSwitchInProgress ||
            _knownIds.Count != scenes.Count)
            return true;

        for (int index = 0; index < scenes.Count; index++)
        {
            if (_knownIds[index] != scenes[index].SceneId ||
                !string.Equals(_knownNames[index], scenes[index].Name, StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    private void Rebuild()
    {
        foreach (Node child in _strip.GetChildren())
            child.QueueFree();

        _knownIds.Clear();
        _knownNames.Clear();
        IReadOnlyList<SceneDocument> scenes = _scenes.Scenes;
        int activeIndex = 0;
        for (int index = 0; index < scenes.Count; index++)
        {
            SceneDocument scene = scenes[index];
            _knownIds.Add(scene.SceneId);
            _knownNames.Add(scene.Name);
            if (scene.SceneId == _scenes.ActiveSceneId)
                activeIndex = index;
        }
        _knownActive = _scenes.ActiveSceneId;
        _knownBusy = _sandbox.IsSceneSwitchInProgress;

        int visibleCount = Math.Min(VisibleTabCount, scenes.Count);
        int firstVisible = scenes.Count <= VisibleTabCount
            ? 0
            : Math.Clamp(activeIndex - 1, 0, scenes.Count - VisibleTabCount);

        for (int index = firstVisible; index < firstVisible + visibleCount; index++)
            _strip.AddChild(CreateTab(scenes[index]));

        _strip.AddChild(CreateSceneMenu(scenes, firstVisible, visibleCount));
        _strip.AddChild(CreateAddButton());
    }

    private Button CreateTab(SceneDocument scene)
    {
        var button = new Button
        {
            Name = $"SceneTab_{scene.SceneId}",
            Text = scene.Name,
            TooltipText = $"Switch to {scene.Name}.",
            ToggleMode = true,
            ButtonPressed = scene.SceneId == _scenes.ActiveSceneId,
            Disabled = _sandbox.IsSceneSwitchInProgress,
            FocusMode = Control.FocusModeEnum.All,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
            CustomMinimumSize = new Vector2(Win98ThemeFactory.Px(72), Win98ThemeFactory.Px(22)),
        };
        SceneId sceneId = scene.SceneId;
        button.Pressed += () =>
        {
            if (sceneId == _scenes.ActiveSceneId)
                button.ButtonPressed = true;
            else
                SwitchToAsync(sceneId);
        };
        return button;
    }

    private MenuButton CreateSceneMenu(IReadOnlyList<SceneDocument> scenes, int firstVisible, int visibleCount)
    {
        var more = new MenuButton
        {
            Name = "SceneMenu",
            Text = "Scene ▾",
            TooltipText = "Manage Scenes and show hidden tabs.",
            Disabled = _sandbox.IsSceneSwitchInProgress,
            FocusMode = Control.FocusModeEnum.All,
            CustomMinimumSize = new Vector2(Win98ThemeFactory.Px(78), Win98ThemeFactory.Px(22)),
        };
        PopupMenu popup = more.GetPopup();
        Win98MenuStyle.Apply(popup);
        popup.AddItem("Rename active Scene...", 1);
        var overflowIds = new Dictionary<long, SceneId>();
        long itemId = 100;
        for (int index = 0; index < scenes.Count; index++)
        {
            if (index >= firstVisible && index < firstVisible + visibleCount)
                continue;
            if (overflowIds.Count == 0)
                popup.AddSeparator("Switch to");
            SceneDocument scene = scenes[index];
            popup.AddItem(scene.Name, (int)itemId);
            overflowIds.Add(itemId, scene.SceneId);
            itemId++;
        }
        popup.IdPressed += id =>
        {
            if (id == 1)
                OpenNameDialog(rename: true);
            else if (overflowIds.TryGetValue(id, out SceneId sceneId))
                SwitchToAsync(sceneId);
        };
        return more;
    }

    private Button CreateAddButton()
    {
        var add = new Button
        {
            Name = "SceneCreateButton",
            Text = "+",
            TooltipText = _scenes.CanCreateScene ? "Create a Scene." : "The Scene limit has been reached.",
            Disabled = _sandbox.IsSceneSwitchInProgress || !_scenes.CanCreateScene,
            FocusMode = Control.FocusModeEnum.All,
            CustomMinimumSize = new Vector2(Win98ThemeFactory.Px(24), Win98ThemeFactory.Px(22)),
        };
        add.Pressed += () => OpenNameDialog(rename: false);
        return add;
    }

    private void BuildNameDialog()
    {
        _nameDialog = new AcceptDialog
        {
            Name = "SceneNameDialog",
            Title = "Scene name",
            DialogText = "Enter a name with 1-64 visible characters.",
            OkButtonText = "Create",
            MinSize = new Vector2I(360, 150),
            Theme = Win98ThemeFactory.Create(),
        };
        _nameInput = new LineEdit
        {
            Name = "SceneNameInput",
            MaxLength = SceneDocument.MaximumNameLength,
            CustomMinimumSize = new Vector2(320, Win98ThemeFactory.Px(24)),
        };
        _nameDialog.AddChild(_nameInput);
        _nameDialog.RegisterTextEnter(_nameInput);
        _nameDialog.Confirmed += SubmitNameAsync;
        AddChild(_nameDialog);
    }

    private void OpenNameDialog(bool rename)
    {
        _renaming = rename;
        _nameDialog.Title = rename ? "Rename Scene" : "Create Scene";
        _nameDialog.OkButtonText = rename ? "Rename" : "Create";
        _nameDialog.DialogText = "Enter a name with 1-64 visible characters.";
        _nameInput.Text = rename ? _scenes.ActiveScene.Name : "New Scene";
        _nameDialog.PopupCentered();
        _nameInput.GrabFocus();
        _nameInput.SelectAll();
    }

    private async void SubmitNameAsync()
    {
        string name = _nameInput.Text;
        SceneLibraryResult result;
        try
        {
            result = _renaming
                ? _scenes.RenameScene(_scenes.ActiveSceneId, name)
                : _scenes.CreateScene(name);
            if (!result.Succeeded && result.Status != SceneLibraryStatus.NoChange)
                throw new InvalidOperationException($"Scene change failed: {result.Status}.");
        }
        catch (Exception exception)
        {
            SetStatus(exception.Message);
            _nameDialog.DialogText = exception.Message;
            _nameDialog.PopupCentered();
            _nameInput.GrabFocus();
            return;
        }

        Rebuild();
        try
        {
            await _scenes.FlushAsync(force: true);
            SetStatus(_renaming ? $"Renamed Scene to {name}." : $"Created Scene: {name}.");
        }
        catch (Exception exception)
        {
            SetStatus($"Scene changed but could not be saved: {exception.Message}");
        }
    }

    private async void SwitchToAsync(SceneId sceneId)
    {
        if (_sandbox.IsSceneSwitchInProgress || sceneId == _scenes.ActiveSceneId)
            return;

        SceneDocument? target = null;
        foreach (SceneDocument scene in _scenes.Scenes)
        {
            if (scene.SceneId == sceneId)
            {
                target = scene;
                break;
            }
        }
        if (target is null)
            return;

        SetStatus($"Switching to {target.Name}...");
        Rebuild();
        SceneRuntimeSwitchResult result = await _sandbox.SwitchSceneAsync(sceneId);
        SetStatus(result.Succeeded
            ? $"Scene: {target.Name}"
            : result.Detail ?? $"Could not switch Scene ({result.Status}).");
        Rebuild();
    }

    private void SetStatus(string status)
    {
        if (GodotObject.IsInstanceValid(_frame))
            _frame!.StatusText = status;
    }
}
