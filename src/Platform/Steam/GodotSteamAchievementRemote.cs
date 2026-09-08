using System;
using DesktopBuddy.App;
using DesktopBuddy.Domain.Achievements;
using DesktopBuddy.Domain.Platform;
using Godot;

namespace DesktopBuddy.Platform.Steam;

/// <summary>
/// GodotSteam adapter for the platform-free achievement publishing port. It never initializes
/// Steam: the injected bridge must already be the Workshop-owned live session. Local qualification
/// remains valid when this adapter is unavailable.
/// </summary>
public sealed class GodotSteamAchievementRemote : IAchievementRemote
{
    private readonly SteamAppIdentity _identity;
    private readonly Node? _bridge;

    public GodotSteamAchievementRemote(SteamAppIdentity identity, Node? initializedBridge)
    {
        _identity = identity;
        _bridge = initializedBridge;
    }

    public bool IsAvailable
    {
        get
        {
            try
            {
                if (!OS.HasFeature(BuildFeatureTags.Steam) ||
                    !DemoScope.ActiveBuildScope.PublishesSteamAchievements ||
                    _identity.RuntimeAppId != SteamAppIdentityResolver.DesktopBuddyBaseAppId ||
                    !GodotObject.IsInstanceValid(_bridge))
                {
                    return false;
                }

                if (!_bridge!.Call("is_available").AsBool() ||
                    !_bridge.Call("has_achievement_capabilities").AsBool())
                {
                    return false;
                }

                long bridgeAppId = _bridge.Call("app_id").AsInt64();
                return bridgeAppId == SteamAppIdentityResolver.DesktopBuddyBaseAppId;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }

    public bool TrySetAchievement(string apiName)
    {
        if (!IsAvailable || string.IsNullOrWhiteSpace(apiName))
            return false;

        try
        {
            return _bridge!.Call("set_achievement", apiName).AsBool();
        }
        catch (Exception)
        {
            return false;
        }
    }

    public bool TryFlush()
    {
        if (!IsAvailable)
            return false;

        try
        {
            return _bridge!.Call("store_stats").AsBool();
        }
        catch (Exception)
        {
            return false;
        }
    }
}
