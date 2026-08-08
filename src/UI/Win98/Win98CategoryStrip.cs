using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace DesktopBuddy.UI.Win98;

/// <summary>Presentation-only category descriptor used by shared customization workspaces.</summary>
public readonly record struct Win98CategoryPresentation(
    string Id,
    string Label,
    Texture2D? Icon = null,
    bool Enabled = true,
    string Tooltip = "");

/// <summary>
/// Reusable Win98 category strip: original icon/text tabs, deterministic keyboard traversal,
/// mouse-wheel horizontal scrolling, and no wrapping. It deliberately knows nothing about
/// cosmetics, decorations, ownership, purchases, or persistence.
/// </summary>
public partial class Win98CategoryStrip : HBoxContainer
{
    private readonly Dictionary<string, Button> _buttons = new(StringComparer.Ordinal);
    private IReadOnlyList<Win98CategoryPresentation> _items = Array.Empty<Win98CategoryPresentation>();
    private Button _previous = null!;
    private Button _next = null!;
    private ScrollContainer _scroll = null!;
    private HBoxContainer _row = null!;
    private string? _selectedId;
    private bool _built;

    public event Action<string>? SelectionChanged;

    public string? SelectedId => _selectedId;

    public override void _Ready()
    {
        Theme = Win98ThemeFactory.Create();
        AddThemeConstantOverride("separation", Win98ThemeFactory.Gap);
        BuildChrome();
        Rebuild();
    }

    public void SetItems(IEnumerable<Win98CategoryPresentation> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        Win98CategoryPresentation[] frozen = items.ToArray();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (Win98CategoryPresentation item in frozen)
        {
            if (string.IsNullOrWhiteSpace(item.Id) || string.IsNullOrWhiteSpace(item.Label))
                throw new ArgumentException("Category IDs and labels are required.", nameof(items));
            if (!ids.Add(item.Id))
                throw new ArgumentException($"Duplicate category ID '{item.Id}'.", nameof(items));
        }

        _items = frozen;
        if (_built)
            Rebuild();
    }

    public bool Select(string id, bool notify = true)
    {
        if (!_buttons.TryGetValue(id, out Button? button) || button.Disabled)
            return false;

        _selectedId = id;
        foreach ((string key, Button candidate) in _buttons)
            candidate.ButtonPressed = string.Equals(key, id, StringComparison.Ordinal);
        button.GrabFocus();
        _scroll.CallDeferred(ScrollContainer.MethodName.EnsureControlVisible, button);
        if (notify)
            SelectionChanged?.Invoke(id);
        return true;
    }

    private void BuildChrome()
    {
        if (_built)
            return;

        _previous = Arrow("CategoryPrevious", "◀", -1);
        AddChild(_previous);

        _scroll = new ScrollContainer
        {
            Name = "CategoryScroll",
            HorizontalScrollMode = ScrollContainer.ScrollMode.Auto,
            VerticalScrollMode = ScrollContainer.ScrollMode.Disabled,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        _scroll.GuiInput += OnScrollInput;
        AddChild(_scroll);

        _row = new HBoxContainer { Name = "CategoryRow" };
        _row.AddThemeConstantOverride("separation", 2);
        _scroll.AddChild(_row);

        _next = Arrow("CategoryNext", "▶", 1);
        AddChild(_next);
        _built = true;
    }

    private Button Arrow(string name, string text, int direction)
    {
        var button = new Button
        {
            Name = name,
            Text = text,
            FocusMode = FocusModeEnum.All,
            CustomMinimumSize = new Vector2(28, Win98ThemeFactory.ControlHeight),
            TooltipText = direction < 0 ? "Scroll categories left." : "Scroll categories right.",
        };
        button.Pressed += () => ScrollBy(direction * 120);
        return button;
    }

    private void Rebuild()
    {
        if (!_built)
            return;

        foreach (Node child in _row.GetChildren())
            child.QueueFree();
        _buttons.Clear();

        for (int index = 0; index < _items.Count; index++)
        {
            Win98CategoryPresentation item = _items[index];
            int captured = index;
            var button = new Button
            {
                Name = $"Category_{Sanitize(item.Id)}",
                Text = item.Label,
                Icon = item.Icon,
                TooltipText = item.Tooltip,
                ToggleMode = true,
                FocusMode = FocusModeEnum.All,
                Disabled = !item.Enabled,
                CustomMinimumSize = new Vector2(Mathf.Max(72f, item.Label.Length * 8f + 24f), 42f),
            };
            button.Pressed += () => Select(item.Id);
            button.GuiInput += input => OnButtonInput(captured, input);
            _row.AddChild(button);
            _buttons[item.Id] = button;
        }

        if (_selectedId is null || !_buttons.TryGetValue(_selectedId, out Button? selected) || selected.Disabled)
        {
            _selectedId = null;
            foreach (Win98CategoryPresentation item in _items)
            {
                if (!item.Enabled)
                    continue;
                _selectedId = item.Id;
                break;
            }
        }

        if (_selectedId is not null && _buttons.ContainsKey(_selectedId))
            Select(_selectedId, notify: false);

        bool overflowPossible = _items.Count > 1;
        _previous.Visible = overflowPossible;
        _next.Visible = overflowPossible;
    }

    private void OnButtonInput(int index, InputEvent input)
    {
        if (input is not InputEventKey { Pressed: true, Echo: false } key)
            return;

        int direction = key.Keycode switch
        {
            Key.Left => -1,
            Key.Right => 1,
            _ => 0,
        };
        if (direction == 0)
            return;

        int candidate = index + direction;
        while (candidate >= 0 && candidate < _items.Count)
        {
            if (_items[candidate].Enabled)
            {
                Select(_items[candidate].Id);
                GetViewport().SetInputAsHandled();
                return;
            }
            candidate += direction;
        }
    }

    private void OnScrollInput(InputEvent input)
    {
        if (input is not InputEventMouseButton { Pressed: true } mouse)
            return;

        int direction = mouse.ButtonIndex switch
        {
            MouseButton.WheelUp => -1,
            MouseButton.WheelDown => 1,
            _ => 0,
        };
        if (direction == 0)
            return;

        ScrollBy(direction * 90);
        AcceptEvent();
    }

    private void ScrollBy(int delta)
    {
        if (!GodotObject.IsInstanceValid(_scroll))
            return;
        _scroll.ScrollHorizontal = Math.Max(0, _scroll.ScrollHorizontal + delta);
    }

    private static string Sanitize(string id)
    {
        char[] chars = id.Select(character => char.IsLetterOrDigit(character) ? character : '_').ToArray();
        return new string(chars);
    }
}
