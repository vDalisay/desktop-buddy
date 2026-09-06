using System.Linq;
using DesktopBuddy.UI.Win98;
using Godot;

namespace DesktopBuddy.Sharing;

/// <summary>
/// Keeps the free-floating Workshop usable as its native window is moved/resized. The original
/// layout gave the subscriptions pane a fixed 545 px split and put all four imported-room actions
/// on one horizontal line. Their combined minimum width could therefore exceed the right pane and
/// make the right edge/actions look clipped even though the native Window itself was still intact.
/// </summary>
public partial class WorkshopPanel
{
    public override void _Process(double delta)
    {
        if (!_built || !Visible)
            return;

        // Re-apply after content changes too, not only after a native resize. Publishing/importing
        // can change child minimum sizes and make SplitContainer clamp itself again on the next
        // layout pass even though the Window width did not change.
        BalanceWorkshopColumns();
        NormalizeImportedRoomActionRows();
    }

    private void BalanceWorkshopColumns()
    {
        HSplitContainer? split = FindChildren("*", nameof(HSplitContainer), true, false)
            .OfType<HSplitContainer>()
            .FirstOrDefault();
        if (split is null)
            return;

        foreach (Control pane in split.GetChildren().OfType<Control>().Take(2))
        {
            pane.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            pane.SizeFlagsStretchRatio = 1.0f;
        }

        // Godot 4.6 SplitOffsets are offsets *from the container's default split*, not absolute
        // pixel positions. The previous guard wrote ~half the window width here, which shifted an
        // already-even split hundreds of pixels to the right and caused exactly the clipping it
        // was meant to fix. Zero means "use the equal/default split".
        split.DraggingEnabled = false;
        split.SplitOffsets = [0];
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
            grid.AddThemeConstantOverride("h_separation", Win98ThemeFactory.Px(4));
            grid.AddThemeConstantOverride("v_separation", Win98ThemeFactory.Px(3));
            row.AddChild(grid);
            row.MoveChild(grid, oldIndex);

            foreach (Button button in buttons)
                button.Reparent(grid, false);

            oldActions.QueueFree();
        }
    }
}
