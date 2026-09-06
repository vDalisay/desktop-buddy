using System;
using System.Linq;
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
/// This partial normalizes those rows after they are composed. It does not continuously shove the
/// split bar around; the previous SplitOffsets workaround was fragile because SplitOffsets are
/// relative to the SplitContainer's normal layout, not absolute pixel coordinates.
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

    private void NormalizeSubscriptionRows()
    {
        if (!GodotObject.IsInstanceValid(_subscriptions))
            return;

        foreach (Node child in _subscriptions.GetChildren())
        {
            if (child is not HBoxContainer oldRow || oldRow.GetMeta("responsive_workshop_row", false).AsBool())
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

        // Never let a restored/dragged native Workshop window exceed the usable desktop. This is a
        // final native-window guard; the responsive rows above are what keep the content itself
        // below the Window's minimum width.
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
