using System;

namespace DesktopBuddy.Domain.Autonomy;

public enum AutonomousMotionGoal
{
    Idle,
    WalkLeft,
    WalkRight,
}

public readonly record struct AutonomousMotionTuning(
    int MinimumIdleTicks,
    int MaximumIdleTicks,
    int MinimumWalkTicks,
    int MaximumWalkTicks,
    int MinimumJumpIntervalTicks,
    int MaximumJumpIntervalTicks,
    int IdleWeight,
    int WalkLeftWeight,
    int WalkRightWeight,
    // Owner switch (2026-07-20): ambient timer-driven jumping reads as random noise, so
    // it ships off. The jump ACTUATION path is untouched and still reachable — tool
    // reactions hop, and M4's behaviours will jump for reasons. The interval range stays
    // valid data so re-enabling is a one-flag change.
    bool AmbientJumpsEnabled = true)
{
    public void Validate()
    {
        ValidateRange(MinimumIdleTicks, MaximumIdleTicks, nameof(MinimumIdleTicks));
        ValidateRange(MinimumWalkTicks, MaximumWalkTicks, nameof(MinimumWalkTicks));
        ValidateRange(
            MinimumJumpIntervalTicks,
            MaximumJumpIntervalTicks,
            nameof(MinimumJumpIntervalTicks));

        if (IdleWeight < 0 || WalkLeftWeight < 0 || WalkRightWeight < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(IdleWeight), "Goal weights cannot be negative.");
        }

        if (IdleWeight + WalkLeftWeight + WalkRightWeight <= 0)
        {
            throw new ArgumentException("At least one autonomy goal weight must be positive.");
        }
    }

    private static void ValidateRange(int minimum, int maximum, string parameterName)
    {
        if (minimum <= 0 || maximum < minimum || maximum == int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                "Tick ranges must be positive, ordered, and safely sampleable.");
        }
    }
}

public readonly record struct AutonomousMotionIntent(
    AutonomousMotionGoal Goal,
    float WalkDirection,
    bool JumpRequested,
    bool IsSuppressed);

/// <summary>
/// Tick-counted ambient motion planner. Suppression pauses its decision stream
/// so a higher-priority state does not consume random choices behind the scenes.
/// It chooses intent only; the Godot drive component owns force application.
/// </summary>
public sealed class AutonomousMotionPlanner
{
    private readonly IRandomSource _random;
    private readonly AutonomousMotionTuning _tuning;
    private AutonomousMotionGoal _goal;
    private int _goalTicksRemaining;
    private int _jumpTicksRemaining;

    public AutonomousMotionPlanner(IRandomSource random, AutonomousMotionTuning tuning)
    {
        _random = random ?? throw new ArgumentNullException(nameof(random));
        tuning.Validate();
        _tuning = tuning;
        SelectNextGoal(blockedLeft: false, blockedRight: false);
        if (_tuning.AmbientJumpsEnabled)
        {
            ScheduleNextJump();
        }
    }

    public AutonomousMotionGoal Goal => _goal;
    public int GoalTicksRemaining => _goalTicksRemaining;
    public int JumpTicksRemaining => _jumpTicksRemaining;

    public AutonomousMotionIntent Tick(
        bool enabled,
        bool canWalk,
        bool canJump,
        bool blockedLeft = false,
        bool blockedRight = false)
    {
        if (!enabled)
        {
            return new AutonomousMotionIntent(_goal, 0.0f, false, true);
        }

        bool currentGoalBlocked =
            (_goal == AutonomousMotionGoal.WalkLeft && blockedLeft) ||
            (_goal == AutonomousMotionGoal.WalkRight && blockedRight);
        if (_goalTicksRemaining <= 0 || currentGoalBlocked)
        {
            SelectNextGoal(blockedLeft, blockedRight);
        }

        _goalTicksRemaining--;

        // Disabled means the timer does not exist: no countdown, no draws from the seeded
        // stream, never a request. Only the ambient timer is gated here.
        bool jumpRequested = false;
        if (_tuning.AmbientJumpsEnabled)
        {
            if (_jumpTicksRemaining > 0)
            {
                _jumpTicksRemaining--;
            }

            jumpRequested = canJump && _jumpTicksRemaining == 0;
            if (jumpRequested)
            {
                ScheduleNextJump();
            }
        }

        float direction = canWalk
            ? _goal switch
            {
                AutonomousMotionGoal.WalkLeft => -1.0f,
                AutonomousMotionGoal.WalkRight => 1.0f,
                _ => 0.0f,
            }
            : 0.0f;

        return new AutonomousMotionIntent(_goal, direction, jumpRequested, false);
    }

    private void SelectNextGoal(bool blockedLeft, bool blockedRight)
    {
        int leftWeight = blockedLeft ? 0 : _tuning.WalkLeftWeight;
        int rightWeight = blockedRight ? 0 : _tuning.WalkRightWeight;
        int totalWeight = _tuning.IdleWeight + leftWeight + rightWeight;
        if (totalWeight <= 0)
        {
            // Both directions are unavailable and the profile has no idle weight. The
            // safe fallback is still idle; wall avoidance may never manufacture motion.
            _goal = AutonomousMotionGoal.Idle;
            _goalTicksRemaining = SampleInclusive(
                _tuning.MinimumIdleTicks, _tuning.MaximumIdleTicks);
            return;
        }

        int selection = _random.NextInt(0, totalWeight);

        if (selection < _tuning.IdleWeight)
        {
            _goal = AutonomousMotionGoal.Idle;
            _goalTicksRemaining = SampleInclusive(_tuning.MinimumIdleTicks, _tuning.MaximumIdleTicks);
            return;
        }

        selection -= _tuning.IdleWeight;
        if (selection < leftWeight)
        {
            _goal = AutonomousMotionGoal.WalkLeft;
        }
        else
        {
            _goal = AutonomousMotionGoal.WalkRight;
        }

        _goalTicksRemaining = SampleInclusive(_tuning.MinimumWalkTicks, _tuning.MaximumWalkTicks);
    }

    private void ScheduleNextJump()
    {
        _jumpTicksRemaining = SampleInclusive(
            _tuning.MinimumJumpIntervalTicks,
            _tuning.MaximumJumpIntervalTicks);
    }

    private int SampleInclusive(int minimum, int maximum) => _random.NextInt(minimum, maximum + 1);
}
