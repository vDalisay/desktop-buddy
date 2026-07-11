using System;

namespace DesktopBuddy.Domain.Physics;

/// <summary>Immutable fixed-tick recovery timing snapshot.</summary>
public readonly record struct RecoveryClockState(
    int UnableTicks,
    int AssistanceTicks,
    bool AssistanceActive,
    float AssistanceRamp,
    bool HardRecoveryDue);

/// <summary>
/// Exact tick-counted standing recovery clock. At 120 Hz assistance starts
/// after two seconds, reaches full strength over five seconds, and permits a
/// hard reset only after ten further seconds of failed assistance.
/// </summary>
public sealed class RecoveryClock
{
    public const int PhysicsTicksPerSecond = 120;
    public const int AssistanceDelayTicks = 2 * PhysicsTicksPerSecond;
    public const int AssistanceRampTicks = 5 * PhysicsTicksPerSecond;
    public const int HardRecoveryDelayTicks = 10 * PhysicsTicksPerSecond;

    private int _unableTicks;

    public RecoveryClockState State { get; private set; }

    public RecoveryClockState Tick(bool stableStanding, bool conscious)
    {
        if (stableStanding || !conscious)
        {
            Reset();
            return State;
        }

        _unableTicks++;
        int assistanceTicks = Math.Max(0, _unableTicks - AssistanceDelayTicks);
        bool assistanceActive = _unableTicks >= AssistanceDelayTicks;
        float ramp = assistanceActive
            ? Math.Clamp(assistanceTicks / (float)AssistanceRampTicks, 0.0f, 1.0f)
            : 0.0f;
        bool hardRecoveryDue = assistanceActive && assistanceTicks >= HardRecoveryDelayTicks;

        State = new RecoveryClockState(
            _unableTicks,
            assistanceTicks,
            assistanceActive,
            ramp,
            hardRecoveryDue);
        return State;
    }

    public void Reset()
    {
        _unableTicks = 0;
        State = default;
    }
}
