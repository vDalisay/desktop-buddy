using System;
using System.Collections.Generic;
using DesktopBuddy.CharacterEditor;
using DesktopBuddy.Domain.Painting;
using Godot;

namespace DesktopBuddy.UI.Win98;

/// <summary>Shows the current foreground color as a pressed preset or custom palette swatch.</summary>
public partial class Win98PaintPaletteStateBootstrap : Node
{
    private const double RefreshIntervalSeconds = 0.05;

    private readonly Dictionary<Button, PaintColor> _swatches = new();
    private PaintCanvasControl? _canvas;
    private GridContainer? _palette;
    private ColorRect? _currentColor;
    private double _refreshRemaining;
    private int _lastChildCount = -1;

    public override void _Ready() => ProcessMode = ProcessModeEnum.Always;

    public override void _Process(double delta)
    {
        _refreshRemaining -= delta;
        if (_refreshRemaining > 0.0)
            return;
        _refreshRemaining = RefreshIntervalSeconds;

        _canvas ??= GetTree().Root.FindChild("CharacterPaintCanvas", true, false) as PaintCanvasControl;
        _palette ??= GetTree().Root.FindChild("PaintPresetPaletteGrid", true, false) as GridContainer;
        _currentColor ??= GetTree().Root.FindChild("PaintCurrentColor", true, false) as ColorRect;
        if (!GodotObject.IsInstanceValid(_canvas) || !GodotObject.IsInstanceValid(_palette))
            return;

        if (_palette!.GetChildCount() != _lastChildCount)
            RebuildSwatches();

        PaintColor selected = _canvas!.Workspace.SelectedColor;
        if (GodotObject.IsInstanceValid(_currentColor))
            _currentColor!.Color = new Color(selected.R / 255f, selected.G / 255f, selected.B / 255f, 1f);
        // Colors are re-read from the tooltip every pass so re-coloured swatches (palette
        // editing) keep the selection marker on the right block.
        foreach (Button button in _swatches.Keys)
        {
            if (GodotObject.IsInstanceValid(button) && TryReadColor(button, out PaintColor color))
                button.SetPressedNoSignal(color == selected);
        }
    }

    private void RebuildSwatches()
    {
        _swatches.Clear();
        _lastChildCount = _palette!.GetChildCount();
        foreach (Node child in _palette.GetChildren())
        {
            if (child is not Button button || button.Name == "PaintAddCustomColorButton" ||
                !TryReadColor(button, out PaintColor color))
                continue;

            button.ToggleMode = true;
            button.ActionMode = BaseButton.ActionModeEnum.Release;
            var selectedStyle = new StyleBoxFlat
            {
                BgColor = new Color(color.R / 255f, color.G / 255f, color.B / 255f),
                BorderColor = Win98ThemeFactory.Selection,
            };
            selectedStyle.SetBorderWidthAll(3);
            button.AddThemeStyleboxOverride("pressed", selectedStyle);
            button.AddThemeStyleboxOverride("hover_pressed", selectedStyle);
            button.AddThemeStyleboxOverride("focus", selectedStyle);
            _swatches[button] = color;
        }
    }

    private static bool TryReadColor(Button button, out PaintColor color)
    {
        string tooltip = button.TooltipText;
        int hash = tooltip.LastIndexOf('#');
        if (hash < 0 || tooltip.Length - hash - 1 < 6)
        {
            color = default;
            return false;
        }

        string hex = tooltip.Substring(hash + 1, 6);
        if (!uint.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out uint rgb))
        {
            color = default;
            return false;
        }

        color = new PaintColor(
            (byte)((rgb >> 16) & 0xFF),
            (byte)((rgb >> 8) & 0xFF),
            (byte)(rgb & 0xFF));
        return true;
    }
}
