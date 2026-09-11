using System;

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

public sealed record SandboxLink(
    SandboxLinkId LinkId,
    SandboxLinkKind Kind,
    SandboxLinkEnd A,
    SandboxLinkEnd B,
    float Length = 0.0f)
{
    public const float MinimumRopeLength = 8.0f;
    public const float MaximumRopeLength = 2048.0f;

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
        return null;
    }

    /// <summary>Same kind joining the same two parts, in either order. Room-anchored links never clash.</summary>
    public bool Duplicates(SandboxLink other) =>
        Kind == other.Kind && !B.IsWorld && !other.B.IsWorld &&
        ((A.PartId == other.A.PartId && B.PartId == other.B.PartId) ||
         (A.PartId == other.B.PartId && B.PartId == other.A.PartId));

    private static bool IsFiniteEnd(SandboxLinkEnd end) => float.IsFinite(end.X) && float.IsFinite(end.Y);

    private static bool IsLocalInBand(SandboxLinkEnd end) =>
        Math.Abs(end.X) <= SandboxPartDefinition.MaximumExtent &&
        Math.Abs(end.Y) <= SandboxPartDefinition.MaximumExtent;
}

public enum SandboxLinkStatus
{
    Succeeded = 0,
    LimitReached,
    PartNotFound,
    LinkNotFound,
    Invalid,
    Duplicate,
}

public readonly record struct SandboxLinkResult(
    SandboxLinkStatus Status,
    SandboxLink? Link = null,
    string? Detail = null)
{
    public bool Succeeded => Status == SandboxLinkStatus.Succeeded;
}
