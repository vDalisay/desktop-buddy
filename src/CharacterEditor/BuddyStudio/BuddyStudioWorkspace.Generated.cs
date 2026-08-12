using System;
using System.Collections.Generic;
using System.Linq;
using DesktopBuddy.Domain.Characters;
using DesktopBuddy.Domain.Content;
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
        string secondary = owned ? "Owned" : PriceText(definition);
        return new Win98CatalogItemPresentation(
            definition.Id,
            generated.DisplayName,
            secondary,
            generated.Thumbnail,
            Tooltip: owned ? "Available to save." : "Preview only until acquired.",
            BadgeText: owned ? "Owned" : "Preview");
    }

    private void RefreshGeneratedSelectionPane(GeneratedBuddyCosmeticResource generated)
    {
        if (!_session.FeatureCatalog.TryGetDefinition(generated.FeatureId, out CosmeticDefinition definition))
            return;

        bool owned = _session.IsCosmeticOwned(definition.Id);
        string equippedId = CharacterDocumentEditor.ReadFeatureId(_session.WorkingDocument!, _slot);
        bool equipped = string.Equals(equippedId, definition.Id, StringComparison.Ordinal) &&
            !_session.HasOwnedPreview(_slot) && !_session.HasUnownedPreview(_slot);
        CatalogueEntry entry = default;
        bool purchasable = !owned && definition.OwnershipContentId is string contentId &&
            _economy.Catalogue.TryGet(contentId, out entry) && entry.Visible &&
            entry.Kind == CatalogueEntryKind.Cosmetic && entry.HasValidPrice;
        bool affordable = !purchasable || entry.PriceMilliCredits <= _economy.BalanceMilliCredits;

        _values.SetRows(
        [
            new Win98ValueRowPresentation("status", "Status", owned ? "Owned" : "Preview", true),
            new Win98ValueRowPresentation("price", "Price", owned ? "—" : PriceText(definition)),
            new Win98ValueRowPresentation("balance", "Balance", ContentDisplayName.Credits(_economy.BalanceMilliCredits)),
        ]);
        _buy.Text = owned ? (equipped ? "Equipped" : "Equip") : purchasable ? "Buy" : "Unavailable";
        _buy.Disabled = equipped || (!owned && (!purchasable || !affordable));
        _buy.TooltipText = equipped ? "This cosmetic is currently equipped."
            : owned ? "Equip this cosmetic on the working character."
            : purchasable && !affordable ? "Earn more credits before buying this cosmetic."
            : purchasable ? "Buy this cosmetic permanently; equip it with the next action."
            : "This generated cosmetic has no valid commerce entry.";

        // v1 generated glasses are positioned by their authoring preset. The existing Studio move
        // helper still resolves shipped definitions internally, so keep these controls disabled
        // until that older helper is replaced during the broader Studio UI revamp.
        _color.Disabled = true;
        _presets.Visible = false;
        _move.Disabled = true;
        _smaller.Disabled = true;
        _larger.Disabled = true;
        _resetTransform.Disabled = true;
        if (_moveMode)
            SetMoveMode(false);
    }
}
