namespace DesktopBuddy.Domain.Platform;

/// <summary>
/// Source-controlled Godot custom feature names used to identify Desktop Buddy build surfaces.
/// <c>steam</c> is platform capability only; content entitlement is resolved from the distribution
/// tags below and must never be widened merely because Steam is present.
/// </summary>
public static class BuildFeatureTags
{
    public const string Steam = "steam";
    public const string SteamDemo = "steam_demo";
    public const string NextFestDemo = "next_fest_demo";
    public const string FullRelease = "full_release";
    public const string ItchIo = "itch_io";
}

/// <summary>The mutually exclusive content surface selected for one executable.</summary>
public enum BuildSurface
{
    /// <summary>
    /// No recognized release entitlement tag. This deliberately behaves like the least-privileged
    /// normal desktop content surface rather than silently widening to Next Fest or Full Release.
    /// </summary>
    UntaggedFallback = 0,
    ItchIo = 1,
    SteamDemo = 2,
    NextFestDemo = 3,
    FullRelease = 4,
}

/// <summary>
/// Pure release-scope resolver for the three cumulative Steam builds plus itch.io and the untagged
/// development/failure fallback. Keeping the decision Godot-free makes the complete tag truth table
/// testable without starting the engine.
///
/// Invalid combinations fail closed. itch.io always wins because it is the most reduced public
/// distribution. A Full Release tag mixed with Demo-family tags never grants Full Release content;
/// if <c>steam_demo</c> is present it falls back to the Initial Steam Demo, otherwise to the untagged
/// fallback. <c>next_fest_demo</c> grants nothing unless <c>steam_demo</c> is also present.
/// </summary>
public readonly struct BuildScopePolicy
{
    public const int NextFestMaximumSceneCount = 10;

    private BuildScopePolicy(BuildSurface surface)
    {
        Surface = surface;
    }

    public BuildSurface Surface { get; }

    public bool IsItchIo => Surface == BuildSurface.ItchIo;

    /// <summary>
    /// True for both versions of the Steam Demo product. Next Fest replaces the public Demo build
    /// under the same Demo AppID; it is not a separate Steam product entitlement.
    /// </summary>
    public bool IsSteamDemo =>
        Surface is BuildSurface.SteamDemo or BuildSurface.NextFestDemo;

    public bool IsInitialSteamDemo => Surface == BuildSurface.SteamDemo;

    public bool IsNextFestDemo => Surface == BuildSurface.NextFestDemo;

    public bool IsFullRelease => Surface == BuildSurface.FullRelease;

    public bool IsUntaggedFallback => Surface == BuildSurface.UntaggedFallback;

    /// <summary>
    /// The Scene/multi-Buddy product surface starts at Next Fest. Initial Demo, itch.io and an
    /// untagged/fail-closed build must not expose Scene creation or switching.
    /// </summary>
    public bool IncludesScenes =>
        Surface is BuildSurface.NextFestDemo or BuildSurface.FullRelease;

    /// <summary>
    /// Room Decorator becomes public only after its Environment state is Scene-owned. That boundary
    /// is now the same Next Fest + Full surface as Scenes themselves; Initial Demo and itch remain
    /// deliberately unchanged, and malformed tags inherit the resolver's fail-closed surface.
    /// </summary>
    public bool IncludesRoomDecorator => IncludesScenes;

    /// <summary>
    /// Next Fest has the owner-locked ten-Scene product cap. Full Release has no artificial Scene
    /// count cap; null means practical storage/UI/safety policy rather than entitlement. A zero
    /// value means the active build surface does not include Scenes at all.
    /// </summary>
    public int? MaximumSceneCount => Surface switch
    {
        BuildSurface.NextFestDemo => NextFestMaximumSceneCount,
        BuildSurface.FullRelease => null,
        _ => 0,
    };

    public bool CanCreateScene(int existingSceneCount)
    {
        if (existingSceneCount < 0)
            throw new System.ArgumentOutOfRangeException(nameof(existingSceneCount));
        if (!IncludesScenes)
            return false;
        return MaximumSceneCount is not int limit || existingSceneCount < limit;
    }

    public static BuildScopePolicy Resolve(
        bool itchIo,
        bool steamDemo,
        bool nextFestDemo,
        bool fullRelease)
    {
        // The separately reduced itch distribution is always the least-privileged winner.
        if (itchIo)
            return new BuildScopePolicy(BuildSurface.ItchIo);

        // Contradictory Full/Demo tags are a packaging error. Never let that error widen content.
        if (fullRelease && (steamDemo || nextFestDemo))
        {
            return new BuildScopePolicy(
                steamDemo ? BuildSurface.SteamDemo : BuildSurface.UntaggedFallback);
        }

        if (fullRelease)
            return new BuildScopePolicy(BuildSurface.FullRelease);

        // A stray next_fest_demo tag must not unlock event-only content by itself.
        if (nextFestDemo && !steamDemo)
            return new BuildScopePolicy(BuildSurface.UntaggedFallback);

        if (steamDemo)
        {
            return new BuildScopePolicy(
                nextFestDemo ? BuildSurface.NextFestDemo : BuildSurface.SteamDemo);
        }

        return new BuildScopePolicy(BuildSurface.UntaggedFallback);
    }
}
