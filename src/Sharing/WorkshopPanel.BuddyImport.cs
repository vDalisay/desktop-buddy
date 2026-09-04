using System;
using System.Threading;
using System.Threading.Tasks;
using DesktopBuddy.CharacterEditor;
using DesktopBuddy.Domain.Sharing;
using DesktopBuddy.Persistence.Characters;
using DesktopBuddy.Platform.Steam;
using DesktopBuddy.UI.Win98;
using Godot;

namespace DesktopBuddy.Sharing;

public partial class WorkshopPanel
{
    private CharacterStore? _characterStore;
    private CharacterSlotEntitlementState? _characterSlots;
    private Control? _buddyImportBlocker;
    private PanelContainer? _buddyImportPanel;
    private Label? _buddyImportMessage;
    private Label? _buddySlotStatus;
    private Button? _applyCurrentBuddyButton;
    private Button? _useNewBuddyButton;
    private PublishedWorkshopItem? _pendingBuddyImport;

    /// <summary>
    /// Binds Workshop buddy imports to the exact same durable slot entitlement used by Paint Buddy.
    /// This is configured by the normal composition root before the window enters the tree.
    /// </summary>
    public void ConfigureBuddyImportPolicy(
        CharacterStore characters,
        CharacterSlotEntitlementState slots)
    {
        _characterStore = characters ?? throw new ArgumentNullException(nameof(characters));
        _characterSlots = slots ?? throw new ArgumentNullException(nameof(slots));
        if (GodotObject.IsInstanceValid(_buddyImportPanel) && _buddyImportPanel!.Visible)
            RefreshBuddyImportDialogState();
    }

    private void BuildBuddyImportDialog(Control overlay)
    {
        _buddyImportBlocker = Win98Dialog.Blocker(overlay, "WorkshopBuddyImportBlocker");
        _buddyImportPanel = Win98Dialog.Create(
            "WorkshopBuddyImportDialog",
            "Import Buddy",
            new Vector2(500, 245),
            out VBoxContainer body,
            HideBuddyImportDialog,
            draggable: false);
        overlay.AddChild(_buddyImportPanel);

        _buddyImportMessage = new Label
        {
            Text = "Choose how to import this Workshop buddy.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        body.AddChild(_buddyImportMessage);

        var slotPanel = new PanelContainer();
        slotPanel.AddThemeStyleboxOverride("panel", Win98ThemeFactory.Recessed(Win98ThemeFactory.Face, 1));
        body.AddChild(slotPanel);
        _buddySlotStatus = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            CustomMinimumSize = new Vector2(0, Win98ThemeFactory.Px(34)),
        };
        slotPanel.AddChild(_buddySlotStatus);

        var actions = new HBoxContainer
        {
            Alignment = BoxContainer.AlignmentMode.End,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        body.AddChild(actions);
        _applyCurrentBuddyButton = Win98Dialog.Action(actions, "Apply to Current Buddy", ApplyPendingBuddyToCurrent);
        _applyCurrentBuddyButton.CustomMinimumSize = new Vector2(170, 34);
        _useNewBuddyButton = Win98Dialog.Action(actions, "Use New Buddy", ImportPendingBuddyAsNew);
        _useNewBuddyButton.CustomMinimumSize = new Vector2(140, 34);
        Win98Dialog.Action(actions, "Cancel", HideBuddyImportDialog);
    }

    private void RequestImport(PublishedWorkshopItem item)
    {
        if (_busy) return;

        if (string.Equals(item.ContentType, ShareContentTypes.BuddyCharacter, StringComparison.Ordinal) &&
            _characterStore is not null && _characterSlots is not null &&
            GodotObject.IsInstanceValid(_buddyImportPanel))
        {
            _pendingBuddyImport = item;
            HidePublishSuccess();
            RefreshBuddyImportDialogState();
            _buddyImportBlocker!.Visible = true;
            _buddyImportPanel!.Visible = true;
            Button focus = _applyCurrentBuddyButton!.Disabled ? _useNewBuddyButton! : _applyCurrentBuddyButton;
            if (!focus.Disabled)
                Callable.From(focus.GrabFocus).CallDeferred();
            return;
        }

        _ = ImportAsync(item);
    }

    private void RefreshBuddyImportDialogState()
    {
        if (_characterStore is null || _characterSlots is null ||
            !GodotObject.IsInstanceValid(_buddyImportPanel))
            return;

        int occupied = _characterStore.CountStoredCharacters();
        int capacity = _characterSlots.Capacity;
        int remaining = Math.Max(0, capacity - occupied);
        bool canCreateNew = remaining > 0;
        Guid? currentId = _selection?.ActiveCharacterId;
        bool canReplaceCurrent = currentId.HasValue && _characterStore.ContainsStoredCharacter(currentId.Value);

        string itemName = _pendingBuddyImport?.DisplayName ?? "this Workshop buddy";
        _buddyImportMessage!.Text =
            $"How should '{itemName}' be imported?\n\n" +
            "Apply to Current Buddy keeps the current buddy's slot and name, but replaces its visual configuration and paint.";
        _buddySlotStatus!.Text = canCreateNew
            ? $"{occupied}/{capacity} buddy slots used  •  {remaining} free"
            : $"{occupied}/{capacity} buddy slots used  •  no free slots";

        _applyCurrentBuddyButton!.Disabled = !canReplaceCurrent;
        _applyCurrentBuddyButton.TooltipText = canReplaceCurrent
            ? "Replace the active saved buddy's appearance without consuming another slot."
            : "The built-in buddy cannot be overwritten. Select a saved buddy first.";

        _useNewBuddyButton!.Disabled = !canCreateNew;
        _useNewBuddyButton.TooltipText = canCreateNew
            ? $"Import into a new buddy slot. {remaining} slot{(remaining == 1 ? string.Empty : "s")} available."
            : "No free buddy slot is available. Apply to the current buddy or free/buy a slot first.";
    }

    private void ApplyPendingBuddyToCurrent()
    {
        if (_pendingBuddyImport is not PublishedWorkshopItem item ||
            _selection?.ActiveCharacterId is not Guid currentId ||
            _characterStore is null || !_characterStore.ContainsStoredCharacter(currentId))
        {
            RefreshBuddyImportDialogState();
            return;
        }

        HideBuddyImportDialog();
        _ = ImportAsync(item, currentId);
    }

    private void ImportPendingBuddyAsNew()
    {
        if (_pendingBuddyImport is not PublishedWorkshopItem item ||
            _characterStore is null || _characterSlots is null)
            return;

        // Re-check immediately before starting. The importer checks again at its commit boundary,
        // so this button can never become a stale route around the slot entitlement.
        if (_characterStore.CountStoredCharacters() >= _characterSlots.Capacity)
        {
            RefreshBuddyImportDialogState();
            SetStatus("No free buddy slot is available. Apply the skin to the current buddy or free/buy a slot first.");
            return;
        }

        HideBuddyImportDialog();
        _ = ImportAsync(item);
    }

    private void HideBuddyImportDialog()
    {
        if (GodotObject.IsInstanceValid(_buddyImportBlocker)) _buddyImportBlocker!.Visible = false;
        if (GodotObject.IsInstanceValid(_buddyImportPanel)) _buddyImportPanel!.Visible = false;
        _pendingBuddyImport = null;
    }

    private async Task RefreshReplacedBuddyAsync(Guid characterId, CancellationToken token)
    {
        CharacterSelectionRuntime? runtime = GetTree().Root.FindChild(
            nameof(CharacterSelectionRuntime),
            true,
            false) as CharacterSelectionRuntime;
        CharacterSelectionCoordinator? coordinator = runtime?.Coordinator;
        if (coordinator is null)
            return;

        CharacterActivationResult activation = await coordinator.QueueUseCharacterAsync(characterId, token);
        if (!activation.WasQueued)
            SetStatus(activation.Detail ?? "The Workshop skin was saved, but the live buddy could not be refreshed yet.");
    }
}
