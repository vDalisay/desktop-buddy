using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using DesktopBuddy.App;
using DesktopBuddy.Platform.Steam;
using Godot;

namespace DesktopBuddy.Testing;

/// <summary>
/// Runs only in the CI lane that materializes the pinned GodotSteam addon. It proves the real
/// GDExtension can be discovered by the project-owned bridge and that its Workshop capability
/// surface still matches the adapter. An unauthenticated GitHub runner is allowed to fail Steam
/// client initialization; missing/incompatible GodotSteam is not allowed.
/// </summary>
public sealed class WorkshopGodotSteamAddonSmokeScenario : IScenario
{
    private const string BridgeScriptPath = "res://src/Platform/Steam/GodotSteamBridge.gd";

    public string Id => "workshop_godotsteam_addon_smoke";

    public Task<ScenarioResult> RunAsync(SceneTree tree, ulong seed)
    {
        var checks = new List<StartupCheck>();
        Node? bridge = null;
        GodotSteamWorkshopTransport? transport = null;

        try
        {
            GDScript? script = GD.Load<GDScript>(BridgeScriptPath);
            bool scriptLoaded = script is not null;
            checks.Add(new StartupCheck(
                "workshop_godotsteam_bridge_loads",
                scriptLoaded,
                scriptLoaded ? BridgeScriptPath : "Bridge script could not be loaded."));
            if (script is null)
                return Task.FromResult(Result(checks, $"seed={seed}"));

            GodotObject instance = (GodotObject)script.New();
            if (instance is not Node bridgeNode)
            {
                checks.Add(new StartupCheck(
                    "workshop_godotsteam_bridge_instantiates",
                    false,
                    "Bridge script did not instantiate as a Node."));
                return Task.FromResult(Result(checks, $"seed={seed}"));
            }

            bridge = bridgeNode;
            bridge.Name = "GodotSteamAddonSmokeBridge";
            tree.Root.AddChild(bridge);
            checks.Add(new StartupCheck(
                "workshop_godotsteam_bridge_instantiates",
                true,
                bridge.Name));

            bool addonPresent = bridge.Call("is_godotsteam_present").AsBool();
            checks.Add(new StartupCheck(
                "workshop_godotsteam_native_api_present",
                addonPresent,
                addonPresent
                    ? "GodotSteam Steam API object is discoverable."
                    : "Pinned GodotSteam addon was materialized but no Steam API object is discoverable."));
            if (!addonPresent)
                return Task.FromResult(Result(checks, $"seed={seed}"));

            bool updateForwarded = false;
            bridge.Connect(
                "workshop_item_updated",
                Callable.From<long, bool, long>((result, needsAgreement, fileId) =>
                    updateForwarded = result == 1 && !needsAgreement && fileId == 99));
            bridge.Call("_on_item_updated", 1L, false, 99L);
            checks.Add(new StartupCheck(
                "workshop_godotsteam_item_updated_callback_shape_matches",
                updateForwarded,
                updateForwarded
                    ? "GodotSteam 4.22 result/legal-agreement/file-ID callback is forwarded."
                    : "Bridge did not forward the three-argument GodotSteam 4.22 item_updated callback."));

            SteamAppIdentity identity = SteamAppIdentityResolver.Resolve();
            bool identityOk = identity.IsConfigured &&
                identity.RuntimeAppId == SteamAppIdentityResolver.DesktopBuddyBaseAppId &&
                identity.WorkshopOwnerAppId == SteamAppIdentityResolver.DesktopBuddyBaseAppId;
            checks.Add(new StartupCheck(
                "workshop_godotsteam_identity_matches_base_app",
                identityOk,
                $"runtime={identity.RuntimeAppId} owner={identity.WorkshopOwnerAppId}"));
            if (!identityOk)
                return Task.FromResult(Result(checks, $"seed={seed}"));

            transport = new GodotSteamWorkshopTransport { Name = "GodotSteamAddonSmokeTransport" };
            tree.Root.AddChild(transport);
            bool initialized = transport.Initialize(bridge, identity);
            string reason = transport.UnavailableReason ?? string.Empty;

            // No Steam client/session exists on the hosted Linux runner. That is a valid runtime
            // failure. Fail only when the bridge/addon contract itself is absent or incompatible.
            bool capabilityCompatible = initialized || !IsBindingFailure(reason);
            checks.Add(new StartupCheck(
                "workshop_godotsteam_422_capabilities_match",
                capabilityCompatible,
                initialized ? "Steam initialized on the runner." : $"Expected offline init result: {reason}"));

            if (initialized)
            {
                bool identitiesKept =
                    transport.RuntimeAppId == identity.RuntimeAppId &&
                    transport.WorkshopOwnerAppId == identity.WorkshopOwnerAppId;
                checks.Add(new StartupCheck(
                    "workshop_godotsteam_transport_keeps_app_identities",
                    identitiesKept,
                    $"runtime={transport.RuntimeAppId} owner={transport.WorkshopOwnerAppId}"));
            }

            return Task.FromResult(Result(checks, $"seed={seed} initialized={initialized}"));
        }
        catch (Exception exception)
        {
            checks.Add(new StartupCheck(
                "workshop_godotsteam_smoke_has_no_unhandled_exception",
                false,
                $"{exception.GetType().Name}: {exception.Message}"));
            return Task.FromResult(Result(checks, $"seed={seed}"));
        }
        finally
        {
            if (GodotObject.IsInstanceValid(transport)) transport!.QueueFree();
            if (GodotObject.IsInstanceValid(bridge)) bridge!.QueueFree();
        }
    }

    private static bool IsBindingFailure(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason)) return true;
        string[] markers =
        [
            "GodotSteam is not installed",
            "Unsupported GodotSteam capability set",
            "GodotSteam is missing the",
            "returned an unexpected value",
            "bridge rejected the Workshop owner AppID",
        ];
        foreach (string marker in markers)
            if (reason.Contains(marker, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    private static ScenarioResult Result(IReadOnlyList<StartupCheck> checks, params string[] messages)
    {
        bool passed = checks.Count > 0;
        foreach (StartupCheck check in checks)
            passed &= check.Passed;
        return new ScenarioResult(passed, checks, messages);
    }
}
