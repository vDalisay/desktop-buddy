namespace DesktopBuddy.UI.Win98;

public static class TopLevelCommandIds
{
    public const string DecorateRoom = "command.decorate_room";
    public const int DecorateRoomOrder = 100;
    public const string BuddyStudio = "command.buddy_studio";
    public const int BuddyStudioOrder = 200;
}

public readonly record struct TopLevelCommandDefinition(
    string Id,
    string Label,
    string Tooltip,
    int Order);
