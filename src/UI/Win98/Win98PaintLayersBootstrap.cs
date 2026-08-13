using System;
using DesktopBuddy.Buddy.Physics;
using DesktopBuddy.CharacterEditor;
using DesktopBuddy.Domain.Painting;
using Godot;

namespace DesktopBuddy.UI.Win98;

/// <summary>Semantic body-part layer selection and editor-only visibility.</summary>
public partial class Win98PaintLayersBootstrap : Node
{
    private const string HelpText =
        "Hidden layers cannot receive paint and return when the editor closes.";

    private CharacterEditorHost? _host;
    private PaintCanvasControl? _canvas;
    private PanelContainer? _panel;
    private ItemList? _list;
    private Label? _status;
    private CheckBox? _visibleToggle;
    private bool _wasPaintActive;
    private bool _syncingVisibility;

    public override void _Ready() => ProcessMode = ProcessModeEnum.Always;

    public override void _Process(double delta)
    {
        _host ??= GetTree().Root.FindChild(nameof(CharacterEditorHost), true, false) as CharacterEditorHost;
        _canvas ??= GetTree().Root.FindChild("CharacterPaintCanvas", true, false) as PaintCanvasControl;
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
        if (_canvas!.FindParent("Win98PaintWorkspace") is not HBoxContainer workspace ||
            workspace.FindChild("Win98CharacterColumnBody", true, false) is not VBoxContainer host)
            return;

        if (host.FindChild("Win98PaintLayerPanel", false, false) is PanelContainer existing)
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
        _panel.AddThemeStyleboxOverride("panel", Win98ThemeFactory.Raised(Win98ThemeFactory.Face, 2));
        host.AddChild(_panel);
        host.MoveChild(_panel, Math.Max(0, host.GetChildCount() - 2));

        var column = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        column.AddThemeConstantOverride("separation", 3);
        _panel.AddChild(column);

        var header = new HBoxContainer
        {
            Name = "PaintLayerHeaderRow",
            Alignment = BoxContainer.AlignmentMode.Center,
        };
        header.AddThemeConstantOverride("separation", 3);
        column.AddChild(header);
        header.AddChild(new Label { Text = "Layers" });
        header.AddChild(new Button
        {
            Name = "PaintLayerHelpButton",
            Text = "?",
            TooltipText = HelpText,
            AccessibilityDescription = HelpText,
            FocusMode = Control.FocusModeEnum.All,
            CustomMinimumSize = new Vector2(22, 22),
        });

        _list = new ItemList
        {
            Name = "PaintLayerList",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            SelectMode = ItemList.SelectModeEnum.Single,
            TooltipText = "Choose which body-part layer receives paint.",
        };
        _list.AddItem("All body parts");
        foreach (PaintPart part in Enum.GetValues<PaintPart>())
            _list.AddItem(FormatPart(part));
        _list.Select(0);
        _list.ItemSelected += SelectLayer;
        _list.ItemActivated += ToggleLayerVisibility;
        Win98ItemListCheck.Attach(_list);
        column.AddChild(_list);

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
    }

    private void SelectLayer(long index)
    {
        if (!GodotObject.IsInstanceValid(_canvas))
            return;

        if (index <= 0)
        {
            _canvas!.ActivePartFilter = null;
            _status!.Text = "Painting: all visible body parts";
            SyncVisibilityToggle(null);
        }
        else
        {
            PaintPart part = (PaintPart)(index - 1);
            _canvas!.ActivePartFilter = part;
            _status!.Text = _canvas.IsPartVisible(part)
                ? $"Painting: {FormatPart(part)} only"
                : $"{FormatPart(part)} is hidden";
            SyncVisibilityToggle(part);
        }

        _canvas.QueueRedraw();
        // Focus lives on the viewport, so releasing it needs the list to still be in the tree:
        // teardown reaches here through _ExitTree → RestoreAllPartVisibility after it has left.
        if (_list?.IsInsideTree() == true)
            _list.ReleaseFocus();
    }

    /// <summary>Double-clicking a layer flips its "Show selected layer" checkbox.</summary>
    private void ToggleLayerVisibility(long index)
    {
        if (index <= 0 || !GodotObject.IsInstanceValid(_visibleToggle))
            return;
        SelectLayer(index);
        _visibleToggle!.ButtonPressed = !_visibleToggle.ButtonPressed;
    }

    private void SetSelectedLayerVisible(bool visible)
    {
        if (_syncingVisibility || !GodotObject.IsInstanceValid(_canvas) ||
            !GodotObject.IsInstanceValid(_list))
            return;

        int selected = GetSelectedIndex();
        if (selected <= 0)
            return;

        PaintPart part = (PaintPart)(selected - 1);
        _canvas!.SetPartVisible(part, visible);
        ApplyPreviewVisibility(part, visible);
        _status!.Text = visible
            ? $"Painting: {FormatPart(part)} only"
            : $"{FormatPart(part)} is hidden";
    }

    private int GetSelectedIndex()
    {
        int[] selected = _list!.GetSelectedItems();
        return selected.Length == 0 ? 0 : selected[0];
    }

    private void SyncVisibilityToggle(PaintPart? part)
    {
        if (!GodotObject.IsInstanceValid(_visibleToggle))
            return;
        _syncingVisibility = true;
        _visibleToggle!.Disabled = part is null;
        _visibleToggle.ButtonPressed = part is null || _canvas!.IsPartVisible(part.Value);
        _syncingVisibility = false;
    }

    private void ApplyPreviewVisibility(PaintPart part, bool visible)
    {
        if (GodotObject.IsInstanceValid(_host?.PreviewRig) && _host!.PreviewRig.IsInitialized)
            _host.PreviewRig.GetPartSocket(ToBuddyPart(part)).Visible = visible;
    }

    private void RestoreAllPartVisibility()
    {
        _canvas?.ShowAllParts();
        if (GodotObject.IsInstanceValid(_host?.PreviewRig) && _host!.PreviewRig.IsInitialized)
        {
            foreach (PaintPart part in Enum.GetValues<PaintPart>())
                _host.PreviewRig.GetPartSocket(ToBuddyPart(part)).Visible = true;
        }
        if (GodotObject.IsInstanceValid(_list))
        {
            _list!.Select(0);
            SelectLayer(0);
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
