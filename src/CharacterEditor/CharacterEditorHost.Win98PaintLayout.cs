using System;
using System.Threading.Tasks;
using DesktopBuddy.UI.Win98;
using Godot;

namespace DesktopBuddy.CharacterEditor;

public partial class CharacterEditorHost
{
    private HBoxContainer _win98PaintWorkspace = null!;
    private HScrollBar _paintHorizontalScroll = null!;
    private VScrollBar _paintVerticalScroll = null!;
    private bool _syncingPaintScrollbars;
    private int _paintRotationQuarterTurns;
    private bool _win98PaintLayoutComposed;

    /// <summary>
    /// Product entry point for the Win98 Paint / Character menu. The paint workspace is the
    /// editor itself: character management at left, a narrow tool strip, a dominant buddy
    /// viewport with classic scrollbars, and the color/action bar along the bottom.
    /// </summary>
    public async Task OpenWin98PaintEditorAsync()
    {
        await OpenPaintEditorAsync();
        if (!IsEditorOpen)
            return;

        for (int frame = 0; frame < 120 && !GodotObject.IsInstanceValid(_paintControls); frame++)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

        if (!GodotObject.IsInstanceValid(_paintControls) ||
            !GodotObject.IsInstanceValid(_paintCanvas))
        {
            return;
        }

        EnsureWin98PaintLayout();
        ApplyWin98PaintLayout();
        _paintCanvas.GrabFocus();
    }

    private void EnsureWin98PaintLayout()
    {
        if (_win98PaintLayoutComposed)
            return;

        if (FindChild("CharacterLibrary", true, false) is not VBoxContainer library ||
            library.GetParent() is not HSplitContainer editorBody ||
            FindChild("CharacterControlsScroll", true, false) is not ScrollContainer appearanceScroll ||
            FindChild("CharacterPreview", true, false) is not SubViewportContainer preview)
        {
            return;
        }

        _editorRoot.Theme = Win98ThemeFactory.Create();
        appearanceScroll.Visible = false;
        _paintModeButton.Visible = false;

        _win98PaintWorkspace = new HBoxContainer
        {
            Name = "Win98PaintWorkspace",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(760, 480),
            MouseFilter = Control.MouseFilterEnum.Pass,
        };
        _win98PaintWorkspace.AddThemeConstantOverride("separation", 2);
        editorBody.AddChild(_win98PaintWorkspace);

        BuildCharacterColumn(library);
        BuildToolColumn();
        BuildViewportAndColorBar(preview);
        MoveEditorActionsIntoWorkspace();

        _paintCanvas.ViewChanged += SyncPaintScrollbars;
        _win98PaintLayoutComposed = true;
        SyncPaintScrollbars();
    }

    private void BuildCharacterColumn(VBoxContainer library)
    {
        var frame = RaisedPanel("Win98CharacterColumn");
        frame.CustomMinimumSize = new Vector2(190, 0);
        frame.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        _win98PaintWorkspace.AddChild(frame);

        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 2);
        frame.AddChild(column);
        column.AddChild(new Label
        {
            Text = "Characters",
            HorizontalAlignment = HorizontalAlignment.Center,
        });

        library.Reparent(column, false);
        library.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        library.CustomMinimumSize = new Vector2(180, 150);

        NewButton.Reparent(column, false);
        NewButton.Text = "+";
        NewButton.TooltipText = "Add a new character.";
        NewButton.CustomMinimumSize = new Vector2(0, 38);

        column.AddChild(new HSeparator());
        column.AddChild(new Label { Text = "Customizable items" });
        var items = new ItemList
        {
            Name = "FutureCustomizationItemList",
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0, 130),
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        items.AddItem("No items equipped");
        items.SetItemDisabled(0, true);
        items.SetItemTooltip(0, "Hats and other customization items will appear here.");
        column.AddChild(items);

        var manage = new HBoxContainer();
        manage.AddThemeConstantOverride("separation", 2);
        column.AddChild(manage);
        DuplicateButton.Reparent(manage, false);
        DeleteButton.Reparent(manage, false);
        RandomizeButton.Reparent(manage, false);
    }

    private void BuildToolColumn()
    {
        var frame = RaisedPanel("Win98PaintToolColumn");
        frame.CustomMinimumSize = new Vector2(124, 0);
        frame.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        _win98PaintWorkspace.AddChild(frame);

        var scroll = new ScrollContainer
        {
            Name = "Win98PaintToolScroll",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        frame.AddChild(scroll);
        _paintControls.Reparent(scroll, false);
        _paintControls.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _paintControls.AddThemeConstantOverride("separation", 3);

        Control? toolRow = FindChild("PaintToolRow", true, false) as Control;
        Button? brush = FindChild("PaintBrushButton", true, false) as Button;
        Button? eraser = FindChild("PaintEraserButton", true, false) as Button;
        if (GodotObject.IsInstanceValid(brush) && GodotObject.IsInstanceValid(eraser))
        {
            var tools = new GridContainer
            {
                Name = "Win98ToolPicker",
                Columns = 2,
                CustomMinimumSize = new Vector2(108, 68),
            };
            tools.AddThemeConstantOverride("h_separation", 2);
            tools.AddThemeConstantOverride("v_separation", 2);
            _paintControls.AddChild(tools);
            _paintControls.MoveChild(tools, 0);
            brush!.Reparent(tools, false);
            eraser!.Reparent(tools, false);
            brush.CustomMinimumSize = new Vector2(52, 32);
            eraser.CustomMinimumSize = new Vector2(52, 32);
        }
        if (GodotObject.IsInstanceValid(toolRow))
            toolRow!.Visible = false;

        var rotateRow = new HBoxContainer { Name = "PaintRotateRow" };
        rotateRow.AddThemeConstantOverride("separation", 2);
        Button rotateLeft = EditorButton("↶", "Rotate buddy 90° left.");
        Button rotateRight = EditorButton("↷", "Rotate buddy 90° right.");
        rotateLeft.Pressed += () => RotatePaintPreview(-1);
        rotateRight.Pressed += () => RotatePaintPreview(1);
        rotateRow.AddChild(rotateLeft);
        rotateRow.AddChild(rotateRight);
        _paintControls.AddChild(rotateRow);

        if (FindChild("PaintHistoryRow", true, false) is Control history)
            _paintControls.MoveChild(rotateRow, history.GetIndex());

        foreach (string name in new[] { "PaintBrushSizeRow", "PaintHistoryRow", "PaintViewRow" })
        {
            if (FindChild(name, true, false) is Control row)
            {
                row.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
                foreach (Node child in row.GetChildren())
                {
                    if (child is Button button)
                        button.CustomMinimumSize = new Vector2(30, 26);
                }
            }
        }
    }

    private void BuildViewportAndColorBar(SubViewportContainer preview)
    {
        var rightColumn = new VBoxContainer
        {
            Name = "Win98PaintViewportColumn",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        rightColumn.AddThemeConstantOverride("separation", 2);
        _win98PaintWorkspace.AddChild(rightColumn);

        var viewportGrid = new GridContainer
        {
            Name = "Win98PaintViewportGrid",
            Columns = 2,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        viewportGrid.AddThemeConstantOverride("h_separation", 0);
        viewportGrid.AddThemeConstantOverride("v_separation", 0);
        rightColumn.AddChild(viewportGrid);

        var viewportFrame = RecessedPanel("Win98PaintViewportFrame");
        viewportFrame.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        viewportFrame.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        viewportFrame.CustomMinimumSize = new Vector2(420, 340);
        viewportGrid.AddChild(viewportFrame);

        preview.Reparent(viewportFrame, false);
        preview.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        preview.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        preview.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        preview.CustomMinimumSize = new Vector2(420, 340);

        _paintVerticalScroll = new VScrollBar
        {
            Name = "PaintVerticalViewportScroll",
            CustomMinimumSize = new Vector2(18, 0),
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            Step = 0.01,
            Page = 0.1,
        };
        _paintVerticalScroll.ValueChanged += _ => ApplyPaintScrollbarPan();
        viewportGrid.AddChild(_paintVerticalScroll);

        _paintHorizontalScroll = new HScrollBar
        {
            Name = "PaintHorizontalViewportScroll",
            CustomMinimumSize = new Vector2(0, 18),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            Step = 0.01,
            Page = 0.1,
        };
        _paintHorizontalScroll.ValueChanged += _ => ApplyPaintScrollbarPan();
        viewportGrid.AddChild(_paintHorizontalScroll);
        viewportGrid.AddChild(new Control { CustomMinimumSize = new Vector2(18, 18) });

        BuildColorFooter(rightColumn);
    }

    private void BuildColorFooter(VBoxContainer rightColumn)
    {
        var footer = RaisedPanel("Win98PaintColorFooter");
        footer.CustomMinimumSize = new Vector2(0, 62);
        rightColumn.AddChild(footer);

        var row = new HBoxContainer
        {
            Alignment = BoxContainer.AlignmentMode.Begin,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        row.AddThemeConstantOverride("separation", 4);
        footer.AddChild(row);

        _currentColorSwatch.Reparent(row, false);
        _currentColorSwatch.CustomMinimumSize = new Vector2(56, 48);

        if (FindChild("PaintPresetPalette", true, false) is Control palette)
        {
            palette.Reparent(row, false);
            palette.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        }

        _paintColorPicker.Reparent(row, false);
        _paintColorPicker.Text = "🎨";
        _paintColorPicker.TooltipText = "Open the full color picker.";
        _paintColorPicker.CustomMinimumSize = new Vector2(46, 48);

        var actions = new VBoxContainer();
        actions.AddThemeConstantOverride("separation", 1);
        row.AddChild(actions);
        var primary = new HBoxContainer();
        primary.AddThemeConstantOverride("separation", 2);
        actions.AddChild(primary);
        SaveButton.Reparent(primary, false);
        UseButton.Reparent(primary, false);
        var secondary = new HBoxContainer();
        secondary.AddThemeConstantOverride("separation", 2);
        actions.AddChild(secondary);
        ResetButton.Reparent(secondary, false);
        CloseButton.Reparent(secondary, false);
    }

    private void MoveEditorActionsIntoWorkspace()
    {
        if (SaveButton.GetParent() is not Control)
            return;

        // Every original bottom-row action has been moved into either character management or
        // the color footer. Hide the now-empty legacy row so the editor matches the mockup.
        if (NewButton.GetParent()?.GetParent() is Control)
        {
            Control? legacyActions = FindLegacyActionRow();
            if (GodotObject.IsInstanceValid(legacyActions))
                legacyActions!.Visible = false;
        }
    }

    private Control? FindLegacyActionRow()
    {
        foreach (Node child in _editorRoot.FindChildren("*", nameof(HBoxContainer), true, false))
        {
            if (child is not HBoxContainer row)
                continue;
            if (row.GetChildCount() == 0)
                return row;
        }
        return null;
    }

    private void ApplyWin98PaintLayout()
    {
        if (!_win98PaintLayoutComposed)
            EnsureWin98PaintLayout();
        if (!_win98PaintLayoutComposed)
            return;

        _win98PaintWorkspace.Visible = true;
        _paintControls.Visible = true;
        _paintCanvas.Visible = true;
        _paintCanvas.MouseFilter = Control.MouseFilterEnum.Stop;
        _paintModeButton.Visible = false;
        SyncPaintScrollbars();
    }

    private void RotatePaintPreview(int delta)
    {
        _paintRotationQuarterTurns = (_paintRotationQuarterTurns + delta) % 4;
        if (_paintRotationQuarterTurns < 0)
            _paintRotationQuarterTurns += 4;
        _preview.RotationDegrees = new Vector3(0, _paintRotationQuarterTurns * 90f, 0);
    }

    private void ApplyPaintScrollbarPan()
    {
        if (_syncingPaintScrollbars ||
            !GodotObject.IsInstanceValid(_paintCanvas) ||
            !GodotObject.IsInstanceValid(_paintHorizontalScroll) ||
            !GodotObject.IsInstanceValid(_paintVerticalScroll))
        {
            return;
        }

        _paintCanvas.SetPanNormalized(
            _paintHorizontalScroll.Value,
            _paintVerticalScroll.Value);
    }

    private void SyncPaintScrollbars()
    {
        if (!GodotObject.IsInstanceValid(_paintCanvas) ||
            !GodotObject.IsInstanceValid(_paintHorizontalScroll) ||
            !GodotObject.IsInstanceValid(_paintVerticalScroll))
        {
            return;
        }

        _syncingPaintScrollbars = true;
        try
        {
            double range = Math.Max(0.0, _paintCanvas.View.Zoom - 1.0);
            ConfigureScroll(_paintHorizontalScroll, range, _paintCanvas.View.Pan.X);
            ConfigureScroll(_paintVerticalScroll, range, _paintCanvas.View.Pan.Y);
        }
        finally
        {
            _syncingPaintScrollbars = false;
        }
    }

    private static void ConfigureScroll(ScrollBar scroll, double range, double value)
    {
        scroll.MinValue = -range;
        scroll.MaxValue = range;
        scroll.Page = range <= 0.0 ? 0.0 : Math.Max(0.05, range * 0.2);
        scroll.Value = Math.Clamp(value, -range, range);
        scroll.MouseFilter = range <= 0.0
            ? Control.MouseFilterEnum.Ignore
            : Control.MouseFilterEnum.Stop;
    }

    private static PanelContainer RaisedPanel(string name)
    {
        var panel = new PanelContainer
        {
            Name = name,
            MouseFilter = Control.MouseFilterEnum.Pass,
        };
        panel.AddThemeStyleboxOverride("panel", Win98ThemeFactory.Raised(Win98ThemeFactory.Face, 2));
        return panel;
    }

    private static PanelContainer RecessedPanel(string name)
    {
        var panel = new PanelContainer
        {
            Name = name,
            MouseFilter = Control.MouseFilterEnum.Pass,
        };
        panel.AddThemeStyleboxOverride("panel", Win98ThemeFactory.Recessed(Win98ThemeFactory.Face, 2));
        return panel;
    }

    private static Button EditorButton(string text, string tooltip)
    {
        return new Button
        {
            Text = text,
            TooltipText = tooltip,
            CustomMinimumSize = new Vector2(52, 30),
            FocusMode = Control.FocusModeEnum.All,
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
    }
}
