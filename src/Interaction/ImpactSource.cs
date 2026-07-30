using DesktopBuddy.Domain.Tools;

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

/// <summary>
/// What a contact from a swing-capable tool is allowed to become, carried as an
/// immutable snapshot rather than read back from mutable controller state. The
/// architecture observes contacts one tick after the solver produced them, so a
/// source that answered "what charge am I at?" at observation time would answer
/// about the wrong moment — this travels with the contact instead.
/// </summary>
public readonly record struct SwingImpactContext(
    SwingImpactMode Mode,
    int SwingEpoch,
    float ReleasedCharge,
    long ReleasedTick)
{
    /// <summary>An ordinary cursor-dragged tool: scored the way it always was.</summary>
    public static SwingImpactContext FreeSwing { get; } =
        new(SwingImpactMode.WeakFreeSwing, 0, 0.0f, 0L);
}

/// <summary>
/// Implemented by impact sources whose contacts depend on what the player was
/// doing with them. A source that does not implement this is scored exactly as
/// before, so loose objects, projectiles, and every non-swing tool are untouched.
/// </summary>
public interface ISwingImpactSource
{
    SwingImpactContext SwingContext { get; }
}

/// <summary>Process-monotonic interaction-instance ID allocator.</summary>
public static class InteractionIds
{
    private static int _next;

    public static int Next() => ++_next;
}
