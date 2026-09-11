using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using DesktopBuddy.Domain.Content;
using DesktopBuddy.App;
using DesktopBuddy.Domain.Environment;
using DesktopBuddy.Domain.Platform;
using DesktopBuddy.Domain.Sandbox;
using DesktopBuddy.Persistence;
using DesktopBuddy.UI.Win98;
using Godot;

namespace DesktopBuddy.Sandbox;

/// <summary>
/// Build/Edit: the paused workspace where the player places and removes the parts a room is built
/// from. It owns controls only — the parts themselves live in the Scene's sandbox document and the
/// live bodies belong to <see cref="SandboxRoot"/>, exactly as the Scene strip owns no Scenes.
/// </summary>
public partial class BuildModeController : Node
{
    private readonly List<SandboxPartDefinition> _palette = [.. SandboxPartCatalogue.Definitions];
    private SandboxRoot _sandbox = null!;
    private SceneProgressCoordinator _scenes = null!;
    private Win98CommandBarBootstrap _commandBar = null!;
    private Win98WindowFrame? _frame;
    private IDisposable? _registration;
    private CanvasLayer? _layer;
    private PanelContainer? _panel;
    private Win98PinnablePanel? _panelPin;
    private ItemList? _partList;
    private SandboxPartPreview? _preview;
    private Label? _description;
    private Label? _hint;
    private bool _configured;
    private int _selectedIndex;

    public bool IsActive { get; private set; }

    public void Configure(SandboxRoot sandbox, Win98CommandBarBootstrap commandBar)
    {
        if (IsInsideTree())
            throw new InvalidOperationException("Build Mode must be configured before entering the tree.");

        _sandbox = sandbox ?? throw new ArgumentNullException(nameof(sandbox));
        _commandBar = commandBar ?? throw new ArgumentNullException(nameof(commandBar));
        _scenes = sandbox.SceneProgress
            ?? throw new ArgumentException("Build Mode requires Scene-enabled progress.", nameof(sandbox));
        _configured = true;
    }

    public override void _Ready()
    {
        if (!_configured)
            throw new InvalidOperationException("Build Mode was not configured.");

        ProcessMode = ProcessModeEnum.Always;
        _registration = _commandBar.RegisterTopLevelCommand(
            new TopLevelCommandDefinition(
                TopLevelCommandIds.BuildMode,
                "Build",
                "Place and remove the parts this room is built from.",
                TopLevelCommandIds.BuildModeOrder),
            Toggle,
            isVisible: () => true,
            isEnabled: () => IsActive || CanEnter());
    }

    public override void _ExitTree()
    {
        if (IsActive)
            _ = LeaveAsync();
        _registration?.Dispose();
        _registration = null;
    }

    private bool CanEnter() =>
        _sandbox.Shell.Mode == InputMode.Play &&
        !_sandbox.Lifecycle.IsEditorModeActive &&
        !_sandbox.IsSceneSwitchInProgress;

    /// <summary>Enters Build/Edit, or returns to Play when it is already open.</summary>
    public void Toggle()
    {
        if (IsActive)
        {
            _ = LeaveAsync();
            return;
        }
        if (!CanEnter())
        {
            SetStatus("Close Work/Edit state before building.");
            return;
        }
        Enter();
    }

    /// <summary>
    /// Entering pauses the room so parts can be placed against a still world; leaving resumes it,
    /// which is the Play half of the same switch.
    /// </summary>
    public void Enter()
    {
        IsActive = true;
        _sandbox.Lifecycle.PauseCoordinator.Set(GameplayPauseReason.BuildMode, true);
        BuildUi();
        RefreshPalette();
        SetStatus("Build: click to place, right-click a part to remove it. Escape plays.");
    }

    /// <summary>Returns to Play and commits the room the player just built.</summary>
    public async Task LeaveAsync()
    {
        IsActive = false;
        // The panel, never the layer: a detached palette lives in its own desktop window, and
        // Win98PinnablePanel mirrors that window's visibility from the panel it follows.
        if (_panel is not null)
            _panel.Visible = false;
        _sandbox.Lifecycle.PauseCoordinator.Set(GameplayPauseReason.BuildMode, false);
        SetStatus("Play.");

        try
        {
            await _scenes.FlushAsync(force: true);
        }
        catch (Exception exception)
        {
            Diagnostics.Log.Error("BuildMode", $"Could not save the built room: {exception}");
            SetStatus($"The room could not be saved: {exception.Message}");
        }
    }

    public override void _Process(double delta)
    {
        _frame ??= GetTree().Root.FindChild(
            nameof(Win98WindowFrame), recursive: true, owned: false) as Win98WindowFrame;

        // Work Mode, a Scene switch or an editor takes the room away from Build without asking.
        if (IsActive && !CanEnter())
            _ = LeaveAsync();
    }

    /// <summary>
    /// Unhandled, not <c>_Input</c>: the palette is a Control, so the GUI must get the click first
    /// or every press on it is swallowed as a placement and no other part can be selected.
    /// </summary>
    public override void _UnhandledInput(InputEvent @event)
    {
        if (!IsActive)
            return;

        if (@event is InputEventKey { Pressed: true, Keycode: Key.Escape })
        {
            GetViewport().SetInputAsHandled();
            _ = LeaveAsync();
            return;
        }
        if (@event is not InputEventMouseButton { Pressed: true } click)
            return;
        if (click.ButtonIndex is not (MouseButton.Left or MouseButton.Right))
            return;

        Vector2 world = _sandbox.GetGlobalMousePosition();
        if (!_sandbox.Boundaries.InnerBounds.HasPoint(world))
            return;

        GetViewport().SetInputAsHandled();
        if (click.ButtonIndex == MouseButton.Right)
            RemovePartAt(world);
        else
            PlaceSelectedPartAt(world);
    }

    /// <summary>Selects one palette part by definition, as clicking its row does.</summary>
    public bool SelectPart(SemanticDefinitionId definitionId)
    {
        for (int index = 0; index < _palette.Count; index++)
        {
            if (_palette[index].Id != definitionId)
                continue;
            _partList?.Select(index);
            _selectedIndex = index;
            ShowSelectedPart();
            return true;
        }
        return false;
    }

    public void PlaceSelectedPartAt(Vector2 world)
    {
        if (SelectedDefinition() is not { } definition)
            return;

        SandboxDocument document = _scenes.ActiveSandbox;
        Rect2 bounds = _sandbox.Boundaries.InnerBounds;
        SandboxEditResult added = document.Add(
            definition.Id,
            new CanonicalRoomPosition(
                Mathf.Clamp((world.X - bounds.Position.X) / Math.Max(1.0f, bounds.Size.X), 0.0f, 1.0f),
                Mathf.Clamp((world.Y - bounds.Position.Y) / Math.Max(1.0f, bounds.Size.Y), 0.0f, 1.0f)));

        if (!added.Succeeded)
        {
            SetStatus(added.Status == SandboxEditStatus.LimitReached
                ? $"This room already holds {SandboxDocument.MaximumParts} parts."
                : $"Could not place the part ({added.Status}).");
            return;
        }

        _sandbox.PlaceBuiltPart(added.Part!);
        SetStatus($"Placed {definition.DisplayName}. {document.Count} parts in this room.");
    }

    public void RemovePartAt(Vector2 world)
    {
        if (!_sandbox.TryPickBuiltPart(world, out SandboxPartId partId))
        {
            SetStatus("No part there to remove.");
            return;
        }

        SandboxEditResult removed = _scenes.ActiveSandbox.Remove(partId);
        if (!removed.Succeeded)
        {
            SetStatus($"Could not remove the part ({removed.Status}).");
            return;
        }

        _sandbox.RemoveBuiltPartBody(partId);
        SetStatus($"Removed a part. {_scenes.ActiveSandbox.Count} parts in this room.");
    }

    private void ShowSelectedPart()
    {
        SandboxPartDefinition? definition = SelectedDefinition();
        _preview?.Show(definition);
        if (_description is not null)
        {
            _description.Text = definition is null
                ? string.Empty
                : $"{definition.Description}\n{definition.Material} · {definition.Width:0}×{definition.Height:0} · mass {definition.Mass:0.#}";
        }
    }

    private SandboxPartDefinition? SelectedDefinition()
    {
        int[] selected = _partList?.GetSelectedItems() ?? [];
        int index = selected.Length > 0 ? selected[0] : _selectedIndex;
        return index >= 0 && index < _palette.Count ? _palette[index] : null;
    }

    private void BuildUi()
    {
        if (_layer is not null)
        {
            _panel!.Visible = true;
            return;
        }

        _layer = new CanvasLayer { Name = "BuildModeLayer", Layer = 90 };
        _panel = Win98Dialog.Create(
            "BuildModePalette", "Build", new Vector2(450, 320), out VBoxContainer body,
            () => _ = LeaveAsync(), draggable: false);
        _panel.Visible = true;
        // Opens centred, like every other shell workspace (owner instruction 2026-09-10). Growing
        // both ways keeps it centred if its content outgrows the authored size.
        _panel.GrowHorizontal = Control.GrowDirection.Both;
        _panel.GrowVertical = Control.GrowDirection.Both;
        _partList = new ItemList
        {
            Name = "BuildModePartList",
            CustomMinimumSize = new Vector2(200, 92),
        };
        _partList.ItemSelected += index =>
        {
            _selectedIndex = (int)index;
            ShowSelectedPart();
        };

        var browser = new HBoxContainer { Name = "BuildModeBrowser" };
        browser.AddThemeConstantOverride("separation", 10);
        body.AddChild(browser);
        browser.AddChild(_partList);

        // What the part actually is, before it is in the room: the shape and material that will
        // be placed, plus what it is for (owner instruction 2026-09-10).
        _preview = new SandboxPartPreview
        {
            Name = "BuildModePartPreview",
            CustomMinimumSize = new Vector2(200, 76),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        var details = new VBoxContainer
        {
            Name = "BuildModeDetails",
            CustomMinimumSize = new Vector2(200, 0),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        details.AddChild(_preview);

        _description = new Label
        {
            Name = "BuildModePartDescription",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(200, 52),
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        details.AddChild(_description);
        browser.AddChild(details);

        _hint = new Label
        {
            Name = "BuildModeHint",
            Text = "Left click places, right click removes.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(200, 0),
        };
        body.AddChild(_hint);
        _layer.AddChild(_panel);
        AddChild(_layer);
        // Same deal as Paint Background: the pin controller owns the title drag, so the palette
        // can be pulled out onto the desktop and pinned back rather than being stuck in the room.
        _panelPin = new Win98PinnablePanel { Name = "BuildModePinController" };
        AddChild(_panelPin);
        _panelPin.Configure(_panel, new Vector2I(470, 350), "BuildModeWindow");
    }


    private void RefreshPalette()
    {
        if (_partList is null)
            return;

        _partList.Clear();
        foreach (SandboxPartDefinition definition in _palette)
            _partList.AddItem(definition.DisplayName);
        if (_palette.Count > 0)
        {
            _partList.Select(Math.Clamp(_selectedIndex, 0, _palette.Count - 1));
        }
        if (_hint is not null)
            _hint.Text = $"{_scenes.ActiveSandbox.Count} of {SandboxDocument.MaximumParts} parts placed.";
        ShowSelectedPart();
    }

    private void SetStatus(string status)
    {
        if (GodotObject.IsInstanceValid(_frame))
            _frame!.StatusText = status;
    }
}
