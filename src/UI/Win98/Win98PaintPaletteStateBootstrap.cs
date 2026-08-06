using System;
using System.Collections.Generic;
using DesktopBuddy.CharacterEditor;
using DesktopBuddy.Domain.Painting;
using Godot;

namespace DesktopBuddy.UI.Win98;

/// <summary>
/// Gives the preset palette an explicit selected state. The foreground color is already shown
/// in the large color well; this additionally recesses the matching preset so selection is not
/// communicated by color alone.
/// </summary>
public partial class Win98PaintPaletteStateBootstrap : Node
{
    private const double RefreshIntervalSeconds = 0.05;

    private readonly Dictionary<Button, PaintColor> _swatches = new();
    private PaintCanvasControl? _canvas;
    private GridContainer? _palette;
    private double _refreshRemaining;
    private PaintColor? _lastColor;

    public override void _Ready() => ProcessMode = ProcessModeEnum.Always;

    public override void _Process(double delta)
    {
        _refreshRemaining -= delta;
        if (_refreshRemaining > 0.0)
            return;
        _refreshRemaining = RefreshIntervalSeconds;

        if (!GodotObject.IsInstanceValid(_canvas))
        {
            _canvas = GetTree().Root.FindChild(
                "CharacterPaintCanvas", recursive: true, owned: false) as PaintCanvasControl;
            _lastColor = null;
        }

        if (!GodotObject.IsInstanceValid(_palette))
            TryCompose();

        if (!GodotObject.IsInstanceValid(_canvas) || _swatches.Count == 0)
            return;

        PaintColor selected = _canvas!.Workspace.SelectedColor;
        if (_lastColor == selected)
            return;

        _lastColor = selected;
        foreach ((Button button, PaintColor color) in _swatches)
        {
            if (!GodotObject.IsInstanceValid(button))
                continue;
            bool active = color == selected;
            button.SetPressedNoSignal(active);
            button.AccessibilityDescription = active
                ? $"Selected preset color #{ToHex(color)}"
                : $"Preset color #{ToHex(color)}";
        }
    }

    private void TryCompose()
    {
        _palette = GetTree().Root.FindChild(
            "PaintPresetPaletteGrid", recursive: true, owned: false) as GridContainer;
        if (!GodotObject.IsInstanceValid(_palette))
            return;

        _swatches.Clear();
        foreach (Node child in _palette!.GetChildren())
        {
            if (child is not Button button || !TryReadPreset(button, out PaintColor color))
                continue;

            button.ToggleMode = true;
            button.ActionMode = BaseButton.ActionModeEnum.Release;
            button.TooltipText = $"Use #{ToHex(color)}";

            var selectedStyle = new StyleBoxFlat
            {
                BgColor = new Color(color.R / 255f, color.G / 255f, color.B / 255f),
                BorderColor = Win98ThemeFactory.Selection,
            };
            selectedStyle.SetBorderWidthAll(3);
            button.AddThemeStyleboxOverride("pressed", selectedStyle);
            button.AddThemeStyleboxOverride("hover_pressed", selectedStyle);
            button.AddThemeStyleboxOverride("focus", selectedStyle);
            _swatches.Add(button, color);
        }

        _lastColor = null;
    }

    private static bool TryReadPreset(Button button, out PaintColor color)
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

    private static string ToHex(PaintColor color) => $"{color.R:X2}{color.G:X2}{color.B:X2}";
}
