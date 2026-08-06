using System.Collections.Generic;
using DesktopBuddy.CharacterEditor;
using Godot;

namespace DesktopBuddy.UI.Win98;

/// <summary>
/// Gives the dynamically composed paint editor one deterministic keyboard traversal loop.
/// The editor is assembled by several deferred bootstraps, so focus neighbours are derived
/// from the final scene-tree order instead of being duplicated in every presenter.
/// </summary>
public partial class Win98PaintFocusBootstrap : Node
{
    private PaintCanvasControl? _canvas;
    private Control? _editorRoot;
    private readonly List<ulong> _wiredInstanceIds = new();

    public override void _Ready()
    {
        // CharacterEditorModeCoordinator pauses gameplay while editing. Deferred controls and
        // visibility changes still need to refresh the focus graph.
        ProcessMode = ProcessModeEnum.Always;
    }

    public override void _Process(double delta)
    {
        ResolveRoots();
        if (!GodotObject.IsInstanceValid(_canvas) ||
            !GodotObject.IsInstanceValid(_editorRoot) ||
            !_canvas!.IsVisibleInTree())
        {
            _wiredInstanceIds.Clear();
            return;
        }

        List<Control> controls = CollectFocusableControls(_editorRoot!);
        if (MatchesCurrentGraph(controls))
            return;

        WireTraversal(controls);
    }

    private void ResolveRoots()
    {
        if (!GodotObject.IsInstanceValid(_canvas))
            _canvas = GetTree().Root.FindChild(
                "CharacterPaintCanvas", recursive: true, owned: false) as PaintCanvasControl;

        if (!GodotObject.IsInstanceValid(_editorRoot))
            _editorRoot = GetTree().Root.FindChild(
                "CharacterEditorPanel", recursive: true, owned: false) as Control;
    }

    private static List<Control> CollectFocusableControls(Control root)
    {
        var result = new List<Control>();
        Collect(root, result);
        return result;
    }

    private static void Collect(Node node, List<Control> result)
    {
        foreach (Node child in node.GetChildren())
        {
            if (child is Control control &&
                control.FocusMode != Control.FocusModeEnum.None &&
                control.Visible &&
                control is not PopupMenu)
            {
                result.Add(control);
            }

            Collect(child, result);
        }
    }

    private bool MatchesCurrentGraph(IReadOnlyList<Control> controls)
    {
        if (controls.Count != _wiredInstanceIds.Count)
            return false;

        for (int index = 0; index < controls.Count; index++)
        {
            if (controls[index].GetInstanceId() != _wiredInstanceIds[index])
                return false;
        }

        return true;
    }

    private void WireTraversal(IReadOnlyList<Control> controls)
    {
        _wiredInstanceIds.Clear();
        if (controls.Count == 0)
            return;

        for (int index = 0; index < controls.Count; index++)
        {
            Control current = controls[index];
            Control previous = controls[(index - 1 + controls.Count) % controls.Count];
            Control next = controls[(index + 1) % controls.Count];
            current.FocusPrevious = current.GetPathTo(previous);
            current.FocusNext = current.GetPathTo(next);
            _wiredInstanceIds.Add(current.GetInstanceId());
        }
    }
}
