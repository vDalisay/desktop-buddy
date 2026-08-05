using System;
using System.Collections.Generic;
using DesktopBuddy.CharacterEditor;
using DesktopBuddy.Domain.Platform;
using DesktopBuddy.Platform;
using DesktopBuddy.Shop;
using Godot;

namespace DesktopBuddy.UI.Win98;

/// <summary>
/// Hosts the Shop, Tools, Settings and editor commands in the classic menu strip directly
/// beneath the Win98 title bar. Compact mode uses one in-scene flyout instead of native windows.
/// </summary>
public partial class Win98CommandBarBootstrap : Node
{
    private Control _uiRoot = null!;
    private Control _legacyDock = null!;
    private DesktopWindowController _window = null!;
    private Win98WindowFrame _frame = null!;
    private CharacterEditorHost _editorHost = null!;

    private ShopPanel _shop = null!;
    private ToolSelectionPanel _tools = null!;
    private SettingsPanel _settings = null!;
    private Node _shopHome = null!;
    private Node _toolsHome = null!;
    private Node _settingsHome = null!;

    private PanelContainer _bar = null!;
    private PanelContainer _flyout = null!;
    private PanelContainer _flyoutBody = null!;
    private Label _flyoutTitle = null!;
    private Button _shopButton = null!;
    private Button _toolsButton = null!;
    private Button _settingsButton = null!;
    private Button _editorButton = null!;
    private Button _modeButton = null!;
    private Button _legacyEditorButton = null!;
    private Button _legacyModeButton = null!;

    private readonly Dictionary<Button, Control> _sections = [];
    private Control? _activeSection;
    private bool _composed;

    // Opening the editor pauses the tree (GameplayPauseReason.CharacterEditor). A pausable
    // autoload would stop here, leaving the frame at MouseFilter.Pass and the menu bar
    // visible over the editor — and a paused full-rect Control still wins mouse picking,
    // so every editor button becomes unclickable.
    public override void _Ready() => ProcessMode = ProcessModeEnum.Always;

    public override void _Process(double delta)
    {
        if (!_composed)
        {
            TryCompose();
            return;
        }

        bool compact = _window.LayoutMode == WindowLayoutMode.Compact;
        bool editorOpen = IsEditorOpen();
        _bar.Visible = !editorOpen;
        _flyout.Visible = compact && !editorOpen && _activeSection is not null;

        if (GodotObject.IsInstanceValid(_legacyDock))
            _legacyDock.Visible = false;

        if (!compact)
            ReturnPanelsToNativeWindows();
        else
            EnsureCompactPanelOwnership();

        LayoutMenuBar();
        LayoutFlyout();
        MirrorModeLabel();
    }

    public override void _ExitTree() => ReturnPanelsToNativeWindows();

    private void TryCompose()
    {
        _uiRoot = FindControl("CharacterEditorUiRoot");
        _legacyDock = FindControl("FloatingDock");
        _frame = GetTree().Root.FindChild(nameof(Win98WindowFrame), true, false) as Win98WindowFrame ?? null!;
        _window = GetTree().Root.FindChild(nameof(DesktopWindowController), true, false) as DesktopWindowController ?? null!;
        _editorHost = GetTree().Root.FindChild(nameof(CharacterEditorHost), true, false) as CharacterEditorHost ?? null!;
        _shop = GetTree().Root.FindChild("ShopPanel", true, false) as ShopPanel ?? null!;
        _tools = GetTree().Root.FindChild("ToolSelectionPanel", true, false) as ToolSelectionPanel ?? null!;
        _settings = GetTree().Root.FindChild("SettingsPanel", true, false) as SettingsPanel ?? null!;
        _legacyEditorButton = FindButton("DockCharacterEditorButton");
        _legacyModeButton = FindButton("DockInteractionModeButton");

        if (!GodotObject.IsInstanceValid(_uiRoot) ||
            !GodotObject.IsInstanceValid(_legacyDock) ||
            !GodotObject.IsInstanceValid(_frame) ||
            !GodotObject.IsInstanceValid(_window) ||
            !GodotObject.IsInstanceValid(_editorHost) ||
            !GodotObject.IsInstanceValid(_shop) ||
            !GodotObject.IsInstanceValid(_tools) ||
            !GodotObject.IsInstanceValid(_settings) ||
            !GodotObject.IsInstanceValid(_legacyEditorButton) ||
            !GodotObject.IsInstanceValid(_legacyModeButton))
        {
            return;
        }

        _shopHome = _shop.GetParent();
        _toolsHome = _tools.GetParent();
        _settingsHome = _settings.GetParent();
        HideNativePanelWindows();
        BuildUi();
        EnsureCompactPanelOwnership();
        _legacyDock.Visible = false;
        _composed = true;
    }

    private void BuildUi()
    {
        _bar = new PanelContainer
        {
            Name = "Win98CommandBar",
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        _bar.Theme = Win98ThemeFactory.Create();
        _bar.AddThemeStyleboxOverride("panel", Win98ThemeFactory.Flat(Win98ThemeFactory.Face));
        Node overlay = _frame.GetParent();
        overlay.AddChild(_bar);

        var row = new HBoxContainer
        {
            Name = "CommandRow",
            Alignment = BoxContainer.AlignmentMode.Begin,
            MouseFilter = Control.MouseFilterEnum.Pass,
        };
        row.AddThemeConstantOverride("separation", 0);
        _bar.AddChild(row);

        _shopButton = AddMenuCommand(row, "Shop", "Open the shop.", () => OpenSection(_shopButton, _shop, "Shop"));
        _toolsButton = AddMenuCommand(row, "Tools", "Choose the active tool.", () => OpenSection(_toolsButton, _tools, "Tools"));
        _settingsButton = AddMenuCommand(row, "Settings", "Open game and window settings.", () => OpenSection(_settingsButton, _settings, "Settings"));
        _editorButton = AddMenuCommand(row, "Paint / Character", "Open the paint workspace.", OpenEditor);
        _modeButton = AddMenuCommand(row, "Work", "Switch between Play and Work input modes.", ToggleMode);

        _sections[_shopButton] = _shop;
        _sections[_toolsButton] = _tools;
        _sections[_settingsButton] = _settings;

        _flyout = new PanelContainer
        {
            Name = "Win98CommandFlyout",
            Visible = false,
            MouseFilter = Control.MouseFilterEnum.Stop,
            ZIndex = 79,
        };
        _flyout.Theme = _bar.Theme;
        _flyout.AddThemeStyleboxOverride("panel", Win98ThemeFactory.Raised(Win98ThemeFactory.Face, 2));
        overlay.AddChild(_flyout);

        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 0);
        _flyout.AddChild(column);

        var titleBar = new PanelContainer
        {
            CustomMinimumSize = new Vector2(0, Win98ThemeFactory.TitleBarHeight),
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        titleBar.AddThemeStyleboxOverride("panel", Win98ThemeFactory.Flat(Win98ThemeFactory.ActiveTitle));
        column.AddChild(titleBar);

        var titleRow = new HBoxContainer();
        titleRow.AddThemeConstantOverride("separation", 2);
        titleBar.AddChild(titleRow);
        _flyoutTitle = new Label
        {
            Text = "Menu",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _flyoutTitle.AddThemeColorOverride("font_color", Colors.White);
        titleRow.AddChild(_flyoutTitle);
        var close = AddMenuCommand(titleRow, "×", "Close this menu.", CloseFlyout);
        close.CustomMinimumSize = new Vector2(22, 18);

        _flyoutBody = new PanelContainer
        {
            Name = "FlyoutBody",
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        _flyoutBody.AddThemeStyleboxOverride("panel", Win98ThemeFactory.Recessed(Win98ThemeFactory.Face, 1));
        column.AddChild(_flyoutBody);
    }

    private static Button AddMenuCommand(Control parent, string text, string tooltip, Action action)
    {
        var button = new Button
        {
            Text = text,
            TooltipText = tooltip,
            FocusMode = Control.FocusModeEnum.All,
            MouseFilter = Control.MouseFilterEnum.Stop,
            CustomMinimumSize = new Vector2(Mathf.Max(42f, text.Length * 8f + 16f), 22f),
        };
        button.AddThemeStyleboxOverride("normal", Win98ThemeFactory.Flat(Colors.Transparent));
        button.AddThemeStyleboxOverride("hover", Win98ThemeFactory.Raised(Win98ThemeFactory.Face, 1));
        button.AddThemeStyleboxOverride("pressed", Win98ThemeFactory.Recessed(Win98ThemeFactory.Face, 1));
        button.Pressed += action;
        parent.AddChild(button);
        return button;
    }

    private void OpenSection(Button button, Control section, string title)
    {
        if (_window.LayoutMode != WindowLayoutMode.Compact)
            return;

        EnsureCompactPanelOwnership();
        if (_activeSection == section && _flyout.Visible)
        {
            CloseFlyout();
            return;
        }

        if (section == _shop)
            _shop.Refresh();
        else if (section == _tools)
            _tools.Refresh();

        if (section.GetParent() != _flyoutBody)
            section.Reparent(_flyoutBody, false);
        section.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        section.Visible = true;
        _activeSection = section;
        _flyoutTitle.Text = title;
        _flyout.Visible = true;
        UpdatePressedStates(button);
        LayoutFlyout();
    }

    private void CloseFlyout()
    {
        _flyout.Visible = false;
        _activeSection = null;
        UpdatePressedStates(null);
    }

    private async void OpenEditor()
    {
        CloseFlyout();
        await _editorHost.OpenWin98PaintEditorAsync();
    }

    private void ToggleMode() => _legacyModeButton.EmitSignal(Button.SignalName.Pressed);

    private void MirrorModeLabel()
    {
        if (GodotObject.IsInstanceValid(_legacyModeButton))
            _modeButton.Text = _legacyModeButton.Text;
    }

    private void UpdatePressedStates(Button? active)
    {
        foreach (Button button in _sections.Keys)
            button.ButtonPressed = button == active;
    }

    private void EnsureCompactPanelOwnership()
    {
        if (_window.LayoutMode != WindowLayoutMode.Compact)
            return;

        HideNativePanelWindows();
        ParkPanel(_shop);
        ParkPanel(_tools);
        ParkPanel(_settings);
    }

    private void ParkPanel(Control panel)
    {
        if (_activeSection == panel && panel.GetParent() == _flyoutBody)
            return;
        if (panel.GetParent() != _uiRoot)
            panel.Reparent(_uiRoot, false);
        panel.Visible = false;
    }

    private void ReturnPanelsToNativeWindows()
    {
        if (!_composed)
            return;

        CloseFlyout();
        ReturnPanel(_shop, _shopHome);
        ReturnPanel(_tools, _toolsHome);
        ReturnPanel(_settings, _settingsHome);
    }

    private static void ReturnPanel(Control panel, Node home)
    {
        if (!GodotObject.IsInstanceValid(panel) || !GodotObject.IsInstanceValid(home) || panel.GetParent() == home)
            return;
        panel.Reparent(home, false);
        panel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        panel.Visible = true;
    }

    private void HideNativePanelWindows()
    {
        HideWindow(_shopHome);
        HideWindow(_toolsHome);
        HideWindow(_settingsHome);
    }

    private static void HideWindow(Node node)
    {
        if (node is Window window)
            window.Hide();
    }

    private void LayoutMenuBar()
    {
        if (!GodotObject.IsInstanceValid(_bar))
            return;

        Rect2 content = _frame.ContentViewportRect;
        if (content.Size.X <= 0f)
            return;

        float height = Mathf.Max(24f, _bar.GetCombinedMinimumSize().Y);
        _bar.SetAnchorsPreset(Control.LayoutPreset.TopLeft);
        _bar.GlobalPosition = content.Position;
        _bar.Size = new Vector2(content.Size.X, height);
    }

    private void LayoutFlyout()
    {
        if (!GodotObject.IsInstanceValid(_flyout) || !GodotObject.IsInstanceValid(_bar))
            return;

        Rect2 content = _frame.ContentViewportRect;
        Rect2 menuRect = _bar.GetGlobalRect();
        if (content.Size.X <= 0f)
            return;

        float width = Mathf.Min(460f, content.Size.X);
        float height = Mathf.Max(180f, content.End.Y - menuRect.End.Y);
        _flyout.SetAnchorsPreset(Control.LayoutPreset.TopLeft);
        _flyout.GlobalPosition = new Vector2(menuRect.Position.X, menuRect.End.Y);
        _flyout.Size = new Vector2(width, height);
    }

    private bool IsEditorOpen()
    {
        Control panel = FindControl("CharacterEditorPanel");
        return GodotObject.IsInstanceValid(panel) && panel.Visible;
    }

    private Control FindControl(string name) =>
        GetTree().Root.FindChild(name, true, false) as Control ?? null!;

    private Button FindButton(string name) =>
        GetTree().Root.FindChild(name, true, false) as Button ?? null!;
}
