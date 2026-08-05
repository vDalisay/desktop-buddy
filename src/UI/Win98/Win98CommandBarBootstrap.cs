using System;
using System.Collections.Generic;
using DesktopBuddy.Domain.Platform;
using DesktopBuddy.Platform;
using DesktopBuddy.Shop;
using Godot;

namespace DesktopBuddy.UI.Win98;

/// <summary>
/// W2 migration layer for the compact horizontal menu. It replaces the temporary button row
/// and native compact-mode panel windows with one persistent Win98 command bar and one in-scene
/// flyout. Fullscreen keeps the existing recovery toolbar/native windows until its dedicated
/// menu pass lands.
/// </summary>
public partial class Win98CommandBarBootstrap : Node
{
    private Control _uiRoot = null!;
    private Control _legacyDock = null!;
    private DesktopWindowController _window = null!;

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
    private Button _collapseButton = null!;
    private Button _legacyEditorButton = null!;
    private Button _legacyModeButton = null!;

    private readonly Dictionary<Button, Control> _sections = [];
    private Control? _activeSection;
    private bool _collapsed;
    private bool _composed;

    public override void _Process(double delta)
    {
        if (!_composed)
        {
            TryCompose();
            return;
        }

        bool compact = _window.LayoutMode == WindowLayoutMode.Compact;
        bool editorOpen = IsEditorOpen();
        _bar.Visible = compact && !editorOpen;
        _flyout.Visible = compact && !editorOpen && !_collapsed && _activeSection is not null;

        // The old row is still controlled by CharacterEditorHost. Keep it out of the compact
        // layout without modifying that host's initialization and diagnostics seams.
        if (GodotObject.IsInstanceValid(_legacyDock))
            _legacyDock.Visible = false;

        if (!compact)
            ReturnPanelsToNativeWindows();
        else
            EnsureCompactPanelOwnership();

        LayoutForViewport();
        MirrorModeLabel();
    }

    public override void _ExitTree() => ReturnPanelsToNativeWindows();

    private void TryCompose()
    {
        _uiRoot = FindControl("CharacterEditorUiRoot");
        _legacyDock = FindControl("FloatingDock");
        _window = GetTree().Root.FindChild(
            nameof(DesktopWindowController), recursive: true, owned: false) as DesktopWindowController
            ?? null!;
        _shop = GetTree().Root.FindChild("ShopPanel", recursive: true, owned: false) as ShopPanel ?? null!;
        _tools = GetTree().Root.FindChild("ToolSelectionPanel", recursive: true, owned: false) as ToolSelectionPanel ?? null!;
        _settings = GetTree().Root.FindChild("SettingsPanel", recursive: true, owned: false) as SettingsPanel ?? null!;
        _legacyEditorButton = FindButton("DockCharacterEditorButton");
        _legacyModeButton = FindButton("DockInteractionModeButton");

        if (!GodotObject.IsInstanceValid(_uiRoot) ||
            !GodotObject.IsInstanceValid(_legacyDock) ||
            !GodotObject.IsInstanceValid(_window) ||
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
            ZIndex = 80,
        };
        _bar.Theme = Win98ThemeFactory.Create();
        _bar.AddThemeStyleboxOverride("panel", Win98ThemeFactory.Raised(Win98ThemeFactory.Face, 2));
        _uiRoot.AddChild(_bar);

        var row = new HBoxContainer
        {
            Name = "CommandRow",
            Alignment = BoxContainer.AlignmentMode.Center,
        };
        row.AddThemeConstantOverride("separation", 2);
        _bar.AddChild(row);

        _shopButton = AddCommand(row, "Shop", "Open the shop inside the game window.", () => OpenSection(_shopButton, _shop, "Shop"));
        _toolsButton = AddCommand(row, "Tools", "Choose the active tool.", () => OpenSection(_toolsButton, _tools, "Tools"));
        _settingsButton = AddCommand(row, "Settings", "Open game and window settings.", () => OpenSection(_settingsButton, _settings, "Settings"));
        _editorButton = AddCommand(row, "Paint / Character", "Open the character editor.", OpenEditor);
        _modeButton = AddCommand(row, "Work", "Switch between Play and Work input modes.", ToggleMode);
        _collapseButton = AddCommand(row, "◀", "Collapse or expand the command bar.", ToggleCollapsed);

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
        _uiRoot.AddChild(_flyout);

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
        var close = AddCommand(titleRow, "×", "Close this menu.", CloseFlyout);
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

    private static Button AddCommand(Control parent, string text, string tooltip, Action action)
    {
        var button = new Button
        {
            Text = text,
            TooltipText = tooltip,
            FocusMode = Control.FocusModeEnum.All,
            MouseFilter = Control.MouseFilterEnum.Stop,
            CustomMinimumSize = new Vector2(68, 26),
        };
        button.Pressed += action;
        parent.AddChild(button);
        return button;
    }

    private void OpenSection(Button button, Control section, string title)
    {
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
            section.Reparent(_flyoutBody, keepGlobalTransform: false);
        section.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        section.Visible = true;
        _activeSection = section;
        _flyoutTitle.Text = title;
        _flyout.Visible = true;
        UpdatePressedStates(button);
    }

    private void CloseFlyout()
    {
        _flyout.Visible = false;
        _activeSection = null;
        UpdatePressedStates(null);
    }

    private void ToggleCollapsed()
    {
        _collapsed = !_collapsed;
        _shopButton.Visible = !_collapsed;
        _toolsButton.Visible = !_collapsed;
        _settingsButton.Visible = !_collapsed;
        _editorButton.Visible = !_collapsed;
        _modeButton.Visible = !_collapsed;
        _collapseButton.Text = _collapsed ? "▶" : "◀";
        if (_collapsed)
            CloseFlyout();
        LayoutForViewport();
    }

    private void OpenEditor()
    {
        CloseFlyout();
        _legacyEditorButton.EmitSignal(Button.SignalName.Pressed);
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
            panel.Reparent(_uiRoot, keepGlobalTransform: false);
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
        panel.Reparent(home, keepGlobalTransform: false);
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

    private void LayoutForViewport()
    {
        Vector2 viewport = GetViewport().GetVisibleRect().Size;
        float barWidth = Mathf.Min(_collapsed ? 82f : 520f, Mathf.Max(120f, viewport.X - 16f));
        float barHeight = 34f;
        _bar.Position = new Vector2((viewport.X - barWidth) * 0.5f, viewport.Y - barHeight - 28f);
        _bar.Size = new Vector2(barWidth, barHeight);

        float flyoutWidth = Mathf.Min(460f, Mathf.Max(300f, viewport.X - 24f));
        float availableHeight = Mathf.Max(180f, viewport.Y - barHeight - 78f);
        float flyoutHeight = Mathf.Min(420f, availableHeight);
        _flyout.Position = new Vector2((viewport.X - flyoutWidth) * 0.5f, _bar.Position.Y - flyoutHeight - 2f);
        _flyout.Size = new Vector2(flyoutWidth, flyoutHeight);
    }

    private bool IsEditorOpen()
    {
        Control panel = FindControl("CharacterEditorPanel");
        return GodotObject.IsInstanceValid(panel) && panel.Visible;
    }

    private Control FindControl(string name) =>
        GetTree().Root.FindChild(name, recursive: true, owned: false) as Control ?? null!;

    private Button FindButton(string name) =>
        GetTree().Root.FindChild(name, recursive: true, owned: false) as Button ?? null!;
}
