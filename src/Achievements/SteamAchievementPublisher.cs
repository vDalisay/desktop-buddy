using System;
using DesktopBuddy.App;
using DesktopBuddy.Domain.Achievements;
using DesktopBuddy.Platform.Steam;
using Godot;

namespace DesktopBuddy.Achievements;

/// <summary>
/// Thin platform publisher. Qualification is always local-first; this class merely mirrors the
/// already-qualified set to Steam through the project-owned GodotSteam bridge. Only the shipped
/// Steam full-release scope may publish: the Demo and editor/development runtimes remain local-only
/// even if they happen to be configured with the base AppID.
/// </summary>
public sealed class SteamAchievementPublisher
{
    private readonly AchievementProgressStore _store;
    private readonly SteamAppIdentity _identity;
    private readonly Node? _bridge;

    public SteamAchievementPublisher(
        AchievementProgressStore store,
        SteamAppIdentity identity,
        Node? initializedBridge)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _identity = identity;
        _bridge = initializedBridge;
    }

    public bool PublishingEnabled =>
        OS.HasFeature("steam") &&
        DemoScope.IsFullRelease &&
        _identity.RuntimeAppId == SteamAppIdentityResolver.DesktopBuddyBaseAppId &&
        GodotObject.IsInstanceValid(_bridge) &&
        _bridge!.Call("is_available").AsBool() &&
        _bridge.Call("has_achievement_capabilities").AsBool();

    /// <summary>
    /// Idempotently mirrors every locally-qualified achievement. Re-sending SetAchievement is
    /// intentional: if Steam was offline or StoreStats failed on an earlier run, the local save is
    /// still authoritative and a later retry sends the small set again. Steamworks SDK 1.61 removed
    /// RequestCurrentStats because the Steam client now synchronizes stats/achievements before game
    /// start. GodotSteam itself stays behind the dynamic bridge so optional-addon and ClassDB
    /// fallback behavior remains identical to the Workshop integration.
    /// </summary>
    public bool TrySynchronize()
    {
        if (!PublishingEnabled)
            return false;

        try
        {
            bool anyQualified = false;
            bool anySet = false;
            foreach (AchievementDefinition definition in AchievementCatalog.Baseline)
            {
                if (!_store.IsQualified(definition.Id))
                    continue;

                anyQualified = true;
                anySet |= _bridge!.Call("set_achievement", definition.SteamApiName).AsBool();
            }

            if (!anyQualified)
                return true;
            if (!anySet)
                return false;

            return _bridge!.Call("store_stats").AsBool();
        }
        catch (Exception)
        {
            // Steam is optional. Qualification remains durable and is retried later/next launch.
            return false;
        }
    }
}
