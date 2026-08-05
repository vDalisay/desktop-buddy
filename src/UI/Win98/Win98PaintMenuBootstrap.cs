using System;
using System.Collections.Generic;
using DesktopBuddy.CharacterEditor;
using Godot;

namespace DesktopBuddy.UI.Win98;

/// <summary>
/// Adds the classic File / Edit / View command row to the integrated paint editor. Commands
/// delegate to the editor's existing buttons, so there is one behavior path for pointer,
/// keyboard, footer and menu activation.
/// </summary>
public partial class Win98PaintMenuBootstrap : Node
{
    private readonly List<(Button MenuCommand, Button Source)> _mirroredCommands = [];
    private CharacterEditorHost? _host;
    private HBoxContainer? _menuBar;
    private PanelContainer? _commandPanel;
    private HBoxContainer? _commandRow;
    private Button? _fileButton;
    private Button? _editButton;
    private Button? _viewButton;
    private string? _activeMenu;

    public override void _Ready()
    {
        // CharacterEditorModeCoordinator pauses the gameplay tree while editing. This bootstrap
        // must remain alive for deferred composition, menu state and Escape handling.
        ProcessMode = ProcessModeEnum.Always;
    }

    public override void _Process(double delta)
    {
        ResolveHost();
        if (!GodotObject.IsInstanceValid(_host) || !_host!.IsEditorOpen)
        {
            CloseMenu();
            return;
        }

        if (!GodotObject.IsInstanceValid(_menuBar))
            TryBuild();

        foreach ((Button menuCommand, Button source) in _mirroredCommands)
        {
            if (GodotObject.IsInstanceValid(menuCommand) && GodotObject.IsInstanceValid(source))
                menuCommand.Disabled = source.Disabled;
        }
    }

    public override void _UnhandledKeyInput(InputEvent input)
    {
        if (_activeMenu is null || input is not InputEventKey { Pressed: true, Keycode: Key.Escape })
            return;

        CloseMenu();
        GetViewport().SetInputAsHandled();
    }

    private void ResolveHost()
    {
        if (!GodotObject.IsInstanceValid(_host))
            _host = GetTree().Root.FindChild(nameof(CharacterEditorHost), true, false) as CharacterEditorHost;
    }

    private void TryBuild()
    {
        if (_host!.FindChild("Win98PaintWorkspace", true, false) is not HBoxContainer workspace ||
            workspace.GetParent() is not HSplitContainer editorBody ||
            editorBody.GetParent() is not VBoxContainer editorColumn)
        {
            return;
        }

        _menuBar = new HBoxContainer
        {
            Name = "Win98PaintMenuBar",
            MouseFilter = Control.MouseFilterEnum.Stop,
            CustomMinimumSize = new Vector2(0, 24),
        };
        _menuBar.AddThemeConstantOverride("separation", 0);
        editorColumn.AddChild(_menuBar);
        editorColumn.MoveChild(_menuBar, editorBody.GetIndex());

        _fileButton = AddMenuButton("File", "File commands");
        _editButton = AddMenuButton("Edit", "Painting history commands");
        _viewButton = AddMenuButton("View", "Viewport commands");
        _fileButton.Pressed += () => ToggleMenu("File");
        _editButton.Pressed += () => ToggleMenu("Edit");
        _viewButton.Pressed += () => ToggleMenu("View");

        _commandPanel = new PanelContainer
        {
            Name = "Win98PaintMenuCommands",
            Visible = false,
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        _commandPanel.AddThemeStyleboxOverride(
            "panel",
            Win98ThemeFactory.Raised(Win98ThemeFactory.Face, 2));
        editorColumn.AddChild(_commandPanel);
        editorColumn.MoveChild(_commandPanel, _menuBar.GetIndex() + 1);

        _commandRow = new HBoxContainer
        {
            Name = "Win98PaintMenuCommandRow",
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        _commandRow.AddThemeConstantOverride("separation", 2);
        _commandPanel.AddChild(_commandRow);
    }

    private Button AddMenuButton(string text, string tooltip)
    {
        var button = new Button
        {
            Text = text,
            TooltipText = tooltip,
            ToggleMode = true,
            FocusMode = Control.FocusModeEnum.All,
            MouseFilter = Control.MouseFilterEnum.Stop,
            CustomMinimumSize = new Vector2(52, 24),
        };
        _menuBar!.AddChild(button);
        return button;
    }

    private void ToggleMenu(string menu)
    {
        if (string.Equals(_activeMenu, menu, StringComparison.Ordinal))
        {
            CloseMenu();
            return;
        }

        _activeMenu = menu;
        _fileButton!.ButtonPressed = menu == "File";
        _editButton!.ButtonPressed = menu == "Edit";
        _viewButton!.ButtonPressed = menu == "View";
        RebuildCommands(menu);
        _commandPanel!.Visible = true;
    }

    private void RebuildCommands(string menu)
    {
        foreach (Node child in _commandRow!.GetChildren())
        {
            _commandRow.RemoveChild(child);
            child.QueueFree();
        }
        _mirroredCommands.Clear();

        switch (menu)
        {
            case "File":
                AddMirroredCommand("Save", "SaveCharacterButton", "Save the current character.");
                AddMirroredCommand("Use Character", "UseCharacterButton", "Save and use this character.");
                AddSeparator();
                AddMirroredCommand("Close", "CloseCharacterEditorButton", "Close the paint editor.");
                break;

            case "Edit":
                AddMirroredCommand("Undo", "PaintUndoButton", "Undo the last paint action (Ctrl+Z).");
                AddMirroredCommand("Redo", "PaintRedoButton", "Redo the last paint action (Ctrl+Y).");
                AddSeparator();
                AddMirroredCommand("Erase All…", "PaintEraseAllButton", "Erase all paint after confirmation.");
                break;

            case "View":
                AddMirroredCommand("Zoom Out", "PaintZoomOutButton", "Zoom out.");
                AddMirroredCommand("Zoom In", "PaintZoomInButton", "Zoom in.");
                AddMirroredCommand("Reset View", "PaintResetViewButton", "Restore the default framing.");
                AddSeparator();
                AddRotateCommand("Rotate Left", 0, "Rotate the buddy 90° left.");
                AddRotateCommand("Rotate Right", 1, "Rotate the buddy 90° right.");
                break;
        }
    }

    private void AddMirroredCommand(string text, string sourceName, string tooltip)
    {
        if (_host!.FindChild(sourceName, true, false) is not Button source)
            return;

        Button command = CommandButton(text, tooltip);
        command.Disabled = source.Disabled;
        command.Pressed += () =>
        {
            if (!source.Disabled)
                source.EmitSignal(Button.SignalName.Pressed);
            CloseMenu();
        };
        _commandRow!.AddChild(command);
        _mirroredCommands.Add((command, source));
    }

    private void AddRotateCommand(string text, int childIndex, string tooltip)
    {
        if (_host!.FindChild("PaintRotateRow", true, false) is not HBoxContainer row ||
            childIndex < 0 || childIndex >= row.GetChildCount() ||
            row.GetChild(childIndex) is not Button source)
        {
            return;
        }

        Button command = CommandButton(text, tooltip);
        command.Pressed += () =>
        {
            source.EmitSignal(Button.SignalName.Pressed);
            CloseMenu();
        };
        _commandRow!.AddChild(command);
    }

    private void AddSeparator()
    {
        _commandRow!.AddChild(new VSeparator
        {
            CustomMinimumSize = new Vector2(4, 24),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        });
    }

    private static Button CommandButton(string text, string tooltip) => new()
    {
        Text = text,
        TooltipText = tooltip,
        FocusMode = Control.FocusModeEnum.All,
        MouseFilter = Control.MouseFilterEnum.Stop,
        CustomMinimumSize = new Vector2(76, 26),
    };

    private void CloseMenu()
    {
        _activeMenu = null;
        if (GodotObject.IsInstanceValid(_commandPanel))
            _commandPanel!.Visible = false;
        if (GodotObject.IsInstanceValid(_fileButton))
            _fileButton!.ButtonPressed = false;
        if (GodotObject.IsInstanceValid(_editButton))
            _editButton!.ButtonPressed = false;
        if (GodotObject.IsInstanceValid(_viewButton))
            _viewButton!.ButtonPressed = false;
    }
}
