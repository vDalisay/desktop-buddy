using System;

namespace DesktopBuddy.Domain.Damage;

/// <summary>
/// The authored timing of scorch marks — how a body part that has been on fire darkens,
/// how long the mark stays, and how long it takes to fade (owner feedback, 2026-08-01).
/// In routed physics ticks rather than seconds, so a paused laboratory holds a scorch mark
/// exactly as it holds a burn.
/// </summary>
public readonly record struct ScorchConstants(
    /// <summary>Routed ticks of continuous burning to reach <see cref="MaxDarkness"/>.</summary>
    int TicksToFullDarkness,

    /// <summary>
    /// The darkest a part may ever get, as a fraction toward the authored scorch colour.
    /// Deliberately below one: a fully black limb reads as a hole in the buddy rather than
    /// as a burnt one, and the buddy cannot be damaged permanently.
    /// </summary>
    float MaxDarkness,

    /// <summary>Ticks the accumulated mark holds after the part stops burning — 10 s.</summary>
    int HoldTicks,

    /// <summary>Ticks the mark then takes to fade back to clean skin — 5 s.</summary>
    int FadeTicks)
{
    /// <summary>The owner's stated shape: hold 10 s, fade over the following 5 s.</summary>
    public static ScorchConstants Default => new(
        TicksToFullDarkness: 720,
        MaxDarkness: 0.72f,
        HoldTicks: 1200,
        FadeTicks: 600);

    /// <summary>
    /// Ill-formed constants make the model inert rather than clamped, for the same reason
    /// <see cref="BurningConstants"/> does: a mark that silently ran on invented numbers
    /// would be indistinguishable from a tuned one.
    /// </summary>
    public bool IsWellFormed() =>
        TicksToFullDarkness > 0 &&
        float.IsFinite(MaxDarkness) && MaxDarkness > 0.0f && MaxDarkness <= 1.0f &&
        HoldTicks >= 0 &&
        FadeTicks > 0;
}

/// <summary>
/// One body part's scorch state. Immutable in and out, on the
/// <see cref="BurningStatus"/> idiom: the caller stores what it was handed and feeds it
/// back, so nothing can observe a half-advanced fade.
/// </summary>
public readonly record struct ScorchPhase(
    /// <summary>How dark this part is right now, in <c>[0, MaxDarkness]</c>.</summary>
    float Darkness,

    /// <summary>Ticks left before the mark starts fading. Re-armed by any further burning.</summary>
    int HoldTicksRemaining,

    /// <summary>Ticks left of the fade, or zero when the mark is not fading.</summary>
    int FadeTicksRemaining,

    /// <summary>
    /// The darkness the fade started from. Carried so the fade always lands exactly on
    /// clean skin in the authored time, whether the part was lightly singed or fully
    /// scorched — a fixed per-tick decrement would take four times as long for the latter.
    /// </summary>
    float FadeFromDarkness)
{
    /// <summary>Clean skin: never burned, or fully recovered.</summary>
    public static ScorchPhase None => new(0.0f, 0, 0, 0.0f);

    /// <summary>True while the part is drawn any darker than clean skin.</summary>
    public bool IsMarked => Darkness > 0.0f;

    /// <summary>True while the mark is holding at full strength before the fade begins.</summary>
    public bool IsHolding => IsMarked && HoldTicksRemaining > 0;

    /// <summary>True while the mark is on its way back to clean skin.</summary>
    public bool IsFading => IsMarked && HoldTicksRemaining <= 0 && FadeTicksRemaining > 0;
}

/// <summary>Allocation-free result of one scorch tick.</summary>
public readonly record struct ScorchTickResult(
    ScorchPhase Phase,

    /// <summary>True on the tick the part finished fading and is clean again.</summary>
    bool Cleared,
    bool IsValid);

/// <summary>
/// The pure per-part scorch model (owner feedback, 2026-08-01). Engine-free and
/// allocation-free, and deliberately separate from <see cref="BurningStatus"/>: burning is
/// a gameplay status with pain, panic and attribution, whereas scorch is a mark on the
/// skin. Nothing here reaches the damage pipeline — it decides a number that presentation
/// tints with, and nothing else.
///
/// <list type="number">
///   <item><b>Darkening accumulates while the part burns</b>, at a rate that reaches the
///   authored ceiling after <see cref="ScorchConstants.TicksToFullDarkness"/>. A part
///   burned twice is darker than one burned once, up to the ceiling.</item>
///   <item><b>The ceiling is never exceeded.</b> A part can be held in the stream forever
///   and still never goes fully black.</item>
///   <item><b>The mark holds, then fades.</b> Ten seconds at full strength after the fire
///   goes out, then five seconds back to clean skin, both authored.</item>
///   <item><b>Any further burning re-arms the hold</b> and cancels a running fade, so a
///   part that catches again does not keep recovering underneath the new fire.</item>
///   <item><b>Clear is immediate and idempotent</b> — the hard reposition's fail-safe, on
///   exactly the same entry point that puts the burn itself out.</item>
/// </list>
/// </summary>
public static class ScorchState
{
    /// <summary>Advances one routed tick for one part.</summary>
    public static ScorchTickResult Tick(
        in ScorchPhase phase,
        bool burning,
        in ScorchConstants constants)
    {
        if (!constants.IsWellFormed())
        {
            return new ScorchTickResult(phase, Cleared: false, IsValid: false);
        }

        if (burning)
        {
            float step = constants.MaxDarkness / constants.TicksToFullDarkness;
            float darkness = Math.Min(phase.Darkness + step, constants.MaxDarkness);
            // The hold is re-armed and any running fade abandoned: a part that catches fire
            // again is not still quietly recovering underneath the new flames.
            return new ScorchTickResult(
                new ScorchPhase(darkness, constants.HoldTicks, 0, 0.0f),
                Cleared: false,
                IsValid: true);
        }

        if (!phase.IsMarked)
        {
            return new ScorchTickResult(ScorchPhase.None, Cleared: false, IsValid: true);
        }

        if (phase.HoldTicksRemaining > 0)
        {
            int hold = phase.HoldTicksRemaining - 1;
            // The fade is armed on the tick the hold runs out, from wherever the mark got
            // to, so it always lands on clean skin in the authored time.
            return new ScorchTickResult(
                hold > 0
                    ? phase with { HoldTicksRemaining = hold }
                    : new ScorchPhase(
                        phase.Darkness, 0, constants.FadeTicks, phase.Darkness),
                Cleared: false,
                IsValid: true);
        }

        int fade = phase.FadeTicksRemaining - 1;
        if (fade <= 0)
        {
            return new ScorchTickResult(ScorchPhase.None, Cleared: true, IsValid: true);
        }

        float remaining = phase.FadeFromDarkness * (fade / (float)constants.FadeTicks);
        return new ScorchTickResult(
            new ScorchPhase(remaining, 0, fade, phase.FadeFromDarkness),
            Cleared: false,
            IsValid: true);
    }

    /// <summary>
    /// Wipes a part's mark immediately. Idempotent, and the entry point the centralized
    /// hard reposition uses — the same fail-safe that puts Burning itself out.
    /// </summary>
    public static ScorchPhase Clear(in ScorchPhase _phase) => ScorchPhase.None;
}
