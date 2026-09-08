using System;

namespace DesktopBuddy.Domain.Achievements;

/// <summary>
/// Engine-independent classifiers for trick achievements. Runtime adapters supply observations;
/// these helpers own only the semantic threshold so behavior is testable without Godot physics.
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
