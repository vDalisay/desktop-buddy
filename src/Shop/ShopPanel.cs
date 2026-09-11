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
    private PlayerRuntimeProgressBinding _progress = null!;
    private EconomyService _economy = null!;
    private ToolCatalogue _catalogue = null!;
    private InteractionDamageComponent? _pipeline;
    private Label _balance = null!;
    private Label _description = null!;
    private PanelChrome.RowSelection _selection = null!;

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
        ArgumentNullException.ThrowIfNull(progress);
        ArgumentNullException.ThrowIfNull(pipeline);
        // CharacterEditorHost still has a compatibility aggregate in its constructor surface. In a
        // composed Scene run the pipeline is already initialized against the authoritative account,
        // so prefer that validated binding and never let purchases/rendering observe stale legacy
        // state. Uninitialized isolated UI fixtures retain the historical aggregate path.
        PlayerRuntimeProgressBinding binding = pipeline.IsInitialized
            ? pipeline.CreatePlayerProgressBinding()
            : new PlayerRuntimeProgressBinding(progress);
        Configure(binding, economy, catalogue, pipeline);
    }

    public void Configure(
        PlayerRuntimeProgressBinding progress,
        EconomyService economy,
        ToolCatalogue catalogue,
        InteractionDamageComponent pipeline)
    {
        _progress = progress ?? throw new ArgumentNullException(nameof(progress));
        _economy = economy ?? throw new ArgumentNullException(nameof(economy));
        _catalogue = catalogue ?? throw new ArgumentNullException(nameof(catalogue));
        _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));

        Name = "ShopPanel";
        PanelChrome.Parts parts = PanelChrome.Build(this, "ShopItemList", status: false);
        _balance = parts.HeaderValue;
        _description = parts.Description;
        _selection = new PanelChrome.RowSelection(_description, ContentDisplayName.Usage);
        foreach (CatalogueEntry entry in CataloguePolicy.SelectableEntries(_catalogue))
        {
            if (ContentIds.TryParseTool(entry.ContentId, out ToolId tool))
                _rows.Add(BuildRow(parts.List, entry, tool));
        }

        _economy.BalanceChanged += OnBalanceChanged;
        IsInitialized = true;
        Refresh();
    }

    public override void _ExitTree()
    {
        if (_economy is not null)
            _economy.BalanceChanged -= OnBalanceChanged;
    }

    private void OnBalanceChanged(long _)
    {
        if (IsVisibleInTree())
            Refresh();
    }

    private Row BuildRow(VBoxContainer list, CatalogueEntry entry, ToolId tool)
    {
        var action = new Button { Text = "Buy" };
        var price = new Label();
        action.Pressed += () => Activate(entry, tool);
        HBoxContainer line = PanelChrome.Row(list, ContentDisplayName.For(entry.ContentId), price, action);
        _selection.Add(entry.ContentId, line);
        action.MouseEntered += () => _selection.Hover(entry.ContentId);
        return new Row(entry, tool, action, price);
    }

    private void Activate(CatalogueEntry entry, ToolId tool)
    {
        bool owned = entry.IsStarting || _progress.IsUnlocked(entry.ContentId);
        if (!owned)
        {
            Purchase(entry, tool);
            return;
        }

        if (_progress.SelectedTool == tool)
            return;

        Equip(entry.ContentId, tool);
    }

    private void Purchase(CatalogueEntry entry, ToolId tool)
    {
        PurchaseResult result = _economy.Purchase(entry.ContentId);
        string name = ContentDisplayName.For(entry.ContentId);
        if (result.Succeeded)
        {
            PurchaseCount++;
            UiFeedbackAudioBootstrap.TryPlayLayer(this, UiSfx.Money);
            RewardPopup.Show(
                this,
                RewardIconProvider.ForContent(entry.ContentId),
                name,
                amountMilliCredits: 0,
                kind: RewardPresentationKind.ToolPurchase);
            Purchased?.Invoke();
            Equip(entry.ContentId, tool);
            return;
        }

        Refresh();
    }

    private void Equip(string contentId, ToolId tool)
    {
        if (!GodotObject.IsInstanceValid(_pipeline))
            return;

        _pipeline!.SelectTool(tool);
        bool applied = _progress.SelectedTool == tool;
        if (applied)
        {
            EquipCount++;
            UiFeedbackAudioBootstrap.TryPlayLayer(this, UiSfx.Equip);
        }
        Refresh();
    }

    public IReadOnlyList<string> OfferedContentIds =>
        _rows.ConvertAll(static row => row.Entry.ContentId);

    public Button? BuyButtonFor(string contentId)
    {
        foreach (Row row in _rows)
        {
            if (string.Equals(row.Entry.ContentId, contentId, StringComparison.Ordinal))
                return row.Action;
        }

        return null;
    }

    public void Refresh()
    {
        if (!IsInitialized)
            return;

        _balance.Text = ContentDisplayName.Credits(_progress.BalanceMilliCredits);
        if (_description.Text.Length == 0)
            _selection.Hover(ContentIds.ForTool(_progress.SelectedTool));
        foreach (Row row in _rows)
        {
            bool owned = row.Entry.IsStarting || _progress.IsUnlocked(row.Entry.ContentId);
            bool active = _progress.SelectedTool == row.Tool;
            // Free for now (owner 2026-09-12; see EconomyService.EverythingIsFree): nothing shows a
            // price, and nothing is out of reach.
            bool affordable = EconomyService.EverythingIsFree ||
                _progress.BalanceMilliCredits >= row.Entry.PriceMilliCredits;
            string name = ContentDisplayName.For(row.Entry.ContentId);
            string price = EconomyService.EverythingIsFree
                ? "Free"
                : ContentDisplayName.Credits(row.Entry.PriceMilliCredits);

            row.Price.Text = owned ? string.Empty : price;
            row.Action.Text = active ? "Equipped" : owned ? "Equip" : "Buy";
            row.Action.Disabled = active || (!owned && !affordable);
            row.Action.TooltipText = active
                ? $"{name} is currently equipped."
                : owned
                    ? $"Equip {name}."
                    : affordable
                        ? (EconomyService.EverythingIsFree
                            ? $"Take {name}; it is yours permanently, and free."
                            : $"Buy {name} permanently for {price}.")
                        : $"{name} costs {price}; you have {ContentDisplayName.Credits(_progress.BalanceMilliCredits)}. Earn more credits to buy it.";
            UiFeedbackAudioBootstrap.Tag(row.Action, layer: UiSfx.NoLayer);
        }
    }

    private readonly record struct Row(
        CatalogueEntry Entry,
        ToolId Tool,
        Button Action,
        Label Price);
}
