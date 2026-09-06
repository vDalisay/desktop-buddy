using System;
using System.Linq;
using System.Threading.Tasks;
using Godot;

namespace DesktopBuddy.Sharing;

/// <summary>
/// Responsive composition guard for the free-floating Workshop window.
///
/// The Workshop must be able to shrink to its native window width without any child advertising a
/// wider minimum size. In particular, the legal notice used to live on the same HBox as the Browse
/// buttons, and subscription/import action buttons used to be single unbreakable rows. Godot then
/// quite correctly laid the controls out wider than the Window, so the right edge (including the
/// title-bar close button) was clipped by the native surface.
///
/// This partial also owns the publish-button wrappers. The core publish methods predate Demo
/// mirroring and only show the success dialog for a plain Published result. A first Demo item may
/// instead return NeedsLegalAgreement, and a successful Demo item may be followed by a failed
/// full-game mirror. In both cases the author still needs access to the real Demo item page.
/// </summary>
public partial class WorkshopPanel
{
    private bool _workshopStaticLayoutNormalized;

    public override void _Process(double delta)
    {
        if (!_built || !Visible)
            return;

        if (!_workshopStaticLayoutNormalized)
        {
            NormalizeStaticWorkshopLayout();
            _workshopStaticLayoutNormalized = true;
        }

        NormalizeSubscriptionRows();
        NormalizeImportedRoomActionRows();
        KeepWorkshopInsideUsableScreen();
    }

    private void NormalizeStaticWorkshopLayout()
    {
        NormalizePublishButtons();

        // The legal sentence is intentionally not part of the Browse-button HBox. A long, single
        // line label in that HBox was the largest minimum-width contributor and could make the
        // complete Workshop root wider than the native Window from the first frame.
        Label? legal = FindChildren("*", nameof(Label), true, false)
            .OfType<Label>()
            .FirstOrDefault(label => label.Text.StartsWith(
                "Publishing is subject to the Steam Workshop Legal Agreement",
                StringComparison.Ordinal));
        if (legal?.GetParent() is HBoxContainer browseRow && browseRow.GetParent() is VBoxContainer column)
        {
            int browseIndex = browseRow.GetIndex();
            legal.Reparent(column, false);
            column.MoveChild(legal, browseIndex + 1);
            legal.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            legal.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            legal.CustomMinimumSize = Vector2.Zero;
        }

        // Let Godot calculate the natural split from the two equally expanding panes. Do not add
        // an artificial offset; doing so is what moved the divider progressively to the right.
        HSplitContainer? split = FindChildren("*", nameof(HSplitContainer), true, false)
            .OfType<HSplitContainer>()
            .FirstOrDefault();
        if (split is not null)
        {
            split.SplitOffsets = [0];
            split.CustomMinimumSize = Vector2.Zero;
            foreach (Control pane in split.GetChildren().OfType<Control>())
            {
                pane.CustomMinimumSize = Vector2.Zero;
                pane.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
                pane.SizeFlagsStretchRatio = 1.0f;
            }
        }
    }

    private void NormalizePublishButtons()
    {
        Button? oldRoom = FindChildren("*", nameof(Button), true, false)
            .OfType<Button>()
            .FirstOrDefault(button => button.Text == "Publish Room Painting");
        Button? oldBuddy = FindChildren("*", nameof(Button), true, false)
            .OfType<Button>()
            .FirstOrDefault(button => button.Text == "Publish Active Buddy");
        if (oldRoom?.GetParent() is not HBoxContainer publishRow || oldBuddy?.GetParent() != publishRow)
            return;

        int roomIndex = oldRoom.GetIndex();
        int buddyIndex = oldBuddy.GetIndex();

        var room = new Button { Text = oldRoom.Text };
        room.Pressed += () => _ = PublishRoomWithDemoFeedbackAsync();
        publishRow.AddChild(room);
        publishRow.MoveChild(room, roomIndex);

        var buddy = new Button { Text = oldBuddy.Text };
        buddy.Pressed += () => _ = PublishBuddyWithDemoFeedbackAsync();
        publishRow.AddChild(buddy);
        publishRow.MoveChild(buddy, buddyIndex);

        _operationButtons.Remove(oldRoom);
        _operationButtons.Remove(oldBuddy);
        _operationButtons.Add(room);
        _operationButtons.Add(buddy);
        _publishBuddy = buddy;

        oldRoom.QueueFree();
        oldBuddy.QueueFree();
    }

    private async Task PublishRoomWithDemoFeedbackAsync()
    {
        if (_busy || _sharing is null || _environment is null || _previews is null) return;
        byte[] pixels = _environment.SnapshotRoomPaintingForSharing();
        SetStatus("Preparing room preview...");
        await RunBusyAsync(async (progress, token) =>
        {
            byte[] preview = await _previews.CaptureRoomAsync(token);
            SetStatus("Publishing room painting...");
            WorkshopPublishResult result = await _sharing.PublishRoomAsync(
                pixels,
                _title.Text,
                _description.Text,
                preview,
                progress,
                token);
            PresentPublishResult(result, "Room painting");
        });
    }

    private async Task PublishBuddyWithDemoFeedbackAsync()
    {
        if (_busy || _sharing is null || _previews is null || _selection?.ActiveCharacterId is not Guid id) return;
        SetStatus("Preparing buddy preview...");
        await RunBusyAsync(async (progress, token) =>
        {
            byte[] preview = await _previews.CaptureBuddyAsync(id, token);
            SetStatus("Publishing active buddy...");
            WorkshopPublishResult result = await _sharing.PublishCharacterAsync(
                id,
                _title.Text,
                _description.Text,
                preview,
                progress,
                token);
            PresentPublishResult(result, "Buddy");
        });
    }

    private void PresentPublishResult(WorkshopPublishResult result, string noun)
    {
        SetPublishStatus(result, noun);

        if (result.PublishedFileId == 0)
            return;

        if (result.Status == WorkshopPublishStatus.NeedsLegalAgreement)
        {
            ShowPublishSuccess(noun, result.PublishedFileId);
            _publishSuccessMessage.Text =
                $"{noun} uploaded as Workshop item {result.PublishedFileId}. Steam still requires the Workshop Legal Agreement. " +
                "Open the item page, accept the agreement if prompted, and make sure the item is Public.";
            return;
        }

        // The Demo item itself is already real at this point; only the extra full-game mirror
        // failed. Keep the warning, but do not strand the author without a way to open the Demo
        // item that Steamworks needs for the Workshop checklist.
        if (result.Status == WorkshopPublishStatus.Failed &&
            result.Detail?.StartsWith("Published to the Demo Workshop as item", StringComparison.OrdinalIgnoreCase) == true)
        {
            ShowPublishSuccess(noun, result.PublishedFileId);
            _publishSuccessMessage.Text = result.Detail +
                "\n\nThe Demo item is available to open. You can retry the full-game mirror separately after checking Steamworks permissions.";
        }
    }

    private void NormalizeSubscriptionRows()
    {
        if (!GodotObject.IsInstanceValid(_subscriptions))
            return;

        foreach (Node child in _subscriptions.GetChildren())
        {
            if (child is not HBoxContainer oldRow || oldRow.HasMeta("responsive_workshop_row"))
                continue;

            Button[] buttons = oldRow.GetChildren().OfType<Button>().ToArray();
            if (buttons.Length < 2)
            {
                oldRow.SetMeta("responsive_workshop_row", true);
                continue;
            }

            // Keep the title on its own row and wrap Open / Import / Unsubscribe underneath. This
            // prevents one long subscription title plus three buttons from setting the pane width.
            int index = oldRow.GetIndex();
            var replacement = new VBoxContainer
            {
                Name = oldRow.Name,
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            };
            replacement.SetMeta("responsive_workshop_row", true);
            _subscriptions.AddChild(replacement);
            _subscriptions.MoveChild(replacement, index);

            buttons[0].Reparent(replacement, false);
            var actions = new GridContainer
            {
                Name = "SubscriptionActionGrid",
                Columns = 3,
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            };
            replacement.AddChild(actions);
            foreach (Button button in buttons.Skip(1))
            {
                button.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
                button.Reparent(actions, false);
            }

            oldRow.QueueFree();
        }
    }

    private void NormalizeImportedRoomActionRows()
    {
        if (!GodotObject.IsInstanceValid(_roomLibrary))
            return;

        foreach (Node child in _roomLibrary.GetChildren())
        {
            if (child is not VBoxContainer row || row.FindChild("ImportedRoomActionGrid", false, false) is not null)
                continue;

            HBoxContainer? oldActions = row.GetChildren().OfType<HBoxContainer>().FirstOrDefault();
            if (oldActions is null)
                continue;

            Button[] buttons = oldActions.GetChildren().OfType<Button>().ToArray();
            if (buttons.Length == 0)
                continue;

            int oldIndex = oldActions.GetIndex();
            var grid = new GridContainer
            {
                Name = "ImportedRoomActionGrid",
                Columns = 2,
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            };
            row.AddChild(grid);
            row.MoveChild(grid, oldIndex);

            foreach (Button button in buttons)
            {
                button.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
                button.Reparent(grid, false);
            }

            oldActions.QueueFree();
        }
    }

    private void KeepWorkshopInsideUsableScreen()
    {
        Rect2I usable = DisplayServer.ScreenGetUsableRect(DisplayServer.WindowGetCurrentScreen());
        Vector2I maximum = new(
            Math.Max(MinSize.X, usable.Size.X),
            Math.Max(MinSize.Y, usable.Size.Y));

        Vector2I clampedSize = new(
            Math.Clamp(Size.X, MinSize.X, maximum.X),
            Math.Clamp(Size.Y, MinSize.Y, maximum.Y));
        if (clampedSize != Size)
            Size = clampedSize;

        int maxX = usable.End.X - Size.X;
        int maxY = usable.End.Y - Size.Y;
        Vector2I clampedPosition = new(
            Math.Clamp(Position.X, usable.Position.X, Math.Max(usable.Position.X, maxX)),
            Math.Clamp(Position.Y, usable.Position.Y, Math.Max(usable.Position.Y, maxY)));
        if (clampedPosition != Position)
            Position = clampedPosition;
    }
}
