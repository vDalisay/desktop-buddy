using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;

namespace DesktopBuddy.CharacterEditor;

public partial class CharacterEditorHost
{
    private HSplitContainer _win98PaintWorkspace = null!;
    private readonly List<Control> _appearanceControls = [];
    private bool _win98PaintLayoutComposed;

    /// <summary>
    /// Product entry point for the Win98 Paint / Character menu. It opens the existing editor,
    /// enters paint immediately, and switches the long appearance form to a dedicated two-pane
    /// workspace: character library on the left, tools beside the rendered buddy canvas.
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
        ApplyWin98PaintLayout(enabled: true);
        _paintCanvas.GrabFocus();
    }

    private void EnsureWin98PaintLayout()
    {
        if (_win98PaintLayoutComposed)
            return;

        if (FindChild("CharacterControlsScroll", true, false) is not ScrollContainer scroll ||
            scroll.GetChildCount() == 0 ||
            scroll.GetChild(0) is not VBoxContainer controls ||
            FindChild("CharacterPreview", true, false) is not SubViewportContainer preview)
        {
            return;
        }

        int insertionIndex = Mathf.Min(preview.GetIndex(), _paintControls.GetIndex());
        _win98PaintWorkspace = new HSplitContainer
        {
            Name = "Win98PaintWorkspace",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(680, 440),
            SplitOffset = 252,
            MouseFilter = Control.MouseFilterEnum.Pass,
        };
        controls.AddChild(_win98PaintWorkspace);
        controls.MoveChild(_win98PaintWorkspace, insertionIndex);

        var toolFrame = new PanelContainer
        {
            Name = "Win98PaintToolPanel",
            CustomMinimumSize = new Vector2(250, 0),
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        _win98PaintWorkspace.AddChild(toolFrame);
        _paintControls.Reparent(toolFrame, keepGlobalTransform: false);
        _paintControls.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _paintControls.SizeFlagsVertical = Control.SizeFlags.ExpandFill;

        var viewportFrame = new PanelContainer
        {
            Name = "Win98PaintViewportFrame",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            MouseFilter = Control.MouseFilterEnum.Pass,
        };
        _win98PaintWorkspace.AddChild(viewportFrame);
        preview.Reparent(viewportFrame, keepGlobalTransform: false);
        preview.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        preview.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        preview.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        preview.CustomMinimumSize = new Vector2(420, 360);

        foreach (Node child in controls.GetChildren())
        {
            if (child is not Control control ||
                control == _paintModeButton ||
                control == _win98PaintWorkspace)
            {
                continue;
            }
            _appearanceControls.Add(control);
        }

        // The existing mode button remains the route back to appearance customization. Its
        // original handler toggles paint first; this deferred handler updates the page layout
        // after that state change has completed.
        _paintModeButton.Pressed += () => Callable.From(
            () => ApplyWin98PaintLayout(IsPaintMode)).CallDeferred();

        _win98PaintLayoutComposed = true;
    }

    private void ApplyWin98PaintLayout(bool enabled)
    {
        if (!_win98PaintLayoutComposed)
            EnsureWin98PaintLayout();
        if (!_win98PaintLayoutComposed)
            return;

        foreach (Control control in _appearanceControls)
        {
            if (GodotObject.IsInstanceValid(control))
                control.Visible = !enabled;
        }

        _win98PaintWorkspace.Visible = true;
        _paintControls.Visible = enabled;
        _paintModeButton.Visible = true;
        _paintModeButton.Text = enabled ? "Appearance" : "Paint";

        if (FindChild("CharacterControlsScroll", true, false) is ScrollContainer scroll)
        {
            scroll.ScrollHorizontal = 0;
            scroll.ScrollVertical = 0;
        }
    }
}
