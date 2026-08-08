using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace DesktopBuddy.UI.Win98;

/// <summary>
/// Presentation-only key/value row. Feature code supplies already-decided labels and values;
/// the panel does not know whether a value represents ownership, price, refund or budget.
/// </summary>
public readonly record struct Win98ValueRowPresentation(
    string Id,
    string Label,
    string Value,
    bool Emphasized = false,
    bool Visible = true);

/// <summary>
/// Small shared Win98 inspector panel used for price/ownership/budget summaries. Business
/// rules stay in the caller; this component only keeps alignment and typography consistent.
/// </summary>
public partial class Win98ValuePanel : PanelContainer
{
    private readonly Dictionary<string, RowParts> _rows = new(StringComparer.Ordinal);
    private IReadOnlyList<Win98ValueRowPresentation> _items = Array.Empty<Win98ValueRowPresentation>();
    private VBoxContainer _column = null!;
    private bool _built;

    public override void _Ready()
    {
        Theme = Win98ThemeFactory.Create();
        AddThemeStyleboxOverride("panel", Win98ThemeFactory.Recessed(Win98ThemeFactory.Face, 1));
        Build();
        Rebuild();
    }

    public void SetRows(IEnumerable<Win98ValueRowPresentation> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        Win98ValueRowPresentation[] frozen = rows.ToArray();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (Win98ValueRowPresentation row in frozen)
        {
            if (string.IsNullOrWhiteSpace(row.Id) || string.IsNullOrWhiteSpace(row.Label))
                throw new ArgumentException("Value-row IDs and labels are required.", nameof(rows));
            if (!ids.Add(row.Id))
                throw new ArgumentException($"Duplicate value-row ID '{row.Id}'.", nameof(rows));
        }

        _items = frozen;
        if (_built)
            Rebuild();
    }

    public bool UpdateValue(string id, string value)
    {
        if (!_rows.TryGetValue(id, out RowParts? parts))
            return false;
        parts.Value.Text = value;
        return true;
    }

    private void Build()
    {
        if (_built)
            return;
        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", 8);
        margin.AddThemeConstantOverride("margin_top", 8);
        margin.AddThemeConstantOverride("margin_right", 8);
        margin.AddThemeConstantOverride("margin_bottom", 8);
        AddChild(margin);

        _column = new VBoxContainer { Name = "ValueRows" };
        _column.AddThemeConstantOverride("separation", 4);
        margin.AddChild(_column);
        _built = true;
    }

    private void Rebuild()
    {
        if (!_built)
            return;
        foreach (Node child in _column.GetChildren())
            child.QueueFree();
        _rows.Clear();

        foreach (Win98ValueRowPresentation item in _items)
        {
            var row = new HBoxContainer { Visible = item.Visible };
            row.AddThemeConstantOverride("separation", 8);
            _column.AddChild(row);

            var label = new Label
            {
                Text = item.Label,
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            var value = new Label
            {
                Text = item.Value,
                HorizontalAlignment = HorizontalAlignment.Right,
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            if (item.Emphasized)
            {
                label.AddThemeFontSizeOverride("font_size", 15);
                value.AddThemeFontSizeOverride("font_size", 15);
            }
            row.AddChild(label);
            row.AddChild(value);
            _rows[item.Id] = new RowParts(row, label, value);
        }
    }

    private sealed record RowParts(HBoxContainer Row, Label Label, Label Value);
}
