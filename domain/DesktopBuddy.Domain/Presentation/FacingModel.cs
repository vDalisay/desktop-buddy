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
    float WalkDirection,
    bool ForceFrontal = false);

/// <summary>Facing tuning subset consumed by the pure model.</summary>
/// <param name="SideCommitTicks">
/// How long the engaged cursor must stay on one side before it flips the committed side.
/// The engaged path's counterpart of <paramref name="WalkCommitTicks"/>: without it a
/// cursor sitting roughly above a walking buddy flipped the side on every rendered frame,
/// and the body turned back and forth in place (owner report 2026-08-13). Zero restores
/// the original instant flip.
/// </param>
public readonly record struct FacingParameters(
    float YawDegrees,
    float TurnSeconds,
    int WalkCommitTicks,
    float WalkDeadband,
    int IdleFlipMinimumTicks,
    int IdleFlipMaximumTicks,
    int SideCommitTicks = 24);

/// <summary>
/// Pure facing arbitration and easing (M3_6_EXPRESSIVE_PRESENTATION_PLAN.md Task 2).
/// Priority: an engaged interaction biases toward the cursor's side after its own short
/// commit streak (fast, but never per-frame — see <see cref="FacingParameters"/>); a
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
    private float _sideStreakSign;
    private int _sideStreakTicks;
    private bool _holdTurnForWalkCommit;
    private bool _idleTimerArmed;
    private int _idleTicksRemaining;

    public FacingModel(IRandomSource random, in FacingParameters parameters)
    {
        _random = random ?? throw new ArgumentNullException(nameof(random));
        if (parameters.YawDegrees <= 0.0f || !float.IsFinite(parameters.YawDegrees) ||
            parameters.TurnSeconds <= 0.0f || !float.IsFinite(parameters.TurnSeconds) ||
            parameters.WalkCommitTicks < 1 ||
            parameters.SideCommitTicks < 0 ||
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
        if (!inputs.InteractionEngaged || inputs.ForceFrontal)
        {
            _sideStreakSign = 0.0f;
            _sideStreakTicks = 0;
        }

        if (inputs.ForceFrontal)
        {
            _walkStreakTicks = 0;
            _walkStreakSign = 0.0f;
            _holdTurnForWalkCommit = false;
            _idleTimerArmed = false;
        }
        else if (inputs.InteractionEngaged)
        {
            // The cursor's side must hold for the commit window before it turns the buddy,
            // so per-frame sign noise around a cursor sitting above the body cannot
            // oscillate the turn. A side already committed keeps the streak cleared, so
            // returning to it after a brief excursion costs nothing.
            float sign = MathF.Sign(inputs.InteractionSide);
            FacingSide side = sign > 0.0f ? FacingSide.Right :
                sign < 0.0f ? FacingSide.Left : CommittedSide;
            if (sign == 0.0f || side == CommittedSide)
            {
                _sideStreakSign = 0.0f;
                _sideStreakTicks = 0;
            }
            else
            {
                _sideStreakTicks = sign == _sideStreakSign ? _sideStreakTicks + ticksElapsed : ticksElapsed;
                _sideStreakSign = sign;
                if (_sideStreakTicks >= _parameters.SideCommitTicks)
                {
                    wanted = side;
                }
            }

            _walkStreakTicks = 0;
            _walkStreakSign = 0.0f;
            _holdTurnForWalkCommit = false;
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
                _holdTurnForWalkCommit = false;
            }
            else
            {
                FacingSide pending = sign > 0.0f ? FacingSide.Right : FacingSide.Left;
                _holdTurnForWalkCommit = pending != CommittedSide;
            }

            _idleTimerArmed = false;
        }
        else
        {
            _walkStreakTicks = 0;
            _walkStreakSign = 0.0f;
            _holdTurnForWalkCommit = false;
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

        if (!inputs.ForceFrontal && wanted != CommittedSide)
        {
            CommittedSide = wanted;
        }

        float desiredYawDegrees = inputs.ForceFrontal
            ? 0.0f
            : CommittedSide switch
            {
                FacingSide.Right => _parameters.YawDegrees,
                FacingSide.Left => -_parameters.YawDegrees,
                _ => 0.0f,
            };
        if (MathF.Abs(desiredYawDegrees - _targetYawDegrees) > 0.0001f)
        {
            _startYawDegrees = CurrentYawDegrees;
            _targetYawDegrees = desiredYawDegrees;
            _turnProgress = 0.0;
        }

        if (_turnProgress < 1.0 && !_holdTurnForWalkCommit && deltaSeconds > 0.0)
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
