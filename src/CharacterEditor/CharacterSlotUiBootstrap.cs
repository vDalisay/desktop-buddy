using System;
using System.IO;
using System.Threading.Tasks;
using DesktopBuddy.App;
using DesktopBuddy.Domain.Economy;
using Godot;

namespace DesktopBuddy.CharacterEditor;

/// <summary>
/// Shipping character-capacity UX. It leaves the existing New Character modal intact and only
/// supplies entitlement state around it: three free slots, a disabled create/duplicate affordance
/// when full, and an explicit permanent expansion purchase. Character documents remain normal
/// directories; there is no preallocated finite slot list.
/// </summary>
public partial class CharacterSlotUiBootstrap : Node
{
    private const double RefreshSeconds = 0.15;

    private SandboxRoot? _sandbox;
    private CharacterEditorHost? _host;
    private Button? _newButton;
    private Button? _buyButton;
    private Label? _slotLabel;
    private CharacterSlotEntitlementState? _slots;
    private double _untilRefresh;
    private bool _purchaseBusy;
    private string? _transientStatus;
    private double _statusSeconds;

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        SetProcess(true);
    }

    public override void _Process(double delta)
    {
        _untilRefresh -= Math.Max(0.0, delta);
        if (_statusSeconds > 0.0)
        {
            _statusSeconds = Math.Max(0.0, _statusSeconds - Math.Max(0.0, delta));
            if (_statusSeconds == 0.0)
                _transientStatus = null;
        }
        if (_untilRefresh > 0.0)
            return;
        _untilRefresh = RefreshSeconds;

        ResolveRuntime();
        if (!GodotObject.IsInstanceValid(_sandbox) || !GodotObject.IsInstanceValid(_host) ||
            !_host!.IsInitialized || !_host.IsEditorOpen)
            return;

        _newButton = GetTree().Root.FindChild("Win98NewCharacterButton", true, false) as Button;
        if (!GodotObject.IsInstanceValid(_newButton) || _newButton!.GetParent() is not Control parent)
            return;

        EnsureControls(parent, _newButton);
        RefreshState();
    }

    private void ResolveRuntime()
    {
        if (!GodotObject.IsInstanceValid(_sandbox))
        {
            _sandbox = GetTree().Root.FindChild("Sandbox", true, false) as SandboxRoot;
            _slots = null;
        }
        if (!GodotObject.IsInstanceValid(_host))
            _host = GetTree().Root.FindChild(nameof(CharacterEditorHost), true, false) as CharacterEditorHost;
        if (_slots is null && GodotObject.IsInstanceValid(_sandbox) &&
            _sandbox!.Progress is not null && _sandbox.Economy is not null)
            _slots = new CharacterSlotEntitlementState(_sandbox.Progress, _sandbox.Economy);
    }

    private void EnsureControls(Control parent, Button newButton)
    {
        _slotLabel = parent.FindChild("CharacterSlotStatus", false, false) as Label;
        if (!GodotObject.IsInstanceValid(_slotLabel))
        {
            _slotLabel = new Label
            {
                Name = "CharacterSlotStatus",
                HorizontalAlignment = HorizontalAlignment.Center,
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
                CustomMinimumSize = new Vector2(0, 18),
            };
            parent.AddChild(_slotLabel);
        }

        _buyButton = parent.FindChild("BuyCharacterSlotButton", false, false) as Button;
        if (!GodotObject.IsInstanceValid(_buyButton))
        {
            _buyButton = new Button
            {
                Name = "BuyCharacterSlotButton",
                TooltipText = "Permanently add one more character slot.",
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                CustomMinimumSize = new Vector2(0, 34),
                Visible = false,
            };
            _buyButton.Pressed += PurchaseSlot;
            parent.AddChild(_buyButton);
        }

        int newIndex = newButton.GetIndex();
        if (_slotLabel!.GetIndex() != Math.Min(newIndex + 1, parent.GetChildCount() - 1))
            parent.MoveChild(_slotLabel, Math.Min(newIndex + 1, parent.GetChildCount() - 1));
        if (_buyButton!.GetIndex() != Math.Min(_slotLabel.GetIndex() + 1, parent.GetChildCount() - 1))
            parent.MoveChild(_buyButton, Math.Min(_slotLabel.GetIndex() + 1, parent.GetChildCount() - 1));
    }

    private void RefreshState()
    {
        if (_slots is null || !GodotObject.IsInstanceValid(_newButton) ||
            !GodotObject.IsInstanceValid(_slotLabel) || !GodotObject.IsInstanceValid(_buyButton))
            return;

        int occupied = CountOccupiedSlots();
        int remaining = _slots.Remaining(occupied);
        long nextCredits = _slots.NextPriceMilliCredits / 1000;
        bool full = remaining <= 0;

        _newButton!.Disabled = full || _purchaseBusy;
        _newButton.Text = full ? "+ New Character (full)" : $"+ New Character ({remaining} free)";
        _newButton.TooltipText = full
            ? "Buy another permanent character slot to create a new buddy."
            : $"Create a new buddy. {remaining} character slot{(remaining == 1 ? string.Empty : "s")} available.";

        if (GodotObject.IsInstanceValid(_host!.DuplicateButton))
        {
            _host.DuplicateButton.Disabled = full || _purchaseBusy || _host.Session.WorkingDocument is null;
            _host.DuplicateButton.TooltipText = full
                ? "Buy another character slot before duplicating this buddy."
                : "Duplicate the selected character into a new slot.";
        }

        _buyButton!.Visible = full;
        _buyButton.Disabled = _purchaseBusy || _sandbox!.Economy.BalanceMilliCredits < _slots.NextPriceMilliCredits;
        _buyButton.Text = _purchaseBusy ? "Buying slot..." : $"Buy permanent slot — {nextCredits} cr";

        _slotLabel!.Text = _transientStatus ?? (full
            ? $"{occupied}/{_slots.Capacity} slots used"
            : $"{occupied}/{_slots.Capacity} slots used • {remaining} free");
    }

    private int CountOccupiedSlots()
    {
        string root = ProjectSettings.GlobalizePath("user://characters");
        int count = 0;
        if (Directory.Exists(root))
        {
            foreach (string directory in Directory.EnumerateDirectories(root))
            {
                string name = Path.GetFileName(Path.TrimEndingDirectorySeparator(directory));
                if (name.Length == 32 && Guid.TryParseExact(name, "N", out _) &&
                    File.Exists(Path.Combine(directory, "character.json")))
                    count++;
            }
        }

        // A freshly-created/duplicated working copy has not reached disk yet but already reserves
        // the next available slot for this editor session.
        if (_host?.Session.WorkingDocument is { } working && _host.Session.IsDirty)
        {
            string expected = Path.Combine(root, working.Id.ToString("N"), "character.json");
            if (!File.Exists(expected))
                count++;
        }
        return count;
    }

    private async void PurchaseSlot()
    {
        if (_purchaseBusy || _slots is null || !GodotObject.IsInstanceValid(_sandbox))
            return;

        _purchaseBusy = true;
        try
        {
            PurchaseResult result = _slots.PurchaseNext();
            if (result.Succeeded || result.Status == PurchaseStatus.AlreadyOwned)
            {
                await FlushProgressObservedAsync();
                _transientStatus = "Permanent character slot added.";
                _statusSeconds = 2.0;
            }
            else
            {
                _transientStatus = result.Status == PurchaseStatus.InsufficientFunds
                    ? "Not enough credits for another slot."
                    : $"Slot purchase failed: {result.Status}.";
                _statusSeconds = 2.5;
            }
        }
        finally
        {
            _purchaseBusy = false;
            RefreshState();
        }
    }

    private async Task FlushProgressObservedAsync()
    {
        try
        {
            await _sandbox!.Saves.FlushProgressAsync(force: true);
        }
        catch (Exception exception)
        {
            GD.PushWarning($"Character slot entitlement remains dirty after save failure: {exception.Message}");
        }
    }
}
