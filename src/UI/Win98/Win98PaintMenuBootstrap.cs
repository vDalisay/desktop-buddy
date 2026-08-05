using System;
using System.Collections.Generic;
using DesktopBuddy.CharacterEditor;
using Godot;

namespace DesktopBuddy.UI.Win98;

/// <summary>
/// Adds the classic File / Edit / View menu row to the integrated paint editor. Commands
/// delegate to the editor's existing buttons, so there is one behavior path for pointer,
/// keyboard, footer and menu activation.
/// </summary>
public partial class Win98PaintMenuBootstrap : Node
{
    private CharacterEditorHost? _host;
    private HBoxContainer? _menuBar;

    public override void _Ready()
    {
        // CharacterEditorModeCoordinator pauses the gameplay tree while editing. This bootstrap
        // must remain alive for deferred composition.
        ProcessMode = ProcessModeEnum.Always;
    }

    public override void _Process(double delta)
    {
        if (!GodotObject.IsInstanceValid(_host))
            _host = GetTree().Root.FindChild(nameof(CharacterEditorHost), true, false) as CharacterEditorHost;

        if (GodotObject.IsInstanceValid(_host) && _host!.IsEditorOpen && !GodotObject.IsInstanceValid(_menuBar))
            TryBuild();
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
            CustomMinimumSize = new Vector2(0, Win98ThemeFactory.ControlHeight),
        };
        _menuBar.AddThemeConstantOverride("separation", 0);
        editorColumn.AddChild(_menuBar);
        editorColumn.MoveChild(_menuBar, editorBody.GetIndex());

        AddMenu("File", [
            ("Save", () => Source("SaveCharacterButton")),
            ("Use Character", () => Source("UseCharacterButton")),
            (null, null),
            ("Close", () => Source("CloseCharacterEditorButton")),
        ]);
        AddMenu("Edit", [
            ("Undo", () => Source("PaintUndoButton")),
            ("Redo", () => Source("PaintRedoButton")),
            (null, null),
            ("Erase All…", () => Source("PaintEraseAllButton")),
        ]);
        AddMenu("View", [
            ("Zoom Out", () => Source("PaintZoomOutButton")),
            ("Zoom In", () => Source("PaintZoomInButton")),
            ("Reset View", () => Source("PaintResetViewButton")),
            (null, null),
            ("Rotate Left", () => RotateSource(0)),
            ("Rotate Right", () => RotateSource(1)),
        ]);
    }

    /// <summary>Items are (label, source-button lookup); a null label adds a separator.</summary>
    private void AddMenu(string title, List<(string? Text, Func<Button?>? Source)> items)
    {
        var button = new MenuButton
        {
            Text = title,
            Flat = false,
            SwitchOnHover = true,
            FocusMode = Control.FocusModeEnum.All,
            CustomMinimumSize = new Vector2(52, Win98ThemeFactory.ControlHeight),
        };
        _menuBar!.AddChild(button);

        PopupMenu popup = button.GetPopup();
        popup.AddThemeStyleboxOverride("panel", Win98ThemeFactory.Raised(Win98ThemeFactory.Face, 2));
        popup.AddThemeStyleboxOverride("hover", Win98ThemeFactory.Flat(Win98ThemeFactory.Selection));
        popup.AddThemeColorOverride("font_color", Win98ThemeFactory.Dark);
        popup.AddThemeColorOverride("font_hover_color", Win98ThemeFactory.Light);
        popup.AddThemeColorOverride("font_disabled_color", Win98ThemeFactory.Shadow);

        for (int index = 0; index < items.Count; index++)
        {
            (string? text, Func<Button?>? _) = items[index];
            if (text is null)
                popup.AddSeparator();
            else
                popup.AddItem(text, index);
        }

        // Disabled state lives on the editor's own buttons, so re-read it each time the menu opens
        // instead of mirroring it every frame.
        popup.AboutToPopup += () =>
        {
            for (int index = 0; index < items.Count; index++)
            {
                int item = popup.GetItemIndex(index);
                if (item >= 0)
                    popup.SetItemDisabled(item, items[index].Source?.Invoke()?.Disabled ?? true);
            }
        };

        popup.IdPressed += id =>
        {
            Button? source = items[(int)id].Source?.Invoke();
            if (source is not null && !source.Disabled)
                source.EmitSignal(Button.SignalName.Pressed);
        };
    }

    private Button? Source(string name) => _host!.FindChild(name, true, false) as Button;

    private Button? RotateSource(int childIndex) =>
        _host!.FindChild("PaintRotateRow", true, false) is HBoxContainer row &&
        childIndex < row.GetChildCount()
            ? row.GetChild(childIndex) as Button
            : null;
}
