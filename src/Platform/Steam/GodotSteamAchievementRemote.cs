using System;
using DesktopBuddy.App;
using DesktopBuddy.Domain.Achievements;
using Godot;

namespace DesktopBuddy.Platform.Steam;

/// <summary>
/// GodotSteam adapter for the domain achievement publishing port. It owns only platform/build
/// gating and bridge calls; qualification, retry policy, and reconciliation remain platform-free.
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
                return OS.HasFeature("steam") &&
                       DemoScope.IsFullRelease &&
                       _identity.RuntimeAppId == SteamAppIdentityResolver.DesktopBuddyBaseAppId &&
                       GodotObject.IsInstanceValid(_bridge) &&
                       _bridge!.Call("is_available").AsBool() &&
                       _bridge.Call("has_achievement_capabilities").AsBool();
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
