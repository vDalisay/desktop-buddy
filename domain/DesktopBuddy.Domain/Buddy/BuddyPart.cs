namespace DesktopBuddy.Domain.Buddy;

/// <summary>
/// Engine-free mirror of the approved six-body anatomy's stable IDs. Values match the
/// Godot-side <c>BuddyPartId</c> so contacts, grabs, expressions, statistics, and saves
/// never depend on scene node names (RAGDOLL §6). Gameplay logic in the Domain keys on
/// this; the Godot layer casts between the two identical enums.
/// </summary>
public enum BuddyPart
{
    Head = 0,
    Torso = 1,
    LeftHand = 2,
    RightHand = 3,
    LeftFoot = 4,
    RightFoot = 5,
}
