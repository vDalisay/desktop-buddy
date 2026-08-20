using System.Collections.Generic;
using System.Threading.Tasks;
using DesktopBuddy.App;
using DesktopBuddy.Content;
using DesktopBuddy.Domain.Content;
using Godot;

namespace DesktopBuddy.Testing;

/// <summary>
/// Milestone 0 boot smoke scenario (ROADMAP.md / AGENT_VERIFICATION_AND_E2E.md
/// Section 7): launch to sandbox composition, assert startup validation passed,
/// and confirm the sandbox root composed. It is the single scenario CI runs on
/// every push as the headless smoke gate.
/// </summary>
public sealed class BootSmokeScenario : IScenario
{
    public string Id => "boot_smoke";

    private const string PowerGrabProfilePath = "res://data/buddy/power_grab_profile.tres";

    public async Task<ScenarioResult> RunAsync(SceneTree tree, ulong seed)
    {
        // The shipped catalogue is a startup invariant like the physics tick and the layer
        // names: it is validated here so a malformed entry fails the CI smoke gate.
        StartupReport report = StartupValidator.Validate([CatalogueLoader.Definition]);
        var checks = new List<StartupCheck>(report.Checks);
        var messages = new List<string> { $"seed={seed}" };

        ToolCatalogue catalogue = CatalogueLoader.Catalogue;
        int launchToolEntries = 0;
        foreach (CatalogueEntry entry in catalogue.Entries)
            launchToolEntries += ContentIds.IsTool(entry.ContentId) ? 1 : 0;
        bool holdsLaunchSet = launchToolEntries == CataloguePolicy.LaunchContentIds.Count;
        checks.Add(new StartupCheck(
            "catalogue_holds_the_launch_set",
            holdsLaunchSet,
            $"tool_entries={launchToolEntries} expected={CataloguePolicy.LaunchContentIds.Count} " +
            $"total_entries={catalogue.Count}"));

        IReadOnlyList<CatalogueEntry> shop = CataloguePolicy.ShopEntries(catalogue);
        int shopEntries = shop.Count;
        int selectable = CataloguePolicy.SelectableEntries(catalogue).Count;
        checks.Add(new StartupCheck(
            "starting_tools_are_selectable_but_never_sold",
            shopEntries == CataloguePolicy.LaunchContentIds.Count -
                CataloguePolicy.NewSaveUnlockedContentIds.Count &&
            selectable == CataloguePolicy.LaunchContentIds.Count,
            $"shop={shopEntries} selectable={selectable}"));

        // Power Grab replaced the passive upgrade: unlike what it replaced, it is both sold
        // and selectable, and the retired ID is gone from the shipped data entirely.
        checks.Add(new StartupCheck(
            "power_grab_is_sold_and_selectable",
            CataloguePolicy.IsSelectable(catalogue, ContentIds.ToolPowerGrab) &&
            catalogue.TryGet(ContentIds.ToolPowerGrab, out CatalogueEntry powerGrab) &&
            powerGrab is { Visible: true, IsStarting: false } &&
            !catalogue.Contains(ContentIds.UpgradeStrength),
            $"selectable={CataloguePolicy.IsSelectable(catalogue, ContentIds.ToolPowerGrab)}"));

        IReadOnlyList<string> catalogueErrors = CataloguePolicy.ValidateLaunchCatalogue(catalogue);
        checks.Add(new StartupCheck(
            "launch_catalogue_matches_the_progression_schedule",
            catalogueErrors.Count == 0,
            catalogueErrors.Count == 0 ? "ok" : string.Join("; ", catalogueErrors)));

        bool acceptedBatShopVisible = false;
        foreach (CatalogueEntry entry in shop)
        {
            if (entry.ContentId == ContentIds.ToolBaseballBat)
            {
                acceptedBatShopVisible = true;
                break;
            }
        }

        checks.Add(new StartupCheck(
            "accepted_baseball_bat_is_shop_visible",
            acceptedBatShopVisible,
            $"visible={acceptedBatShopVisible} shop={shopEntries}"));

        // 13B-4: every composition root grabs with the same authored Power Grab profile. The
        // scene files are the authority for what each root is wired to, so they are what is
        // compared — a fourth root pointing at its own copy is the drift this catches.
        string[] roots =
        [
            "res://scenes/sandbox.tscn",
            "res://scenes/buddy_lab.tscn",
            "res://scenes/dual_profile_lab.tscn",
        ];
        var missingProfile = new List<string>();
        foreach (string scene in roots)
        {
            if (!Godot.FileAccess.FileExists(scene))
            {
                missingProfile.Add($"{scene} (missing)");
                continue;
            }

            using Godot.FileAccess file = Godot.FileAccess.Open(
                scene, Godot.FileAccess.ModeFlags.Read);
            if (!file.GetAsText().Contains(PowerGrabProfilePath))
                missingProfile.Add(scene);
        }

        checks.Add(new StartupCheck(
            "every_composition_root_shares_one_power_grab_profile",
            missingProfile.Count == 0,
            missingProfile.Count == 0
                ? PowerGrabProfilePath
                : $"not wired to {PowerGrabProfilePath}: {string.Join("; ", missingProfile)}"));

        var packed = GD.Load<PackedScene>("res://scenes/sandbox.tscn");
        bool loaded = packed is not null;
        checks.Add(new StartupCheck("sandbox_scene_loadable", loaded, "res://scenes/sandbox.tscn"));

        bool composed = false;
        if (loaded)
        {
            Node instance = packed!.Instantiate();
            tree.Root.AddChild(instance);

            // Let _Ready run and one process frame elapse so composition settles.
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);

            composed = instance is SandboxRoot && instance.IsInsideTree();
            checks.Add(new StartupCheck("sandbox_composed", composed,
                $"{instance.GetType().Name} insideTree={instance.IsInsideTree()}"));

            // The catalogue is loaded once and shared: the shop a scene sells from is the
            // same object the startup validator just checked, not a second parse of it.
            bool sameCatalogue = instance is SandboxRoot root &&
                ReferenceEquals(root.Economy.Catalogue, catalogue);
            checks.Add(new StartupCheck(
                "the_sandbox_sells_from_the_validated_catalogue",
                sameCatalogue,
                $"shared={sameCatalogue}"));
            composed &= sameCatalogue;

            // The shell applies the opening room layout from its own _Ready, before the root has
            // attached the handlers that mirror it. Anything still holding construction-time
            // defaults clamps the buddy inside a ghost room the size of the old 480x360 window.
            bool roomMirrored = instance is SandboxRoot mirrored &&
                mirrored.Buddy.Recovery.SafeBounds == mirrored.Boundaries.InnerBounds;
            checks.Add(new StartupCheck(
                "containment_mirrors_the_opening_room_layout",
                roomMirrored,
                instance is SandboxRoot probe
                    ? $"safeBounds={probe.Buddy.Recovery.SafeBounds} inner={probe.Boundaries.InnerBounds}"
                    : "sandbox not composed"));
            composed &= roomMirrored;

            instance.QueueFree();
        }
        else
        {
            checks.Add(new StartupCheck("sandbox_composed", false, "scene not loaded"));
        }

        bool passed = report.Ok && holdsLaunchSet && acceptedBatShopVisible && loaded && composed &&
            catalogueErrors.Count == 0 && missingProfile.Count == 0;
        return new ScenarioResult(passed, checks, messages);
    }
}
