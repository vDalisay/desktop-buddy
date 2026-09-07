using Godot;

namespace DesktopBuddy.Platform.Steam;

public partial class GodotSteamWorkshopTransport
{
    /// <summary>
    /// True only when the bridge proved its GodotSteam capability surface and the remaining
    /// failure was Steam client/session initialization. Retrying a permanent binding/configuration
    /// failure would only create an infinite poll loop.
    /// </summary>
    public bool CanRetryInitialization =>
        !IsInitialized &&
        GodotObject.IsInstanceValid(_bridge) &&
        _bridge!.Call("can_retry_initialization").AsBool();
}
