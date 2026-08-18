using System;
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
    private const double RefreshIntervalSeconds = 0.10;

    private PaintCanvasControl? _canvas;
    private Control? _editorRoot;
    private readonly List<ulong> _wiredInstanceIds = new();
    private readonly List<Control> _focusableControls = new();
    private double _refreshRemaining;

    public override void _Ready()
    {
        // CharacterEditorModeCoordinator pauses gameplay while editing. Deferred controls and
        // visibility changes still need to refresh the focus graph.
        ProcessMode = ProcessModeEnum.Always;
    }

    public override void _Process(double delta)
    {
        _refreshRemaining -= Math.Max(0.0, delta);
        if (_refreshRemaining > 0.0)
            return;
        _refreshRemaining = RefreshIntervalSeconds;

        ResolveRoots();
        if (!GodotObject.IsInstanceValid(_canvas) ||
            !GodotObject.IsInstanceValid(_editorRoot) ||
            !_canvas!.IsVisibleInTree())
        {
            _wiredInstanceIds.Clear();
            _focusableControls.Clear();
            return;
        }

        _focusableControls.Clear();
        Collect(_editorRoot!, _focusableControls);
        if (MatchesCurrentGraph(_focusableControls))
            return;

        WireTraversal(_focusableControls);
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

    private static void Collect(Node node, List<Control> result)
    {
        foreach (Node child in node.GetChildren())
        {
            if (child is Control control)
            {
                bool enabled = control is not BaseButton { Disabled: true };
                if (enabled &&
                    control.FocusMode != Control.FocusModeEnum.None &&
                    control.IsVisibleInTree())
                {
                    result.Add(control);
                }

                // A hidden container makes its entire subtree unreachable even when descendants
                // retain their local Visible flag. Avoid wiring stale controls behind collapsed UI.
                if (!control.IsVisibleInTree())
                    continue;
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