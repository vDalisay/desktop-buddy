using System;
using DesktopBuddy.UI;
using Godot;

namespace DesktopBuddy.Work;

public partial class WorkCompanionView
{
    private const double WheelResizeFactor = 1.08;
    private bool _wheelResizeLeftHeld;

    /// <summary>
    /// User-testing resize gesture: while LMB is held anywhere on the companion, wheel up
    /// grows the Work window and wheel down shrinks it. The existing native corner-resize
    /// button remains available as a second path. ResizeWorkCompanion routes through the
    /// controller's recovery/minimum-size policy, and the coordinator observes the resulting
    /// ClientBoundsChanged event to persist the new geometry.
    /// </summary>
    public override void _Input(InputEvent input)
    {
        if (!Visible || !GodotObject.IsInstanceValid(_root) || input is not InputEventMouseButton mouse)
            return;

        if (mouse.ButtonIndex == MouseButton.Left)
        {
            _wheelResizeLeftHeld = mouse.Pressed;
            return;
        }

        if (!_wheelResizeLeftHeld || !mouse.Pressed ||
            mouse.ButtonIndex is not (MouseButton.WheelUp or MouseButton.WheelDown))
        {
            return;
        }

        Rect2I current = _sandbox.Window.WorkCompanionRect;
        double factor = mouse.ButtonIndex == MouseButton.WheelUp
            ? WheelResizeFactor
            : 1.0 / WheelResizeFactor;
        var requested = new Vector2I(
            Math.Max(1, Mathf.RoundToInt((float)(current.Size.X * factor))),
            Math.Max(1, Mathf.RoundToInt((float)(current.Size.Y * factor))));
        _sandbox.Window.ResizeWorkCompanion(requested);
        if (_sandbox.Window.WorkCompanionRect.Size != current.Size)
            UiFeedbackAudioBootstrap.TryPlay(this, UiFeedbackCue.Resize);
        GetViewport().SetInputAsHandled();
    }
}
