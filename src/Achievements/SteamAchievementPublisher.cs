using System;
using DesktopBuddy.Domain.Achievements;
using DesktopBuddy.Platform.Steam;
using Godot;

namespace DesktopBuddy.Achievements;

/// <summary>
/// Thin platform publisher. Qualification is always local-first; this class merely mirrors the
/// already-qualified set to Steam. Demo runtimes are hard-disabled and therefore cannot unlock a
/// Steam achievement even when they share the same progress.json with a later full-game install.
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
        _identity.RuntimeAppId == SteamAppIdentityResolver.DesktopBuddyBaseAppId &&
        GodotObject.IsInstanceValid(_bridge) &&
        (bool)_bridge!.Call("is_available") &&
        Engine.HasSingleton("Steam");

    /// <summary>
    /// Idempotently mirrors every locally-qualified achievement. Re-sending SetAchievement is
    /// intentional: if Steam was offline or StoreStats failed on an earlier run, the local save is
    /// still authoritative and the next full-game run simply retries the whole small set.
    /// </summary>
    public bool TrySynchronize()
    {
        if (!PublishingEnabled)
            return false;

        GodotObject steam = Engine.GetSingleton("Steam");
        if (!GodotObject.IsInstanceValid(steam) ||
            !steam.HasMethod("setAchievement") ||
            !steam.HasMethod("storeStats"))
        {
            return false;
        }

        try
        {
            if (steam.HasMethod("requestCurrentStats"))
                steam.Call("requestCurrentStats");

            bool any = false;
            foreach (AchievementDefinition definition in AchievementCatalog.Baseline)
            {
                if (!_store.IsQualified(definition.Id))
                    continue;
                steam.Call("setAchievement", definition.SteamApiName);
                any = true;
            }

            if (any)
                steam.Call("storeStats");
            return true;
        }
        catch (Exception)
        {
            // Steam is optional. Qualification remains durable and is retried later/next launch.
            return false;
        }
    }
}
