using System;
using System.Diagnostics;
using DesktopBuddy.Diagnostics;
using Godot;

namespace DesktopBuddy.Platform.Steam;

/// <summary>
/// Result of the optional pre-initialization Steam launch-context check.
/// A warning is always fail-open: local gameplay must remain available even when the optional
/// GodotSteam launch capability or the Windows process probe is unavailable.
/// </summary>
public readonly record struct SteamLaunchGuardResult(
    bool RestartRequested,
    bool Warning,
    string Detail);

/// <summary>
/// Applies Valve's restart-through-Steam recommendation without changing Desktop Buddy's
/// offline-first contract. In release Windows Steam exports, a direct launch is redirected only
/// when the Steam client is already running. If Steam is closed, this class makes no Steamworks
/// call at all and normal local boot continues.
/// </summary>
public static class SteamLaunchGuard
{
    private const string GuardScriptPath = "res://src/Platform/Steam/GodotSteamLaunchGuard.gd";

    public static SteamLaunchGuardResult Evaluate()
    {
        // Editor/debug runs and non-Steam distributions must never acquire a Steam launch policy.
        // The public web build also physically removes src/Platform/Steam, so this call site is
        // compiled out there in addition to this runtime guard.
        if (BuildInfo.IsDebugBuild ||
            DesktopBuddy.Platform.OperatingSystem.IsBrowser() ||
            !OS.HasFeature("steam"))
        {
            return default;
        }

        // Current Steam shipping presets are Windows-only. Preserve fail-open behavior on any
        // future platform until it has an equally reliable client-presence probe.
        if (!System.OperatingSystem.IsWindows())
            return default;

        if (!IsSteamClientRunning(out string processProbeFailure))
        {
            // Expected offline path: do not invoke RestartAppIfNecessary because Valve documents
            // that it may start Steam when the client is closed.
            return string.IsNullOrEmpty(processProbeFailure)
                ? default
                : new SteamLaunchGuardResult(false, true, processProbeFailure);
        }

        SteamAppIdentity identity = SteamAppIdentityResolver.Resolve();
        if (identity.RuntimeAppId == 0)
        {
            return new SteamLaunchGuardResult(
                false,
                true,
                "Steam client is running, but no runtime Steam AppID is configured; continuing local play.");
        }

        Node? guard = null;
        try
        {
            GDScript? script = GD.Load<GDScript>(GuardScriptPath);
            if (script is null)
            {
                return new SteamLaunchGuardResult(
                    false,
                    true,
                    $"Steam launch guard script could not be loaded at {GuardScriptPath}; continuing local play.");
            }

            GodotObject instance = (GodotObject)script.New();
            if (instance is not Node guardNode)
            {
                if (GodotObject.IsInstanceValid(instance)) instance.Free();
                return new SteamLaunchGuardResult(
                    false,
                    true,
                    "Steam launch guard did not instantiate as a Node; continuing local play.");
            }

            guard = guardNode;
            Variant value = guard.Call("request_restart_if_necessary", (long)identity.RuntimeAppId);
            if (value.VariantType != Variant.Type.Dictionary)
            {
                return new SteamLaunchGuardResult(
                    false,
                    true,
                    "Steam launch guard returned an unexpected value; continuing local play.");
            }

            Godot.Collections.Dictionary response = value.AsGodotDictionary();
            bool capabilityAvailable =
                response.TryGetValue("capability_available", out Variant capability) && capability.AsBool();
            bool restartRequested =
                response.TryGetValue("restart_requested", out Variant restart) && restart.AsBool();
            string reason = response.TryGetValue("reason", out Variant detail)
                ? detail.AsString()
                : string.Empty;

            if (!capabilityAvailable)
            {
                return new SteamLaunchGuardResult(
                    false,
                    true,
                    string.IsNullOrWhiteSpace(reason)
                        ? "GodotSteam restart capability is unavailable; continuing local play."
                        : reason);
            }

            return new SteamLaunchGuardResult(restartRequested, false, reason);
        }
        catch (Exception exception)
        {
            return new SteamLaunchGuardResult(
                false,
                true,
                $"Steam launch-context check failed open: {exception.Message}");
        }
        finally
        {
            if (GodotObject.IsInstanceValid(guard)) guard!.Free();
        }
    }

    private static bool IsSteamClientRunning(out string failure)
    {
        failure = string.Empty;
        Process[] processes = [];
        try
        {
            processes = Process.GetProcessesByName("steam");
            return processes.Length > 0;
        }
        catch (Exception exception)
        {
            failure = $"Could not determine whether the Steam client is running; preserving local play: {exception.Message}";
            return false;
        }
        finally
        {
            foreach (Process process in processes)
                process.Dispose();
        }
    }
}
