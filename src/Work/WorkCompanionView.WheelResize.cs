using System;
using DesktopBuddy.UI;
using Godot;

namespace DesktopBuddy.Work;

public partial class WorkCompanionView
{
    private const double WheelResizeFactor = 1.025;
    private bool _wheelResizeActive;
    private Vector2 _wheelResizeAnchor;

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
            if (!mouse.Pressed)
            {
                _wheelResizeActive = false;
                return;
            }

            Rect2I rect = _sandbox.Window.WorkCompanionRect;
            _wheelResizeActive = IsDragSurface(ToCompositionPosition(mouse.Position)) &&
                !IsOverControlButton(mouse.Position);
            _wheelResizeAnchor = new Vector2(
                mouse.Position.X / Math.Max(1, rect.Size.X),
                mouse.Position.Y / Math.Max(1, rect.Size.Y));
            return;
        }

        if (!_wheelResizeActive || !mouse.Pressed ||
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
        _sandbox.Window.ResizeWorkCompanion(requested, _wheelResizeAnchor);
        if (_sandbox.Window.WorkCompanionRect.Size != current.Size)
            UiFeedbackAudioBootstrap.TryPlay(this, UiFeedbackCue.Resize);
        GetViewport().SetInputAsHandled();
    }
}
