namespace DesktopBuddy.Objects;

/// <summary>
/// Which drawn shape a loose object takes in the Mii3D presentation. Authored per profile, so
/// giving an object a model is a <c>.tres</c> field rather than a new presenter — and an
/// object that authors nothing keeps the flat circle it has always had, in both modes.
/// </summary>
public enum LooseObjectVisualKind
{
    /// <summary>Flat circle only, in both presentation modes. The default.</summary>
    None,

    /// <summary>A traditional panelled ball.</summary>
    SoccerBall,

    /// <summary>A generic drink can — no wordmark, no real product's trade dress.</summary>
    Can,

    /// <summary>A first-aid case: white lid, coloured base, a cross, and a carry handle.</summary>
    RepairKit,

    /// <summary>A stitched ball: off-white sphere with the traditional two-lobed seam.</summary>
    Baseball,
}
