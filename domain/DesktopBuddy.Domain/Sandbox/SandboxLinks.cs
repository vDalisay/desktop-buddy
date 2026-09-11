using System;
using System.Collections.Generic;

namespace DesktopBuddy.Domain.Sandbox;

public readonly struct SandboxLinkId : IEquatable<SandboxLinkId>
{
    private SandboxLinkId(Guid value) => Value = value;

    public Guid Value { get; }
    public bool IsValid => Value != Guid.Empty;
    public static SandboxLinkId New() => new(Guid.NewGuid());

    public static SandboxLinkId From(Guid value)
    {
        if (value == Guid.Empty)
            throw new ArgumentException("A sandbox link ID cannot be empty.", nameof(value));
        return new SandboxLinkId(value);
    }

    public bool Equals(SandboxLinkId other) => Value == other.Value;
    public override bool Equals(object? obj) => obj is SandboxLinkId other && Equals(other);
    public override int GetHashCode() => Value.GetHashCode();
    public override string ToString() => IsValid ? Value.ToString("N") : string.Empty;
    public static bool operator ==(SandboxLinkId left, SandboxLinkId right) => left.Equals(right);
    public static bool operator !=(SandboxLinkId left, SandboxLinkId right) => !left.Equals(right);
}

/// <summary>The NF-3 passive links. Motors, springs and pulleys are later tools.</summary>
public enum SandboxLinkKind
{
    /// <summary>Holds two points no further apart than its length; goes slack when closer.</summary>
    Rope = 0,

    /// <summary>Pins two points together and lets the bodies turn about them.</summary>
    Hinge = 1,

    /// <summary>Locks two parts at the relative position and angle they had when welded.</summary>
    Weld = 2,
}

/// <summary>
/// One end of a link. On a part it is a point in that part's own unrotated space, in pixels, so it
/// follows the part wherever it moves or turns. With no part it is a fixed point in the room, in
/// canonical room space, so it stays put relative to the room when the window is resized.
/// </summary>
public readonly record struct SandboxLinkEnd(SandboxPartId PartId, float X, float Y)
{
    public bool IsWorld => !PartId.IsValid;

    public static SandboxLinkEnd OnPart(SandboxPartId partId, float localX, float localY)
    {
        if (!partId.IsValid)
            throw new ArgumentException("A part end requires a part.", nameof(partId));
        return new SandboxLinkEnd(partId, localX, localY);
    }

    public static SandboxLinkEnd World(float canonicalX, float canonicalY) =>
        new(default, canonicalX, canonicalY);
}

/// <summary>
/// One link. <see cref="Strength"/> (0..1) is how much it takes to break it — 1 is unbreakable.
/// <see cref="Elasticity"/> (0..1) is how far a rope stretches under load; <see cref="Stiffness"/>
/// (0..1) how hard a hinge resists turning, from swinging free to nearly locked. Each is a plain
/// fraction here; the room turns it into forces.
/// </summary>
public sealed record SandboxLink(
    SandboxLinkId LinkId,
    SandboxLinkKind Kind,
    SandboxLinkEnd A,
    SandboxLinkEnd B,
    float Length = 0.0f,
    float Strength = 1.0f,
    float Elasticity = 0.0f,
    float Stiffness = 0.0f)
{
    public const float MinimumRopeLength = 8.0f;
    public const float MaximumRopeLength = 2048.0f;

    public bool IsUnbreakable => Strength >= 1.0f;

    /// <summary>The same link with its tuning clamped into 0..1; non-finite input becomes the default.</summary>
    public SandboxLink WithTuning(float strength, float elasticity, float stiffness) => this with
    {
        Strength = Unit(strength, 1.0f),
        Elasticity = Kind == SandboxLinkKind.Rope ? Unit(elasticity, 0.0f) : 0.0f,
        Stiffness = Kind == SandboxLinkKind.Hinge ? Unit(stiffness, 0.0f) : 0.0f,
    };

    private static float Unit(float value, float fallback) => float.IsFinite(value) ? Math.Clamp(value, 0.0f, 1.0f) : fallback;

    public bool Touches(SandboxPartId partId) => A.PartId == partId || B.PartId == partId;

    /// <summary>
    /// Why this link cannot exist, or null. The first end is always on a part; a Weld also needs a
    /// part at the second end, because welding to the room is just freezing the part.
    /// </summary>
    public string? Problem()
    {
        if (!LinkId.IsValid)
            return "A link requires a stable ID.";
        if (!Enum.IsDefined(Kind))
            return $"Unknown link kind {(int)Kind}.";
        if (A.IsWorld)
            return "A link must start on a part.";
        if (Kind == SandboxLinkKind.Weld && B.IsWorld)
            return "A weld joins two parts; freeze a part to fix it to the room.";
        if (!B.IsWorld && A.PartId == B.PartId)
            return "A link cannot join a part to itself.";
        if (!IsFiniteEnd(A) || !IsFiniteEnd(B))
            return "Link ends must be finite points.";
        if (!IsLocalInBand(A) || (!B.IsWorld && !IsLocalInBand(B)))
            return "A link end must lie on its part.";
        if (B.IsWorld && (B.X is < 0.0f or > 1.0f || B.Y is < 0.0f or > 1.0f))
            return "A room anchor must lie inside the room.";
        if (Kind == SandboxLinkKind.Rope &&
            (!float.IsFinite(Length) || Length < MinimumRopeLength || Length > MaximumRopeLength))
        {
            return $"A rope must be {MinimumRopeLength}-{MaximumRopeLength} pixels long.";
        }
        if (!IsUnit(Strength) || !IsUnit(Elasticity) || !IsUnit(Stiffness))
            return "A link's strength, stretch and stiffness must each be between 0 and 1.";
        return null;
    }

    /// <summary>Same kind joining the same two parts, in either order. Room-anchored links never clash.</summary>
    public bool Duplicates(SandboxLink other) =>
        Kind == other.Kind && !B.IsWorld && !other.B.IsWorld &&
        ((A.PartId == other.A.PartId && B.PartId == other.B.PartId) ||
         (A.PartId == other.B.PartId && B.PartId == other.A.PartId));

    private static bool IsFiniteEnd(SandboxLinkEnd end) => float.IsFinite(end.X) && float.IsFinite(end.Y);

    private static bool IsUnit(float value) => float.IsFinite(value) && value is >= 0.0f and <= 1.0f;

    private static bool IsLocalInBand(SandboxLinkEnd end) =>
        Math.Abs(end.X) <= SandboxPartDefinition.MaximumExtent &&
        Math.Abs(end.Y) <= SandboxPartDefinition.MaximumExtent;
}

/// <summary>A named starting point for a new link, as the Build palette offers it.</summary>
public sealed record SandboxLinkPreset(
    string Name,
    SandboxLinkKind Kind,
    float Strength,
    float Elasticity,
    float Stiffness,
    string Description);

public static class SandboxLinkPresets
{
    /// <summary>In palette order; the first of each kind is what the kind's hotkey picks.</summary>
    public static IReadOnlyList<SandboxLinkPreset> All { get; } =
    [
        new("Strong Rope", SandboxLinkKind.Rope, 1.0f, 0.1f, 0.0f, "Ties a part to another part or the room. Never snaps."),
        new("Medium Rope", SandboxLinkKind.Rope, 0.6f, 0.25f, 0.0f, "Holds a beam or a Buddy, but a heavy jerk will snap it."),
        new("Weak Rope", SandboxLinkKind.Rope, 0.25f, 0.3f, 0.0f, "String. Snaps under anything heavy."),
        new("Bungee", SandboxLinkKind.Rope, 0.8f, 0.9f, 0.0f, "Stretches a long way and springs back."),
        new("Free Hinge", SandboxLinkKind.Hinge, 1.0f, 0.0f, 0.0f, "An axle where two parts overlap, or a pin to the room. Swings freely."),
        new("Stiff Hinge", SandboxLinkKind.Hinge, 1.0f, 0.0f, 0.6f, "Bends under load and springs back to where it was made."),
        new("Weak Hinge", SandboxLinkKind.Hinge, 0.3f, 0.0f, 0.0f, "Swings freely, and tears loose when yanked."),
        new("Unbreakable Weld", SandboxLinkKind.Weld, 1.0f, 0.0f, 0.0f, "Locks two overlapping parts together for good."),
        new("Strong Weld", SandboxLinkKind.Weld, 0.7f, 0.0f, 0.0f, "Locks two parts together until something big hits it."),
        new("Weak Weld", SandboxLinkKind.Weld, 0.3f, 0.0f, 0.0f, "Tacked on. Knocks loose easily."),
    ];
}

public enum SandboxLinkStatus
{
    Succeeded = 0,
    LimitReached,
    PartNotFound,
    LinkNotFound,
    Invalid,
    Duplicate,
    WireNotFound,
}

public readonly record struct SandboxLinkResult(
    SandboxLinkStatus Status,
    SandboxLink? Link = null,
    string? Detail = null)
{
    public bool Succeeded => Status == SandboxLinkStatus.Succeeded;
}
