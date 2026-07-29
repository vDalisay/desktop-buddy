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

    public ActivityId Current { get; private set; } = ActivityId.None;
    public int RemainingTicks { get; private set; }
    // Refusing is a two-handed "no thanks" the buddy performs standing still, holding the
    // thing it is about to put down — so it keeps the eat reach and the stationary gate.
    public bool IsStationary => Current is ActivityId.Eat or ActivityId.Refuse;
    public bool EatReachActive => Current is ActivityId.Eat or ActivityId.Refuse;

    /// <summary>True while the buddy is shaking its head at something it will not eat.</summary>
    public bool IsRefusing => Current == ActivityId.Refuse;
    public int EatBitesCompleted { get; private set; }
    public int EatBiteCount => Profile.EatBiteCount;
    public float EatItemScale => Current == ActivityId.Eat
        ? Mathf.Clamp(1.0f - (EatBitesCompleted / (float)Profile.EatBiteCount), 0.0f, 1.0f)
        : 0.0f;
    public float EatCycleProgress { get; private set; }
    public float EatLift { get; private set; }
    public float EatFinalLowering { get; private set; }
    public bool IsInitialized { get; private set; }

    public void Initialize()
    {
        if (!GodotObject.IsInstanceValid(Profile) || Profile.Validate().Count > 0)
            throw new InvalidOperationException("BehaviorActivityComponent requires a valid profile.");
        IsInitialized = true;
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
            ActivityId.Eat => Profile.EatChestHoldTicks +
                (Profile.EatBiteCount * Profile.EatBiteCycleTicks) +
                Profile.EatFinalLowerHoldTicks,
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

    private void TickEat()
    {
        int biteSequenceTicks = Profile.EatBiteCount * Profile.EatBiteCycleTicks;
        int totalTicks = Profile.EatChestHoldTicks + biteSequenceTicks +
            Profile.EatFinalLowerHoldTicks;
        int elapsed = totalTicks - RemainingTicks;
        if (elapsed < Profile.EatChestHoldTicks)
        {
            EatCycleProgress = 0.0f;
            EatLift = 0.0f;
            EatFinalLowering = 0.0f;
            return;
        }

        int eatingTick = elapsed - Profile.EatChestHoldTicks;
        if (eatingTick >= biteSequenceTicks)
        {
            EatCycleProgress = 1.0f;
            EatLift = 0.0f;
            EatFinalLowering = 1.0f;
            return;
        }

        int cycleTick = eatingTick % Profile.EatBiteCycleTicks;
        EatCycleProgress = cycleTick / (float)Profile.EatBiteCycleTicks;

        // Smooth chest-to-mouth lift, a short bite hold, then return to the chest.
        EatLift = EatCycleProgress switch
        {
            < 0.35f => Mathf.SmoothStep(0.0f, 1.0f, EatCycleProgress / 0.35f),
            < 0.68f => 1.0f,
            _ => Mathf.SmoothStep(1.0f, 0.0f, (EatCycleProgress - 0.68f) / 0.32f),
        };

        EatFinalLowering = EatBitesCompleted >= Profile.EatBiteCount &&
            EatCycleProgress >= 0.68f
            ? Mathf.SmoothStep(0.0f, 1.0f, (EatCycleProgress - 0.68f) / 0.32f)
            : 0.0f;

        int biteTick = Mathf.RoundToInt(Profile.EatBiteMoment * Profile.EatBiteCycleTicks);
        if (cycleTick == biteTick && EatBitesCompleted < Profile.EatBiteCount)
        {
            EatBitesCompleted++;
            EatBiteCompleted?.Invoke(EatBitesCompleted, Profile.EatBiteCount);
        }
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
