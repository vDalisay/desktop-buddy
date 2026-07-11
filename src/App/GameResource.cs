using Godot;

namespace DesktopBuddy.App;

/// <summary>
/// Base type for the project's typed static-content Resources
/// (ARCHITECTURE.md Section 6 / NFR-006.2). Concrete tuning definitions
/// (rig profile, drive profile, pain profile, tool definitions, mood/economy
/// profile, ...) arrive with the milestone that consumes them; this base and
/// the <see cref="StartupValidator"/> that runs <see cref="Validate"/> are the
/// seam established in Milestone 0.
///
/// Resources are immutable at runtime: mutable counters/timers belong to
/// component state or versioned JSON saves, never to a Resource. Subclasses
/// override <see cref="Validate"/> to reject malformed data at startup (missing
/// PackedScenes, invalid cooldowns, incomplete six-part definitions, ...).
/// </summary>
[GlobalClass]
public partial class GameResource : Resource
{
    /// <summary>
    /// Returns human-readable validation error messages for this resource;
    /// an empty array means the resource is valid. Startup fails fast on any
    /// non-empty result in development builds (ARCHITECTURE.md Section 16).
    /// </summary>
    public virtual Godot.Collections.Array<string> Validate() => new();
}
