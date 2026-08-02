using System;
using System.Collections.Generic;
using DesktopBuddy.Domain.Content;
using DesktopBuddy.Domain.Economy;
using DesktopBuddy.Domain.Persistence;
using DesktopBuddy.Economy;
using DesktopBuddy.Ui;
using Godot;

namespace DesktopBuddy.Shop;

/// <summary>
/// The player-facing shop (FR-013): one row per offered catalogue entry with its price and a
/// buy action, plus the live balance. It only reads <see cref="CataloguePolicy.ShopEntries"/>,
/// so an entry that has not passed its owner gate stays invisible and unbuyable, and it spends
/// exclusively through <see cref="EconomyService.Purchase"/> — the price comes from the
/// catalogue, never from this UI.
/// </summary>
public partial class ShopPanel : PanelContainer
{
    private readonly List<Row> _rows = [];
    private BuddyProgressState _progress = null!;
    private EconomyService _economy = null!;
    private ToolCatalogue _catalogue = null!;
    private Label _balance = null!;
    private Label _status = null!;

    /// <summary>Raised when a purchase changes ownership, so other panels can refresh.</summary>
    public event Action? Purchased;

    public bool IsInitialized { get; private set; }
    public int PurchaseCount { get; private set; }

    public void Configure(
        BuddyProgressState progress,
        EconomyService economy,
        ToolCatalogue catalogue)
    {
        _progress = progress ?? throw new ArgumentNullException(nameof(progress));
        _economy = economy ?? throw new ArgumentNullException(nameof(economy));
        _catalogue = catalogue ?? throw new ArgumentNullException(nameof(catalogue));

        Name = "ShopPanel";
        PanelChrome.Parts parts = PanelChrome.Build(this, "Shop", "ShopItemList");
        _balance = parts.HeaderValue;
        _status = parts.Status;
        foreach (CatalogueEntry entry in CataloguePolicy.ShopEntries(_catalogue))
            _rows.Add(BuildRow(parts.List, entry));

        IsInitialized = true;
        Refresh();
    }

    private Row BuildRow(VBoxContainer list, CatalogueEntry entry)
    {
        var buy = new Button { Text = "Buy" };
        buy.Pressed += () => Buy(entry.ContentId);
        PanelChrome.Row(
            list,
            ContentDisplayName.For(entry.ContentId),
            new Label { Text = ContentDisplayName.Credits(entry.PriceMilliCredits) },
            buy);
        return new Row(entry, buy);
    }

    private void Buy(string contentId)
    {
        PurchaseResult result = _economy.Purchase(contentId);
        string name = ContentDisplayName.For(contentId);
        _status.Text = result.Status switch
        {
            PurchaseStatus.Purchased =>
                $"Bought {name} for {ContentDisplayName.Credits(result.PriceMilliCredits)}.",
            PurchaseStatus.InsufficientFunds =>
                $"{name} costs {ContentDisplayName.Credits(result.PriceMilliCredits)} — " +
                $"you have {ContentDisplayName.Credits(result.BalanceMilliCredits)}.",
            PurchaseStatus.AlreadyOwned => $"You already own {name}.",
            _ => $"{name} is not for sale ({result.Status}).",
        };

        if (result.Succeeded)
        {
            PurchaseCount++;
            Purchased?.Invoke();
        }

        Refresh();
    }

    /// <summary>The offered content ids, in the order the shop presents them.</summary>
    public IReadOnlyList<string> OfferedContentIds =>
        _rows.ConvertAll(static row => row.Entry.ContentId);

    /// <summary>
    /// The buy control for one offered entry (test observability). Content ids contain dots,
    /// which Godot strips from node names, so lookup goes through the row list rather than
    /// through <c>FindChild</c>.
    /// </summary>
    public Button? BuyButtonFor(string contentId)
    {
        foreach (Row row in _rows)
        {
            if (string.Equals(row.Entry.ContentId, contentId, StringComparison.Ordinal))
                return row.Buy;
        }

        return null;
    }

    /// <summary>Re-reads balance and ownership; safe to call whenever the panel is shown.</summary>
    public void Refresh()
    {
        if (!IsInitialized)
            return;

        _balance.Text = ContentDisplayName.Credits(_progress.BalanceMilliCredits);
        foreach (Row row in _rows)
        {
            bool owned = _progress.IsToolUnlocked(row.Entry.ContentId);
            bool affordable = _progress.BalanceMilliCredits >= row.Entry.PriceMilliCredits;
            row.Buy.Text = owned ? "Owned" : "Buy";
            row.Buy.Disabled = owned || !affordable;
        }
    }

    private readonly record struct Row(CatalogueEntry Entry, Button Buy);
}
