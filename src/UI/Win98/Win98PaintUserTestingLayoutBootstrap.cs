using System;
using DesktopBuddy.CharacterEditor;
using Godot;

namespace DesktopBuddy.UI.Win98;

/// <summary>
/// User-testing layout follow-up for Paint Buddy. The palette can be detached into a draggable
/// Win98 window, while turn/zoom controls live on the preview itself instead of consuming tool-rail
/// space. All behavior stays on the existing controls; this class only changes presentation.
/// </summary>
public partial class Win98PaintUserTestingLayoutBootstrap : Node
{
    private PaintCanvasControl? _canvas;
    private Control? _palette;
    private Node? _paletteHome;
    private int _paletteHomeIndex;
    private Button? _paletteToggle;
    private PanelContainer? _paletteWindow;
    private VBoxContainer? _paletteWindowBody;
    private PanelContainer? _viewportControls;
    private bool _paletteFloating;

    public override void _Ready() => ProcessMode = ProcessModeEnum.Always;

    public override void _Process(double delta)
    {
        _canvas ??= GetTree().Root.FindChild("CharacterPaintCanvas", true, false) as PaintCanvasControl;
        if (!GodotObject.IsInstanceValid(_canvas))
            return;

        bool paintActive = _canvas!.IsVisibleInTree();
        if (!paintActive)
        {
            if (_paletteFloating)
                DockPalette();
            if (GodotObject.IsInstanceValid(_viewportControls))
                _viewportControls!.Visible = false;
            return;
        }

        EnsureViewportControls();
        EnsurePaletteFloatUi();
        if (GodotObject.IsInstanceValid(_viewportControls))
            _viewportControls!.Visible = true;
    }

    public override void _ExitTree()
    {
        if (_paletteFloating)
            DockPalette();
    }

    private void EnsureViewportControls()
    {
        if (GodotObject.IsInstanceValid(_viewportControls))
            return;

        if (GetTree().Root.FindChild("CharacterPreview", true, false) is not SubViewportContainer preview ||
            GetTree().Root.FindChild("PaintRotateRow", true, false) is not HBoxContainer rotateRow ||
            GetTree().Root.FindChild("PaintViewRow", true, false) is not HBoxContainer viewRow)
        {
            return;
        }

        _viewportControls = new PanelContainer
        {
            Name = "PaintViewportControlCluster",
            MouseFilter = Control.MouseFilterEnum.Pass,
            ZIndex = 100,
            CustomMinimumSize = new Vector2(132, 66),
        };
        _viewportControls.AddThemeStyleboxOverride(
            "panel",
            Win98ThemeFactory.Raised(Win98ThemeFactory.Face, 2));
        preview.AddChild(_viewportControls);
        _viewportControls.SetAnchorsPreset(Control.LayoutPreset.BottomLeft);
        _viewportControls.OffsetLeft = 8;
        _viewportControls.OffsetTop = -74;
        _viewportControls.OffsetRight = 140;
        _viewportControls.OffsetBottom = -8;

        var column = new VBoxContainer
        {
            Name = "PaintViewportControlRows",
            MouseFilter = Control.MouseFilterEnum.Pass,
        };
        column.AddThemeConstantOverride("separation", 2);
        _viewportControls.AddChild(column);

        rotateRow.Reparent(column, false);
        viewRow.Reparent(column, false);
        rotateRow.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        viewRow.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        ConfigureCompactRow(rotateRow);
        ConfigureCompactRow(viewRow);

        _viewportControls.MoveToFront();
    }

    private static void ConfigureCompactRow(Control row)
    {
        foreach (Node child in row.GetChildren())
        {
            if (child is not Button button)
                continue;
            button.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            button.CustomMinimumSize = new Vector2(28, 27);
        }
    }

    private void EnsurePaletteFloatUi()
    {
        if (!GodotObject.IsInstanceValid(_palette))
        {
            _palette = GetTree().Root.FindChild("PaintPresetPalette", true, false) as Control;
            if (!GodotObject.IsInstanceValid(_palette) || _palette!.GetParent() is null)
                return;
            _paletteHome = _palette.GetParent();
            _paletteHomeIndex = _palette.GetIndex();
        }

        if (!GodotObject.IsInstanceValid(_paletteWindow))
        {
            if (GetTree().Root.FindChild("CharacterEditorUiRoot", true, false) is not Control root)
                return;

            _paletteWindow = Win98Dialog.Create(
                "PaintFloatingPaletteWindow",
                "Color Palette",
                new Vector2(360, 126),
                out VBoxContainer body,
                DockPalette);
            _paletteWindowBody = body;
            root.AddChild(_paletteWindow);
            _paletteWindow.Visible = false;
        }

        if (GodotObject.IsInstanceValid(_paletteToggle))
            return;

        if (_paletteHome is not HBoxContainer homeRow)
            return;

        _paletteToggle = new Button
        {
            Name = "PaintFloatPaletteButton",
            Text = "Float\nPalette",
            TooltipText = "Detach the color swatches into a draggable palette window.",
            CustomMinimumSize = new Vector2(58, 48),
            FocusMode = Control.FocusModeEnum.All,
        };
        _paletteToggle.Pressed += TogglePalette;
        homeRow.AddChild(_paletteToggle);
        int targetIndex = Math.Min(_paletteHomeIndex + 1, homeRow.GetChildCount() - 1);
        homeRow.MoveChild(_paletteToggle, targetIndex);
    }

    private void TogglePalette()
    {
        if (_paletteFloating)
            DockPalette();
        else
            FloatPalette();
    }

    private void FloatPalette()
    {
        if (_paletteFloating || !GodotObject.IsInstanceValid(_palette) ||
            !GodotObject.IsInstanceValid(_paletteWindow) ||
            !GodotObject.IsInstanceValid(_paletteWindowBody))
        {
            return;
        }

        _palette!.Reparent(_paletteWindowBody!, false);
        _palette.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _paletteWindow!.Visible = true;
        _paletteWindow.MoveToFront();
        _paletteFloating = true;
        if (GodotObject.IsInstanceValid(_paletteToggle))
        {
            _paletteToggle!.Text = "Dock\nPalette";
            _paletteToggle.TooltipText = "Return the color swatches to the Paint Buddy footer.";
        }
    }

    private void DockPalette()
    {
        if (!_paletteFloating)
        {
            if (GodotObject.IsInstanceValid(_paletteWindow))
                _paletteWindow!.Visible = false;
            return;
        }

        if (GodotObject.IsInstanceValid(_palette) && GodotObject.IsInstanceValid(_paletteHome as GodotObject))
        {
            _palette!.Reparent(_paletteHome!, false);
            int safeIndex = Math.Clamp(_paletteHomeIndex, 0, _paletteHome!.GetChildCount() - 1);
            _paletteHome.MoveChild(_palette, safeIndex);
        }
        if (GodotObject.IsInstanceValid(_paletteWindow))
            _paletteWindow!.Visible = false;
        _paletteFloating = false;
        if (GodotObject.IsInstanceValid(_paletteToggle))
        {
            _paletteToggle!.Text = "Float\nPalette";
            _paletteToggle.TooltipText = "Detach the color swatches into a draggable palette window.";
        }
    }
}
