using System;
using DesktopBuddy.CharacterEditor;
using DesktopBuddy.Domain.Painting;
using Godot;

namespace DesktopBuddy.UI.Win98;

/// <summary>
/// Adds a compact semantic layer inspector to the Win98 paint workspace. The current paint
/// architecture stores one surface per trusted body part, so these are safe body-part layers
/// rather than arbitrary user-created image layers.
/// </summary>
public partial class Win98PaintLayersBootstrap : Node
{
    private PaintCanvasControl? _canvas;
    private PanelContainer? _panel;
    private Label? _status;

    public override void _Process(double delta)
    {
        if (!GodotObject.IsInstanceValid(_canvas))
        {
            _canvas = GetTree().Root.FindChild(
                "CharacterPaintCanvas",
                recursive: true,
                owned: false) as PaintCanvasControl;
        }

        if (!GodotObject.IsInstanceValid(_canvas))
            return;

        if (!GodotObject.IsInstanceValid(_panel))
            TryCompose();

        if (GodotObject.IsInstanceValid(_panel))
            _panel!.Visible = _canvas!.IsVisibleInTree();
    }

    private void TryCompose()
    {
        if (_canvas!.FindParent("Win98PaintWorkspace") is not HBoxContainer workspace)
            return;

        if (workspace.FindChild("Win98PaintLayerPanel", recursive: false, owned: false) is PanelContainer existing)
        {
            _panel = existing;
            return;
        }

        _panel = new PanelContainer
        {
            Name = "Win98PaintLayerPanel",
            CustomMinimumSize = new Vector2(150, 0),
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            MouseFilter = Control.MouseFilterEnum.Pass,
        };
        _panel.AddThemeStyleboxOverride(
            "panel",
            Win98ThemeFactory.Raised(Win98ThemeFactory.Face, 2));
        workspace.AddChild(_panel);

        var column = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        column.AddThemeConstantOverride("separation", 3);
        _panel.AddChild(column);

        column.AddChild(new Label
        {
            Text = "Layers",
            HorizontalAlignment = HorizontalAlignment.Center,
        });

        var selector = new OptionButton
        {
            Name = "PaintLayerSelector",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            TooltipText = "Limit painting to one body-part layer, or paint all visible parts.",
        };
        selector.AddItem("All body parts", 0);
        int id = 1;
        foreach (PaintPart part in Enum.GetValues<PaintPart>())
            selector.AddItem(FormatPart(part), id++);
        selector.ItemSelected += index => SelectLayer(selector, index);
        column.AddChild(selector);

        var list = new ItemList
        {
            Name = "PaintLayerList",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            SelectMode = ItemList.SelectModeEnum.Single,
            TooltipText = "The current paint document contains one persistent surface per body part.",
        };
        list.AddItem("Head");
        list.AddItem("Torso");
        list.AddItem("Left hand");
        list.AddItem("Right hand");
        list.AddItem("Left foot");
        list.AddItem("Right foot");
        list.ItemSelected += index =>
        {
            selector.Select((int)index + 1);
            SelectLayer(selector, (long)index + 1);
        };
        column.AddChild(list);

        _status = new Label
        {
            Name = "PaintLayerStatus",
            Text = "Painting: all body parts",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        column.AddChild(_status);

        var help = new Label
        {
            Text = "Select a layer to prevent strokes from touching overlapping body parts.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        help.AddThemeColorOverride("font_color", Win98ThemeFactory.DisabledText);
        column.AddChild(help);
    }

    private void SelectLayer(OptionButton selector, long index)
    {
        if (!GodotObject.IsInstanceValid(_canvas))
            return;

        if (index <= 0)
        {
            _canvas!.ActivePartFilter = null;
            if (GodotObject.IsInstanceValid(_status))
                _status!.Text = "Painting: all body parts";
        }
        else
        {
            PaintPart part = (PaintPart)(index - 1);
            _canvas!.ActivePartFilter = part;
            if (GodotObject.IsInstanceValid(_status))
                _status!.Text = $"Painting: {FormatPart(part)} only";
        }

        _canvas.QueueRedraw();
        selector.ReleaseFocus();
    }

    private static string FormatPart(PaintPart part) => part switch
    {
        PaintPart.LeftHand => "Left hand",
        PaintPart.RightHand => "Right hand",
        PaintPart.LeftFoot => "Left foot",
        PaintPart.RightFoot => "Right foot",
        _ => part.ToString(),
    };
}
