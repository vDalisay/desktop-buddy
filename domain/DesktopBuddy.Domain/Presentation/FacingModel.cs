using System;
using DesktopBuddy.Domain.Autonomy;

namespace DesktopBuddy.Domain.Presentation;

/// <summary>Committed facing side. Frontal exists only before the first commitment.</summary>
public enum FacingSide
{
    Frontal = 0,
    Left = 1,
    Right = 2,
}

/// <summary>
/// Semantic inputs to facing arbitration, sampled per rendered frame. InteractionSide is
/// the sign of the engaged cursor relative to the buddy (positive = cursor to the right);
/// zero keeps the current side while engaged.
/// </summary>
public readonly record struct FacingInputs(
    bool InteractionEngaged,
    float InteractionSide,
    float WalkDirection);

/// <summary>Facing tuning subset consumed by the pure model.</summary>
public readonly record struct FacingParameters(
    float YawDegrees,
    float TurnSeconds,
    int WalkCommitTicks,
    float WalkDeadband,
    int IdleFlipMinimumTicks,
    int IdleFlipMaximumTicks);

/// <summary>
/// Pure facing arbitration and easing (M3_6_EXPRESSIVE_PRESENTATION_PLAN.md Task 2).
/// Priority: an engaged interaction biases toward the cursor's side immediately; a
/// sustained drive walk direction commits its side only after the hysteresis streak
/// (autonomy jitter cannot flip-flop the model); seeded idle variety occasionally flips
/// the side while idle. The yaw eases start-to-target through zero on a monotonic
/// smoothstep, so it can never overshoot the accepted magnitude. Presentation-only:
/// nothing here reads or writes physics.
/// </summary>
public sealed class FacingModel
{
    private readonly IRandomSource _random;
    private readonly FacingParameters _parameters;

    private float _startYawDegrees;
    private float _targetYawDegrees;
    private double _turnProgress = 1.0;
    private float _walkStreakSign;
    private int _walkStreakTicks;
    private bool _idleTimerArmed;
    private int _idleTicksRemaining;

    public FacingModel(IRandomSource random, in FacingParameters parameters)
    {
        _random = random ?? throw new ArgumentNullException(nameof(random));
        if (parameters.YawDegrees <= 0.0f || !float.IsFinite(parameters.YawDegrees) ||
            parameters.TurnSeconds <= 0.0f || !float.IsFinite(parameters.TurnSeconds) ||
            parameters.WalkCommitTicks < 1 ||
            !float.IsFinite(parameters.WalkDeadband) || parameters.WalkDeadband < 0.0f ||
            parameters.IdleFlipMinimumTicks < 1 ||
            parameters.IdleFlipMaximumTicks <= parameters.IdleFlipMinimumTicks)
        {
            throw new ArgumentOutOfRangeException(nameof(parameters), parameters, "Invalid facing parameters.");
        }

        _parameters = parameters;
    }

    public FacingSide CommittedSide { get; private set; } = FacingSide.Frontal;
    public float CurrentYawDegrees { get; private set; }

    /// <summary>
    /// Advances arbitration by <paramref name="ticksElapsed"/> physics ticks and the
    /// ease by <paramref name="deltaSeconds"/>; returns the current yaw in degrees.
    /// </summary>
    public float Update(in FacingInputs inputs, int ticksElapsed, double deltaSeconds)
    {
        if (ticksElapsed < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ticksElapsed), ticksElapsed, "Ticks cannot be negative.");
        }

        FacingSide wanted = CommittedSide;
        if (inputs.InteractionEngaged)
        {
            if (inputs.InteractionSide > 0.0f)
            {
                wanted = FacingSide.Right;
            }
            else if (inputs.InteractionSide < 0.0f)
            {
                wanted = FacingSide.Left;
            }

            _walkStreakTicks = 0;
            _walkStreakSign = 0.0f;
            _idleTimerArmed = false;
        }
        else if (MathF.Abs(inputs.WalkDirection) > _parameters.WalkDeadband)
        {
            float sign = MathF.Sign(inputs.WalkDirection);
            if (sign == _walkStreakSign)
            {
                _walkStreakTicks += ticksElapsed;
            }
            else
            {
                _walkStreakSign = sign;
                _walkStreakTicks = ticksElapsed;
            }

            if (_walkStreakTicks >= _parameters.WalkCommitTicks)
            {
                wanted = sign > 0.0f ? FacingSide.Right : FacingSide.Left;
            }

            _idleTimerArmed = false;
        }
        else
        {
            _walkStreakTicks = 0;
            _walkStreakSign = 0.0f;
            if (!_idleTimerArmed)
            {
                _idleTicksRemaining = NextIdleInterval();
                _idleTimerArmed = true;
            }

            _idleTicksRemaining -= ticksElapsed;
            if (_idleTicksRemaining <= 0)
            {
                wanted = CommittedSide switch
                {
                    FacingSide.Left => FacingSide.Right,
                    FacingSide.Right => FacingSide.Left,
                    _ => _random.NextInt(0, 2) == 0 ? FacingSide.Left : FacingSide.Right,
                };
                _idleTicksRemaining = NextIdleInterval();
            }
        }

        if (wanted != CommittedSide)
        {
            CommittedSide = wanted;
            _startYawDegrees = CurrentYawDegrees;
            _targetYawDegrees = wanted == FacingSide.Right
                ? _parameters.YawDegrees
                : -_parameters.YawDegrees;
            _turnProgress = 0.0;
        }

        if (_turnProgress < 1.0 && deltaSeconds > 0.0)
        {
            _turnProgress = Math.Min(1.0, _turnProgress + (deltaSeconds / _parameters.TurnSeconds));
            float eased = SmoothStep((float)_turnProgress);
            CurrentYawDegrees = _startYawDegrees + ((_targetYawDegrees - _startYawDegrees) * eased);
        }

        return CurrentYawDegrees;
    }

    private int NextIdleInterval() => _random.NextInt(
        _parameters.IdleFlipMinimumTicks, _parameters.IdleFlipMaximumTicks + 1);

    private static float SmoothStep(float t) => t * t * (3.0f - (2.0f * t));
}
