using Godot;

namespace DesktopBuddy.Objects;

/// <summary>How placing one authored loose object treats existing loose objects in the room.</summary>
public enum LooseObjectSpawnPolicy
{
    ReplaceExisting = 0,
    Additive = 1,
}

public partial class LooseObjectProfile
{
    /// <summary>
    /// Existing content keeps the historical room-wide replacement behavior by default. Content
    /// such as grenades may explicitly opt into additive placement while the registry remains the
    /// authoritative capacity/eviction boundary.
    /// </summary>
    [Export] public LooseObjectSpawnPolicy SpawnPolicy { get; set; } = LooseObjectSpawnPolicy.ReplaceExisting;
}
