namespace DesktopBuddy.Interaction;

/// <summary>
/// Attribution contract for anything whose physical contact can enter the pain
/// pipeline (RAGDOLL §7.1). <see cref="InteractionId"/> is the stable per-instance
/// half of the contact-episode key; <see cref="ContentId"/> is the stable content
/// attribution used for statistics and harmful-history memory — a
/// <see cref="DesktopBuddy.Domain.Content.ContentIds"/> value, always a plain
/// <c>string</c> so it can cross domain and save seams unchanged (ARCHITECTURE §5).
/// </summary>
public interface IImpactSource
{
    int InteractionId { get; }

    string ContentId { get; }
}

/// <summary>Process-monotonic interaction-instance ID allocator.</summary>
public static class InteractionIds
{
    private static int _next;

    public static int Next() => ++_next;
}
