using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DesktopBuddy.App;
using DesktopBuddy.Content;
using DesktopBuddy.Domain.Content;
using DesktopBuddy.Domain.Persistence;
using DesktopBuddy.Domain.Tools;
using DesktopBuddy.Economy;
using DesktopBuddy.Shop;
using Godot;

namespace DesktopBuddy.Testing;

/// <summary>
/// The unified player catalogue over the real sandbox composition: every released selectable
/// tool appears once, unowned tools buy at the authored price, and owned tools equip through
/// the pipeline's single selection seam.
/// </summary>
public sealed class ShopPanelScenario : IScenario
{
    public string Id => "shop_panel_purchase";

    public async Task<ScenarioResult> RunAsync(SceneTree tree, ulong seed)
    {
        var checks = new List<StartupCheck>();
        var loaded = await M4LifecycleScenarioSupport.Load(tree, new ManualMonotonicTimeSource());
        if (loaded is null)
        {
            return new ScenarioResult(
                false,
                [new StartupCheck("sandbox_loadable", false, "sandbox")],
                [$"seed={seed}"]);
        }

        SandboxRoot sandbox = loaded.Value.Sandbox;
        BuddyProgressState progress = sandbox.Progress;
        EconomyService economy = sandbox.Economy;
        ToolCatalogue catalogue = CatalogueLoader.Catalogue;
        IReadOnlyList<CatalogueEntry> offered = CataloguePolicy.SelectableEntries(catalogue);

        var shop = new ShopPanel();
        tree.Root.AddChild(shop);
        shop.Configure(progress, economy, catalogue, sandbox.Pipeline);
        await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);

        try
        {
            var rows = new List<Button>();
            foreach (CatalogueEntry entry in offered)
            {
                if (shop.BuyButtonFor(entry.ContentId) is Button button)
                    rows.Add(button);
            }

            bool listMatchesPolicy = rows.Count == offered.Count &&
                shop.OfferedContentIds.SequenceEqual(offered.Select(static e => e.ContentId)) &&
                offered.All(static e => e.Visible && e.IsSelectable);
            checks.Add(new StartupCheck("catalogue_lists_all_selectable_entries", listMatchesPolicy,
                $"rows={rows.Count} offered={offered.Count} catalogue={catalogue.Count}"));

            CatalogueEntry grab = offered.First(static e => e.ContentId == ContentIds.ToolGrab);
            Button grabAction = shop.BuyButtonFor(grab.ContentId)!;
            bool startingToolIsOwned = progress.SelectedTool == ToolId.Grab &&
                grabAction is { Disabled: true, Text: "Equipped" };
            checks.Add(new StartupCheck("catalogue_includes_starting_tools_as_owned", startingToolIsOwned,
                $"selected={progress.SelectedTool} grab={grabAction.Text}/{grabAction.Disabled}"));

            IReadOnlyList<CatalogueEntry> purchasable = offered
                .Where(static e => !e.IsStarting)
                .ToArray();
            bool brokeDisablesUnownedBuys = progress.BalanceMilliCredits == 0 &&
                purchasable.All(entry => shop.BuyButtonFor(entry.ContentId) is { Disabled: true, Text: "Buy" });
            checks.Add(new StartupCheck("catalogue_refuses_purchases_while_broke", brokeDisablesUnownedBuys,
                $"balance={progress.BalanceMilliCredits}"));

            CatalogueEntry cheapest = purchasable
                .OrderBy(static e => e.PriceMilliCredits)
                .First();
            ContentIds.TryParseTool(cheapest.ContentId, out ToolId boughtTool);

            economy.DepositPassive(cheapest.PriceMilliCredits);
            shop.Refresh();
            long balanceBefore = progress.BalanceMilliCredits;

            Button action = shop.BuyButtonFor(cheapest.ContentId)!;
            bool becameBuyable = !action.Disabled && action.Text == "Buy";
            action.EmitSignal(BaseButton.SignalName.Pressed);
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);

            bool bought = becameBuyable &&
                progress.IsToolUnlocked(cheapest.ContentId) &&
                progress.BalanceMilliCredits == balanceBefore - cheapest.PriceMilliCredits &&
                shop.PurchaseCount == 1 &&
                action is { Disabled: false, Text: "Equip" };
            checks.Add(new StartupCheck("catalogue_purchase_charges_authored_price_once", bought,
                $"id={cheapest.ContentId} price={cheapest.PriceMilliCredits} " +
                $"balance={balanceBefore}->{progress.BalanceMilliCredits} action={action.Text}"));

            long afterPurchase = progress.BalanceMilliCredits;
            action.EmitSignal(BaseButton.SignalName.Pressed);
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);

            bool equipped = progress.BalanceMilliCredits == afterPurchase &&
                progress.SelectedTool == boughtTool &&
                sandbox.Pipeline.SelectedTool == boughtTool &&
                action is { Disabled: true, Text: "Equipped" } &&
                shop.PurchaseCount == 1 &&
                shop.EquipCount == 1;
            checks.Add(new StartupCheck("catalogue_equips_owned_tool_without_second_charge", equipped,
                $"selected={progress.SelectedTool} purchases={shop.PurchaseCount} " +
                $"equips={shop.EquipCount} balance={progress.BalanceMilliCredits}"));
        }
        finally
        {
            shop.QueueFree();
            await M4LifecycleScenarioSupport.Cleanup(tree, sandbox);
        }

        return new ScenarioResult(
            checks.All(static check => check.Passed),
            checks,
            [$"seed={seed}"]);
    }
}
