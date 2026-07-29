using System;

namespace DesktopBuddy.Domain.Mood;

/// <summary>
/// How hard the buddy is working right now, which is what sets the appetite rate (owner
/// decision, 2026-07-29). These are states the runtime already knows; nothing new is sensed
/// to classify them.
/// </summary>
public enum HungerActivity
{
    /// <summary>The player is in Work mode or the buddy is hidden — barely burning anything.</summary>
    Working,

    /// <summary>Ordinary Play-mode presence: idling, wandering, being handled.</summary>
    Playing,

    /// <summary>Actually exerting: chasing, catching, carrying, and throwing objects.</summary>
    Exerting,
}

/// <summary>
/// Maps what the runtime knows about the session onto an appetite rate. Pure so the mapping
/// is testable without a scene: the lifecycle coordinator only supplies the three facts.
/// </summary>
public static class HungerActivityPolicy
{
    /// <param name="hidden">Hidden to tray or a locked session.</param>
    /// <param name="workMode">The shell is in Work mode — the player is doing something else.</param>
    /// <param name="activeInteraction">
    /// The buddy is being handled or is handling something: grabbed, gloved, petted, or
    /// carrying an object. This is the "playing baseball" case.
    /// </param>
    public static HungerActivity Classify(bool hidden, bool workMode, bool activeInteraction)
    {
        // Hidden and Work mode are the same thing for the stomach: the buddy is idling on
        // someone else's desktop, not playing.
        if (hidden || workMode)
            return HungerActivity.Working;

        return activeInteraction ? HungerActivity.Exerting : HungerActivity.Playing;
    }
}

/// <summary>
/// Approved appetite tuning (owner decision, 2026-07-29). Rates are points of fullness lost
/// per minute; the bar itself is <see cref="Capacity"/> points wide.
/// </summary>
public readonly record struct HungerTuning(
    float Capacity = 200.0f,
    float WorkingDrainPerMinute = 2.0f,
    float PlayingDrainPerMinute = 10.0f,
    float ExertingDrainPerMinute = 20.0f)
{
    /// <summary>
    /// Spelled out rather than <c>new()</c>: on a record struct that is the zero value, which
    /// would silently hand every buddy a zero-width stomach.
    /// </summary>
    public static HungerTuning Default => new(200.0f, 2.0f, 10.0f, 20.0f);

    public float DrainPerMinute(HungerActivity activity) => activity switch
    {
        HungerActivity.Working => WorkingDrainPerMinute,
        HungerActivity.Playing => PlayingDrainPerMinute,
        HungerActivity.Exerting => ExertingDrainPerMinute,
        _ => throw new ArgumentOutOfRangeException(nameof(activity), activity, "Unknown activity."),
    };

    public void Validate()
    {
        if (!float.IsFinite(Capacity) || Capacity <= 0.0f)
            throw new ArgumentOutOfRangeException(nameof(Capacity), Capacity, "Capacity must be finite and positive.");
        if (!float.IsFinite(WorkingDrainPerMinute) || WorkingDrainPerMinute < 0.0f ||
            !float.IsFinite(PlayingDrainPerMinute) || PlayingDrainPerMinute < 0.0f ||
            !float.IsFinite(ExertingDrainPerMinute) || ExertingDrainPerMinute < 0.0f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(PlayingDrainPerMinute),
                "Appetite rates must be finite and non-negative.");
        }
    }
}

/// <summary>
/// The buddy's appetite: a hidden <c>0…Capacity</c> fullness bar that food fills and time
/// empties (owner decision, 2026-07-29). It replaces the per-item reuse cooldown for food:
/// whether the buddy eats is now a question about its stomach, not about a timer since the
/// last bite.
///
/// <para><b>The rule is arithmetic, not a threshold.</b> The buddy accepts an item only when
/// it fits — <c>fullness + fill &lt;= capacity</c>. A nearly full buddy will still take a
/// small snack while refusing a large meal, which is exactly the intended choice: portion
/// size matters, not just how hungry it is.</para>
///
/// <para>Fullness is persistent semantic state, like mood: it survives a relaunch rather than
/// resetting the buddy's stomach every session. Drain is applied from elapsed seconds on the
/// routed clock and never catches up across a suspend (FR-016.8).</para>
/// </summary>
public sealed class HungerModel
{
    /// <summary>
    /// Floating-point slack for the fits-exactly case. An item that exactly fills the bar is
    /// accepted; without this, 160 + 40 against a 200 bar could refuse on a rounding hair.
    /// </summary>
    private const float FitEpsilon = 0.001f;

    private readonly HungerTuning _tuning;
    private float _fullness;

    public HungerModel(HungerTuning? tuning = null, float initialFullness = 0.0f)
    {
        _tuning = tuning ?? HungerTuning.Default;
        _tuning.Validate();
        _fullness = Clamp(initialFullness);
    }

    /// <summary>Current fullness in points. <c>0</c> is famished, <see cref="Capacity"/> is full.</summary>
    public float Fullness => _fullness;

    public float Capacity => _tuning.Capacity;

    /// <summary>Points of room left in the bar — the largest item the buddy would accept.</summary>
    public float Appetite => Math.Max(0.0f, _tuning.Capacity - _fullness);

    /// <summary>
    /// Whether an item of this size fits. A non-filling item (<c>0</c>) always fits, so a
    /// consumable that is not food is never refused for appetite.
    /// </summary>
    public bool Accepts(float fill) =>
        float.IsFinite(fill) && fill <= 0.0f ||
        (float.IsFinite(fill) && _fullness + fill <= _tuning.Capacity + FitEpsilon);

    /// <summary>Eats an item, filling the bar and clamping at capacity.</summary>
    public void Fill(float amount)
    {
        if (!float.IsFinite(amount) || amount <= 0.0f)
            return;

        _fullness = Clamp(_fullness + amount);
    }

    /// <summary>
    /// Burns appetite over a monotonic elapsed span at the rate for what the buddy is doing.
    /// Negative or non-finite spans are ignored, so a clock correction cannot feed the buddy.
    /// </summary>
    public void Drain(double elapsedSeconds, HungerActivity activity)
    {
        if (!double.IsFinite(elapsedSeconds) || elapsedSeconds <= 0.0)
            return;

        double perSecond = _tuning.DrainPerMinute(activity) / 60.0;
        _fullness = Clamp(_fullness - (float)(perSecond * elapsedSeconds));
    }

    /// <summary>Restores persisted fullness on load.</summary>
    public void Restore(float fullness) => _fullness = Clamp(fullness);

    private float Clamp(float value) =>
        !float.IsFinite(value) ? 0.0f : Math.Clamp(value, 0.0f, _tuning.Capacity);
}
