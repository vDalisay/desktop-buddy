namespace DesktopBuddy.UI.Win98;

public static class TopLevelCommandIds
{
    public const string DecorateRoom = "command.decorate_room";
    public const int DecorateRoomOrder = 100;
    public const string BuddyStudio = "command.buddy_studio";
    public const int BuddyStudioOrder = 200;
    public const string Workshop = "command.workshop";
    public const int WorkshopOrder = 300;
}

/// <param name="DockRight">
/// Puts the command in the right-hand group beside the balance instead of the left-hand strip
/// of places to go. Outward-facing links live there so they read as a separate offer rather than
/// as another room of the game (owner instruction 2026-09-06).
/// </param>
public readonly record struct TopLevelCommandDefinition(
    string Id,
    string Label,
    string Tooltip,
    int Order,
    bool DockRight = false);