using System;
using DesktopBuddy.Buddy.Physics;
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
    private CharacterEditorHost? _host;
    private PaintCanvasControl? _canvas;
    private PanelContainer? _panel;
    private Label? _status;
    private OptionButton? _selector;
    private CheckBox? _visibleToggle;
    private bool _wasPaintActive;
    private bool _syncingVisibility;

    // The editor pauses the tree while open, which is exactly when its paint workspace exists.
    public override void _Ready() => ProcessMode = ProcessModeEnum.Always;

    public override void _Process(double delta)
    {
        if (!GodotObject.IsInstanceValid(_host))
        {
            _host = GetTree().Root.FindChild(
                nameof(CharacterEditorHost),
                recursive: true,
                owned: false) as CharacterEditorHost;
        }

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

        bool active = _canvas!.IsVisibleInTree();
        if (GodotObject.IsInstanceValid(_panel))
            _panel!.Visible = active;

        if (_wasPaintActive && !active)
            RestoreAllPartVisibility();
        _wasPaintActive = active;
    }

    public override void _ExitTree() => RestoreAllPartVisibility();

    private void TryCompose()
    {
        // The layer list lives in the character column, in the slot the retired "Customizable
        // items" placeholder used to occupy: layers belong next to the character being painted,
        // and equipment moved out to its own clothing shop.
        if (_canvas!.FindParent("Win98PaintWorkspace") is not HBoxContainer workspace ||
            workspace.FindChild("Win98CharacterColumnBody", recursive: true, owned: false)
                is not VBoxContainer host)
        {
            return;
        }

        if (host.FindChild("Win98PaintLayerPanel", recursive: false, owned: false) is PanelContainer existing)
        {
            _panel = existing;
            return;
        }

        _panel = new PanelContainer
        {
            Name = "Win98PaintLayerPanel",
            CustomMinimumSize = new Vector2(0, 178),
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            MouseFilter = Control.MouseFilterEnum.Pass,
        };
        _panel.AddThemeStyleboxOverride(
            "panel",
            Win98ThemeFactory.Raised(Win98ThemeFactory.Face, 2));
        host.AddChild(_panel);
        // Keep the Duplicate/Delete/Randomize row pinned to the bottom of the column.
        host.MoveChild(_panel, host.GetChildCount() - 2);

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

        _selector = new OptionButton
        {
            Name = "PaintLayerSelector",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            TooltipText = "Limit painting to one visible body-part layer, or paint all visible parts.",
        };
        _selector.AddItem("All body parts", 0);
        int id = 1;
        foreach (PaintPart part in Enum.GetValues<PaintPart>())
            _selector.AddItem(FormatPart(part), id++);
        _selector.ItemSelected += index => SelectLayer(_selector, index);
        column.AddChild(_selector);

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
            _selector.Select((int)index + 1);
            SelectLayer(_selector, (long)index + 1);
        };
        Win98ItemListCheck.Attach(list);
        column.AddChild(list);

        _visibleToggle = new CheckBox
        {
            Name = "PaintLayerVisibleToggle",
            Text = "Show selected layer",
            ButtonPressed = true,
            Disabled = true,
            TooltipText = "Temporarily hide the selected body part in this editor preview.",
        };
        _visibleToggle.Toggled += SetSelectedLayerVisible;
        column.AddChild(_visibleToggle);

        _status = new Label
        {
            Name = "PaintLayerStatus",
            Text = "Painting: all visible body parts",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        column.AddChild(_status);

        var help = new Label
        {
            Text = "Hidden layers cannot receive paint and return when the editor closes.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        help.AddThemeColorOverride("font_color", Win98ThemeFactory.Shadow);
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
                _status!.Text = "Painting: all visible body parts";
            SyncVisibilityToggle(null);
        }
        else
        {
            PaintPart part = (PaintPart)(index - 1);
            _canvas!.ActivePartFilter = part;
            if (GodotObject.IsInstanceValid(_status))
            {
                _status!.Text = _canvas.IsPartVisible(part)
                    ? $"Painting: {FormatPart(part)} only"
                    : $"{FormatPart(part)} is hidden";
            }
            SyncVisibilityToggle(part);
        }

        _canvas.QueueRedraw();
        selector.ReleaseFocus();
    }

    private void SetSelectedLayerVisible(bool visible)
    {
        if (_syncingVisibility ||
            !GodotObject.IsInstanceValid(_canvas) ||
            !GodotObject.IsInstanceValid(_selector) ||
            _selector!.Selected <= 0)
        {
            return;
        }

        PaintPart part = (PaintPart)(_selector.Selected - 1);
        _canvas!.SetPartVisible(part, visible);
        ApplyPreviewVisibility(part, visible);
        if (GodotObject.IsInstanceValid(_status))
        {
            _status!.Text = visible
                ? $"Painting: {FormatPart(part)} only"
                : $"{FormatPart(part)} is hidden";
        }
    }

    private void SyncVisibilityToggle(PaintPart? part)
    {
        if (!GodotObject.IsInstanceValid(_visibleToggle))
            return;

        _syncingVisibility = true;
        try
        {
            _visibleToggle!.Disabled = part is null;
            _visibleToggle.ButtonPressed = part is null || _canvas!.IsPartVisible(part.Value);
        }
        finally
        {
            _syncingVisibility = false;
        }
    }

    private void ApplyPreviewVisibility(PaintPart part, bool visible)
    {
        if (!GodotObject.IsInstanceValid(_host) ||
            !GodotObject.IsInstanceValid(_host!.PreviewRig) ||
            !_host.PreviewRig.IsInitialized)
        {
            return;
        }

        _host.PreviewRig.GetPartSocket(ToBuddyPart(part)).Visible = visible;
    }

    private void RestoreAllPartVisibility()
    {
        if (GodotObject.IsInstanceValid(_canvas))
            _canvas!.ShowAllParts();

        if (GodotObject.IsInstanceValid(_host) &&
            GodotObject.IsInstanceValid(_host!.PreviewRig) &&
            _host.PreviewRig.IsInitialized)
        {
            foreach (PaintPart part in Enum.GetValues<PaintPart>())
                _host.PreviewRig.GetPartSocket(ToBuddyPart(part)).Visible = true;
        }

        if (GodotObject.IsInstanceValid(_selector))
        {
            _selector!.Select(0);
            SelectLayer(_selector, 0);
        }
    }

    private static BuddyPartId ToBuddyPart(PaintPart part) => part switch
    {
        PaintPart.Head => BuddyPartId.Head,
        PaintPart.Torso => BuddyPartId.Torso,
        PaintPart.LeftHand => BuddyPartId.LeftHand,
        PaintPart.RightHand => BuddyPartId.RightHand,
        PaintPart.LeftFoot => BuddyPartId.LeftFoot,
        PaintPart.RightFoot => BuddyPartId.RightFoot,
        _ => throw new ArgumentOutOfRangeException(nameof(part), part, "Unknown paint layer."),
    };

    private static string FormatPart(PaintPart part) => part switch
    {
        PaintPart.LeftHand => "Left hand",
        PaintPart.RightHand => "Right hand",
        PaintPart.LeftFoot => "Left foot",
        PaintPart.RightFoot => "Right foot",
        _ => part.ToString(),
    };
}
