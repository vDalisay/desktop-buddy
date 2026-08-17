using System;
using DesktopBuddy.Domain.Characters;
using DesktopBuddy.Domain.Content;
using DesktopBuddy.Economy;
using DesktopBuddy.UI;
using Godot;

namespace DesktopBuddy.CharacterEditor.BuddyStudio;

public partial class BuddyStudioWorkspace
{
    private bool _captureStorePolishInstalled;
    private Label? _captureStoreItemName;
    private Label? _captureStoreColorHeader;

    /// <summary>
    /// Presentation-only CAP-6 store hierarchy. The historical workspace still owns all commerce,
    /// preview and save behavior; this pass only rearranges existing controls and keeps the visual
    /// states unambiguous for capture.
    /// </summary>
    private void CaptureStorePolishProcess()
    {
        if (!IsConfigured || !IsInsideTree() || !GodotObject.IsInstanceValid(_catalog) ||
            !GodotObject.IsInstanceValid(_values) || !GodotObject.IsInstanceValid(_buy))
        {
            return;
        }

        EnsureCaptureStoreHierarchy();
        CharacterDocument? preview = _session.PreviewDocument;
        if (preview is null)
            return;

        string previewId = CharacterDocumentEditor.ReadFeatureId(preview, _slot);
        if (!_session.FeatureCatalog.TryGetDefinition(previewId, out CosmeticDefinition definition))
            return;

        string displayName = BuddyGeneratedCosmeticRegistry.Current.TryGet(previewId, out GeneratedBuddyCosmeticResource generated)
            ? generated.DisplayName
            : CosmeticName(definition);
        if (GodotObject.IsInstanceValid(_captureStoreItemName))
            _captureStoreItemName!.Text = displayName;

        bool owned = _session.IsCosmeticOwned(definition.Id);
        bool equipped = IsEquipped(definition.Id);
        CatalogueEntry entry = default;
        bool purchasable = !owned && definition.OwnershipContentId is string contentId &&
            _economy.Catalogue.TryGet(contentId, out entry) && entry.Visible &&
            entry.Kind == CatalogueEntryKind.Cosmetic && entry.HasValidPrice;
        bool affordable = !purchasable || entry.PriceMilliCredits <= _economy.BalanceMilliCredits;

        // Same three rows as the existing inspector, but the copy now describes one fact each.
        // No "Owned preview" / "UNOWNED PREVIEW" duplicate jargon: selection is already visible
        // in the grid, ownership is here, and price is its own row.
        _values.UpdateValue("status", equipped ? "Equipped" : owned ? "Owned" : "Preview");
        _values.UpdateValue("price", owned ? "—" : PriceText(definition));
        _values.UpdateValue("balance", ContentDisplayName.Credits(_economy.BalanceMilliCredits));
        _buy.Text = owned ? (equipped ? "Equipped" : "Equip") :
            purchasable ? "Buy" : "Earn in Work Mode";
        _buy.Disabled = equipped || (!owned && (!purchasable || !affordable));

        // Persistent equipped accent and current preview selection are separate layers. The grid
        // owns the inset preview outline; this workspace owns which item is actually equipped.
        foreach (CosmeticDefinition candidate in _session.FeatureCatalog.GetDefinitions(_slot))
            _catalog.SetAccent(candidate.Id, IsEquipped(candidate.Id));
    }

    private void EnsureCaptureStoreHierarchy()
    {
        if (_captureStorePolishInstalled)
            return;

        PanelContainer? pane = FindChild("BuddyStudioInspectorPane", recursive: true, owned: false) as PanelContainer;
        VBoxContainer? column = pane?.FindChild("*", recursive: false, owned: false) as VBoxContainer;
        if (!GodotObject.IsInstanceValid(column))
        {
            // Pane() wraps one VBoxContainer but does not promise its generated name. Fall back to
            // the direct child scan rather than depending on a scene-tree string.
            if (pane is not null)
            {
                foreach (Node child in pane.GetChildren())
                {
                    if (child is VBoxContainer candidate)
                    {
                        column = candidate;
                        break;
                    }
                    if (child is MarginContainer margin)
                    {
                        foreach (Node nested in margin.GetChildren())
                            if (nested is VBoxContainer nestedColumn)
                            {
                                column = nestedColumn;
                                break;
                            }
                    }
                    if (column is not null)
                        break;
                }
            }
        }
        if (!GodotObject.IsInstanceValid(column))
            return;

        Label? oldHeader = null;
        foreach (Node child in column!.GetChildren())
        {
            if (child is Label label)
            {
                oldHeader = label;
                break;
            }
        }
        if (oldHeader is not null)
            oldHeader.Text = "Style Store";

        _captureStoreItemName = new Label
        {
            Name = "BuddyStudioSelectedItemName",
            Text = "Style",
            HorizontalAlignment = HorizontalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        _captureStoreItemName.AddThemeFontSizeOverride("font_size", 16);
        column.AddChild(_captureStoreItemName);

        _captureStoreColorHeader = new Label
        {
            Name = "BuddyStudioColorHeader",
            Text = "Color",
        };
        column.AddChild(_captureStoreColorHeader);

        // Store card first: item name -> state/price/balance -> primary action. Customization then
        // follows underneath, while the existing spacer keeps Save/Exit anchored to the bottom.
        int headerIndex = oldHeader?.GetIndex() ?? 0;
        column.MoveChild(_captureStoreItemName, Math.Min(headerIndex + 1, column.GetChildCount() - 1));
        column.MoveChild(_values, Math.Min(headerIndex + 2, column.GetChildCount() - 1));
        column.MoveChild(_buy, Math.Min(headerIndex + 3, column.GetChildCount() - 1));
        column.MoveChild(_captureStoreColorHeader, Math.Min(headerIndex + 4, column.GetChildCount() - 1));
        column.MoveChild(_color, Math.Min(headerIndex + 5, column.GetChildCount() - 1));
        column.MoveChild(_presets, Math.Min(headerIndex + 6, column.GetChildCount() - 1));

        _buy.CustomMinimumSize = new Vector2(0, 34);
        _captureStorePolishInstalled = true;
    }
}
