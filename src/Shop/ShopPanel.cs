using System;
using System.Collections.Generic;
using DesktopBuddy.Domain.Content;
using DesktopBuddy.Domain.Economy;
using DesktopBuddy.Domain.Persistence;
using DesktopBuddy.Domain.Tools;
using DesktopBuddy.Economy;
using DesktopBuddy.Interaction;
using DesktopBuddy.UI;
using DesktopBuddy.Ui;
using Godot;

namespace DesktopBuddy.Shop;

/// <summary>
/// Unified player catalogue: every released selectable tool appears in one place. Unowned
/// entries are bought through the economy service; owned entries equip through the gameplay
/// pipeline's single selection seam. Starting tools therefore live in the same list instead
/// of requiring a second Tools menu.
/// </summary>
public partial class ShopPanel : PanelContainer
{
    private readonly List<Row> _rows = [];
    private BuddyProgressState _progress = null!;
    private EconomyService _economy = null!;
    private ToolCatalogue _catalogue = null!;
    private InteractionDamageComponent? _pipeline;
    private Label _balance = null!;
    private Label _status = null!;
    private Label _description = null!;

    /// <summary>Raised when a purchase changes ownership, so legacy consumers can refresh.</summary>
    public event Action? Purchased;

    public bool IsInitialized { get; private set; }
    public int PurchaseCount { get; private set; }
    public int EquipCount { get; private set; }

    public void Configure(
        BuddyProgressState progress,
        EconomyService economy,
        ToolCatalogue catalogue,
        InteractionDamageComponent pipeline)
    {
        _progress = progress ?? throw new ArgumentNullException(nameof(progress));
        _economy = economy ?? throw new ArgumentNullException(nameof(economy));
        _catalogue = catalogue ?? throw new ArgumentNullException(nameof(catalogue));
        _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));

        Name = "ShopPanel";
        PanelChrome.Parts parts = PanelChrome.Build(this, "ShopItemList");
        _balance = parts.HeaderValue;
        _status = parts.Status;
        _description = parts.Description;
        foreach (CatalogueEntry entry in CataloguePolicy.SelectableEntries(_catalogue))
        {
            if (ContentIds.TryParseTool(entry.ContentId, out ToolId tool))
                _rows.Add(BuildRow(parts.List, entry, tool));
        }

        // The balance moves while this panel is open — every hit pays out — and the panel used
        // to show whatever it happened to say when it was last acted on (owner report
        // 2026-08-21). Same event the corner readout listens to, so the two never disagree.
        _economy.BalanceChanged += OnBalanceChanged;
        IsInitialized = true;
        Refresh();
    }

    public override void _ExitTree()
    {
        // The economy service outlives every node, so the unsubscribe is unconditional.
        if (_economy is not null)
            _economy.BalanceChanged -= OnBalanceChanged;
    }

    private void OnBalanceChanged(long _) => Refresh();

    private Row BuildRow(VBoxContainer list, CatalogueEntry entry, ToolId tool)
    {
        var action = new Button { Text = "Buy" };
        var price = new Label();
        action.Pressed += () => Activate(entry, tool);
        HBoxContainer line = PanelChrome.Row(list, ContentDisplayName.For(entry.ContentId), price, action);
        // Both the row and its button: a Stop-filtered button takes the pick from its own parent,
        // so hooking only the row would blank the description the moment the cursor reached Buy.
        line.MouseEntered += () => ShowDescription(entry.ContentId);
        action.MouseEntered += () => ShowDescription(entry.ContentId);
        return new Row(entry, tool, action, price);
    }

    /// <summary>Last row hovered wins, and it stays put on the way out — a description that
    /// blanked between rows flickered more than it informed.</summary>
    private void ShowDescription(string contentId)
    {
        string usage = ContentDisplayName.Usage(contentId);
        if (usage.Length > 0)
            _description.Text = usage;
    }

    private void Activate(CatalogueEntry entry, ToolId tool)
    {
        bool owned = entry.IsStarting || _progress.IsToolUnlocked(entry.ContentId);
        if (!owned)
        {
            Purchase(entry, tool);
            return;
        }

        // The settled row is already disabled; re-firing it must not re-count an equip.
        if (_progress.SelectedTool == tool)
            return;

        Equip(entry.ContentId, tool);
    }

    private void Purchase(CatalogueEntry entry, ToolId tool)
    {
        PurchaseResult result = _economy.Purchase(entry.ContentId);
        string name = ContentDisplayName.For(entry.ContentId);
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
            UiFeedbackAudioBootstrap.TryPlayLayer(this, UiSfx.Money);
            RewardPopup.Show(
                this,
                RewardIconProvider.ForContent(entry.ContentId),
                name,
                amountMilliCredits: 0);
            Purchased?.Invoke();
            // Buying is the player saying "I want this now": a second click to equip was pure
            // ceremony, and the tutorial no longer has to teach it as its own step.
            string bought = _status.Text;
            Equip(entry.ContentId, tool);
            _status.Text = $"{bought} {_status.Text}";
            return;
        }

        Refresh();
    }

    private void Equip(string contentId, ToolId tool)
    {
        string name = ContentDisplayName.For(contentId);
        if (!GodotObject.IsInstanceValid(_pipeline))
        {
            _status.Text = $"{name} could not be equipped right now.";
            return;
        }

        _pipeline!.SelectTool(tool);
        bool applied = _progress.SelectedTool == tool;
        _status.Text = applied
            ? $"{name} equipped."
            : $"{name} could not be equipped.";
        if (applied)
        {
            EquipCount++;
            UiFeedbackAudioBootstrap.TryPlayLayer(this, UiSfx.Equip);
        }
        Refresh();
    }

    /// <summary>The catalogue content ids, in authored selectable order.</summary>
    public IReadOnlyList<string> OfferedContentIds =>
        _rows.ConvertAll(static row => row.Entry.ContentId);

    /// <summary>
    /// Compatibility/test lookup for the row action. It is named BuyButtonFor for existing
    /// callers, but the same control changes from Buy to Equip/Equipped as ownership changes.
    /// </summary>
    public Button? BuyButtonFor(string contentId)
    {
        foreach (Row row in _rows)
        {
            if (string.Equals(row.Entry.ContentId, contentId, StringComparison.Ordinal))
                return row.Action;
        }

        return null;
    }

    /// <summary>Re-reads balance, ownership and active-tool state; safe whenever shown.</summary>
    public void Refresh()
    {
        if (!IsInitialized)
            return;

        _balance.Text = ContentDisplayName.Credits(_progress.BalanceMilliCredits);
        if (_description.Text.Length == 0)
            ShowDescription(ContentIds.ForTool(_progress.SelectedTool));
        foreach (Row row in _rows)
        {
            bool owned = row.Entry.IsStarting || _progress.IsToolUnlocked(row.Entry.ContentId);
            bool active = _progress.SelectedTool == row.Tool;
            bool affordable = _progress.BalanceMilliCredits >= row.Entry.PriceMilliCredits;
            string name = ContentDisplayName.For(row.Entry.ContentId);
            string price = ContentDisplayName.Credits(row.Entry.PriceMilliCredits);

            row.Price.Text = owned ? string.Empty : price;
            row.Action.Text = active ? "Equipped" : owned ? "Equip" : "Buy";
            row.Action.Disabled = active || (!owned && !affordable);
            row.Action.TooltipText = active
                ? $"{name} is currently equipped."
                : owned
                    ? $"Equip {name}."
                    : affordable
                        ? $"Buy {name} permanently for {price}."
                        : $"{name} costs {price}; you have {ContentDisplayName.Credits(_progress.BalanceMilliCredits)}. Earn more credits to buy it.";
            // No layer tag: Purchase and Equip sound themselves, so a press that fails — too
            // expensive, pipeline gone — stays honestly silent.
            UiFeedbackAudioBootstrap.Tag(row.Action, layer: UiSfx.NoLayer);
        }
    }

    private readonly record struct Row(
        CatalogueEntry Entry,
        ToolId Tool,
        Button Action,
        Label Price);
}