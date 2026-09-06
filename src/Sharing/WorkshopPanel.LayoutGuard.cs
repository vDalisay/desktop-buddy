using System;
using System.Threading.Tasks;
using Godot;

namespace DesktopBuddy.Sharing;

/// <summary>
/// Publish feedback and the size rule for the free-floating Workshop window.
///
/// The core publish methods predate Demo mirroring and only show the success dialog for a plain
/// Published result. A first Demo item may instead return NeedsLegalAgreement, and a successful
/// Demo item may be followed by a failed full-game mirror. In both cases the author still needs
/// access to the real Demo item page.
///
/// The window itself is composed in Win98ThemeFactory.Px units, so its size has to follow the
/// interface scale. It never shrinks below the width its own content composes to: Godot does not
/// reflow an overflowing child, the native surface simply clips it, taking the right-hand buttons
/// and part of the title bar with them.
/// </summary>
public partial class WorkshopPanel
{
    public override void _Process(double delta)
    {
        if (_built && Visible)
            KeepWorkshopInsideUsableScreen();
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

    private void KeepWorkshopInsideUsableScreen()
    {
        Rect2I usable = DisplayServer.ScreenGetUsableRect(DisplayServer.WindowGetCurrentScreen());
        if (usable.Size.X <= 0 || usable.Size.Y <= 0)
            return;

        // A window narrower than its own content does not shrink that content - the overhang is
        // simply clipped by the native surface, taking the right-hand buttons and part of the
        // close box with it. So the minimum tracks the composed width, exactly as a detached
        // Win98 panel does, and it re-tracks it after every interface scale change.
        Vector2 content = _root.GetCombinedMinimumSize();
        Vector2I minimum = new(
            Math.Clamp(Mathf.CeilToInt(content.X), 1, usable.Size.X),
            Math.Clamp(Mathf.CeilToInt(content.Y), 1, usable.Size.Y));
        if (minimum != MinSize)
            MinSize = minimum;

        Vector2I clampedSize = new(
            Math.Clamp(Size.X, minimum.X, Math.Max(minimum.X, usable.Size.X)),
            Math.Clamp(Size.Y, minimum.Y, Math.Max(minimum.Y, usable.Size.Y)));
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
