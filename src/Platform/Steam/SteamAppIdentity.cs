using Godot;

namespace DesktopBuddy.Platform.Steam;

/// <summary>
/// Steam identity is deliberately split in two. The running application owns Steam initialization,
/// while Workshop items may belong to the base game's consumer AppID. They are identical for the
/// full game today and may differ for the future Steam demo without changing the share format.
/// </summary>
public readonly record struct SteamAppIdentity(uint RuntimeAppId, uint WorkshopOwnerAppId)
{
    public bool IsConfigured => RuntimeAppId != 0 && WorkshopOwnerAppId != 0;
    public bool IsCrossApp => IsConfigured && RuntimeAppId != WorkshopOwnerAppId;
}

public static class SteamAppIdentityResolver
{
    public const uint DesktopBuddyBaseAppId = 5_114_950;

    public const string RuntimeProjectSetting = "steam/initialization/app_id";
    public const string WorkshopOwnerProjectSetting = "desktop_buddy/steam/workshop_owner_app_id";

    public const string RuntimeEnvironmentVariable = "DESKTOP_BUDDY_STEAM_RUNTIME_APP_ID";
    public const string LegacyRuntimeEnvironmentVariable = "DESKTOP_BUDDY_STEAM_APP_ID";
    public const string WorkshopOwnerEnvironmentVariable = "DESKTOP_BUDDY_WORKSHOP_OWNER_APP_ID";

    /// <summary>
    /// Runtime overrides are useful for local/depot validation. The base game's Workshop owner is
    /// public product configuration, not a secret, and intentionally defaults to Desktop Buddy's
    /// canonical AppID so a later demo only has to override its runtime identity.
    /// </summary>
    public static SteamAppIdentity Resolve()
    {
        uint runtime = ReadEnvironment(RuntimeEnvironmentVariable);
        if (runtime == 0)
            runtime = ReadEnvironment(LegacyRuntimeEnvironmentVariable);
        if (runtime == 0)
            runtime = ReadProject(RuntimeProjectSetting);

        uint workshopOwner = ReadEnvironment(WorkshopOwnerEnvironmentVariable);
        if (workshopOwner == 0)
            workshopOwner = ReadProject(WorkshopOwnerProjectSetting);
        if (workshopOwner == 0)
            workshopOwner = DesktopBuddyBaseAppId;

        return new SteamAppIdentity(runtime, workshopOwner);
    }

    private static uint ReadEnvironment(string name)
    {
        string value = OS.GetEnvironment(name);
        return uint.TryParse(value, out uint parsed) && parsed != 0 ? parsed : 0;
    }

    private static uint ReadProject(string setting)
    {
        // GodotSteam 4.22 owns steam/initialization/app_id as a String project setting and
        // migrates older integer-form values at startup. Parse the Variant text so both the
        // current String form and pre-migration integer form remain compatible.
        Variant configured = ProjectSettings.GetSetting(setting, 0);
        string value = configured.ToString();
        return uint.TryParse(value, out uint parsed) && parsed != 0 ? parsed : 0;
    }
}
