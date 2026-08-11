using System;
using DesktopBuddy.CharacterEditor;
using Godot;

namespace DesktopBuddy.UI.Win98;

/// <summary>
/// User-testing paint corrections that intentionally run after the older Win98 polish pass.
/// It keeps the complete modern tool set visible, exposes the two paint-mapping modifiers,
/// and makes the active color/tool affordances explicit without changing paint semantics.
/// </summary>
public partial class Win98PaintUserTestingBootstrap : Node
{
    private PaintCanvasControl? _canvas;
    private HBoxContainer? _mappingOptions;
    private CheckBox? _mirror;
    private CheckBox? _backside;
    private bool _syncing;

    public override void _Ready() => ProcessMode = ProcessModeEnum.Always;

    public override void _Process(double delta)
    {
        _canvas ??= GetTree().Root.FindChild("CharacterPaintCanvas", true, false) as PaintCanvasControl;
        if (!GodotObject.IsInstanceValid(_canvas))
            return;

        EnsureMappingOptions();
        CorrectCompleteToolRail();
        DecorateCurrentColor();
        SyncMappingOptions();
    }

    private void EnsureMappingOptions()
    {
        if (GodotObject.IsInstanceValid(_mappingOptions))
            return;
        if (GetTree().Root.FindChild("CharacterPaintControls", true, false) is not VBoxContainer controls)
            return;

        _mappingOptions = controls.FindChild("PaintMappingOptions", false, false) as HBoxContainer;
        if (!GodotObject.IsInstanceValid(_mappingOptions))
        {
            _mappingOptions = new HBoxContainer
            {
                Name = "PaintMappingOptions",
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            };
            _mappingOptions.AddThemeConstantOverride("separation", 8);
            controls.AddChild(_mappingOptions);
            if (controls.FindChild("PaintBrushSizeRow", false, false) is Control sizeRow)
                controls.MoveChild(_mappingOptions, sizeRow.GetIndex());
        }

        _mirror = _mappingOptions!.FindChild("PaintMirrorToggle", false, false) as CheckBox;
        if (!GodotObject.IsInstanceValid(_mirror))
        {
            _mirror = new CheckBox
            {
                Name = "PaintMirrorToggle",
                Text = "Mirror",
                TooltipText = "Paint the reflected point on the same body-part surface at the same time.",
                FocusMode = Control.FocusModeEnum.All,
            };
            _mirror.Toggled += enabled =>
            {
                if (_syncing || !GodotObject.IsInstanceValid(_canvas)) return;
                _canvas!.Workspace.MirrorEnabled = enabled;
                _canvas.QueueRedraw();
            };
            _mappingOptions.AddChild(_mirror);
        }

        _backside = _mappingOptions.FindChild("PaintBacksideToggle", false, false) as CheckBox;
        if (!GodotObject.IsInstanceValid(_backside))
        {
            _backside = new CheckBox
            {
                Name = "PaintBacksideToggle",
                Text = "Paint backside too",
                TooltipText = "Repeat each stroke half a turn around the same body-part surface.",
                FocusMode = Control.FocusModeEnum.All,
            };
            _backside.Toggled += enabled =>
            {
                if (_syncing || !GodotObject.IsInstanceValid(_canvas)) return;
                _canvas!.Workspace.PaintBacksideEnabled = enabled;
                _canvas.QueueRedraw();
            };
            _mappingOptions.AddChild(_backside);
        }
    }

    private void SyncMappingOptions()
    {
        if (!GodotObject.IsInstanceValid(_canvas) ||
            !GodotObject.IsInstanceValid(_mirror) ||
            !GodotObject.IsInstanceValid(_backside))
            return;

        _syncing = true;
        _mirror!.SetPressedNoSignal(_canvas!.Workspace.MirrorEnabled);
        _backside!.SetPressedNoSignal(_canvas.Workspace.PaintBacksideEnabled);
        _syncing = false;
    }

    private void CorrectCompleteToolRail()
    {
        if (GetTree().Root.FindChild("Win98ToolPicker", true, false) is not GridContainer picker)
            return;

        Button? brush = FindButton(picker, "PaintBrushButton");
        Button? spray = FindButton(picker, "PaintSprayButton");
        Button? fill = FindButton(picker, "PaintFillButton");
        Button? eraser = FindButton(picker, "PaintEraserButton");
        Button? curve = FindButton(picker, "PaintCurveButton");
        Button? pick = FindButton(picker, "PaintEyedropperButton");
        Button? pan = FindButton(picker, "PaintPanButton");
        if (!Valid(brush) || !Valid(spray) || !Valid(fill) || !Valid(eraser) ||
            !Valid(curve) || !Valid(pick) || !Valid(pan))
            return;

        picker.Columns = 1;
        picker.CustomMinimumSize = new Vector2(116, 0);
        picker.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;

        Label paintHeader = EnsureHeader(picker, "PaintToolGroupHeader", "Paint");
        HSeparator separator = picker.FindChild("PaintToolGroupSeparator", false, false) as HSeparator
            ?? new HSeparator { Name = "PaintToolGroupSeparator" };
        if (separator.GetParent() is null) picker.AddChild(separator);
        Label inspectHeader = EnsureHeader(picker, "PaintInspectGroupHeader", "Inspect & move");

        Node[] order = [paintHeader, brush!, spray!, fill!, eraser!, curve!, separator, inspectHeader, pick!, pan!];
        for (int index = 0; index < order.Length; index++)
            Move(picker, order[index], index);

        Configure(brush!, PaintToolIconProvider.Brush, "Brush", "Paint with the selected color. (B)");
        Configure(spray!, PaintToolIconProvider.Spray, "Spray", "Airbrush with the selected color. (S)");
        Configure(fill!, PaintToolIconProvider.Fill, "Bucket Fill", "Fill one connected paint region. (F)");
        Configure(eraser!, PaintToolIconProvider.Eraser, "Eraser", "Remove paint with the current brush size. (E)");
        Configure(curve!, PaintToolIconProvider.Curve, "Curve", "Draw a baseline, then set two bends. (C)");
        Configure(pick!, PaintToolIconProvider.PickColor, "Pick Color", "Sample a painted color from the buddy. (I)");
        Configure(pan!, PaintToolIconProvider.Pan, "Pan View", "Move the canvas without painting. (H)");
    }

    private static Button? FindButton(Node parent, string name) =>
        parent.FindChild(name, true, false) as Button;

    private static bool Valid(GodotObject? value) => GodotObject.IsInstanceValid(value);

    private static Label EnsureHeader(GridContainer picker, string name, string text)
    {
        if (picker.FindChild(name, false, false) is Label existing)
        {
            existing.Text = text;
            return existing;
        }
        var label = new Label { Name = name, Text = text, HorizontalAlignment = HorizontalAlignment.Left };
        label.AddThemeColorOverride("font_color", Win98ThemeFactory.Shadow);
        picker.AddChild(label);
        return label;
    }

    private static void Configure(Button button, string iconId, string fallback, string tooltip)
    {
        PaintToolIconProvider.Apply(button, iconId, fallback, tooltip);
        button.ToggleMode = true;
        button.Alignment = HorizontalAlignment.Center;
        button.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        button.CustomMinimumSize = new Vector2(116, 31);
    }

    private static void Move(Node parent, Node child, int index)
    {
        if (child.GetParent() != parent)
            child.Reparent(parent, false);
        int safe = Math.Clamp(index, 0, parent.GetChildCount() - 1);
        if (child.GetIndex() != safe)
            parent.MoveChild(child, safe);
    }

    private void DecorateCurrentColor()
    {
        if (GetTree().Root.FindChild("PaintCurrentColor", true, false) is not ColorRect swatch ||
            swatch.FindChild("PaintCurrentColorBorder", false, false) is not null)
            return;

        var border = new PanelContainer
        {
            Name = "PaintCurrentColorBorder",
            MouseFilter = Control.MouseFilterEnum.Ignore,
            TooltipText = "Current paint color",
        };
        StyleBoxFlat style = Win98ThemeFactory.Recessed(Colors.Transparent, 2);
        style.DrawCenter = false;
        border.AddThemeStyleboxOverride("panel", style);
        swatch.AddChild(border);
        border.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
    }
}
