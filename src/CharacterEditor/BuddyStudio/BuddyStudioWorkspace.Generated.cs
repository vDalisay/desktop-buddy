#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using DesktopBuddy.Domain.Characters;
using DesktopBuddy.Domain.Content;
using DesktopBuddy.UI;
using DesktopBuddy.UI.Win98;
using DesktopBuddy.Ui;
using Godot;

namespace DesktopBuddy.CharacterEditor.BuddyStudio;

/// <summary>
/// Asset Forge integration companion for the existing Studio workspace. The shared Studio UI
/// remains untouched: this companion restores the composed immutable catalogue after the legacy
/// refresh path paints the shipped-only set, and supplies generated thumbnails/commerce state.
/// Remove this synchronization shim once the workspace's historical shipped-only calls are
/// naturally retired during its planned full-release UI revamp.
/// </summary>
public partial class BuddyStudioWorkspace
{
    private int _assetForgeExpectedCatalogCount = -1;
    private CharacterFeatureSlot _assetForgeObservedSlot = (CharacterFeatureSlot)(-1);
    private string? _assetForgeObservedPreviewId;

    public override void _Process(double delta)
    {
        AssetForgeProcessNavigation();
        if (!IsConfigured || !IsInsideTree() || !GodotObject.IsInstanceValid(_catalog) || _session.PreviewDocument is null)
            return;

        IReadOnlyList<CosmeticDefinition> definitions = _session.FeatureCatalog.GetDefinitions(_slot);
        int actualCount = CatalogTileCount();
        string previewId = CharacterDocumentEditor.ReadFeatureId(_session.PreviewDocument, _slot);
        bool catalogueNeedsRestore =
            _assetForgeObservedSlot != _slot ||
            _assetForgeExpectedCatalogCount != definitions.Count ||
            actualCount != definitions.Count;

        if (catalogueNeedsRestore)
        {
            _catalog.SetItems(definitions.Select(AssetForgePresentation));
            _catalog.Select(previewId, notify: false);
            _assetForgeObservedSlot = _slot;
            _assetForgeExpectedCatalogCount = definitions.Count;
        }

        if (!string.Equals(_assetForgeObservedPreviewId, previewId, StringComparison.Ordinal) || catalogueNeedsRestore)
        {
            _assetForgeObservedPreviewId = previewId;
            if (BuddyGeneratedCosmeticRegistry.Current.TryGet(previewId, out GeneratedBuddyCosmeticResource generated))
                RefreshGeneratedSelectionPane(generated);
        }

        // Run last: both the legacy and generated refresh paths may have just written their
        // historical ownership copy. CAP-6 then normalizes the visible hierarchy without changing
        // either path's transaction/session state.
        CaptureStorePolishProcess();
    }

    private int CatalogTileCount()
    {
        Node? grid = _catalog.FindChild("CatalogTileGrid", recursive: true, owned: false);
        return GodotObject.IsInstanceValid(grid) ? grid!.GetChildCount() : -1;
    }

    private Win98CatalogItemPresentation AssetForgePresentation(CosmeticDefinition definition)
    {
        if (!BuddyGeneratedCosmeticRegistry.Current.TryGet(definition.Id, out GeneratedBuddyCosmeticResource generated))
            return Presentation(definition);

        bool owned = _session.IsCosmeticOwned(definition.Id);
        bool equipped = IsEquipped(definition.Id);
        string secondary = owned ? string.Empty : PriceText(definition);
        Color? priceColor = null;
        if (!owned && definition.OwnershipContentId is string contentId &&
            _economy.Catalogue.TryGet(contentId, out CatalogueEntry entry) && entry.HasValidPrice)
        {
            priceColor = entry.PriceMilliCredits <= _economy.BalanceMilliCredits
                ? Color.Color8(0, 128, 0)
                : Color.Color8(192, 0, 0);
        }

        return new Win98CatalogItemPresentation(
            definition.Id,
            generated.DisplayName,
            secondary,
            generated.Thumbnail,
            Tooltip: equipped ? "Currently equipped."
                : owned ? "Single-click to preview; double-click to equip."
                : "Preview only until acquired.",
            BadgeText: equipped ? "Equipped" : owned ? "Owned" : string.Empty,
            Accented: equipped,
            SecondaryColor: priceColor);
    }

    private void RefreshGeneratedSelectionPane(GeneratedBuddyCosmeticResource generated)
    {
        if (!_session.FeatureCatalog.TryGetDefinition(generated.FeatureId, out CosmeticDefinition definition))
            return;

        bool owned = _session.IsCosmeticOwned(definition.Id);
        bool equipped = IsEquipped(definition.Id);
        CatalogueEntry entry = default;
        bool purchasable = !owned && definition.OwnershipContentId is string contentId &&
            _economy.Catalogue.TryGet(contentId, out entry) && entry.Visible &&
            entry.Kind == CatalogueEntryKind.Cosmetic && entry.HasValidPrice;
        bool affordable = !purchasable || entry.PriceMilliCredits <= _economy.BalanceMilliCredits;

        string status = equipped ? "Equipped" : owned ? "Owned" : "Preview";
        _values.SetRows(
        [
            new Win98ValueRowPresentation("status", "Status", status, true),
            new Win98ValueRowPresentation("price", "Price", owned ? "—" : PriceText(definition)),
            new Win98ValueRowPresentation("balance", "Balance", ContentDisplayName.Credits(_economy.BalanceMilliCredits)),
        ]);
        _buy.Text = owned ? (equipped ? "Equipped" : "Equip") : purchasable ? "Buy" : "Unavailable";
        _buy.Disabled = equipped || (!owned && (!purchasable || !affordable));
        // No layer tag here: PurchaseOrEquipAsync sounds the commitment for every route in.
        UiFeedbackAudioBootstrap.Tag(_buy, layer: UiSfx.NoLayer);
        _buy.TooltipText = equipped ? "This cosmetic is currently equipped."
            : owned ? "Equip this cosmetic on the working character."
            : purchasable && !affordable ? "Earn more credits before buying this cosmetic."
            : purchasable ? "Buy this cosmetic permanently; equip it with the next action."
            : "This generated cosmetic has no valid commerce entry.";

        bool hasColor = definition.ColorChannels.Count > 0;
        _color.Disabled = !hasColor;
        _presets.Visible = hasColor;
        _color.Color = FromRgba(CharacterDocumentEditor.ReadFeatureColor(_session.PreviewDocument!, _slot));
        _move.Disabled = true;
        _smaller.Disabled = true;
        _larger.Disabled = true;
        _resetTransform.Disabled = true;
        if (_moveMode)
            SetMoveMode(false);
    }
}
