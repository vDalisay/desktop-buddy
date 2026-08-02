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

    public async Task<ScenarioResult> RunAsync(SceneTree tree, ulong seed)
    {
        // The shipped catalogue is a startup invariant like the physics tick and the layer
        // names: it is validated here so a malformed entry fails the CI smoke gate.
        StartupReport report = StartupValidator.Validate([CatalogueLoader.Definition]);
        var checks = new List<StartupCheck>(report.Checks);
        var messages = new List<string> { $"seed={seed}" };

        ToolCatalogue catalogue = CatalogueLoader.Catalogue;
        checks.Add(new StartupCheck(
            "catalogue_holds_the_launch_set",
            catalogue.Count == CataloguePolicy.LaunchContentIds.Count,
            $"entries={catalogue.Count} expected={CataloguePolicy.LaunchContentIds.Count}"));

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

            instance.QueueFree();
        }
        else
        {
            checks.Add(new StartupCheck("sandbox_composed", false, "scene not loaded"));
        }

        bool passed = report.Ok && acceptedBatShopVisible && loaded && composed;
        return new ScenarioResult(passed, checks, messages);
    }
}
