using System;
using System.Collections.Generic;
using DesktopBuddy.CharacterEditor;
using DesktopBuddy.Domain.Painting;
using Godot;

namespace DesktopBuddy.UI.Win98;

/// <summary>Adds session-scoped custom swatches and a terminal + action to the paint palette.</summary>
public partial class Win98PaintCustomPaletteBootstrap : Node
{
    private const int MaxCustomColors = 24;

    private readonly List<PaintColor> _customColors = [];
    private PaintCanvasControl? _canvas;
    private GridContainer? _palette;
    private ColorPickerButton? _picker;
    private ColorRect? _currentColor;
    private Button? _addButton;

    public override void _Ready() => ProcessMode = ProcessModeEnum.Always;

    public override void _Process(double delta)
    {
        _canvas ??= GetTree().Root.FindChild("CharacterPaintCanvas", true, false) as PaintCanvasControl;
        _palette ??= GetTree().Root.FindChild("PaintPresetPaletteGrid", true, false) as GridContainer;
        _picker ??= GetTree().Root.FindChild("PaintColorWheel", true, false) as ColorPickerButton;
        _currentColor ??= GetTree().Root.FindChild("PaintCurrentColor", true, false) as ColorRect;

        if (!GodotObject.IsInstanceValid(_canvas) || !GodotObject.IsInstanceValid(_palette))
            return;

        EnsureAddButton();
        EnsureCustomSwatches();
        KeepAddButtonLast();
        UpdateAddButtonState();
    }

    private void EnsureAddButton()
    {
        if (GodotObject.IsInstanceValid(_addButton))
            return;

        _addButton = _palette!.FindChild("PaintAddCustomColorButton", false, false) as Button;
        if (GodotObject.IsInstanceValid(_addButton))
            return;

        _addButton = new Button
        {
            Name = "PaintAddCustomColorButton",
            Text = "+",
            TooltipText = "Add the current brush color to the palette.",
            AccessibilityDescription = "Add current brush color to custom palette",
            FocusMode = Control.FocusModeEnum.All,
            CustomMinimumSize = new Vector2(22, 18),
        };
        _addButton.Pressed += AddCurrentColor;
        _palette.AddChild(_addButton);
    }

    private void AddCurrentColor()
    {
        if (!GodotObject.IsInstanceValid(_canvas) || _customColors.Count >= MaxCustomColors)
            return;

        PaintColor color = _canvas!.Workspace.SelectedColor;
        int existing = _customColors.IndexOf(color);
        if (existing < 0)
        {
            _customColors.Add(color);
            CreateCustomSwatch(color, _customColors.Count - 1);
        }

        ApplyColor(color);
        KeepAddButtonLast();
        UpdateAddButtonState();
    }

    private void EnsureCustomSwatches()
    {
        if (!GodotObject.IsInstanceValid(_palette))
            return;

        for (int index = 0; index < _customColors.Count; index++)
        {
            string name = $"PaintCustomPalette{index}";
            if (_palette!.FindChild(name, false, false) is null)
                CreateCustomSwatch(_customColors[index], index);
        }
    }

    private void CreateCustomSwatch(PaintColor color, int index)
    {
        if (!GodotObject.IsInstanceValid(_palette))
            return;

        string hex = ToHex(color);
        var button = new Button
        {
            Name = $"PaintCustomPalette{index}",
            TooltipText = $"Use custom color #{hex}",
            AccessibilityDescription = $"Custom color #{hex}",
            ToggleMode = true,
            FocusMode = Control.FocusModeEnum.All,
            CustomMinimumSize = new Vector2(22, 18),
        };
        ApplySwatchStyles(button, color);
        button.Pressed += () => ApplyColor(color);
        _palette!.AddChild(button);
        if (GodotObject.IsInstanceValid(_addButton))
            _palette.MoveChild(button, Math.Max(0, _addButton!.GetIndex()));
    }

    private void ApplyColor(PaintColor color)
    {
        if (!GodotObject.IsInstanceValid(_canvas))
            return;

        _canvas!.Workspace.SelectedColor = color;
        var godotColor = new Color(color.R / 255f, color.G / 255f, color.B / 255f, 1f);
        if (GodotObject.IsInstanceValid(_picker))
            _picker!.Color = godotColor;
        if (GodotObject.IsInstanceValid(_currentColor))
            _currentColor!.Color = godotColor;
        _canvas.QueueRedraw();
    }

    private void KeepAddButtonLast()
    {
        if (GodotObject.IsInstanceValid(_palette) && GodotObject.IsInstanceValid(_addButton))
            _palette!.MoveChild(_addButton!, _palette.GetChildCount() - 1);
    }

    private void UpdateAddButtonState()
    {
        if (!GodotObject.IsInstanceValid(_addButton))
            return;
        bool full = _customColors.Count >= MaxCustomColors;
        _addButton!.Disabled = full;
        _addButton.TooltipText = full
            ? $"Custom palette limit reached ({MaxCustomColors})."
            : "Add the current brush color to the palette.";
    }

    private static void ApplySwatchStyles(Button button, PaintColor color)
    {
        var fill = new Color(color.R / 255f, color.G / 255f, color.B / 255f, 1f);
        var normal = new StyleBoxFlat { BgColor = fill, BorderColor = Colors.Black };
        normal.SetBorderWidthAll(1);
        var selected = new StyleBoxFlat { BgColor = fill, BorderColor = Win98ThemeFactory.Selection };
        selected.SetBorderWidthAll(3);
        button.AddThemeStyleboxOverride("normal", normal);
        button.AddThemeStyleboxOverride("hover", normal);
        button.AddThemeStyleboxOverride("pressed", selected);
        button.AddThemeStyleboxOverride("hover_pressed", selected);
        button.AddThemeStyleboxOverride("focus", selected);
    }

    private static string ToHex(PaintColor color) => $"{color.R:X2}{color.G:X2}{color.B:X2}";
}
