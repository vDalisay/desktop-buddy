using System;
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
    private int _lastGuardedWorkshopWidth = -1;

    public override void _Process(double delta)
    {
        if (!_built || !Visible)
            return;

        if (_lastGuardedWorkshopWidth != Size.X)
        {
            _lastGuardedWorkshopWidth = Size.X;
            BalanceWorkshopColumns();
        }

        NormalizeImportedRoomActionRows();
    }

    private void BalanceWorkshopColumns()
    {
        HSplitContainer? split = FindChildren("*", nameof(HSplitContainer), true, false)
            .OfType<HSplitContainer>()
            .FirstOrDefault();
        if (split is null)
            return;

        // Keep both sides useful instead of permanently reserving 545 px for subscriptions.
        // The right side is the one with the denser controls, so a roughly even split is the
        // stable default at every user-resizable window width.
        int inset = Win98ThemeFactory.Px(20);
        int available = Math.Max(1, Size.X - inset);
        int minimumPane = Math.Min(Win98ThemeFactory.Px(280), available / 2);
        int target = Math.Clamp(available / 2, minimumPane, Math.Max(minimumPane, available - minimumPane));
        split.SplitOffsets = [target];
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
