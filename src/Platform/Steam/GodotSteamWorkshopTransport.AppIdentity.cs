using Godot;

namespace DesktopBuddy.Platform.Steam;

public partial class GodotSteamWorkshopTransport
{
    public bool Initialize(Node bridge, SteamAppIdentity identity) =>
        Initialize(bridge, identity.RuntimeAppId, identity.WorkshopOwnerAppId);

    /// <summary>
    /// Initializes Steam under the running application. Consumption (subscriptions/downloads) is
    /// always scoped to that runtime AppID. A distinct Workshop owner is retained only as an
    /// explicitly allowed cross-app publish target for the Demo mirror.
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

        // Steam subscriptions/download callbacks belong to the running application. Keep the
        // transport's ordinary Workshop identity on that runtime AppID; targeted publishing can
        // temporarily select the separately-authorized full-game consumer AppID when mirroring.
        if (!GodotObject.IsInstanceValid(_bridge) ||
            !_bridge!.Call("configure_workshop_app_id", (long)runtimeAppId).AsBool())
        {
            if (GodotObject.IsInstanceValid(_bridge))
                _bridge!.Call("shutdown");
            SetUnavailable("GodotSteam bridge rejected the runtime Workshop AppID.");
            return false;
        }

        _workshopOwnerAppId = runtimeAppId;
        ConfigureCrossAppPublishTarget(runtimeAppId, workshopOwnerAppId);
        return true;
    }
}
