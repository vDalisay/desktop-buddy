using System;
using DesktopBuddy.Domain.Presentation;
using Godot;

namespace DesktopBuddy.Buddy.Behavior;

/// <summary>
/// Authoritative fixed-tick state for behavior-backed activities. It owns only
/// semantic duration and gameplay intent; visual clip selection remains presentation.
/// </summary>
[GlobalClass]
public partial class BehaviorActivityComponent : Node
{
    public event Action<ActivityId>? ActivityChanged;
    public event Action<int, int>? EatBiteCompleted;

    [Export] public BehaviorActivityProfile Profile { get; set; } = null!;

    private ConsumeGesture _gesture;

    public ActivityId Current { get; private set; } = ActivityId.None;
    public int RemainingTicks { get; private set; }
    // Refusing is performed standing still, so it shares the stationary gate — but NOT the
    // eat reach: the buddy holds the thing it is refusing in ONE hand and shakes its head at
    // the player, rather than raising it to its mouth with both (owner correction 2026-07-29).
    public bool IsStationary => Current is ActivityId.Eat or ActivityId.Refuse;
    public bool EatReachActive => Current == ActivityId.Eat;

    /// <summary>True while the buddy is shaking its head at something it will not eat.</summary>
    public bool IsRefusing => Current == ActivityId.Refuse;

    /// <summary>
    /// How far through the refusal window the buddy is, in <c>[0, 1]</c>. Presentation seeks
    /// the head-shake clip by this rather than advancing it in real time, so the two shakes
    /// always fill exactly the authored window instead of finishing early and leaving the
    /// buddy standing frozen with food in its hand.
    /// </summary>
    public float RefuseProgress => Current == ActivityId.Refuse
        ? Mathf.Clamp(1.0f - (RemainingTicks / (float)Profile.RefuseDurationTicks), 0.0f, 1.0f)
        : 0.0f;
    public int EatBitesCompleted { get; private set; }

    /// <summary>
    /// Care steps the running gesture takes. Five for the Meal's bites, one for the Drink's
    /// single raise (owner instruction 2026-08-01) -- the schedule is authored per item, so
    /// this is the gesture's number rather than the profile's.
    /// </summary>
    public int EatBiteCount => _gesture.IsValid ? _gesture.StepCount : Profile.EatBiteCount;

    /// <summary>The gesture the current (or next) Eat runs.</summary>
    public ConsumeGesture Gesture => _gesture;
    public float EatItemScale => Current == ActivityId.Eat
        ? Mathf.Clamp(1.0f - (EatBitesCompleted / (float)EatBiteCount), 0.0f, 1.0f)
        : 0.0f;
    public float EatCycleProgress { get; private set; }
    public float EatLift { get; private set; }
    public float EatFinalLowering { get; private set; }
    public bool IsInitialized { get; private set; }

    public void Initialize()
    {
        if (!GodotObject.IsInstanceValid(Profile) || Profile.Validate().Count > 0)
            throw new InvalidOperationException("BehaviorActivityComponent requires a valid profile.");
        _gesture = DefaultGesture;
        IsInitialized = true;
    }

    /// <summary>The authored Meal schedule, and the fallback for anything that authors none.</summary>
    public ConsumeGesture MealGesture => DefaultGesture;

    private ConsumeGesture DefaultGesture => ConsumeGesture.Bites(
        Profile.EatChestHoldTicks,
        Profile.EatBiteCount,
        Profile.EatBiteCycleTicks,
        Profile.EatBiteMoment,
        Profile.EatFinalLowerHoldTicks);

    /// <summary>
    /// Chooses the schedule the next Eat runs. Called by the object worker from the item's own
    /// profile immediately before <see cref="SetActivity"/>; an invalid one falls back to the
    /// Meal's, so a malformed drink is slow rather than broken.
    /// </summary>
    public void SetConsumeGesture(ConsumeGesture gesture)
    {
        if (!IsInitialized)
            throw new InvalidOperationException("BehaviorActivityComponent used before initialization.");
        _gesture = gesture.IsValid ? gesture : DefaultGesture;
    }

    public void SetActivity(ActivityId activity)
    {
        if (!IsInitialized)
            throw new InvalidOperationException("BehaviorActivityComponent used before initialization.");
        if (activity is not (ActivityId.None or ActivityId.Eat or ActivityId.Wave or ActivityId.Refuse))
            throw new ArgumentOutOfRangeException(nameof(activity), activity, "Activity is not behavior-backed.");

        if (activity == ActivityId.None)
        {
            Clear();
            return;
        }

        Current = activity;
        EatBitesCompleted = 0;
        EatCycleProgress = 0.0f;
        EatLift = 0.0f;
        EatFinalLowering = 0.0f;
        RemainingTicks = activity switch
        {
            ActivityId.Eat => _gesture.TotalTicks,
            ActivityId.Refuse => Profile.RefuseDurationTicks,
            _ => Profile.WaveDurationTicks,
        };
        ActivityChanged?.Invoke(Current);
    }

    public void PhysicsTick()
    {
        if (!IsInitialized)
            throw new InvalidOperationException("BehaviorActivityComponent used before initialization.");
        if (RemainingTicks <= 0)
            return;

        if (Current == ActivityId.Eat)
            TickEat();

        RemainingTicks--;
        if (RemainingTicks == 0)
            Clear();
    }

    public void Interrupt() => Clear();

    /// <summary>
    /// Plays one routed tick of the running consume gesture. The schedule itself -- windows,
    /// easing, and which tick a care step lands on -- lives in the engine-free
    /// <see cref="ConsumeGesture"/>; this only holds the counter and raises the event.
    /// </summary>
    private void TickEat()
    {
        int elapsed = _gesture.TotalTicks - RemainingTicks;
        ConsumeGestureSample sample = _gesture.Sample(elapsed, EatBitesCompleted);
        EatCycleProgress = sample.CycleProgress;
        EatLift = sample.Lift;
        EatFinalLowering = sample.FinalLowering;

        if (sample.CompletedSteps <= EatBitesCompleted)
            return;

        EatBitesCompleted = sample.CompletedSteps;
        EatBiteCompleted?.Invoke(EatBitesCompleted, _gesture.StepCount);
    }

    private void Clear()
    {
        if (Current == ActivityId.None && RemainingTicks == 0)
            return;
        Current = ActivityId.None;
        RemainingTicks = 0;
        EatCycleProgress = 0.0f;
        EatLift = 0.0f;
        EatFinalLowering = 0.0f;
        ActivityChanged?.Invoke(Current);
    }
}
