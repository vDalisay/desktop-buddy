using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace DesktopBuddy.UI.Win98;

/// <summary>
/// Domain-neutral visual data for one catalogue tile. Callers decide what SecondaryText means
/// (price, Free, Owned, refund, etc.); this shared component deliberately carries no purchase
/// or ownership semantics.
/// </summary>
public readonly record struct Win98CatalogItemPresentation(
    string Id,
    string DisplayName,
    string SecondaryText,
    Texture2D? Preview = null,
    bool Selectable = true,
    string Tooltip = "",
    string BadgeText = "",
    bool Accented = false,
    Color? SecondaryColor = null);

/// <summary>
/// Shared responsive visual catalogue for Buddy Studio and Environment customization. It owns
/// tile layout, selected state and keyboard navigation only. Domain actions such as Buy, Place,
/// Sell, equip and affordability remain in the feature-specific controller.
/// </summary>
public partial class Win98CatalogGrid : ScrollContainer
{
    private readonly ButtonGroup _selectionGroup = new() { AllowUnpress = false };
    public const float DefaultTileWidth = 132f;
    public const float DefaultTileHeight = 154f;
    private const float Gap = 6f;

    private readonly Dictionary<string, TileParts> _tiles = new(StringComparer.Ordinal);
    private IReadOnlyList<Win98CatalogItemPresentation> _items = Array.Empty<Win98CatalogItemPresentation>();
    private GridContainer _grid = null!;
    private string? _selectedId;
    private bool _built;
    private float _tileWidth = DefaultTileWidth;
    private float _tileHeight = DefaultTileHeight;

    public event Action<string>? SelectionChanged;
    public event Action<string>? ItemActivated;

    public string? SelectedId => _selectedId;

    /// <summary>Persistent caller-authored accent, independent of the current preview selection.</summary>
    public bool IsAccented(string id) =>
        _tiles.TryGetValue(id, out TileParts? parts) &&
        new[] { "normal", "hover", "pressed", "hover_pressed", "focus" }.All(
            state => parts.Button.HasThemeStyleboxOverride(state)) &&
        parts.Button.GetThemeStylebox("normal") is StyleBoxFlat border &&
        border.BorderColor == Win98ThemeFactory.ActiveTitle;

    /// <summary>Inset outline owned by the grid's current selected/preview item.</summary>
    public bool IsPreviewOutlined(string id) =>
        _tiles.TryGetValue(id, out TileParts? parts) && parts.SelectionOutline.Visible;

    public override void _Ready()
    {
        Theme = Win98ThemeFactory.Create();
        HorizontalScrollMode = ScrollMode.Disabled;
        VerticalScrollMode = ScrollMode.Auto;
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        SizeFlagsVertical = SizeFlags.ExpandFill;
        BuildGrid();
        Rebuild();
    }

    public override void _Notification(int what)
    {
        if (what == NotificationResized && _built)
            UpdateColumns();
    }

    public void ConfigureTileSize(float width, float height)
    {
        if (width < 80f || height < 80f)
            throw new ArgumentOutOfRangeException(nameof(width), "Catalogue tiles must remain usable.");
        _tileWidth = width;
        _tileHeight = height;
        if (_built)
            Rebuild();
    }

    public void SetItems(IEnumerable<Win98CatalogItemPresentation> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        Win98CatalogItemPresentation[] frozen = items.ToArray();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (Win98CatalogItemPresentation item in frozen)
        {
            if (string.IsNullOrWhiteSpace(item.Id) || string.IsNullOrWhiteSpace(item.DisplayName))
                throw new ArgumentException("Catalogue item IDs and display names are required.", nameof(items));
            if (!ids.Add(item.Id))
                throw new ArgumentException($"Duplicate catalogue item ID '{item.Id}'.", nameof(items));
        }

        _items = frozen;
        if (_built)
            Rebuild();
    }

    public bool Select(string id, bool notify = true)
    {
        if (!_tiles.TryGetValue(id, out TileParts? parts) || parts.Button.Disabled)
            return false;

        _selectedId = id;
        foreach ((string key, TileParts tile) in _tiles)
        {
            bool selected = string.Equals(key, id, StringComparison.Ordinal);
            tile.Button.ButtonPressed = selected;
            tile.SelectionOutline.Visible = selected;
        }
        parts.Button.GrabFocus();
        if (notify)
            SelectionChanged?.Invoke(id);
        return true;
    }

    public bool Activate(string id)
    {
        if (!Select(id)) return false;
        ItemActivated?.Invoke(id);
        return true;
    }

    /// <summary>
    /// Refresh one tile without rebuilding the entire grid. Useful when ownership, price text,
    /// affordability or a preview badge changes after a feature-specific transaction.
    /// </summary>
    public bool UpdateItem(Win98CatalogItemPresentation item)
    {
        int index = IndexOf(item.Id);
        if (index < 0 || !_tiles.TryGetValue(item.Id, out TileParts? parts))
            return false;

        var mutable = _items.ToArray();
        mutable[index] = item;
        _items = mutable;
        Apply(parts, item);
        return true;
    }

    private void BuildGrid()
    {
        if (_built)
            return;
        _grid = new GridContainer
        {
            Name = "CatalogTileGrid",
            Columns = 1,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        _grid.AddThemeConstantOverride("h_separation", (int)Gap);
        _grid.AddThemeConstantOverride("v_separation", (int)Gap);
        AddChild(_grid);
        _built = true;
    }

    private void Rebuild()
    {
        if (!_built)
            return;

        foreach (Node child in _grid.GetChildren())
            child.QueueFree();
        _tiles.Clear();

        for (int index = 0; index < _items.Count; index++)
        {
            Win98CatalogItemPresentation item = _items[index];
            int captured = index;
            TileParts parts = BuildTile(item, captured);
            _grid.AddChild(parts.Button);
            _tiles[item.Id] = parts;
        }

        if (_selectedId is not null && _tiles.TryGetValue(_selectedId, out TileParts? selected) && !selected.Button.Disabled)
            Select(_selectedId, notify: false);
        else
            _selectedId = null;

        UpdateColumns();
    }

    private TileParts BuildTile(Win98CatalogItemPresentation item, int index)
    {
        var button = new Button
        {
            Name = $"Catalog_{Sanitize(item.Id)}",
            ToggleMode = true,
            ButtonGroup = _selectionGroup,
            FocusMode = FocusModeEnum.All,
            CustomMinimumSize = new Vector2(_tileWidth, _tileHeight),
            TooltipText = item.Tooltip,
            Disabled = !item.Selectable,
            ClipContents = true,
        };
        button.Pressed += () => Select(item.Id);
        button.GuiInput += input => OnTileInput(index, input);
        ApplyAccent(button, item.Accented);

        var margin = new MarginContainer { MouseFilter = MouseFilterEnum.Ignore };
        margin.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        margin.AddThemeConstantOverride("margin_left", 6);
        margin.AddThemeConstantOverride("margin_top", 6);
        margin.AddThemeConstantOverride("margin_right", 6);
        margin.AddThemeConstantOverride("margin_bottom", 6);
        button.AddChild(margin);

        var column = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        column.AddThemeConstantOverride("separation", 3);
        margin.AddChild(column);

        var previewFrame = new PanelContainer
        {
            CustomMinimumSize = new Vector2(0, Mathf.Max(64f, _tileHeight - 72f)),
            SizeFlagsVertical = SizeFlags.ExpandFill,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        previewFrame.AddThemeStyleboxOverride("panel", Win98ThemeFactory.Recessed(Win98ThemeFactory.Light, 1));
        column.AddChild(previewFrame);

        var preview = new TextureRect
        {
            Texture = item.Preview,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        previewFrame.AddChild(preview);

        var name = new Label
        {
            Text = item.DisplayName,
            HorizontalAlignment = HorizontalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        column.AddChild(name);

        var footer = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        footer.AddThemeConstantOverride("separation", 3);
        column.AddChild(footer);
        var secondary = new Label
        {
            Text = item.SecondaryText,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            HorizontalAlignment = HorizontalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        secondary.AddThemeFontSizeOverride("font_size", 12);
        ApplySecondaryColor(secondary, item.SecondaryColor);
        footer.AddChild(secondary);
        var badge = new Label
        {
            Text = item.BadgeText,
            Visible = !string.IsNullOrWhiteSpace(item.BadgeText),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        badge.AddThemeFontSizeOverride("font_size", 11);
        footer.AddChild(badge);

        // Selection is deliberately a separate inset outline. A caller may use the outer accent
        // for a persistent state such as Equipped while the player previews a different tile.
        var selectionOutline = new PanelContainer
        {
            Name = "PreviewSelectionOutline",
            MouseFilter = MouseFilterEnum.Ignore,
            Visible = false,
            ZIndex = 4,
        };
        selectionOutline.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        selectionOutline.OffsetLeft = 3;
        selectionOutline.OffsetTop = 3;
        selectionOutline.OffsetRight = -3;
        selectionOutline.OffsetBottom = -3;
        var selectionBox = Win98ThemeFactory.Flat(Colors.Transparent);
        selectionBox.DrawCenter = false;
        selectionBox.BorderColor = Win98ThemeFactory.ActiveTitle;
        selectionBox.SetBorderWidthAll(2);
        selectionOutline.AddThemeStyleboxOverride("panel", selectionBox);
        button.AddChild(selectionOutline);

        return new TileParts(button, preview, name, secondary, badge, selectionOutline);
    }

    private static void Apply(TileParts parts, Win98CatalogItemPresentation item)
    {
        parts.Button.Disabled = !item.Selectable;
        parts.Button.TooltipText = item.Tooltip;
        parts.Preview.Texture = item.Preview;
        parts.Name.Text = item.DisplayName;
        parts.Secondary.Text = item.SecondaryText;
        ApplySecondaryColor(parts.Secondary, item.SecondaryColor);
        parts.Badge.Text = item.BadgeText;
        parts.Badge.Visible = !string.IsNullOrWhiteSpace(item.BadgeText);
        ApplyAccent(parts.Button, item.Accented);
    }

    private void UpdateColumns()
    {
        float available = Mathf.Max(_tileWidth, Size.X - Gap);
        int columns = Mathf.Max(1, Mathf.FloorToInt((available + Gap) / (_tileWidth + Gap)));
        _grid.Columns = columns;
    }

    private void OnTileInput(int index, InputEvent input)
    {
        if (input is InputEventMouseButton
            {
                ButtonIndex: MouseButton.Left,
                Pressed: true,
                DoubleClick: true,
            })
        {
            Activate(_items[index].Id);
            GetViewport().SetInputAsHandled();
            return;
        }

        if (input is not InputEventKey { Pressed: true, Echo: false } key)
            return;

        int columns = Mathf.Max(1, _grid.Columns);
        int delta = key.Keycode switch
        {
            Key.Left => -1,
            Key.Right => 1,
            Key.Up => -columns,
            Key.Down => columns,
            _ => 0,
        };
        if (delta == 0)
            return;

        int candidate = index + delta;
        while (candidate >= 0 && candidate < _items.Count)
        {
            if (_items[candidate].Selectable)
            {
                Select(_items[candidate].Id);
                GetViewport().SetInputAsHandled();
                return;
            }
            candidate += Math.Sign(delta);
        }
    }

    private int IndexOf(string id)
    {
        for (int index = 0; index < _items.Count; index++)
            if (string.Equals(_items[index].Id, id, StringComparison.Ordinal))
                return index;
        return -1;
    }

    private static string Sanitize(string id)
    {
        char[] chars = id.Select(character => char.IsLetterOrDigit(character) ? character : '_').ToArray();
        return new string(chars);
    }

    private static void ApplyAccent(Button button, bool accented)
    {
        if (!accented)
        {
            button.RemoveThemeStyleboxOverride("normal");
            button.RemoveThemeStyleboxOverride("hover");
            button.RemoveThemeStyleboxOverride("pressed");
            button.RemoveThemeStyleboxOverride("hover_pressed");
            button.RemoveThemeStyleboxOverride("focus");
            return;
        }

        var border = new StyleBoxFlat
        {
            BgColor = Win98ThemeFactory.Face,
            BorderColor = Win98ThemeFactory.ActiveTitle,
        };
        border.SetBorderWidthAll(4);
        button.AddThemeStyleboxOverride("normal", border);
        button.AddThemeStyleboxOverride("hover", (StyleBoxFlat)border.Duplicate());
        button.AddThemeStyleboxOverride("pressed", (StyleBoxFlat)border.Duplicate());
        button.AddThemeStyleboxOverride("hover_pressed", (StyleBoxFlat)border.Duplicate());
        button.AddThemeStyleboxOverride("focus", (StyleBoxFlat)border.Duplicate());
    }

    private static void ApplySecondaryColor(Label label, Color? color)
    {
        if (color is Color value)
            label.AddThemeColorOverride("font_color", value);
        else
            label.RemoveThemeColorOverride("font_color");
    }

    private sealed record TileParts(
        Button Button,
        TextureRect Preview,
        Label Name,
        Label Secondary,
        Label Badge,
        PanelContainer SelectionOutline);
}
