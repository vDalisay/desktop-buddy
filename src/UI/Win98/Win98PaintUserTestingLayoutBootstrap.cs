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
    private PanelContainer? _palettePanel;
    private Win98PinnablePanel? _palettePin;
    private PanelContainer? _viewportControls;

    public override void _Ready() => ProcessMode = ProcessModeEnum.Always;

    public override void _Process(double delta)
    {
        _canvas ??= GetTree().Root.FindChild("CharacterPaintCanvas", true, false) as PaintCanvasControl;
        if (!GodotObject.IsInstanceValid(_canvas))
            return;

        bool paintActive = _canvas!.IsVisibleInTree();
        if (!paintActive)
        {
            _palettePin?.Dock();
            if (GodotObject.IsInstanceValid(_viewportControls))
                _viewportControls!.Visible = false;
            return;
        }

        EnsureViewportControls();
        EnsurePaletteFloatUi();
        if (GodotObject.IsInstanceValid(_viewportControls))
            _viewportControls!.Visible = true;
    }

    public override void _ExitTree() => _palettePin?.Dock();

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
        if (GodotObject.IsInstanceValid(_palettePanel))
            return;

        _palette = GetTree().Root.FindChild("PaintPresetPalette", true, false) as Control;
        Control? current = GetTree().Root.FindChild("PaintCurrentColor", true, false) as Control;
        Control? picker = GetTree().Root.FindChild("PaintColorWheel", true, false) as Control;
        if (!GodotObject.IsInstanceValid(_palette) || !GodotObject.IsInstanceValid(current) ||
            !GodotObject.IsInstanceValid(picker) || _palette!.GetParent() is not HBoxContainer homeRow ||
            current!.GetParent() != homeRow || picker!.GetParent() != homeRow)
            return;

        int index = current.GetIndex();
        _palettePanel = Win98Dialog.Create(
            "PaintPalettePanel",
            "Color Palette",
            new Vector2(360, 86),
            out VBoxContainer body,
            () => _palettePin?.Dock(),
            draggable: false);
        homeRow.AddChild(_palettePanel);
        homeRow.MoveChild(_palettePanel, index);
        var paletteRow = new HBoxContainer { Name = "PaintFloatingPaletteContent" };
        paletteRow.AddThemeConstantOverride("separation", 8);
        body.AddChild(paletteRow);
        current.Reparent(paletteRow, false);
        _palette.Reparent(paletteRow, false);
        picker.Reparent(paletteRow, false);
        _palette.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _palettePanel.Visible = true;

        _palettePin = new Win98PinnablePanel { Name = "PaintPalettePinController" };
        AddChild(_palettePin);
        _palettePin.Configure(_palettePanel, new Vector2I(760, 150), "PaintFloatingPaletteWindow");
    }
}
