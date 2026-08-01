using System;

namespace DesktopBuddy.Domain.Presentation;

/// <summary>How a consumable is taken.</summary>
public enum ConsumeGestureStyle
{
    /// <summary>The Meal: repeated chest-to-mouth bites, one care step each.</summary>
    Bites,

    /// <summary>
    /// The Drink: raised to the head once, held there, and then gone (owner instruction
    /// 2026-08-01). Nobody takes five bites out of a can.
    /// </summary>
    SingleRaise,
}

/// <summary>
/// One sample of a consume gesture at a routed tick.
/// </summary>
/// <param name="Lift">Chest-to-mouth blend in <c>[0, 1]</c>; the hands ride this.</param>
/// <param name="FinalLowering">
/// Return-to-rest blend in <c>[0, 1]</c>, non-zero only on the closing beat.
/// </param>
/// <param name="CycleProgress">Progress through the current beat, for presentation only.</param>
/// <param name="CompletedSteps">
/// Care steps landed so far. The gesture is finished — and the item is gone — when this
/// reaches <see cref="ConsumeGesture.StepCount"/>.
/// </param>
public readonly record struct ConsumeGestureSample(
    float Lift,
    float FinalLowering,
    float CycleProgress,
    int CompletedSteps);

/// <summary>
/// The pure schedule behind the Eat activity: how long the whole gesture runs, where the
/// hands are at any tick of it, and on which ticks a care step lands.
///
/// <para>Two styles share it because they differ only in shape, never in consequence: the
/// authoritative final step is what the care transaction completes on either way, so a Drink
/// cannot pay twice and a cancelled one still pays nothing (FR-008.10).</para>
///
/// <para>The <see cref="ConsumeGestureStyle.Bites"/> arithmetic is the M4 eat schedule moved
/// here unchanged — same windows, same easing, same bite moment — so every measured meal
/// signature is bit-identical.</para>
/// </summary>
public readonly record struct ConsumeGesture(
    ConsumeGestureStyle Style,
    int ChestHoldTicks,
    int StepCount,
    int StepCycleTicks,
    float StepMoment,
    int FinalLowerHoldTicks,
    float RiseEnd,
    float ReturnStart)
{
    /// <summary>Where the Meal's lift finishes rising, as a fraction of one bite cycle.</summary>
    private const float BiteRiseEnd = 0.35f;

    /// <summary>Where the Meal's bite hold ends and the return begins.</summary>
    private const float BiteReturnStart = 0.68f;

    /// <summary>The Meal's repeated-bite gesture, on its shipped M4 windows.</summary>
    public static ConsumeGesture Bites(
        int chestHoldTicks,
        int biteCount,
        int biteCycleTicks,
        float biteMoment,
        int finalLowerHoldTicks) =>
        new(ConsumeGestureStyle.Bites, chestHoldTicks, biteCount, biteCycleTicks, biteMoment,
            finalLowerHoldTicks, BiteRiseEnd, BiteReturnStart);

    /// <summary>
    /// The Drink's gesture: one raise to the head over <paramref name="raiseTicks"/>, a hold of
    /// exactly <paramref name="holdTicks"/> there, and then it is gone.
    ///
    /// <para>The windows are solved from the authored durations rather than borrowed from the
    /// bite cycle, because the bite cycle's hold is a third of its length and a two-second hold
    /// would have silently become two thirds of one. The closing return mirrors the raise; it
    /// is cosmetic, since the item leaves on the step at the end of the hold.</para>
    /// </summary>
    public static ConsumeGesture SingleRaise(
        int chestHoldTicks,
        int raiseTicks,
        int holdTicks)
    {
        int rise = Math.Max(1, raiseTicks);
        int hold = Math.Max(1, holdTicks);
        int cycleTicks = Math.Max(12, rise + hold + rise);
        float riseEnd = rise / (float)cycleTicks;
        float returnStart = (rise + hold) / (float)cycleTicks;
        return new ConsumeGesture(
            ConsumeGestureStyle.SingleRaise, chestHoldTicks, 1, cycleTicks, returnStart, 0,
            riseEnd, returnStart);
    }

    /// <summary>Routed ticks the whole gesture occupies.</summary>
    public int TotalTicks => ChestHoldTicks + (StepCount * StepCycleTicks) + FinalLowerHoldTicks;

    public bool IsValid =>
        ChestHoldTicks >= 0 &&
        StepCount >= 1 &&
        StepCycleTicks >= 12 &&
        float.IsFinite(StepMoment) && StepMoment is > 0.0f and < 1.0f &&
        FinalLowerHoldTicks >= 0 &&
        float.IsFinite(RiseEnd) && RiseEnd is > 0.0f and < 1.0f &&
        float.IsFinite(ReturnStart) && ReturnStart > RiseEnd && ReturnStart < 1.0f;

    /// <summary>
    /// Samples the gesture at <paramref name="elapsed"/> routed ticks in, given how many steps
    /// have already landed. Pure: the caller owns the counter, and this only says whether it
    /// should advance.
    /// </summary>
    public ConsumeGestureSample Sample(int elapsed, int completedSteps)
    {
        int sequenceTicks = StepCount * StepCycleTicks;
        if (elapsed < ChestHoldTicks)
            return new ConsumeGestureSample(0.0f, 0.0f, 0.0f, completedSteps);

        int activeTick = elapsed - ChestHoldTicks;
        if (activeTick >= sequenceTicks)
            return new ConsumeGestureSample(0.0f, 1.0f, 1.0f, completedSteps);

        int cycleTick = activeTick % StepCycleTicks;
        float cycleProgress = cycleTick / (float)StepCycleTicks;

        // Smooth chest-to-mouth lift, a hold at the mouth, then the return. The window
        // boundaries are per-gesture rather than constants, so the Drink's two-second hold is
        // two seconds instead of whatever fraction of a bite cycle happens to fall there.
        float lift;
        if (cycleProgress < RiseEnd)
            lift = SmoothStep(cycleProgress / RiseEnd);
        else if (cycleProgress < ReturnStart)
            lift = 1.0f;
        else
            lift = 1.0f - SmoothStep((cycleProgress - ReturnStart) / (1.0f - ReturnStart));

        float finalLowering = completedSteps >= StepCount && cycleProgress >= ReturnStart
            ? SmoothStep((cycleProgress - ReturnStart) / (1.0f - ReturnStart))
            : 0.0f;

        int stepTick = (int)Math.Round(StepMoment * StepCycleTicks);
        int steps = completedSteps;
        if (cycleTick == stepTick && completedSteps < StepCount)
            steps = completedSteps + 1;

        return new ConsumeGestureSample(lift, finalLowering, cycleProgress, steps);
    }

    /// <summary>
    /// The same easing the engine's <c>smoothstep</c> applies, restated here so the schedule
    /// stays engine-free and testable.
    /// </summary>
    private static float SmoothStep(float t)
    {
        float clamped = Math.Clamp(t, 0.0f, 1.0f);
        return clamped * clamped * (3.0f - (2.0f * clamped));
    }
}
