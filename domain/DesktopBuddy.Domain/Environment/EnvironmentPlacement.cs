using System;

namespace DesktopBuddy.Domain.Environment;

public enum EnvironmentGridSize { Fine, Medium, Large }

public readonly record struct RoomScreenBounds(float X, float Y, float Width, float Height)
{
    public bool IsValid => float.IsFinite(X) && float.IsFinite(Y) && float.IsFinite(Width) &&
        float.IsFinite(Height) && Width > 0f && Height > 0f;
}

public static class EnvironmentPlacement
{
    public const float FloorZoneStart = .72f;

    public static bool TryMap(
        float screenX,
        float screenY,
        in RoomScreenBounds room,
        DecorationAnchorKind anchor,
        bool snap,
        EnvironmentGridSize grid,
        out CanonicalRoomPosition position)
    {
        position = default;
        if (!room.IsValid || !float.IsFinite(screenX) || !float.IsFinite(screenY) ||
            screenX < room.X || screenX > room.X + room.Width ||
            screenY < room.Y || screenY > room.Y + room.Height ||
            !Enum.IsDefined(anchor) || !Enum.IsDefined(grid)) return false;

        float x = (screenX - room.X) / room.Width;
        float y = (screenY - room.Y) / room.Height;
        if (snap)
        {
            int divisions = grid switch
            {
                EnvironmentGridSize.Fine => 32,
                EnvironmentGridSize.Medium => 16,
                EnvironmentGridSize.Large => 8,
                _ => throw new ArgumentOutOfRangeException(nameof(grid), grid, null),
            };
            x = MathF.Round(x * divisions, MidpointRounding.AwayFromZero) / divisions;
            y = MathF.Round(y * divisions, MidpointRounding.AwayFromZero) / divisions;
        }
        position = new CanonicalRoomPosition(Math.Clamp(x, 0f, 1f), Math.Clamp(y, 0f, 1f));
        return IsValidAnchor(position, anchor);
    }

    public static (float X, float Y) ToScreen(in CanonicalRoomPosition position, in RoomScreenBounds room)
    {
        if (!room.IsValid) throw new ArgumentException("Room screen bounds are invalid.", nameof(room));
        return (room.X + position.X * room.Width, room.Y + position.Y * room.Height);
    }

    public static bool IsValidAnchor(in CanonicalRoomPosition position, DecorationAnchorKind anchor) => anchor switch
    {
        DecorationAnchorKind.Floor => position.Y >= FloorZoneStart,
        DecorationAnchorKind.Wall => position.Y < FloorZoneStart,
        DecorationAnchorKind.RoomSurface => true,
        _ => false,
    };
}
