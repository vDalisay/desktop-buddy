using System;

namespace DesktopBuddy.Domain.Content;

/// <summary>
/// Stable provider-qualified identity for new systemic, Creator and declarative UGC definitions.
/// Existing shipped <see cref="ContentIds"/> remain their own persisted legacy contract and are not
/// silently rewritten into this format.
///
/// Canonical forms currently have exactly two provider families:
/// <code>
/// core:construction/wood_beam
/// core:device/button
/// ugc:&lt;pack-guid-N&gt;/entity/my_launcher
/// </code>
///
/// IDs have no filesystem semantics: only lower-case ASCII letters, digits, underscore and dash are
/// accepted in path segments. Dots, backslashes, traversal segments, absolute paths, CLR/Godot names
/// and display-name-derived identifiers are therefore not representable by this type.
/// </summary>
public readonly struct SemanticDefinitionId : IEquatable<SemanticDefinitionId>, IComparable<SemanticDefinitionId>
{
    public const string CoreProvider = "core";
    public const string UgcProvider = "ugc";
    public const int MaximumLength = 192;
    public const int MaximumSegmentLength = 64;

    private readonly string? _value;
    private readonly string? _provider;
    private readonly string? _path;

    private SemanticDefinitionId(string value, string provider, string path)
    {
        _value = value;
        _provider = provider;
        _path = path;
    }

    public string Value => _value ?? string.Empty;
    public string Provider => _provider ?? string.Empty;
    public string Path => _path ?? string.Empty;
    public bool IsValid => _value is not null;
    public bool IsCore => IsValid && Provider == CoreProvider;
    public bool IsUgc => IsValid && Provider == UgcProvider;

    public static SemanticDefinitionId Parse(string value)
    {
        if (!TryParse(value, out SemanticDefinitionId id))
            throw new FormatException($"Invalid semantic definition ID '{value}'.");
        return id;
    }

    public static bool TryParse(string? value, out SemanticDefinitionId id)
    {
        id = default;
        if (string.IsNullOrEmpty(value) || value.Length > MaximumLength)
            return false;
        if (!string.Equals(value, value.Trim(), StringComparison.Ordinal))
            return false;

        int separator = value.IndexOf(':');
        if (separator <= 0 || separator == value.Length - 1 || value.IndexOf(':', separator + 1) >= 0)
            return false;

        string provider = value[..separator];
        string path = value[(separator + 1)..];
        if (provider is not (CoreProvider or UgcProvider))
            return false;
        if (!IsCanonicalSegment(provider) || !IsCanonicalPath(path))
            return false;

        if (provider == UgcProvider && !TryParseUgcPath(path, out _))
            return false;

        id = new SemanticDefinitionId(value, provider, path);
        return true;
    }

    public static SemanticDefinitionId CreateCore(string path) => Parse($"{CoreProvider}:{path}");

    public static SemanticDefinitionId CreateUgc(Guid packId, string path)
    {
        if (packId == Guid.Empty)
            throw new ArgumentException("UGC pack ID cannot be empty.", nameof(packId));
        return Parse($"{UgcProvider}:{packId:N}/{path}");
    }

    public bool TryGetUgcPackId(out Guid packId)
    {
        if (!IsUgc)
        {
            packId = Guid.Empty;
            return false;
        }

        return TryParseUgcPath(Path, out packId);
    }

    public override string ToString() => Value;

    public bool Equals(SemanticDefinitionId other) =>
        string.Equals(Value, other.Value, StringComparison.Ordinal);

    public override bool Equals(object? obj) => obj is SemanticDefinitionId other && Equals(other);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);

    public int CompareTo(SemanticDefinitionId other) =>
        string.Compare(Value, other.Value, StringComparison.Ordinal);

    public static bool operator ==(SemanticDefinitionId left, SemanticDefinitionId right) => left.Equals(right);
    public static bool operator !=(SemanticDefinitionId left, SemanticDefinitionId right) => !left.Equals(right);

    private static bool TryParseUgcPath(string path, out Guid packId)
    {
        packId = Guid.Empty;
        int slash = path.IndexOf('/');
        if (slash <= 0 || slash == path.Length - 1)
            return false;

        string packSegment = path[..slash];
        return packSegment.Length == 32 &&
            Guid.TryParseExact(packSegment, "N", out packId) &&
            packId != Guid.Empty;
    }

    private static bool IsCanonicalPath(string path)
    {
        if (path.Length == 0 || path[0] == '/' || path[^1] == '/')
            return false;

        int segmentStart = 0;
        for (int i = 0; i <= path.Length; i++)
        {
            if (i != path.Length && path[i] != '/')
                continue;

            int length = i - segmentStart;
            if (length <= 0 || length > MaximumSegmentLength)
                return false;
            if (!IsCanonicalSegment(path.AsSpan(segmentStart, length)))
                return false;

            segmentStart = i + 1;
        }

        return true;
    }

    private static bool IsCanonicalSegment(ReadOnlySpan<char> segment)
    {
        if (segment.Length == 0 || segment.Length > MaximumSegmentLength)
            return false;

        foreach (char character in segment)
        {
            bool lower = character is >= 'a' and <= 'z';
            bool digit = character is >= '0' and <= '9';
            if (!lower && !digit && character is not ('_' or '-'))
                return false;
        }

        return true;
    }
}
