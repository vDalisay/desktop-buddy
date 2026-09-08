using DesktopBuddy.Domain.Characters;
using Godot;

namespace DesktopBuddy.Buddy.Behavior;

/// <summary>
/// Room-surface interest for distributions that ship no room customization.
///
/// <para>Compiled in place of <c>RoomInterestBootstrap.Environment.cs</c> when the itch scope
/// removes <c>src/Environment</c>. Both searches report "nothing found", which is what the real
/// implementation already returned there: <c>EnvironmentCustomizationBootstrap</c> returns early
/// under the itch scope, so no decoration layer and no background presenter is ever built.</para>
///
/// <para>The buddy keeps its favourite colour and the rest of its room awareness; it simply has no
/// painted wall or placed decoration to notice, because that build cannot make one.</para>
/// </summary>
public sealed partial class RoomInterestBootstrap
{
    private void ResolveRoomSurfaces()
    {
    }

    private void ForgetRoomSurfaces()
    {
    }

    private bool TryFindClosestColourDecoration(Rgba32 favorite, out Vector2 point, out double score)
    {
        point = default;
        score = double.PositiveInfinity;
        return false;
    }

    private bool TryFindClosestPaintedColour(Rgba32 favorite, out Vector2 point, out double score)
    {
        point = default;
        score = double.PositiveInfinity;
        return false;
    }
}
