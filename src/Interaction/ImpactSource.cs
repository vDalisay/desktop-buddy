namespace DesktopBuddy.Interaction;

/// <summary>
/// Attribution contract for anything whose physical contact can enter the pain
/// pipeline (RAGDOLL §7.1). <see cref="InteractionId"/> is the stable per-instance
/// half of the contact-episode key; <see cref="ContentId"/> is the tool/content
/// attribution used for statistics and harmful-history memory — a
/// <see cref="DesktopBuddy.Domain.Tools.ToolId"/> value for tools, or a negative
/// <see cref="ImpactContent"/> constant for non-tool sources.
/// </summary>
public interface IImpactSource
{
    int InteractionId { get; }

    int ContentId { get; }
}

/// <summary>
/// Non-tool content attributions. Negative so they can never collide with
/// <see cref="DesktopBuddy.Domain.Tools.ToolId"/> values. The generic loose-object
/// source covers post-expiry impacts until the originating-throw attribution
/// machinery lands with the full tool catalogue (RAGDOLL §7.1).
/// </summary>
public static class ImpactContent
{
    public const int LooseObject = -1;
    public const int RoomBoundary = -2;
}

/// <summary>Process-monotonic interaction-instance ID allocator.</summary>
public static class InteractionIds
{
    private static int _next;

    public static int Next() => ++_next;
}
