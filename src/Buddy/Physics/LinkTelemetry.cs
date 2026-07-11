using Godot;

namespace DesktopBuddy.Buddy.Physics;

/// <summary>Fixed-buffer development measurement for one structural link.</summary>
public readonly record struct LinkTelemetry(
    StringName LinkId,
    float Separation,
    float Strain,
    Vector2 ForceOnA,
    bool LimitActive,
    bool ForceClamped);
