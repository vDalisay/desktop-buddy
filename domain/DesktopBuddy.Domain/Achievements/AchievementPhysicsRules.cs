using System;

namespace DesktopBuddy.Domain.Achievements;

/// <summary>
/// Small engine-independent classifiers for physics achievements. Runtime adapters supply the
/// observations; this type owns only the semantic threshold so trick-achievement behavior can be
/// unit-tested without a Godot process.
/// </summary>
public static class AchievementPhysicsRules
{
    public static bool IsSideWallRicochet(
        float previousVelocityX,
        float currentVelocityX,
        bool nearLeftWall,
        bool nearRightWall,
        float minimumHorizontalSpeed)
    {
        if (!float.IsFinite(previousVelocityX) || !float.IsFinite(currentVelocityX) ||
            !float.IsFinite(minimumHorizontalSpeed))
        {
            return false;
        }

        float minimum = Math.Max(0.0f, minimumHorizontalSpeed);
        bool reboundedFromLeft = nearLeftWall &&
            previousVelocityX < -minimum &&
            currentVelocityX > minimum;
        bool reboundedFromRight = nearRightWall &&
            previousVelocityX > minimum &&
            currentVelocityX < -minimum;
        return reboundedFromLeft || reboundedFromRight;
    }
}
