using Godot;

namespace DesktopBuddy.Platform.Steam;

public partial class GodotSteamWorkshopTransport
{
    private uint _runtimeAppId;

    /// <summary>The AppID Steam itself was initialized for (base game today, future demo later).</summary>
    public uint RuntimeAppId => _runtimeAppId == 0 ? AppId : _runtimeAppId;

    /// <summary>The consumer AppID that owns Desktop Buddy's Workshop items.</summary>
    public uint WorkshopOwnerAppId => AppId;

    public bool Initialize(Node bridge, SteamAppIdentity identity) =>
        Initialize(bridge, identity.RuntimeAppId, identity.WorkshopOwnerAppId);

    /// <summary>
    /// Initializes Steam under the running application, then points UGC create/update/browser
    /// operations at the Workshop owner. The existing two-argument initializer remains the
    /// same-app compatibility path used when both identities are identical.
    /// </summary>
    public bool Initialize(Node bridge, uint runtimeAppId, uint workshopOwnerAppId)
    {
        if (runtimeAppId == 0 || workshopOwnerAppId == 0)
        {
            SetUnavailable("Both the Steam runtime AppID and Workshop owner AppID must be configured.");
            return false;
        }

        if (!Initialize(bridge, runtimeAppId))
            return false;

        if (!GodotObject.IsInstanceValid(_bridge) ||
            !_bridge!.Call("configure_workshop_app_id", (long)workshopOwnerAppId).AsBool())
        {
            if (GodotObject.IsInstanceValid(_bridge))
                _bridge!.Call("shutdown");
            SetUnavailable("GodotSteam bridge rejected the Workshop owner AppID.");
            return false;
        }

        _runtimeAppId = runtimeAppId;

        // The original adapter intentionally centralizes every UGC API call through _appId.
        // After Steam itself is initialized, that field becomes the UGC consumer/owner identity.
        // Callback correlation therefore also expects the AppID associated with the Workshop item.
        _appId = workshopOwnerAppId;
        return true;
    }
}
