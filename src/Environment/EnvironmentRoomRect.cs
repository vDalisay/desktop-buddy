using System;
using DesktopBuddy.Domain.Environment;
using DesktopBuddy.UI.Win98;
using Godot;

namespace DesktopBuddy.Environment;

/// <summary>Screen rectangle the room occupies: the window content minus the command bar.</summary>
public static class EnvironmentRoomRect
{
    public static Rect2 Resolve(Node node)
    {
        var frame = node.GetTree().Root.FindChild(nameof(Win98WindowFrame), true, false) as Win98WindowFrame;
        Rect2 room = GodotObject.IsInstanceValid(frame) ? frame!.ContentViewportRect : node.GetViewport().GetVisibleRect();
        if (node.GetTree().Root.FindChild("Win98CommandBar", true, false) is Control bar && bar.Visible)
        {
            float top = Math.Max(room.Position.Y, bar.GetGlobalRect().End.Y);
            room = new Rect2(room.Position.X, top, room.Size.X, Math.Max(1, room.End.Y - top));
        }
        return room;
    }

    public static RoomScreenBounds Bounds(Node node)
    {
        Rect2 room = Resolve(node);
        return new RoomScreenBounds(room.Position.X, room.Position.Y, room.Size.X, room.Size.Y);
    }
}
