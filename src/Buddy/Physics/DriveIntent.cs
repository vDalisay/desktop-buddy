namespace DesktopBuddy.Buddy.Physics;

/// <summary>
/// Resolved actuation request handed to <see cref="ActiveDriveComponent"/> for
/// one tick. The buddy root arbitrates its sources: a player-constraint fear
/// response (priority 4) supersedes ambient autonomy (priority 7), so when
/// <see cref="ResistanceStrength"/> is positive the drive applies bounded
/// opposing force instead of walking/jumping. The drive only applies the
/// resolved intent; it never chooses it.
/// </summary>
public readonly record struct DriveIntent(
    float WalkDirection,
    bool JumpRequested,
    float ResistanceDirection,
    float ResistanceStrength);
