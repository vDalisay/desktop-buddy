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
    private readonly Dictionary<Key, MenuButton> _altMenus = new();
    private CharacterEditorHost? _host;
    private PaintCanvasControl? _canvas;
    private HBoxContainer? _menuBar;
    private MenuButton? _activeInvoker;

    public override void _Ready()
    {
        // CharacterEditorModeCoordinator pauses the gameplay tree while editing. This bootstrap
        // must remain alive for deferred composition and keyboard menu handling.
        ProcessMode = ProcessModeEnum.Always;
    }

    public override void _Process(double delta)
    {
        if (!GodotObject.IsInstanceValid(_host))
            _host = GetTree().Root.FindChild(nameof(CharacterEditorHost), true, false) as CharacterEditorHost;
        if (!GodotObject.IsInstanceValid(_canvas))
            _canvas = GetTree().Root.FindChild("CharacterPaintCanvas", true, false) as PaintCanvasControl;

        if (GodotObject.IsInstanceValid(_host) && _host!.IsEditorOpen && !GodotObject.IsInstanceValid(_menuBar))
            TryBuild();
    }

    public override void _UnhandledKeyInput(InputEvent input)
    {
        if (input is not InputEventKey { Pressed: true, Echo: false } key ||
            !GodotObject.IsInstanceValid(_canvas) ||
            !_canvas!.IsVisibleInTree())
        {
            return;
        }

        if (key.Keycode == Key.Escape && CloseOpenMenu())
        {
            GetViewport().SetInputAsHandled();
            return;
        }

        if (key.Keycode == Key.F10 && !key.CtrlPressed && !key.AltPressed && !key.MetaPressed)
        {
            FocusFirstMenu();
            GetViewport().SetInputAsHandled();
            return;
        }

        if (!key.AltPressed || key.CtrlPressed || key.MetaPressed ||
            !_altMenus.TryGetValue(key.Keycode, out MenuButton? menu))
        {
            return;
        }

        OpenMenu(menu);
        GetViewport().SetInputAsHandled();
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

        AddMenu("File", Key.F, [
            ("Save", () => Source("SaveCharacterButton")),
            ("Use Character", () => Source("UseCharacterButton")),
            (null, null),
            ("Close", () => Source("CloseCharacterEditorButton")),
        ]);
        AddMenu("Edit", Key.E, [
            ("Undo", () => Source("PaintUndoButton")),
            ("Redo", () => Source("PaintRedoButton")),
            (null, null),
            ("Erase All…", () => Source("PaintEraseAllButton")),
        ]);
        AddMenu("View", Key.V, [
            ("Zoom Out", () => Source("PaintZoomOutButton")),
            ("Zoom In", () => Source("PaintZoomInButton")),
            ("Reset View", () => Source("PaintResetViewButton")),
            (null, null),
            ("Rotate Left", () => RotateSource(0)),
            ("Rotate Right", () => RotateSource(1)),
        ]);
    }

    /// <summary>Items are (label, source-button lookup); a null label adds a separator.</summary>
    private void AddMenu(string title, Key accelerator, List<(string? Text, Func<Button?>? Source)> items)
    {
        var button = new MenuButton
        {
            Name = $"Paint{title}MenuButton",
            Text = title,
            TooltipText = $"Open the {title} menu (Alt+{accelerator}).",
            Flat = false,
            SwitchOnHover = true,
            FocusMode = Control.FocusModeEnum.All,
            CustomMinimumSize = new Vector2(52, Win98ThemeFactory.ControlHeight),
        };
        _menuBar!.AddChild(button);
        _altMenus[accelerator] = button;

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
            _activeInvoker = button;
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
            RestoreMenuFocus(button);
        };
    }

    private void FocusFirstMenu()
    {
        if (_menuBar?.GetChildCount() > 0 && _menuBar.GetChild(0) is MenuButton first)
            first.GrabFocus();
    }

    private void OpenMenu(MenuButton menu)
    {
        CloseOpenMenu();
        menu.GrabFocus();
        _activeInvoker = menu;
        menu.ShowPopup();
    }

    private bool CloseOpenMenu()
    {
        if (!GodotObject.IsInstanceValid(_activeInvoker))
            return false;

        PopupMenu popup = _activeInvoker!.GetPopup();
        if (!popup.Visible)
            return false;

        popup.Hide();
        RestoreMenuFocus(_activeInvoker);
        return true;
    }

    private void RestoreMenuFocus(MenuButton invoker)
    {
        invoker.CallDeferred(Control.MethodName.GrabFocus);
        _activeInvoker = null;
    }

    private Button? Source(string name) => _host!.FindChild(name, true, false) as Button;

    private Button? RotateSource(int childIndex) =>
        _host!.FindChild("PaintRotateRow", true, false) is HBoxContainer row &&
        childIndex < row.GetChildCount()
            ? row.GetChild(childIndex) as Button
            : null;
}
