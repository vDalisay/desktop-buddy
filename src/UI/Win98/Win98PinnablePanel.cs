using System;
using DesktopBuddy.Ui;
using Godot;

namespace DesktopBuddy.UI.Win98;

/// <summary>Moves one existing Win98 panel between its authored dock and a native desktop window.</summary>
public partial class Win98PinnablePanel : Node
{
    private PanelContainer _panel = null!;
    private Node _home = null!;
    private Window _window = null!;
    private Button _pin = null!;
    private int _homeIndex;
    private Vector2I _floatingSize;
    private bool _titlePressed;
    private Vector2 _titlePress;
    private float _anchorLeft;
    private float _anchorTop;
    private float _anchorRight;
    private float _anchorBottom;
    private float _offsetLeft;
    private float _offsetTop;
    private float _offsetRight;
    private float _offsetBottom;
    private bool _configured;

    public bool IsFloating { get; private set; }

    public void Configure(PanelContainer panel, Vector2I floatingSize, string windowName)
    {
        if (_configured) return;
        _panel = panel ?? throw new ArgumentNullException(nameof(panel));
        _home = panel.GetParent() ?? throw new InvalidOperationException("A pinnable panel must be docked before configuration.");
        _homeIndex = panel.GetIndex();
        _floatingSize = floatingSize;
        ProcessMode = ProcessModeEnum.Always;

        _window = new Window
        {
            Name = windowName,
            Title = string.Empty,
            Size = floatingSize,
            MinSize = new Vector2I(Math.Min(240, floatingSize.X), Math.Min(120, floatingSize.Y)),
            Borderless = true,
            Unresizable = false,
            Visible = false,
        };
        DockWindow.ApplyOwnedWindowFlags(_window);
        AddChild(_window);
        _window.CloseRequested += Dock;
        _window.WindowInput += OnFloatingWindowInput;

        PanelContainer titleBar = panel.FindChild("TitleBar", true, false) as PanelContainer
            ?? throw new InvalidOperationException("A pinnable panel requires a Win98 title bar.");
        HBoxContainer titleRow = titleBar.GetChildCount() > 0 && titleBar.GetChild(0) is HBoxContainer row
            ? row
            : throw new InvalidOperationException("A pinnable panel requires a Win98 title row.");
        _pin = new Button
        {
            Name = "PinBox",
            Text = OperatingSystem.IsBrowser() ? "P" : "📌",
            TooltipText = "Detach this panel into a desktop window. Press again to return it.",
            FocusMode = Control.FocusModeEnum.All,
        };
        _pin.Pressed += Toggle;
        Button? close = titleRow.FindChild("CloseBox", false, false) as Button;
        if (!GodotObject.IsInstanceValid(close))
        {
            foreach (Node child in titleRow.GetChildren())
                if (child is Button { TooltipText: "Close this window." } candidate)
                {
                    close = candidate;
                    break;
                }
        }
        int closeIndex = close?.GetIndex() ?? titleRow.GetChildCount();
        titleRow.AddChild(_pin);
        titleRow.MoveChild(_pin, closeIndex);
        if (GodotObject.IsInstanceValid(close))
            titleRow.MoveChild(close!, titleRow.GetChildCount() - 1);
        StyleTitleButton(_pin);
        titleBar.MouseDefaultCursorShape = Control.CursorShape.Move;
        titleBar.GuiInput += OnTitleInput;
        _configured = true;
    }

    public override void _Process(double delta)
    {
        if (!_configured || !IsFloating) return;
        if (_panel.Visible != _window.Visible)
        {
            // Mirrored visibility only, never focus. The panel this follows is hidden and shown
            // by its own owner - Paint Room hides its tools for the duration of every stroke -
            // and grabbing focus each time it came back pulled the player out of the window they
            // were painting in, on every single click (owner report 2026-08-23).
            if (_panel.Visible) DockWindow.ShowOwned(_window, takeFocus: false);
            else _window.Hide();
        }

        // The panel's own minimum is not final when it is detached - the layout has not run in
        // its new parent yet - and it grows again whenever the interface scale or palette
        // changes. A window narrower than its panel clips the right edge off, taking half the
        // close button with it (owner report 2026-08-24).
        Vector2 minimum = _panel.GetCombinedMinimumSize();
        var wanted = new Vector2I(Mathf.CeilToInt(minimum.X), Mathf.CeilToInt(minimum.Y));
        if (_window.MinSize != wanted)
            _window.MinSize = wanted;
        if (_window.Size.X < wanted.X || _window.Size.Y < wanted.Y)
            _window.Size = new Vector2I(
                Math.Max(_window.Size.X, wanted.X),
                Math.Max(_window.Size.Y, wanted.Y));
    }

    public void Toggle()
    {
        if (IsFloating) Dock();
        else Float();
    }

    public void Float(bool startDrag = false)
    {
        if (!_configured || IsFloating) return;
        Rect2 screenRect = _panel.GetGlobalRect();
        Vector2I mainPosition = GetWindow().Position;
        Vector2 minimum = _panel.GetCombinedMinimumSize();
        Vector2I wantedSize = new(
            Math.Max(_floatingSize.X, Mathf.CeilToInt(minimum.X)),
            Math.Max(_floatingSize.Y, Mathf.CeilToInt(minimum.Y)));
        Rect2I usable = DisplayServer.ScreenGetUsableRect(DisplayServer.WindowGetCurrentScreen());
        if (usable.Size.X <= 0 || usable.Size.Y <= 0)
            usable = new Rect2I(GetWindow().Position, GetWindow().Size);
        wantedSize = new Vector2I(
            Math.Min(wantedSize.X, Math.Max(_window.MinSize.X, usable.Size.X)),
            Math.Min(wantedSize.Y, Math.Max(_window.MinSize.Y, usable.Size.Y)));
        _anchorLeft = _panel.AnchorLeft;
        _anchorTop = _panel.AnchorTop;
        _anchorRight = _panel.AnchorRight;
        _anchorBottom = _panel.AnchorBottom;
        _offsetLeft = _panel.OffsetLeft;
        _offsetTop = _panel.OffsetTop;
        _offsetRight = _panel.OffsetRight;
        _offsetBottom = _panel.OffsetBottom;
        // Size the window first, then anchor the panel to it with zero offsets. Setting the
        // panel's own size afterwards would bake that size into the offsets, and the panel then
        // keeps overhanging a smaller window for as long as it floats - which is how the close
        // button ended up sliced off the right edge on the first detach (owner report
        // 2026-08-24). Anchored flush, the panel is exactly the window, always.
        _window.Size = wantedSize;
        _panel.Reparent(_window, false);
        _panel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        Vector2I wantedPosition = mainPosition + new Vector2I(
            Mathf.RoundToInt(screenRect.Position.X),
            Mathf.RoundToInt(screenRect.Position.Y));
        _window.Position = new Vector2I(
            Math.Clamp(wantedPosition.X, usable.Position.X, Math.Max(usable.Position.X, usable.End.X - wantedSize.X)),
            Math.Clamp(wantedPosition.Y, usable.Position.Y, Math.Max(usable.Position.Y, usable.End.Y - wantedSize.Y)));
        IsFloating = true;
        _pin.TooltipText = "Return this panel to its default position.";
        DockWindow.ShowOwned(_window);
        if (startDrag)
            Callable.From(_window.StartDrag).CallDeferred();
    }

    public void Dock()
    {
        if (!_configured || !IsFloating) return;
        _window.Hide();
        _panel.Reparent(_home, false);
        _home.MoveChild(_panel, Math.Clamp(_homeIndex, 0, _home.GetChildCount() - 1));
        _panel.AnchorLeft = _anchorLeft;
        _panel.AnchorTop = _anchorTop;
        _panel.AnchorRight = _anchorRight;
        _panel.AnchorBottom = _anchorBottom;
        _panel.OffsetLeft = _offsetLeft;
        _panel.OffsetTop = _offsetTop;
        _panel.OffsetRight = _offsetRight;
        _panel.OffsetBottom = _offsetBottom;
        IsFloating = false;
        _pin.TooltipText = "Detach this panel into a desktop window. Press again to return it.";
    }

    /// <summary>
    /// Hands focus back to the game after every click in the detached window. A floating tool
    /// panel is somewhere to reach for a tool, never somewhere to work: leaving it focused meant
    /// the next keystroke or stroke went to the wrong window (owner instruction 2026-08-24).
    /// </summary>
    private void OnFloatingWindowInput(InputEvent input)
    {
        if (!IsFloating || input is not InputEventMouseButton { Pressed: false })
            return;

        // Deferred: the button under the cursor still has to handle this release.
        Callable.From(ReturnFocusToGame).CallDeferred();
    }

    private void ReturnFocusToGame()
    {
        if (!_configured || !IsFloating)
            return;
        Window game = GetWindow();
        if (GodotObject.IsInstanceValid(game) && game.Visible)
            game.GrabFocus();
    }

    private void OnTitleInput(InputEvent input)
    {
        switch (input)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.Left } click:
                _titlePressed = click.Pressed;
                _titlePress = click.GlobalPosition;
                if (click.Pressed && IsFloating)
                    _window.StartDrag();
                break;
            case InputEventMouseMotion motion when _titlePressed && !IsFloating &&
                motion.GlobalPosition.DistanceTo(_titlePress) >= 4.0f:
                _titlePressed = false;
                Float(startDrag: true);
                break;
        }
    }

    private static void StyleTitleButton(Button button) => Win98ThemeFactory.StyleTitleButton(button);
}
