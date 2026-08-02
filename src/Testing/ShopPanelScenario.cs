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
/// The dock shop and tool picker over the real sandbox composition: the shop offers exactly
/// the gate-passed entries and spends the authored price once, and the picker equips only what
/// is owned, through the pipeline's single selection seam.
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
        IReadOnlyList<CatalogueEntry> offered = CataloguePolicy.ShopEntries(catalogue);

        var shop = new ShopPanel();
        tree.Root.AddChild(shop);
        shop.Configure(progress, economy, catalogue);
        var tools = new ToolSelectionPanel();
        tree.Root.AddChild(tools);
        tools.Configure(progress, sandbox.Pipeline, catalogue);
        // The same wiring the dock host installs: a purchase in one window immediately makes
        // the tool selectable in the other, without waiting for a reopen.
        shop.Purchased += tools.Refresh;
        await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);

        try
        {
            // Only gate-passed, non-starting entries are on sale (the owner's "no unfinished
            // shop entry is shown" rule), and each carries a spendable price.
            var rows = new List<Button>();
            foreach (CatalogueEntry entry in offered)
            {
                if (shop.BuyButtonFor(entry.ContentId) is Button button)
                    rows.Add(button);
            }

            bool listMatchesPolicy = rows.Count == offered.Count &&
                shop.OfferedContentIds.SequenceEqual(offered.Select(static e => e.ContentId)) &&
                offered.All(static e => e.Visible && !e.IsStarting && e.HasValidPrice);
            checks.Add(new StartupCheck("shop_lists_only_gate_passed_entries", listMatchesPolicy,
                $"rows={rows.Count} offered={offered.Count} catalogue={catalogue.Count}"));

            bool brokeDisablesEveryBuy = progress.BalanceMilliCredits == 0 &&
                rows.All(static button => button.Disabled);
            checks.Add(new StartupCheck("shop_refuses_purchases_while_broke", brokeDisablesEveryBuy,
                $"balance={progress.BalanceMilliCredits}"));

            CatalogueEntry cheapest = offered
                .Where(static e => ContentIds.TryParseTool(e.ContentId, out _))
                .OrderBy(static e => e.PriceMilliCredits)
                .First();
            ContentIds.TryParseTool(cheapest.ContentId, out ToolId boughtTool);

            // An unowned tool is listed with its price and refuses to equip.
            Button? unownedSelect = tools.SelectButtonFor(cheapest.ContentId);
            bool unownedIsBlocked = unownedSelect is { Disabled: true } &&
                !progress.IsToolUnlocked(cheapest.ContentId);
            checks.Add(new StartupCheck("tools_cannot_equip_an_unowned_tool", unownedIsBlocked,
                $"id={cheapest.ContentId} disabled={unownedSelect?.Disabled}"));

            economy.DepositPassive(cheapest.PriceMilliCredits);
            shop.Refresh();
            long balanceBefore = progress.BalanceMilliCredits;

            Button buy = shop.BuyButtonFor(cheapest.ContentId)!;
            buy.EmitSignal(BaseButton.SignalName.Pressed);
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);

            bool bought = progress.IsToolUnlocked(cheapest.ContentId) &&
                progress.BalanceMilliCredits == balanceBefore - cheapest.PriceMilliCredits &&
                shop.PurchaseCount == 1;
            checks.Add(new StartupCheck("shop_purchase_charges_the_authored_price", bought,
                $"id={cheapest.ContentId} price={cheapest.PriceMilliCredits} " +
                $"balance={balanceBefore}->{progress.BalanceMilliCredits}"));

            long afterPurchase = progress.BalanceMilliCredits;
            buy.EmitSignal(BaseButton.SignalName.Pressed);
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            bool ownedIsInert = buy is { Disabled: true, Text: "Owned" } &&
                progress.BalanceMilliCredits == afterPurchase &&
                shop.PurchaseCount == 1;
            checks.Add(new StartupCheck("shop_never_charges_twice", ownedIsInert,
                $"disabled={buy.Disabled} text={buy.Text} balance={progress.BalanceMilliCredits}"));

            // The purchase reached the picker (the shop raises Purchased), and equipping
            // routes through the pipeline rather than writing progress directly.
            Button equip = tools.SelectButtonFor(cheapest.ContentId)!;
            bool becameSelectable = !equip.Disabled;
            equip.EmitSignal(BaseButton.SignalName.Pressed);
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            bool equipped = becameSelectable &&
                progress.SelectedTool == boughtTool &&
                sandbox.Pipeline.SelectedTool == boughtTool &&
                equip.Text == "Equipped" &&
                tools.SelectionCount == 1;
            checks.Add(new StartupCheck("tools_equip_a_bought_tool", equipped,
                $"selectable={becameSelectable} selected={progress.SelectedTool} " +
                $"picks={tools.SelectionCount}"));
        }
        finally
        {
            shop.QueueFree();
            tools.QueueFree();
            await M4LifecycleScenarioSupport.Cleanup(tree, sandbox);
        }

        return new ScenarioResult(
            checks.All(static check => check.Passed),
            checks,
            [$"seed={seed}"]);
    }
}
