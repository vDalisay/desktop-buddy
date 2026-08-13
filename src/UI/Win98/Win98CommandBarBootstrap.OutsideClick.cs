using Godot;

namespace DesktopBuddy.UI.Win98;

public partial class Win98CommandBarBootstrap
{
    /// <summary>
    /// Native menu popups already dismiss themselves on an outside click. The compact
    /// Shop/Tools/Settings flyout is ordinary Control content, so give it the same menu
    /// behavior without stealing clicks from the play surface beneath it.
    /// </summary>
    public override void _Input(InputEvent input)
    {
        if (!_composed || _activeSection is null ||
            input is not InputEventMouseButton { Pressed: true } mouse ||
            !GodotObject.IsInstanceValid(_bar) || !GodotObject.IsInstanceValid(_flyout))
        {
            return;
        }

        Vector2 point = mouse.Position;
        if (_bar.GetGlobalRect().HasPoint(point) || _flyout.GetGlobalRect().HasPoint(point))
            return;

        CloseFlyout();
    }
}
