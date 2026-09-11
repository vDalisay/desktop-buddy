using System;
using System.Collections.Generic;
using System.Linq;
using DesktopBuddy.Domain.Content;
using DesktopBuddy.Domain.Environment;

namespace DesktopBuddy.Domain.Sandbox;

/// <summary>Stable identity of one placed systemic part inside one Scene.</summary>
public readonly struct SandboxPartId : IEquatable<SandboxPartId>, IComparable<SandboxPartId>
{
    private SandboxPartId(Guid value) => Value = value;

    public Guid Value { get; }
    public bool IsValid => Value != Guid.Empty;
    public static SandboxPartId New() => new(Guid.NewGuid());

    public static SandboxPartId From(Guid value)
    {
        if (value == Guid.Empty)
            throw new ArgumentException("Sandbox part ID cannot be empty.", nameof(value));
        return new SandboxPartId(value);
    }

    public bool Equals(SandboxPartId other) => Value == other.Value;
    public override bool Equals(object? obj) => obj is SandboxPartId other && Equals(other);
    public override int GetHashCode() => Value.GetHashCode();
    public int CompareTo(SandboxPartId other) => Value.CompareTo(other.Value);
    public override string ToString() => IsValid ? Value.ToString("N") : string.Empty;
    public static bool operator ==(SandboxPartId left, SandboxPartId right) => left.Equals(right);
    public static bool operator !=(SandboxPartId left, SandboxPartId right) => !left.Equals(right);
}

public enum SandboxPartShape
{
    Box = 0,
    Circle = 1,
}

public enum SandboxPartMaterial
{
    Wood = 0,
    Metal = 1,
    Rubber = 2,
}

/// <summary>
/// Canonical, immutable description of one buildable part. Definitions are shared and are never
/// mutated by a placement: per-part tuning lives in <see cref="SandboxPartOverrides"/>.
/// </summary>
public sealed record SandboxPartDefinition(
    SemanticDefinitionId Id,
    string DisplayName,
    string Description,
    SandboxPartShape Shape,
    SandboxPartMaterial Material,
    float Width,
    float Height,
    float Mass,
    float Bounce,
    float Friction,
    SandboxDeviceKind Device = SandboxDeviceKind.None) : ISemanticDefinition
{
    public const float MinimumExtent = 4.0f;
    public const float MaximumExtent = 512.0f;

    public float Radius => Math.Min(Width, Height) * 0.5f;

    /// <summary>Plain material boxes — beams, plates, blocks — can be given their own length and thickness.</summary>
    public bool IsResizable => Shape == SandboxPartShape.Box && Device == SandboxDeviceKind.None;

    public IReadOnlyList<string> Validate()
    {
        var problems = new List<string>();
        if (!Id.IsValid)
            problems.Add("Part definition requires a semantic ID.");
        if (string.IsNullOrWhiteSpace(DisplayName))
            problems.Add("Part definition requires a display name.");
        if (string.IsNullOrWhiteSpace(Description))
            problems.Add("Part definition requires a description for the build palette.");
        if (!IsExtent(Width) || !IsExtent(Height))
            problems.Add($"Part '{Id}' extents must be between {MinimumExtent} and {MaximumExtent} pixels.");
        if (Shape == SandboxPartShape.Circle && !Width.Equals(Height))
            problems.Add($"Round part '{Id}' must have equal extents.");
        if (!float.IsFinite(Mass) || Mass <= 0.0f)
            problems.Add($"Part '{Id}' requires a positive mass.");
        if (!IsUnit(Bounce))
            problems.Add($"Part '{Id}' bounce must be between 0 and 1.");
        if (!float.IsFinite(Friction) || Friction < 0.0f || Friction > 2.0f)
            problems.Add($"Part '{Id}' friction must be between 0 and 2.");
        if (!Enum.IsDefined(Device))
            problems.Add($"Part '{Id}' names an unknown device kind.");
        return problems;
    }

    private static bool IsExtent(float value) =>
        float.IsFinite(value) && value >= MinimumExtent && value <= MaximumExtent;

    private static bool IsUnit(float value) => float.IsFinite(value) && value is >= 0.0f and <= 1.0f;
}

/// <summary>
/// Bounded per-placement tuning. Every override is optional; an absent value means "use the shared
/// definition", so restoring a default can never write back into the definition itself. The two
/// device settings only mean something on their device: a Piston's push, a Timer's interval.
/// </summary>
public readonly record struct SandboxPartOverrides(
    float? MassScale = null,
    float? Bounce = null,
    float? GravityScale = null,
    bool Frozen = false,
    float? PistonPush = null,
    float? TimerSeconds = null,
    float? Length = null,
    float? Thickness = null)
{
    /// <summary>A resizable part's length (its unrotated width), in pixels.</summary>
    public const float MinimumLength = 16.0f;
    public const float MaximumLength = 384.0f;

    /// <summary>A resizable part's thickness (its unrotated height), in pixels.</summary>
    public const float MinimumThickness = 4.0f;
    public const float MaximumThickness = 64.0f;

    public const float MinimumMassScale = 0.1f;
    public const float MaximumMassScale = 10.0f;
    public const float MinimumGravityScale = -2.0f;
    public const float MaximumGravityScale = 4.0f;

    /// <summary>The speed, in pixels a second, a Piston gives what it shoves.</summary>
    public const float DefaultPistonPush = 850.0f;
    public const float MinimumPistonPush = 150.0f;
    public const float MaximumPistonPush = 1300.0f;

    /// <summary>How often a running Timer sends a pulse.</summary>
    public const float DefaultTimerSeconds = 1.0f;
    public const float MinimumTimerSeconds = 0.1f;
    public const float MaximumTimerSeconds = 10.0f;

    public static SandboxPartOverrides None => default;

    public bool HasAny => MassScale.HasValue || Bounce.HasValue || GravityScale.HasValue || Frozen ||
        PistonPush.HasValue || TimerSeconds.HasValue || Length.HasValue || Thickness.HasValue;

    /// <summary>Clamps every present value into its allowed band; out-of-band input never throws.</summary>
    public SandboxPartOverrides Clamped() => new(
        Clamp(MassScale, MinimumMassScale, MaximumMassScale),
        Clamp(Bounce, 0.0f, 1.0f),
        Clamp(GravityScale, MinimumGravityScale, MaximumGravityScale),
        Frozen,
        Clamp(PistonPush, MinimumPistonPush, MaximumPistonPush),
        Clamp(TimerSeconds, MinimumTimerSeconds, MaximumTimerSeconds),
        Clamp(Length, MinimumLength, MaximumLength),
        Clamp(Thickness, MinimumThickness, MaximumThickness));

    /// <summary>
    /// The part as this placement makes it: a resizable part at its own length and thickness, its
    /// mass growing with its area so a long beam is heavier than a short one. Everything else — and
    /// any part that is not resizable — is the shared definition, which is never mutated.
    /// </summary>
    public SandboxPartDefinition SizedDefinition(SandboxPartDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (!definition.IsResizable)
            return definition;
        float width = Clamp(Length, MinimumLength, MaximumLength) ?? definition.Width;
        float height = Clamp(Thickness, MinimumThickness, MaximumThickness) ?? definition.Height;
        if (width.Equals(definition.Width) && height.Equals(definition.Height))
            return definition;
        return definition with
        {
            Width = width,
            Height = height,
            Mass = definition.Mass * width * height / (definition.Width * definition.Height),
        };
    }

    public float PistonPushValue => Clamp(PistonPush, MinimumPistonPush, MaximumPistonPush) ?? DefaultPistonPush;

    public float TimerSecondsValue =>
        Clamp(TimerSeconds, MinimumTimerSeconds, MaximumTimerSeconds) ?? DefaultTimerSeconds;

    public float MassFor(SandboxPartDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        return definition.Mass * (Clamp(MassScale, MinimumMassScale, MaximumMassScale) ?? 1.0f);
    }

    public float BounceFor(SandboxPartDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        return Clamp(Bounce, 0.0f, 1.0f) ?? definition.Bounce;
    }

    public float GravityScaleValue =>
        Clamp(GravityScale, MinimumGravityScale, MaximumGravityScale) ?? 1.0f;

    private static float? Clamp(float? value, float minimum, float maximum)
    {
        if (value is not { } candidate || !float.IsFinite(candidate))
            return null;
        return Math.Clamp(candidate, minimum, maximum);
    }
}

/// <summary>
/// One part standing in a Scene: which definition it is, where it rests, and its own bounded
/// tuning. No engine transforms, velocities or node paths are persisted here — a reloaded room
/// rebuilds its parts at rest, exactly like a reloaded Buddy roster does.
/// </summary>
public sealed record PlacedSandboxPart(
    SandboxPartId PartId,
    SemanticDefinitionId DefinitionId,
    CanonicalRoomPosition Position,
    float RotationDegrees,
    SandboxPartOverrides Overrides)
{
    public PlacedSandboxPart WithPosition(CanonicalRoomPosition position) => this with { Position = position };

    public PlacedSandboxPart WithRotation(float rotationDegrees) =>
        this with { RotationDegrees = NormalizeRotation(rotationDegrees) };

    public PlacedSandboxPart WithOverrides(SandboxPartOverrides overrides) =>
        this with { Overrides = overrides.Clamped() };

    public static float NormalizeRotation(float degrees)
    {
        if (!float.IsFinite(degrees))
            return 0.0f;
        float wrapped = degrees % 360.0f;
        return wrapped < 0.0f ? wrapped + 360.0f : wrapped;
    }
}

public enum SandboxEditStatus
{
    Succeeded = 0,
    LimitReached,
    PartNotFound,
    UnknownDefinition,
    NoChange,
}

public readonly record struct SandboxEditResult(SandboxEditStatus Status, PlacedSandboxPart? Part = null)
{
    public bool Succeeded => Status == SandboxEditStatus.Succeeded;
}

/// <summary>
/// Engine-free durable systemic state for one Scene: the parts the player has built with. It is
/// versioned separately from <c>scene.json</c> so construction can grow its schema without touching
/// the Scene root, and it carries its own revision for the Scene transaction's dirty tracking.
/// </summary>
public sealed class SandboxDocument
{
    public const int CurrentSchemaVersion = 1;

    /// <summary>Bounded so one room cannot be built into an unopenable save or an unplayable tick.</summary>
    public const int MaximumParts = 200;

    /// <summary>Two per part on average: enough for carts and chains, bounded for the fixed tick.</summary>
    public const int MaximumLinks = 400;

    /// <summary>The systemic-sandbox budget's signal-wire allowance for one room.</summary>
    public const int MaximumWires = 192;

    private readonly List<PlacedSandboxPart> _parts;
    private readonly List<SandboxLink> _links;
    private readonly List<SandboxWire> _wires = [];

    public SandboxDocument(
        IEnumerable<PlacedSandboxPart>? parts = null,
        long revision = 0,
        int schemaVersion = CurrentSchemaVersion,
        IEnumerable<SandboxLink>? links = null,
        IEnumerable<SandboxWire>? wires = null)
    {
        if (schemaVersion <= 0 || schemaVersion > CurrentSchemaVersion)
            throw new ArgumentOutOfRangeException(nameof(schemaVersion), "Unsupported sandbox schema version.");
        if (revision < 0)
            throw new ArgumentOutOfRangeException(nameof(revision), "Sandbox revision cannot be negative.");

        _parts = parts?.ToList() ?? [];
        var seen = new HashSet<SandboxPartId>();
        foreach (PlacedSandboxPart part in _parts)
        {
            ArgumentNullException.ThrowIfNull(part);
            if (!part.PartId.IsValid)
                throw new ArgumentException("A placed part requires a stable ID.", nameof(parts));
            if (!part.DefinitionId.IsValid)
                throw new ArgumentException("A placed part requires a semantic definition ID.", nameof(parts));
            if (!seen.Add(part.PartId))
                throw new ArgumentException("A Scene cannot contain duplicate part IDs.", nameof(parts));
        }
        if (_parts.Count > MaximumParts)
            throw new ArgumentException($"A Scene cannot hold more than {MaximumParts} parts.", nameof(parts));

        _links = [];
        foreach (SandboxLink link in links ?? [])
        {
            ArgumentNullException.ThrowIfNull(link);
            SandboxLinkResult admitted = Admit(link);
            if (!admitted.Succeeded)
                throw new ArgumentException($"Invalid sandbox link: {admitted.Detail ?? admitted.Status.ToString()}", nameof(links));
            _links.Add(link);
        }

        foreach (SandboxWire wire in wires ?? [])
        {
            ArgumentNullException.ThrowIfNull(wire);
            SandboxWireResult admitted = Admit(wire);
            if (!admitted.Succeeded)
                throw new ArgumentException($"Invalid sandbox wire: {admitted.Detail ?? admitted.Status.ToString()}", nameof(wires));
            _wires.Add(wire);
        }

        SchemaVersion = schemaVersion;
        Revision = revision;
    }

    public int SchemaVersion { get; }
    public long Revision { get; private set; }
    public IReadOnlyList<PlacedSandboxPart> Parts => _parts;
    public IReadOnlyList<SandboxLink> Links => _links;
    public IReadOnlyList<SandboxWire> Wires => _wires;
    public int Count => _parts.Count;
    public bool CanAdd => _parts.Count < MaximumParts;

    public bool TryGet(SandboxPartId partId, out PlacedSandboxPart? part)
    {
        part = _parts.FirstOrDefault(candidate => candidate.PartId == partId);
        return part is not null;
    }

    public SandboxEditResult Add(
        SemanticDefinitionId definitionId,
        CanonicalRoomPosition position,
        float rotationDegrees = 0.0f,
        SandboxPartOverrides overrides = default,
        SandboxPartId? partId = null)
    {
        if (!definitionId.IsValid)
            return new SandboxEditResult(SandboxEditStatus.UnknownDefinition);
        if (!CanAdd)
            return new SandboxEditResult(SandboxEditStatus.LimitReached);

        SandboxPartId id = partId ?? SandboxPartId.New();
        if (_parts.Any(part => part.PartId == id))
            return new SandboxEditResult(SandboxEditStatus.NoChange);

        var placed = new PlacedSandboxPart(
            id,
            definitionId,
            position,
            PlacedSandboxPart.NormalizeRotation(rotationDegrees),
            overrides.Clamped());
        _parts.Add(placed);
        Touch();
        return new SandboxEditResult(SandboxEditStatus.Succeeded, placed);
    }

    public SandboxEditResult Remove(SandboxPartId partId)
    {
        int index = IndexOf(partId);
        if (index < 0)
            return new SandboxEditResult(SandboxEditStatus.PartNotFound);

        PlacedSandboxPart removed = _parts[index];
        _parts.RemoveAt(index);
        // A link to a part that is gone would be a constraint on nothing; it goes with the part,
        // and so does every wire into or out of it.
        _links.RemoveAll(link => link.Touches(partId));
        _wires.RemoveAll(wire => wire.Touches(partId));
        Touch();
        return new SandboxEditResult(SandboxEditStatus.Succeeded, removed);
    }

    public SandboxEditResult Move(SandboxPartId partId, CanonicalRoomPosition position, float rotationDegrees)
    {
        int index = IndexOf(partId);
        if (index < 0)
            return new SandboxEditResult(SandboxEditStatus.PartNotFound);

        PlacedSandboxPart current = _parts[index];
        float rotation = PlacedSandboxPart.NormalizeRotation(rotationDegrees);
        if (current.Position.Equals(position) && current.RotationDegrees.Equals(rotation))
            return new SandboxEditResult(SandboxEditStatus.NoChange, current);

        PlacedSandboxPart moved = current.WithPosition(position).WithRotation(rotation);
        _parts[index] = moved;
        Touch();
        return new SandboxEditResult(SandboxEditStatus.Succeeded, moved);
    }

    public SandboxEditResult SetOverrides(SandboxPartId partId, SandboxPartOverrides overrides)
    {
        int index = IndexOf(partId);
        if (index < 0)
            return new SandboxEditResult(SandboxEditStatus.PartNotFound);

        PlacedSandboxPart current = _parts[index];
        SandboxPartOverrides clamped = overrides.Clamped();
        if (current.Overrides.Equals(clamped))
            return new SandboxEditResult(SandboxEditStatus.NoChange, current);

        PlacedSandboxPart updated = current.WithOverrides(clamped);
        _parts[index] = updated;
        Touch();
        return new SandboxEditResult(SandboxEditStatus.Succeeded, updated);
    }

    /// <summary>
    /// An independent copy of everything in this document, identities kept, for a save commit that
    /// must not see edits made while it writes. It lives here so a field added to the document is
    /// copied by the one method that knows every field: a hand-written copy in the coordinator
    /// kept only parts and silently dropped every link from every save.
    /// </summary>
    public SandboxDocument Snapshot() => new(_parts, Revision, SchemaVersion, _links, _wires);

    /// <summary>Copies this room's parts, links and wires under fresh IDs, for Scene duplication.</summary>
    public SandboxDocument CopyWithNewPartIds()
    {
        var map = _parts.ToDictionary(part => part.PartId, _ => SandboxPartId.New());
        SandboxLinkEnd Remap(SandboxLinkEnd end) => end.IsWorld ? end : end with { PartId = map[end.PartId] };
        return new SandboxDocument(
            _parts.Select(part => part with { PartId = map[part.PartId] }),
            revision: 0,
            links: _links.Select(link => link with
            {
                LinkId = SandboxLinkId.New(),
                A = Remap(link.A),
                B = Remap(link.B),
            }),
            wires: _wires.Select(wire => wire with
            {
                WireId = SandboxWireId.New(),
                From = map[wire.From],
                To = map[wire.To],
            }));
    }

    /// <summary>
    /// Wires one device's output to another device's input. Nothing is stored unless the wire is
    /// valid — both ends are devices with those ports, facing the right way, and not the same part —
    /// so a rejected wire never changes the room.
    /// </summary>
    public SandboxWireResult AddWire(SandboxPartId from, string fromPort, SandboxPartId to, string toPort)
    {
        var wire = new SandboxWire(SandboxWireId.New(), from, fromPort, to, toPort);
        SandboxWireResult admitted = Admit(wire);
        if (!admitted.Succeeded)
            return admitted;

        _wires.Add(wire);
        Touch();
        return new SandboxWireResult(SandboxLinkStatus.Succeeded, wire);
    }

    public SandboxWireResult RemoveWire(SandboxWireId wireId)
    {
        int index = _wires.FindIndex(wire => wire.WireId == wireId);
        if (index < 0)
            return new SandboxWireResult(SandboxLinkStatus.WireNotFound);
        SandboxWire removed = _wires[index];
        _wires.RemoveAt(index);
        Touch();
        return new SandboxWireResult(SandboxLinkStatus.Succeeded, removed);
    }

    /// <summary>The device a placed part is, or <see cref="SandboxDeviceKind.None"/>.</summary>
    public SandboxDeviceKind DeviceOf(SandboxPartId partId) =>
        TryGet(partId, out PlacedSandboxPart? part) &&
        SandboxPartCatalogue.TryGet(part!.DefinitionId, out SandboxPartDefinition definition)
            ? definition.Device
            : SandboxDeviceKind.None;

    private SandboxWireResult Admit(SandboxWire wire)
    {
        if (!wire.WireId.IsValid)
            return new SandboxWireResult(SandboxLinkStatus.Invalid, null, "A wire requires a stable ID.");
        if (IndexOf(wire.From) < 0 || IndexOf(wire.To) < 0)
            return new SandboxWireResult(SandboxLinkStatus.PartNotFound);
        if (wire.From == wire.To)
            return new SandboxWireResult(SandboxLinkStatus.Invalid, null, "A wire cannot join a device to itself.");
        if (!SandboxDevices.HasPort(DeviceOf(wire.From), wire.FromPort, SandboxPortDirection.Output))
            return new SandboxWireResult(SandboxLinkStatus.Invalid, null, "A wire must start at a device's output.");
        if (!SandboxDevices.HasPort(DeviceOf(wire.To), wire.ToPort, SandboxPortDirection.Input))
            return new SandboxWireResult(SandboxLinkStatus.Invalid, null, "A wire must end at a device's input.");
        if (_wires.Count >= MaximumWires)
            return new SandboxWireResult(SandboxLinkStatus.LimitReached);
        foreach (SandboxWire existing in _wires)
        {
            if (existing.WireId == wire.WireId)
                return new SandboxWireResult(SandboxLinkStatus.Invalid, null, "Duplicate wire ID.");
            if (existing.Duplicates(wire))
                return new SandboxWireResult(SandboxLinkStatus.Duplicate, existing);
        }
        return new SandboxWireResult(SandboxLinkStatus.Succeeded, wire);
    }

    /// <summary>Keeps a stored wire only if this room can honour it; the load path, so no revision change.</summary>
    internal bool TryRestoreWire(SandboxWire wire)
    {
        if (!Admit(wire).Succeeded)
            return false;
        _wires.Add(wire);
        return true;
    }

    public bool TryGetLink(SandboxLinkId linkId, out SandboxLink? link)
    {
        link = _links.FirstOrDefault(candidate => candidate.LinkId == linkId);
        return link is not null;
    }

    /// <summary>
    /// Adds one link between existing parts, or from a part to the room. Nothing is stored unless
    /// the whole link is valid, so a rejected link never leaves half a constraint behind.
    /// </summary>
    public SandboxLinkResult AddLink(
        SandboxLinkKind kind,
        SandboxLinkEnd a,
        SandboxLinkEnd b,
        float length = 0.0f)
    {
        var link = new SandboxLink(
            SandboxLinkId.New(),
            kind,
            a,
            b,
            kind == SandboxLinkKind.Rope
                ? Math.Clamp(float.IsFinite(length) ? length : 0.0f, SandboxLink.MinimumRopeLength, SandboxLink.MaximumRopeLength)
                : 0.0f);
        SandboxLinkResult admitted = Admit(link);
        if (!admitted.Succeeded)
            return admitted;

        _links.Add(link);
        Touch();
        return new SandboxLinkResult(SandboxLinkStatus.Succeeded, link);
    }

    public SandboxLinkResult RemoveLink(SandboxLinkId linkId)
    {
        int index = _links.FindIndex(link => link.LinkId == linkId);
        if (index < 0)
            return new SandboxLinkResult(SandboxLinkStatus.LinkNotFound);
        SandboxLink removed = _links[index];
        _links.RemoveAt(index);
        Touch();
        return new SandboxLinkResult(SandboxLinkStatus.Succeeded, removed);
    }

    /// <summary>
    /// Re-anchors an existing link — same identity, same kind, same parts — at new end points. Build
    /// uses it after a linked part is moved or turned, so the link holds where the player left the
    /// parts instead of snapping them back together when Play resumes.
    /// </summary>
    public SandboxLinkResult ReseatLink(SandboxLinkId linkId, SandboxLinkEnd a, SandboxLinkEnd b, float length)
    {
        int index = _links.FindIndex(link => link.LinkId == linkId);
        if (index < 0)
            return new SandboxLinkResult(SandboxLinkStatus.LinkNotFound);

        SandboxLink current = _links[index];
        if (a.PartId != current.A.PartId || b.PartId != current.B.PartId)
            return new SandboxLinkResult(SandboxLinkStatus.Invalid, current, "Re-seating cannot change which parts a link joins.");

        SandboxLink updated = current with
        {
            A = a,
            B = b,
            Length = current.Kind == SandboxLinkKind.Rope
                ? Math.Clamp(float.IsFinite(length) ? length : current.Length, SandboxLink.MinimumRopeLength, SandboxLink.MaximumRopeLength)
                : 0.0f,
        };
        if (updated.Problem() is { } problem)
            return new SandboxLinkResult(SandboxLinkStatus.Invalid, current, problem);
        if (updated == current)
            return new SandboxLinkResult(SandboxLinkStatus.Succeeded, current);

        _links[index] = updated;
        Touch();
        return new SandboxLinkResult(SandboxLinkStatus.Succeeded, updated);
    }

    /// <summary>Removes every link touching one part, keeping the part.</summary>
    public int RemoveLinksOf(SandboxPartId partId)
    {
        int removed = _links.RemoveAll(link => link.Touches(partId));
        if (removed > 0)
            Touch();
        return removed;
    }

    private SandboxLinkResult Admit(SandboxLink link)
    {
        if (link.Problem() is { } problem)
            return new SandboxLinkResult(SandboxLinkStatus.Invalid, null, problem);
        if (IndexOf(link.A.PartId) < 0 || (!link.B.IsWorld && IndexOf(link.B.PartId) < 0))
            return new SandboxLinkResult(SandboxLinkStatus.PartNotFound);
        if (_links.Count >= MaximumLinks)
            return new SandboxLinkResult(SandboxLinkStatus.LimitReached);
        foreach (SandboxLink existing in _links)
        {
            if (existing.LinkId == link.LinkId)
                return new SandboxLinkResult(SandboxLinkStatus.Invalid, null, "Duplicate link ID.");
            if (existing.Duplicates(link))
                return new SandboxLinkResult(SandboxLinkStatus.Duplicate, existing);
        }
        return new SandboxLinkResult(SandboxLinkStatus.Succeeded, link);
    }

    private int IndexOf(SandboxPartId partId)
    {
        for (int index = 0; index < _parts.Count; index++)
        {
            if (_parts[index].PartId == partId)
                return index;
        }
        return -1;
    }

    private void Touch() => Revision++;
}
