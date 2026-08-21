using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DesktopBuddy.CharacterEditor;
using DesktopBuddy.Onboarding;
using DesktopBuddy.Domain.Painting;
using Godot;

namespace DesktopBuddy.UI.Win98;

/// <summary>
/// Owns the paint palette: per-character color blocks (persisted in the character document),
/// the terminal + action, double-click block editing and Delete-key removal. Blocks keep a
/// fixed size and pack along the horizontal axis first, wrapping into extra scrolling rows.
/// </summary>
public partial class Win98PaintCustomPaletteBootstrap : Node
{
    private const int ModalZIndex = 200;
    private const int MaxColors = 64;
    private const float SwatchWidth = 22f;
    private const float SwatchHeight = 18f;
    private const float SwatchSpacing = 1f;

    private static readonly string[] DefaultPalette =
    [
        "000000", "808080", "FFFFFF", "800000", "FF0000", "808000", "FFFF00", "008000",
        "00FF00", "008080", "00FFFF", "000080", "0000FF", "800080", "FF00FF", "C0C0C0",
    ];

    private readonly List<PaintColor> _colors = [];
    private int _selectedIndex = -1;
    private CharacterEditorHost? _host;
    private PaintCanvasControl? _canvas;
    private GridContainer? _palette;
    private ColorPickerButton? _picker;
    private ColorRect? _currentColor;
    private Button? _addButton;
    private PanelContainer? _editPanel;
    private Control? _editBlocker;
    private ColorPicker? _editPicker;
    private Guid? _loadedCharacter;
    private bool _paletteLoaded;
    private bool _rebuildQueued;
    private int _editIndex = -1;
    private PaintColor _editOriginal;

    public override void _Ready() => ProcessMode = ProcessModeEnum.Always;

    public override void _Process(double delta)
    {
        _host ??= GetTree().Root.FindChild(nameof(CharacterEditorHost), true, false) as CharacterEditorHost;
        _canvas ??= GetTree().Root.FindChild("CharacterPaintCanvas", true, false) as PaintCanvasControl;
        _palette ??= GetTree().Root.FindChild("PaintPresetPaletteGrid", true, false) as GridContainer;
        _picker ??= GetTree().Root.FindChild("PaintColorWheel", true, false) as ColorPickerButton;
        _currentColor ??= GetTree().Root.FindChild("PaintCurrentColor", true, false) as ColorRect;

        if (!GodotObject.IsInstanceValid(_canvas) || !GodotObject.IsInstanceValid(_palette))
            return;

        if (GetTree().Root.FindChild("CharacterEditorUiRoot", true, false) is Control root)
            EnsureEditDialog(root);

        SyncCharacterPalette();
        EnsureAddButton();
        if (_rebuildQueued)
            RebuildSwatches();
        UpdateGridColumns();
        RefreshSelection();
    }

    /// <summary>
    /// Exactly one block carries the selection ring. The blocks are toggle buttons, so a click
    /// latches one pressed and nothing un-latches the rest on its own; every colour ever picked
    /// stayed ringed until this ran (owner report 2026-08-19). Index-based rather than
    /// colour-based so two blocks holding the same colour do not both light up.
    /// </summary>
    private void RefreshSelection()
    {
        if (!GodotObject.IsInstanceValid(_canvas) || !GodotObject.IsInstanceValid(_palette))
            return;

        PaintColor selected = _canvas!.Workspace.SelectedColor;
        if (_selectedIndex < 0 || _selectedIndex >= _colors.Count || _colors[_selectedIndex] != selected)
            _selectedIndex = _colors.IndexOf(selected);

        foreach (Node child in _palette!.GetChildren())
        {
            if (child is not Button block || block == _addButton)
                continue;
            block.SetPressedNoSignal(block.GetIndex() == _selectedIndex);
        }
    }

    /// <summary>Delete removes the focused color block.</summary>
    public override void _UnhandledKeyInput(InputEvent input)
    {
        if (input is not InputEventKey { Pressed: true, Echo: false, Keycode: Key.Delete } ||
            !GodotObject.IsInstanceValid(_palette))
            return;

        if (GetViewport().GuiGetFocusOwner() is not Button focused || focused.GetParent() != _palette ||
            focused == _addButton)
            return;

        RemoveColor(focused.GetIndex());
        GetViewport().SetInputAsHandled();
    }

    // ---- per-character state -------------------------------------------------------------

    private void SyncCharacterPalette()
    {
        Guid? character = _host?.Session.SelectedCharacterId;
        if (_paletteLoaded && character == _loadedCharacter)
            return;

        _loadedCharacter = character;
        _paletteLoaded = true;
        _editIndex = -1;
        IReadOnlyList<string> stored = _host?.Session.Palette ?? [];
        _colors.Clear();
        _colors.AddRange((stored.Count > 0 ? stored : DefaultPalette).Select(ParseHex));
        _rebuildQueued = true;
    }

    /// <summary>Writes the palette back into the character document (saved with the character).</summary>
    private void Persist()
    {
        if (GodotObject.IsInstanceValid(_host) && _host!.Session.WorkingDocument is not null)
            _host.Session.SetPalette(_colors.Select(ToHex).ToArray());
    }

    // ---- grid ----------------------------------------------------------------------------

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
            TooltipText = "Add the current brush color as a new palette block.",
            AccessibilityDescription = "Add current brush color to palette",
            FocusMode = Control.FocusModeEnum.All,
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin,
            CustomMinimumSize = new Vector2(SwatchWidth, SwatchHeight),
        };
        _addButton.Pressed += AddCurrentColor;
        _palette.AddChild(_addButton);
    }

    private void RebuildSwatches()
    {
        _rebuildQueued = false;
        foreach (Node child in _palette!.GetChildren())
        {
            if (child == _addButton)
                continue;
            // Removed synchronously: swatch order is addressed by child index this same frame.
            _palette.RemoveChild(child);
            child.QueueFree();
        }

        for (int index = 0; index < _colors.Count; index++)
            CreateSwatch(index, _colors[index]);
        if (GodotObject.IsInstanceValid(_addButton))
            _palette.MoveChild(_addButton!, _palette.GetChildCount() - 1);
    }

    private void CreateSwatch(int index, PaintColor color)
    {
        var button = new Button
        {
            Name = $"PaintPalette{index}",
            ToggleMode = true,
            FocusMode = Control.FocusModeEnum.All,
            // Fixed size: a new row must never squeeze the rows above it.
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin,
            SizeFlagsVertical = Control.SizeFlags.ShrinkBegin,
            CustomMinimumSize = new Vector2(SwatchWidth, SwatchHeight),
        };
        ApplySwatchColor(button, color);
        int captured = index;
        button.Pressed += () =>
        {
            _selectedIndex = Math.Clamp(captured, 0, _colors.Count - 1);
            ApplyColor(_colors[_selectedIndex]);
        };
        button.GuiInput += input =>
        {
            // The colour step wants one single click on a swatch; a double-click on that same
            // swatch used to open the block editor over the prompt. Blocked for the length of
            // the walkthrough only — TutorialInputGate reopens it when the prompt goes away.
            if (input is InputEventMouseButton { DoubleClick: true, ButtonIndex: MouseButton.Left } &&
                TutorialInputGate.AllowsPaletteEditing)
            {
                OpenEditor(captured);
            }
        };
        _palette!.AddChild(button);
        _palette.MoveChild(button, index);
    }

    /// <summary>
    /// Rows are packed full-width so the trailing + always sits in the slot the next block
    /// will occupy; overflow wraps into extra rows that the scroll container carries.
    /// </summary>
    private void UpdateGridColumns()
    {
        if (_palette!.GetChildCount() == 0)
            return;
        int fit = Math.Max(1, (int)((_palette.Size.X + SwatchSpacing) / (SwatchWidth + SwatchSpacing)));
        if (_palette.Columns != fit)
            _palette.Columns = fit;
    }

    // ---- mutations -----------------------------------------------------------------------

    /// <summary>Always appends a new block: the current brush color, white when unavailable.</summary>
    private void AddCurrentColor()
    {
        if (_colors.Count >= MaxColors)
            return;

        PaintColor color = GodotObject.IsInstanceValid(_canvas)
            ? _canvas!.Workspace.SelectedColor
            : new PaintColor(255, 255, 255);
        _colors.Add(color);
        _selectedIndex = _colors.Count - 1;
        _rebuildQueued = true;
        Persist();
        ApplyColor(color);
    }

    private void RemoveColor(int index)
    {
        if (index < 0 || index >= _colors.Count)
            return;
        _colors.RemoveAt(index);
        _rebuildQueued = true;
        Persist();
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

    // ---- block editor sub-window ---------------------------------------------------------

    internal void EnsureEditDialog(Control root)
    {
        if (GodotObject.IsInstanceValid(_editPanel))
            return;

        _editBlocker = Win98Dialog.Blocker(root, "PaintColorBlockModalBlocker");
        _editBlocker.ZIndex = ModalZIndex;
        if (root.FindChild("PaintColorBlockEditor", false, false) is PanelContainer existing)
        {
            _editPanel = existing;
            _editPicker = existing.FindChild("PaintColorBlockPicker", true, false) as ColorPicker;
            return;
        }

        _editPanel = Win98Dialog.Create(
            "PaintColorBlockEditor",
            "Edit color block",
            new Vector2(320, 420),
            out VBoxContainer body,
            CancelEdit);
        _editPanel.ZIndex = ModalZIndex + 1;
        root.AddChild(_editPanel);

        _editPicker = new ColorPicker
        {
            Name = "PaintColorBlockPicker",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            EditAlpha = false,
        };
        _editPicker.ColorChanged += PreviewEditColor;
        body.AddChild(_editPicker);

        var actions = new HBoxContainer
        {
            Name = "PaintColorBlockActions",
            Alignment = BoxContainer.AlignmentMode.Center,
            SizeFlagsVertical = Control.SizeFlags.ShrinkEnd,
        };
        actions.AddThemeConstantOverride("separation", 8);
        body.AddChild(actions);
        Win98Dialog.Action(actions, "Delete", DeleteEdited);
        Win98Dialog.Action(actions, "Save", SaveEdited);
    }

    private void OpenEditor(int index)
    {
        if (!GodotObject.IsInstanceValid(_editPanel) || index < 0 || index >= _colors.Count)
            return;

        _editIndex = index;
        _editOriginal = _colors[index];
        _editPicker!.Color = new Color(_editOriginal.R / 255f, _editOriginal.G / 255f, _editOriginal.B / 255f);
        _editBlocker!.Visible = true;
        _editPanel!.Visible = true;
        _editPanel.MoveToFront();
    }

    private void PreviewEditColor(Color value)
    {
        if (_editIndex < 0 || _editIndex >= _colors.Count)
            return;
        _colors[_editIndex] = FromGodot(value);
        if (_palette!.GetChildCount() > _editIndex && _palette.GetChild(_editIndex) is Button swatch)
            ApplySwatchColor(swatch, _colors[_editIndex]);
    }

    private void CancelEdit()
    {
        if (_editIndex >= 0 && _editIndex < _colors.Count)
        {
            _colors[_editIndex] = _editOriginal;
            _rebuildQueued = true;
        }
        CloseEditor();
    }

    private void SaveEdited()
    {
        if (_editIndex >= 0 && _editIndex < _colors.Count)
        {
            ApplyColor(_colors[_editIndex]);
            Persist();
        }
        CloseEditor();
    }

    private void DeleteEdited()
    {
        int index = _editIndex;
        CloseEditor();
        RemoveColor(index);
    }

    private void CloseEditor()
    {
        _editIndex = -1;
        if (GodotObject.IsInstanceValid(_editPanel)) _editPanel!.Visible = false;
        if (GodotObject.IsInstanceValid(_editBlocker)) _editBlocker!.Visible = false;
    }

    // ---- helpers -------------------------------------------------------------------------

    private static void ApplySwatchColor(Button button, PaintColor color)
    {
        string hex = ToHex(color);
        button.TooltipText = $"Use #{hex} — double-click to edit, Delete to remove.";
        button.AccessibilityDescription = $"Color #{hex}";

        var fill = new Color(color.R / 255f, color.G / 255f, color.B / 255f, 1f);
        var normal = new StyleBoxFlat { BgColor = fill, BorderColor = Colors.Black };
        normal.SetBorderWidthAll(1);
        var selected = new StyleBoxFlat { BgColor = fill, BorderColor = Win98ThemeFactory.Selection };
        selected.SetBorderWidthAll(3);
        button.AddThemeStyleboxOverride("normal", normal);
        button.AddThemeStyleboxOverride("hover", normal);
        button.AddThemeStyleboxOverride("pressed", selected);
        button.AddThemeStyleboxOverride("hover_pressed", selected);
        button.AddThemeStyleboxOverride("focus", normal);
    }

    private static PaintColor FromGodot(Color value) => new(
        (byte)Math.Clamp(Math.Round(value.R * 255), 0, 255),
        (byte)Math.Clamp(Math.Round(value.G * 255), 0, 255),
        (byte)Math.Clamp(Math.Round(value.B * 255), 0, 255));

    private static string ToHex(PaintColor color) => $"{color.R:X2}{color.G:X2}{color.B:X2}";

    private static PaintColor ParseHex(string hex)
    {
        string trimmed = hex.TrimStart('#');
        return trimmed.Length == 6 &&
            uint.TryParse(trimmed, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint rgb)
            ? new PaintColor((byte)((rgb >> 16) & 0xFF), (byte)((rgb >> 8) & 0xFF), (byte)(rgb & 0xFF))
            : new PaintColor(255, 255, 255);
    }
}
