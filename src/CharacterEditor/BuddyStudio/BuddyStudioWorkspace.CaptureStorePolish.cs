using System;
using DesktopBuddy.App;
using DesktopBuddy.Domain.Characters;
using DesktopBuddy.Domain.Content;
using DesktopBuddy.Economy;
using DesktopBuddy.UI;
using DesktopBuddy.UI.Win98;
using DesktopBuddy.Ui;
using Godot;

namespace DesktopBuddy.CharacterEditor.BuddyStudio;

public partial class BuddyStudioWorkspace
{
    private bool _captureStoreStateObserved;
    private string? _captureStoreLastPreviewId;
    private bool _captureStoreLastOwned;
    private bool _captureStoreLastEquipped;

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

        CharacterDocument? preview = _session.PreviewDocument;
        if (preview is null)
            return;

        string previewId = CharacterDocumentEditor.ReadFeatureId(preview, _slot);
        if (!_session.FeatureCatalog.TryGetDefinition(previewId, out CosmeticDefinition definition))
            return;

        string displayName = BuddyGeneratedCosmeticRegistry.Current.TryGet(previewId, out GeneratedBuddyCosmeticResource generated)
            ? generated.DisplayName
            : CosmeticName(definition);
        if (GodotObject.IsInstanceValid(_selectedItemName) &&
            !string.Equals(_selectedItemName!.Text, displayName, StringComparison.Ordinal))
        {
            _selectedItemName.Text = displayName;
        }

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
        _buy.TooltipText = equipped ? "This cosmetic is currently equipped."
            : owned ? "Equip this owned cosmetic on the working character."
            : purchasable && !affordable ? $"Costs {PriceText(definition)}. Earn more credits before buying."
            : purchasable ? $"Buy this cosmetic permanently for {PriceText(definition)}."
            : "This cosmetic is earned elsewhere and cannot be bought here.";

        bool hasColor = definition.ColorChannels.Count > 0;
        _color.TooltipText = hasColor
            ? "Choose a color for this cosmetic."
            : "This cosmetic has no editable color channel.";
        bool transformable = definition.TransformPolicy == CosmeticTransformPolicy.MoveAndUniformScale;
        _move.TooltipText = transformable
            ? "Move the selected cosmetic on the preview."
            : "This cosmetic does not support repositioning.";
        _smaller.TooltipText = transformable
            ? "Make the selected cosmetic smaller."
            : "This cosmetic does not support resizing.";
        _larger.TooltipText = transformable
            ? "Make the selected cosmetic larger."
            : "This cosmetic does not support resizing.";
        _resetTransform.TooltipText = transformable
            ? "Reset this cosmetic's position and size."
            : "This cosmetic has no editable transform to reset.";
        _save.TooltipText = _save.Disabled
            ? "There are no unsaved character changes."
            : "Save this character (Ctrl+S).";

        // Persistent equipped accent and current preview selection are separate layers. The grid
        // owns the inset preview outline; this workspace owns which item is actually equipped.
        foreach (CosmeticDefinition candidate in _session.FeatureCatalog.GetDefinitions(_slot))
            _catalog.SetAccent(candidate.Id, IsEquipped(candidate.Id));

        ApplyCaptureStoreAcknowledgement(previewId, owned, equipped);
    }

    private void ApplyCaptureStoreAcknowledgement(string previewId, bool owned, bool equipped)
    {
        bool sameItem = string.Equals(_captureStoreLastPreviewId, previewId, StringComparison.Ordinal);
        bool committed = _captureStoreStateObserved && sameItem &&
            ((!_captureStoreLastOwned && owned) || (!_captureStoreLastEquipped && equipped));

        _captureStoreStateObserved = true;
        _captureStoreLastPreviewId = previewId;
        _captureStoreLastOwned = owned;
        _captureStoreLastEquipped = equipped;

        if (!committed || !GodotObject.IsInstanceValid(_buy))
            return;

        if (GetTree().Root.FindChild(nameof(SandboxRoot), recursive: true, owned: false) is SandboxRoot sandbox &&
            GodotObject.IsInstanceValid(sandbox) && GodotObject.IsInstanceValid(sandbox.Shell))
        {
            Win98Motion.Pulse(_buy, sandbox.Shell.CurrentLocalSettings);
        }
    }

}
