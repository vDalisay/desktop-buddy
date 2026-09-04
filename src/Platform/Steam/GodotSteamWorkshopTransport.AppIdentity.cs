using Godot;

namespace DesktopBuddy.Platform.Steam;

public partial class GodotSteamWorkshopTransport
{
    public bool Initialize(Node bridge, SteamAppIdentity identity) =>
        Initialize(bridge, identity.RuntimeAppId, identity.WorkshopOwnerAppId);

    /// <summary>
    /// Initializes Steam under the running application, then points UGC create/update/browser
    /// operations at the distinct Workshop owner. Runtime and Workshop identity remain separate
    /// fields for the lifetime of the transport; a future demo never changes the meaning of either.
    /// </summary>
    public bool Initialize(Node bridge, uint runtimeAppId, uint workshopOwnerAppId)
    {
        if (runtimeAppId == 0 || workshopOwnerAppId == 0)
        {
            SetUnavailable("Both the Steam runtime AppID and Workshop owner AppID must be configured.");
            return false;
        }

        if (!InitializeSteam(bridge, runtimeAppId))
            return false;

        if (!GodotObject.IsInstanceValid(_bridge) ||
            !_bridge!.Call("configure_workshop_app_id", (long)workshopOwnerAppId).AsBool())
        {
            if (GodotObject.IsInstanceValid(_bridge))
                _bridge!.Call("shutdown");
            SetUnavailable("GodotSteam bridge rejected the Workshop owner AppID.");
            return false;
        }

        _workshopOwnerAppId = workshopOwnerAppId;
        return true;
    }
}
