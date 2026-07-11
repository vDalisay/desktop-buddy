using Godot;

namespace DesktopBuddy.Buddy.Physics;

/// <summary>Read-only physical standing measurements from the current tick.</summary>
public readonly record struct StandingSnapshot(
    int SupportContactCount,
    float TorsoTilt,
    float HeadAboveTorso,
    float FeetBelowTorso,
    float CenterOfMassError,
    float MaximumBodySpeed,
    int StableTicks,
    bool MeetsCriteria,
    bool IsStable,
    Vector2 CenterOfMass,
    Vector2 SupportCenter);
