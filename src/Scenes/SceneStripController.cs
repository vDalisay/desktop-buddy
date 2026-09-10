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
    private readonly List<SceneId> _knownIds = [];
    private readonly List<string> _knownNames = [];
    private SandboxRoot _sandbox = null!;
    private SceneProgressCoordinator _scenes = null!;
    private Win98CommandBarBootstrap _commandBar = null!;
    private string _saveRoot = null!;
    private Win98WindowFrame? _frame;
    private HBoxContainer _strip = null!;
    private Control? _nameBlocker;
    private Label? _nameMessage;
    private Label? _nameTitle;
    private Button? _nameConfirm;
    private LineEdit? _nameInput;
    private SceneId _knownActive;
    private bool _knownBusy;
    private bool _configured;
    private bool _renaming;

    /// <summary>
    /// The tab row itself. The Win98 command bar reparents it when the desktop chrome exists;
    /// headless verification has no window frame, so the controls are addressed from here.
    /// </summary>
    public Control SceneStripRoot => _strip;

    public void Configure(SandboxRoot sandbox, Win98CommandBarBootstrap commandBar, string saveRoot)
    {
        if (IsInsideTree())
            throw new InvalidOperationException("Scene strip must be configured before entering the tree.");

        _sandbox = sandbox ?? throw new ArgumentNullException(nameof(sandbox));
        _commandBar = commandBar ?? throw new ArgumentNullException(nameof(commandBar));
        _saveRoot = string.IsNullOrWhiteSpace(saveRoot)
            ? throw new ArgumentException("Scene strip requires a resolved save root.", nameof(saveRoot))
            : System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(saveRoot));
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
        Rebuild();
    }

    public override void _Process(double delta)
    {
        _strip.Visible = _sandbox.Shell.Mode == InputMode.Play;
        _frame ??= GetTree().Root.FindChild(
            nameof(Win98WindowFrame), recursive: true, owned: false) as Win98WindowFrame;

        if (SceneViewChanged())
            Rebuild();
        UpdatePlacementPreview();
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

        _strip.AddChild(CreateSceneButton());
        RefreshSceneMenu();
    }

    /// <summary>
    /// One click, one workspace. This replaced three Scene tabs, a "Scene ▾" dropdown and a "+"
    /// button, which between them scattered switching, renaming, the cast and the overflow list
    /// across the command bar (owner instruction 2026-09-10).
    /// </summary>
    private Button CreateSceneButton()
    {
        var button = new Button
        {
            Name = "SceneMenu",
            Text = $"Scene: {_scenes.ActiveScene.Name}",
            TooltipText = "Open the Scene workspace: switch, create, rename and manage the cast.",
            Disabled = _sandbox.IsSceneSwitchInProgress,
            FocusMode = Control.FocusModeEnum.All,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
            CustomMinimumSize = new Vector2(Win98ThemeFactory.Px(148), Win98ThemeFactory.Px(22)),
        };
        button.Pressed += OpenSceneMenu;
        return button;
    }

    /// <summary>
    /// One Win98 modal, built the way every other workspace in this shell builds one. The Scene
    /// menu used raw AcceptDialog/ConfirmationDialog windows, so it wore the engine's own chrome
    /// instead of the shell's while the rest of the game stayed in period (owner report
    /// 2026-09-10). The blocker is the modal; hiding it closes the dialog.
    /// </summary>
    internal Control? OpenShellModal(
        string name,
        string title,
        Vector2 size,
        out VBoxContainer body,
        out Label heading)
    {
        body = null!;
        heading = null!;
        if (_frame is null || !GodotObject.IsInstanceValid(_frame))
            return null;

        Control blocker = Win98Dialog.Blocker(_frame, $"{name}Blocker");
        blocker.ZIndex = 500;
        foreach (Node child in blocker.GetChildren())
            child.QueueFree();

        PanelContainer dialog = Win98Dialog.Create(name, title, size, out body, () => blocker.Visible = false);
        blocker.AddChild(dialog);
        heading = new Label
        {
            Name = $"{name}Message",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        body.AddChild(heading);
        blocker.Visible = true;
        dialog.Visible = true;
        return blocker;
    }

    internal static HBoxContainer ModalActions(VBoxContainer body, string name)
    {
        var actions = new HBoxContainer
        {
            Name = name,
            Alignment = BoxContainer.AlignmentMode.End,
        };
        actions.AddThemeConstantOverride("separation", Win98ThemeFactory.Gap);
        body.AddChild(actions);
        return actions;
    }

    private void OpenNameDialog(bool rename)
    {
        _renaming = rename;
        _nameBlocker = OpenShellModal(
            "SceneNameDialog",
            rename ? "Rename Scene" : "Create Scene",
            new Vector2(360, 168),
            out VBoxContainer body,
            out Label message);
        if (_nameBlocker is null)
            return;

        _nameMessage = message;
        _nameMessage.Text = "Enter a name with 1-64 visible characters.";
        _nameInput = new LineEdit
        {
            Name = "SceneNameInput",
            MaxLength = SceneDocument.MaximumNameLength,
            CustomMinimumSize = new Vector2(320, Win98ThemeFactory.Px(24)),
            Text = rename ? _scenes.ActiveScene.Name : "New Scene",
        };
        _nameInput.TextSubmitted += _ => SubmitNameAsync();
        body.AddChild(_nameInput);

        HBoxContainer actions = ModalActions(body, "SceneNameActions");
        _nameConfirm = Win98Dialog.Action(actions, rename ? "Rename" : "Create", SubmitNameAsync);
        _nameConfirm.Name = "SceneNameConfirmButton";
        Win98Dialog.Action(actions, "Cancel", () => _nameBlocker!.Visible = false).Name =
            "SceneNameCancelButton";
        _nameInput.GrabFocus();
        _nameInput.SelectAll();
    }

    private async void SubmitNameAsync()
    {
        if (_nameInput is null || !GodotObject.IsInstanceValid(_nameInput))
            return;
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
            // The dialog stays open on a rejected name so the player can correct it in place.
            SetStatus(exception.Message);
            if (_nameMessage is not null && GodotObject.IsInstanceValid(_nameMessage))
                _nameMessage.Text = exception.Message;
            _nameInput.GrabFocus();
            return;
        }

        if (_nameBlocker is not null && GodotObject.IsInstanceValid(_nameBlocker))
            _nameBlocker.Visible = false;
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
